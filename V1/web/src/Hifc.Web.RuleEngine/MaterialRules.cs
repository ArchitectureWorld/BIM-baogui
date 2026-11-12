using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using Hifc.Reference;
using Hifc.Web.Contracts;

namespace Hifc.Web.RuleEngine;

public sealed class ResolvedMaterialDecision
{
    public MaterialPreviewRequest Request { get; set; }
    public MaterialPreview Preview { get; set; }
    public MaterialDecision Decision { get; set; }
}
public sealed partial class RuleAdapter
{
    private const string WorkbookSchema="fbdf2a40-b13c-4a9a-aeda-525e010a4785";
    private const string RedlineSchema="b238a635-91f4-46cc-ac56-f41b7adac784";
    private static readonly JsonSerializerOptions MaterialJson=new(){PropertyNameCaseInsensitive=true};
    private sealed class MaterialResolution
    {
        public EditDraft[] Drafts {get;set;}=[];
        public NativeDataWrite[] Writes {get;set;}=[];
        public Dictionary<string,string> Units {get;set;}=new();
    }
    public MaterialPreview PreviewMaterial(CheckResult basis,MaterialPreviewRequest request)
    {
        if(basis?.Complete!=true||request==null)throw new InvalidOperationException("请先完成当前模型全面检查。");
        ProtocolGuard.Match(basis.Context,request.Context);
        if(request.Kind!="workbook"&&request.Kind!="redline")throw new InvalidOperationException("不支持的资料类型。");
        var preview=new MaterialPreview{Context=basis.Context,Id=Guid.NewGuid().ToString("N"),Kind=request.Kind,FileName=request.FileName};
        if(request.Kind=="workbook") {
            var book=Uploaded(request,32*1024*1024,ReportingWorkbookReader.Read);
            book.Path=request.SourcePath??request.FileName;
            foreach(var item in book.Tables.SelectMany(t=>t.Items))if(item.Provenance!=null)item.Provenance.Path=book.Path;
            preview.Sha256=book.Sha256;preview.DataJson=JsonSerializer.Serialize(book,MaterialJson);
            preview.Tables=book.Tables.Select(t=>new MaterialTableInfo{Id=t.Id,Title=t.Title,Kind=t.Kind,BuildingName=t.BuildingName}).ToArray();
            var table=request.TableId==null?(book.Tables.Length==1?book.Tables[0]:null):book.Tables.SingleOrDefault(t=>t.Id==request.TableId);
            if(table==null) {preview.Issues=book.Issues.Concat(new[]{"请选择一张报规表后重新预览。"}).ToArray();return preview;}
            var plan=ReportingWorkbookPlanner.Build(table,CoreRows(basis),basis.ModelKind,request.TargetBuildingUniqueId);
            preview.Changes=plan.Changes.Select((c,i)=>new MaterialChange{Key=table.Id+"|"+i,FieldKey=FindField(basis,c).Key,ElementUniqueId=c.ElementUniqueId,ElementName=c.ElementName,
                Property=c.Property,CurrentValue=c.CurrentValue,Value=c.Value,Unit=c.ValueUnit,SourceLocation=c.SourceLocation,Issue=c.Issue,Selected=c.Selected,CanSelect=c.CanSelect,IsConflict=c.IsConflict}).ToArray();
            preview.References=plan.References.Select(r=>new MaterialReference{Field=r.Field,Value=r.Value,LevelName=r.LevelName,Unit=r.Unit,Status=r.Status,SourceLocation=r.SourceLocation}).ToArray();
            preview.Issues=book.Issues.Concat(plan.Issues).Distinct().ToArray();return preview;
        }
        RedlineData data;
        if(!string.IsNullOrEmpty(request.ContentBase64)) {
            data=Uploaded(request,20*1024*1024,RedlineWorkbook.Read); data.SourcePath=request.SourcePath??request.FileName;
            if(!string.IsNullOrEmpty(request.DataJson)) {
                var options=ReadRedline(request.DataJson); data.Accepted=options.Accepted;data.Unit=options.Unit;data.Frame=options.Frame;data.Axes=options.Axes;data.CoordinateSystemName=options.CoordinateSystemName;
            }
        } else data=ReadRedline(request.DataJson);
        preview.Sha256=data.SourceHash;preview.DataJson=JsonSerializer.Serialize(data,MaterialJson);
        var validation=RedlineDataPolicy.Validate(data);var report=CompareRedline(basis,data,request.Evidence,request.ToleranceM,request.MinimumGapM);
        preview.Issues=validation.Issues.Concat(report.Issues).Distinct().ToArray();
        preview.References=new[]{new MaterialReference{Field="双载体红线",Value=report.Pair?.Detail,Status=report.Pair?.Status},new MaterialReference{Field="人工红线坐标",Value=report.Manual?.Detail,Status=report.Manual?.Status}};
        return preview;
    }
    private static RedlineData ReadRedline(string json)
    {
        if(string.IsNullOrEmpty(json)||json.Length>2*1024*1024)throw new InvalidOperationException("人工红线资料为空或过大。");
        return JsonSerializer.Deserialize<RedlineData>(json,MaterialJson)??throw new InvalidOperationException("人工红线资料为空。");
    }
    private static T Uploaded<T>(MaterialPreviewRequest request,int maximum,Func<string,T> reader)
    {
        if(request.ContentBase64==null||request.ContentBase64.Length>(maximum+2L)/3*4)throw new InvalidOperationException("上传资料为空或超过允许大小。");
        if(!string.Equals(Path.GetExtension(request.FileName),".xlsx",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("只接受 .xlsx 资料；不运行宏或公式。");
        byte[] bytes;
        try{bytes=Convert.FromBase64String(request.ContentBase64);}catch(FormatException){throw new InvalidOperationException("资料上传编码无效。");}
        if(bytes.Length==0||bytes.Length>maximum)throw new InvalidOperationException("上传资料为空或超过允许大小。");
        using(var memory=new MemoryStream(bytes))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read)) {
            if(zip.Entries.Count>512||zip.Entries.Sum(e=>e.Length)>96*1024*1024L||zip.Entries.Any(e=>e.Length>16*1024*1024L))throw new InvalidOperationException("工作簿解压内容超过允许大小。");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in zip.Entries) {
                var name=entry.FullName.Replace('\\','/');
                if(!names.Add(name)||name.StartsWith('/')||name.Split('/').Any(p=>p=="..")||name.Contains(':'))throw new InvalidOperationException("工作簿包含重复或不安全的条目路径。");
                if(name.EndsWith(".xml",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".rels",StringComparison.OrdinalIgnoreCase)) {
                    using var input=entry.Open();using var xml=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16*1024*1024});while(xml.Read()){}
                }
            }
        }
        // The original client path is provenance only. Never resolve it on the service host.
        var directory=Path.Combine(Path.GetTempPath(),"hifc-web-material-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try {var file=Path.Combine(directory,"upload.xlsx");File.WriteAllBytes(file,bytes);return reader(file);}
        finally {Directory.Delete(directory,true);}
    }
    public byte[] CreateWorkbookTemplate(CheckResult basis)
    {
        if(basis?.Complete!=true)throw new InvalidOperationException("请先全面检查模型。");
        var directory=Path.Combine(Path.GetTempPath(),"hifc-web-template-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try {
            var path=Path.Combine(directory,"template.xlsx");
            var building=basis.Fields.FirstOrDefault(f=>f.PropertySet=="Pset_建筑技术信息属性集"&&f.Property=="建筑名称"&&!string.IsNullOrWhiteSpace(f.Value))?.Value
                ??basis.Elements.FirstOrDefault(e=>e.Element.Entity=="IfcBuilding")?.Element.Building;
            ReportingWorkbookTemplate.WriteForModel(path,basis.ModelKind,building,
                ReportingWorkbookTemplate.ActualLevelNames(CoreRows(basis),Planning(basis.Settings)));
            return File.ReadAllBytes(path);
        }
        finally {Directory.Delete(directory,true);}
    }
    private static FieldResult FindField(CheckResult basis,ReportingWorkbookChange c) => basis.Fields.Single(f=>f.ElementUniqueId==c.ElementUniqueId&&f.OwnerUniqueId==c.OwnerUniqueId&&f.PropertySet==c.PropertySet&&f.Source==c.Source&&f.Binding==c.Binding);
    private static MaterialResolution ResolveMaterials(CheckResult basis,ResolvedMaterialDecision[] materials,DateTime now)
    {
        if(materials.Select(m=>m?.Preview?.Id).Distinct().Count()!=materials.Length)throw new InvalidOperationException("资料决定重复或不完整。");
        var drafts=new List<EditDraft>();var units=new Dictionary<string,string>();var writes=new Dictionary<string,NativeDataWrite>();
        foreach(var material in materials) {
            var request=material.Request;var preview=material.Preview;var decision=material.Decision;
            if(request==null||preview==null||decision==null||!decision.Accepted||decision.PreviewId!=preview.Id||preview.Kind!=request.Kind)throw new InvalidOperationException("资料尚未确认或预览已失效。");
            ProtocolGuard.Match(basis.Context,request.Context);
            ProtocolGuard.Match(basis.Context,preview.Context);
            if(decision.SelectedKeys.Distinct().Count()!=decision.SelectedKeys.Length||decision.SelectedKeys.Any(k=>!preview.Changes.Any(c=>c.Key==k&&c.CanSelect)))throw new InvalidOperationException("资料选择包含无效、空白或未预览的来源。");
            if(request.Kind=="redline") {
                if(writes.ContainsKey(RedlineSchema))throw new InvalidOperationException("同轮仅可确认一份人工红线资料。");
                var data=ReadRedline(preview.DataJson);var json=RedlineDataPolicy.Serialize(data);
                writes[RedlineSchema]=DataWrite(basis,RedlineSchema,"HifcManualRedlineV1",json);
                if(!string.IsNullOrEmpty(request.ContentBase64))writes[RedlineSchema].SourceFiles=[new(){Path=request.SourcePath??request.FileName,Sha256=preview.Sha256}];
                continue;
            }
            if(request.Kind!="workbook")throw new InvalidOperationException("不支持的资料类型。");
            var book=JsonSerializer.Deserialize<ReportingWorkbookDocument>(preview.DataJson,MaterialJson)??throw new InvalidOperationException("报规资料预览已失效。");
            var table=request.TableId==null?book.Tables.Single():book.Tables.Single(t=>t.Id==request.TableId);
            var plan=ReportingWorkbookPlanner.Build(table,CoreRows(basis),basis.ModelKind,request.TargetBuildingUniqueId);
            for(int i=0;i<plan.Changes.Length;i++)plan.Changes[i].Selected=decision.SelectedKeys.Contains(table.Id+"|"+i);
            var accepted=ReportingWorkbookPlanner.Decide(plan,decision.RetainReferences);
            foreach(var c in accepted.Changes) {
                var f=FindField(basis,c);drafts.Add(new EditDraft{FieldKey=f.Key,OriginalValue=f.Value,OriginalSignature=f.SourceSignature,Value=c.Value});units.Add(f.Key,c.ValueUnit);
            }
            var oldJson=writes.GetValueOrDefault(WorkbookSchema)?.Json??basis.ProjectData.SingleOrDefault(d=>d.SchemaId==WorkbookSchema)?.Json;
            var archive=string.IsNullOrEmpty(oldJson)?new JsonArray():JsonNode.Parse(oldJson) as JsonArray??throw new InvalidOperationException("模型报规资料格式损坏，未覆盖原记录。");
            if(archive.Any(e=>e?["Import"]==null))throw new InvalidOperationException("模型报规资料格式损坏，未覆盖原记录。");
            for(int i=archive.Count-1;i>=0;i--)if((string)archive[i]["Import"]["TableId"]==accepted.TableId&&(string)archive[i]["Import"]["TargetBuildingUniqueId"]==accepted.TargetBuildingUniqueId)archive.RemoveAt(i);
            archive.Add(new JsonObject{["Import"]=JsonSerializer.SerializeToNode(accepted,MaterialJson),["WrittenUtc"]=now.ToString("O"),
                ["AppliedChanges"]=JsonSerializer.SerializeToNode(accepted.Changes.Select(c=>new{Id=c.ElementId,Name=c.ElementName,c.Source,OldValue=c.CurrentDisplayValue,NewValue=c.PreviewValue,
                    ExpectedRaw=c.Value,c.OwnerUniqueId,Action="赋值",c.Binding,Detail="报规资料："+c.SourceLocation}).ToArray(),MaterialJson)});
            var sources=(writes.GetValueOrDefault(WorkbookSchema)?.SourceFiles??[]).Concat(accepted.SourceFiles.Select(s=>new NativeSourceFile{Path=s.Path,Sha256=s.Sha256})).ToArray();
            if(sources.GroupBy(s=>s.Path,StringComparer.OrdinalIgnoreCase).Any(g=>g.Select(s=>s.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1))throw new InvalidOperationException("同一来源文件的上传版本不一致，请重新预览。");
            writes[WorkbookSchema]=DataWrite(basis,WorkbookSchema,"HifcReportingWorkbookArchiveV1",archive.ToJsonString());
            writes[WorkbookSchema].SourceFiles=sources.DistinctBy(s=>s.Path,StringComparer.OrdinalIgnoreCase).ToArray();
        }
        return new MaterialResolution{Drafts=drafts.ToArray(),Writes=writes.Values.ToArray(),Units=units};
    }
    private static NativeDataWrite DataWrite(CheckResult basis,string schema,string name,string json)
    {
        if(json.Length>64*1024*1024)throw new InvalidOperationException("模型资料超出允许大小，未覆盖原记录。");
        var existing=basis.ProjectData.SingleOrDefault(d=>d.SchemaId==schema);
        if(existing!=null&&(existing.SchemaName!=name||existing.Hash!=Canonical.Hash(existing.Json)))throw new InvalidOperationException("现有模型资料身份或散列不一致。");
        return new NativeDataWrite{SchemaId=schema,SchemaName=name,ExpectedHash=existing?.Hash??Canonical.Hash(""),Json=json,Hash=Canonical.Hash(json)};
    }
    private static RedlineReport CompareRedline(CheckResult basis,RedlineData data,NativeRedlineEvidence evidence,double tolerance,double minimumGap)
    {
        if(!double.IsFinite(tolerance)||tolerance<=0||!double.IsFinite(minimumGap)||minimumGap<0)throw new InvalidOperationException("红线水平容差必须为正数，最小净距必须为非负有限数值（米）。");
        var rows=CoreRows(basis);var carriers=evidence?.Carriers??[];
        foreach(var captured in carriers) {
            var element=basis.Elements.SingleOrDefault(e=>e.Element.UniqueId==captured.UniqueId)?.Element;
            if(element==null||element.Id!=captured.ElementId||string.IsNullOrEmpty(element.GeometryFingerprint)||element.GeometryFingerprint!=captured.GeometryFingerprint)
                throw new InvalidOperationException("红线几何或共享坐标已变化，请重新全面检查后预览。");
        }
        var land=rows.Where(r=>RedlineRoles.Resolve(r)==RedlineRoles.NetLand).ToArray();var control=rows.Where(r=>RedlineRoles.Resolve(r)==RedlineRoles.Control).ToArray();
        var report=new RedlineReport();
        if(land.Length!=1||control.Length!=1) {
            report.Pair=RedlineComparison.Pending("需要各一个明确分类的规划净用地与净用地控制红线载体。");
            report.Manual=RedlineComparison.Pending("模型双载体尚未唯一确定，保留人工资料，未作模型对照。");
            report.Issues=[report.Pair.Detail,report.Manual.Detail];return report;
        }
        else {
            var a=carriers.SingleOrDefault(c=>c.UniqueId==land[0].UniqueId);var b=carriers.SingleOrDefault(c=>c.UniqueId==control[0].UniqueId);
            var pair=(evidence?.Pairs??[]).SingleOrDefault(p=>p.LeftUniqueId==land[0].UniqueId&&p.RightUniqueId==control[0].UniqueId||p.LeftUniqueId==control[0].UniqueId&&p.RightUniqueId==land[0].UniqueId);
            report.Pair=RedlineComparison.Pair(Carrier(a,RedlineRoles.NetLand,tolerance),Carrier(b,RedlineRoles.Control,tolerance),pair?.Error==null?pair?.IntersectionVolumeM3:null,tolerance,minimumGap);
        }
        var carrier=land.Length==1?carriers.SingleOrDefault(c=>c.UniqueId==land[0].UniqueId):null;
        var points=data.Frame==RedlineFrame.Shared?carrier?.TopShared:carrier?.TopInternal;
        var outline=Outline(points,carrier?.UnsupportedReason??(carrier==null?"缺少当前模型红线几何证据。":null),data.Axes==RedlineAxes.XNorthYEast);
        report.Manual=RedlineComparison.Manual(data,outline,tolerance);
        report.Issues=new[]{report.Pair,report.Manual}.Where(c=>!c.Passed).Select(c=>c.Detail).ToArray();return report;
        static RedlineOutline Outline(NativePoint3[] points,string error,bool swap=false) => new(){UnsupportedReason=error,Vertices=(points??[]).Select((p,i)=>new RedlinePoint{Sequence=i+1,X=swap?p.Y:p.X,Y=swap?p.X:p.Y}).ToArray()};
        static RedlineCarrier Carrier(NativeRedlineCarrier c,string role,double tolerance) {
            if(c==null)return null;var top=Outline(c.TopInternal,c.UnsupportedReason);var bottom=Outline(c.BottomInternal,c.UnsupportedReason);
            return new RedlineCarrier{ElementId=c.ElementId,Name=c.Name,Role=role,Outline=top,BottomM=c.BottomM,TopM=c.TopM,SolidVerified=c.HorizontalLinearFaces&&RedlineComparison.Outlines(top,bottom,tolerance).Passed};
        }
    }
}
