using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Hifc.Reference;
using Hifc.Web.Contracts;
using Hifc.Web.RuleEngine;

internal static class Program
{
    static int tests, failures;
    static void Test(string name,Action body){tests++;try{body();Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;Console.WriteLine("FAIL "+name+": "+ex);}}
    static void Assert(bool yes,string message){if(!yes)throw new Exception(message);}
    static void Reject(Action body){try{body();}catch(InvalidOperationException){return;}throw new Exception("Unsafe operation accepted");}
    sealed class Fixture
    {
        internal RuleAdapter Engine=new();internal InventoryPage[] Inventory;internal CapturePlan Plan;internal ParameterValue[] Values;internal NativeDefinitionSnapshot[] Definitions=[];
        internal Fixture(ElementRecord[] elements=null,ModelSettings settings=null,ProjectDataRecord[] data=null,string kind="总图"){
            elements??=[Element("land",1,"IfcSlab","规划净用地")];settings??=new ModelSettings{RuleProfileId=PlatformRules.ProfileId};
            var context=new RequestContext{RuleVersion=Engine.RuleVersion,RequestId="request",DocumentSessionId="document",ScanId="scan",SnapshotHash=Canonical.InventoryHash(elements),ConfigurationHash=Canonical.ConfigurationHash(settings,data??[])};
            Inventory=[new InventoryPage{Context=context,Elements=elements,Settings=settings,ProjectData=data??[],Stage="报规",ModelKind=kind,PageCount=1,TotalElements=elements.Length,PageHash=Canonical.InventoryHash(elements)}];
            Plan=Engine.CreateCapturePlan(Inventory);
            Values=Plan.Targets.DistinctBy(t=>t.OwnerUniqueId+"|"+t.Origin+"|"+t.Source).Select(t=>new ParameterValue{OwnerId=t.OwnerId,OwnerUniqueId=t.OwnerUniqueId,Source=t.Source,Origin=t.Origin}).ToArray();
        }
        internal ParameterValue Set(string owner,string source,string value,string definition="Text",string storage="String",double? metric=null){
            var v=Values.Single(v=>v.OwnerUniqueId==owner&&v.Source==source);v.Exists=true;v.HasValue=value!=null;v.RawValue=value;v.StorageType=storage;v.DefinitionType=definition;v.MetricValue=metric;v.SharedGuid=ReferencePolicy.SourceGuid(source).ToString("D");return v;
        }
        internal CheckResult Check(bool repeated=false){
            foreach(var d in Definitions)d.Hash=Canonical.DefinitionHash(d);
            var pieces=repeated?Values.Chunk(Math.Max(1,Values.Length/2)).ToArray():[Values];
            var pages=pieces.Select((part,i)=>new CapturePage{Context=Plan.Context,PageIndex=i,PageCount=pieces.Length,TotalValues=Values.Length,Values=part,PageHash=Canonical.ValuesHash(part),Definitions=Definitions}).ToArray();
            return Engine.Evaluate(Inventory,Plan,pages);
        }
        internal EditPlan Edit(CheckResult basis,EditDraft[] drafts=null,bool suggestions=true,ResolvedMaterialDecision[] materials=null)=>Engine.BuildEditPlan(basis,Values,drafts??[],suggestions,DateTime.UtcNow,materials);
    }
    static ElementRecord Element(string uid,int id,string entity,string slot=null,string level=null,double? area=null)=>new(){Id=id,UniqueId=uid,TypeId=id+100,TypeUniqueId=entity=="IfcBuilding"||entity=="IfcBuildingStorey"?null:uid+"-type",Name=uid,Building="综合楼",Entity=entity,SourceEntity=entity,DeclaredSlot=slot,CategoryId=entity=="IfcSpace"?-2003200:-2000032,LevelUniqueId=level,AreaScheme=entity=="IfcSpace"?"建筑面积":null,NativeAreaM2=area,ProjectedAreaM2=area,ProjectionKey=uid};
    static EditDraft Draft(FieldResult f,string value)=>new(){FieldKey=f.Key,OriginalValue=f.Value,OriginalSignature=f.SourceSignature,Value=value};
    static ExportRequest Export(CheckResult basis)=>new(){Context=basis.Context,FileName="OUT_测试_报规_总图_20261029_V1.ifc"};
    static void Main(){
        Test("current_core_resources_and_profiles",()=>{var f=new Fixture();Assert(f.Engine.RuleVersion=="reference-0.4.20-web2"&&f.Engine.Profiles.Any(p=>p.Id==PlatformRules.ProfileId)&&PlatformRules.Rules().Length>0,"profile missing");});
        Test("capture_includes_alias_bridges_and_definition_evidence",()=>{var f=new Fixture([Element("site",1,"IfcSlab","总平构件")]);Assert(f.Plan.Targets.Any(t=>t.Source=="总平构件建筑密度")&&f.Plan.Targets.Any(t=>t.Source=="建筑密度"),"boolean alias missing");Assert(f.Plan.DefinitionSources.Any(s=>s.StartsWith("__HIFCV1_")),"bridges not captured");});
        Test("field_metadata_uses_platform_choices_and_required_policy",()=>{var f=new Fixture([Element("region",1,"IfcSlab","建筑区域","floor",50)],kind:"单体建筑");var b=f.Check();Assert(b.Fields.Any(x=>x.Property=="主功能类别"&&x.InputKind=="editable-choice"&&x.Options.Length>0),"custom choice metadata missing");Assert(b.Fields.Single(x=>x.Property=="子功能类别").ParentProperty=="主功能类别","parent metadata missing");});
        Test("repeated_definition_pages_deduplicate_without_changing_capture",()=>{var f=new Fixture();f.Definitions=[new(){Source="类目",ParameterId=50,Guid=ReferencePolicy.SourceGuid("类目").ToString("D"),DefinitionType="Text",Origin="instance"}];var b=f.Check(true);Assert(b.Definitions.Length==1&&b.CaptureHash==Canonical.CaptureHash(f.Values,f.Definitions),"definition duplicates persisted");});
        Test("zero_false_and_metric_values_are_preserved",()=>{var f=new Fixture();f.Set("land","规划净用地投影面积","0","Area","Double",0);var b=f.Check();var a=b.Fields.Single(x=>x.Property=="投影面积");Assert(a.Value=="0"&&a.SuggestedValue==null&&a.MetricValue==0,"zero overwritten");});
        Test("manual_floor_area_synchronizes_regions_in_same_plan",()=>{var floor=Element("floor",1,"IfcBuildingStorey");floor.LevelElevationM=0;floor.LevelUniqueId="floor";var a=Element("a",2,"IfcSlab","建筑区域","floor",20);var b=Element("b",3,"IfcSlab","建筑区域","floor",30);
            var f=new Fixture([floor,a,b],new ModelSettings{RuleProfileId=PlatformRules.ProfileId,LevelsConfirmed=true,BuildingLevelUniqueIds=["floor"]},kind:"单体建筑");var result=f.Check();var field=result.Fields.Single(x=>x.ElementUniqueId=="floor"&&x.Property=="楼层面积");var plan=f.Edit(result,[Draft(field,"200")],false);
            var regions=plan.Changes.Where(c=>c.Source=="建筑区域投影面积").ToArray();Assert(regions.Length==2&&regions.Select(c=>decimal.Parse(c.Value)).Sum()==200,"manual floor not allocated");Assert(regions.All(c=>c.Unit=="m²"),"area units missing");});
        Test("overlap_only_blocks_confirmed_building_regions_on_same_level",()=>{var a=Element("a",1,"IfcSpace","建筑区域","f",20);var b=Element("b",2,"IfcSpace","室内停车场","f",30);a.ProjectionOverlaps=[new(){OtherUniqueId="b",OverlapM2=10}];var f=new Fixture([a,b],kind:"单体建筑");Assert(!f.Check().CalculationIssues.Any(s=>s.Contains("投影重叠")),"non building region blocked aggregation");});
        Test("control_line_required_values_cannot_be_overridden",()=>{var f=new Fixture([Element("net-control",1,"IfcSlab","规划控制线")]);f.Inventory[0].Elements[0].Name="净用地控制红线";f.Inventory[0].Context.SnapshotHash=f.Inventory[0].PageHash=Canonical.InventoryHash(f.Inventory[0].Elements);var b=f.Check();var field=b.Fields.FirstOrDefault(x=>!string.IsNullOrEmpty(x.RequiredValue));Assert(field!=null,"control required policy missing");Reject(()=>f.Edit(b,[Draft(field,"任意")],false));});
        Test("legacy_boolean_migration_is_typed_and_binds_every_owner",()=>{var f=new Fixture([Element("building",1,"IfcBuilding")],kind:"单体建筑");var b=f.Check();var field=b.Fields.First(x=>x.RawType=="Boolean"&&BooleanPolicy.LegacySource(x.Source)==null);var value=f.Set("building",field.Source,"是");f.Definitions=[new(){Source=field.Source,ParameterId=88,Guid=ReferencePolicy.SourceGuid(field.Source).ToString("D"),DefinitionType="Text",Origin="instance",CategoryIds=[-2000032],Values=[value]}];b=f.Check();field=b.Fields.Single(x=>x.Key==field.Key);var plan=f.Edit(b,[Draft(field,"0")],false);var op=plan.Native.Definitions.Single(x=>x.Source==field.Source);Assert(op.DefinitionType=="YesNo"&&op.ReplaceParameterId==88&&op.MigrationValues.Single().Value=="0"&&op.Guid==BooleanPolicy.SourceGuid(field.Source).ToString("D"),"unsafe migration");});
        Test("boolean_definition_dependencies_and_foreign_text_block",()=>{var f=new Fixture([Element("building",1,"IfcBuilding")],kind:"单体建筑");var field=f.Check().Fields.First(x=>x.RawType=="Boolean"&&BooleanPolicy.LegacySource(x.Source)==null);var value=f.Set("building",field.Source,"1");f.Definitions=[new(){Source=field.Source,ParameterId=88,Guid=Guid.NewGuid().ToString("D"),DefinitionType="Text",Origin="instance",Values=[value]}];var b=f.Check();Reject(()=>f.Edit(b,[Draft(b.Fields.Single(x=>x.Key==field.Key),"0")],false));f.Definitions[0].Guid=value.SharedGuid;f.Definitions[0].Dependencies=["明细表"];b=f.Check();Reject(()=>f.Edit(b,[Draft(b.Fields.Single(x=>x.Key==field.Key),"0")],false));});
        Test("numeric_zero_one_never_retypes_real_density",()=>{var f=new Fixture();var field=f.Check().Fields.FirstOrDefault(x=>x.RawType=="Real");var b=f.Check();var plan=f.Edit(b,[Draft(field,"1")],false);Assert(plan.Native.Definitions.All(x=>x.DefinitionType!="YesNo"),"real inferred as boolean");});
        Test("settings_and_definition_tampering_invalidates_edit",()=>{var f=new Fixture();var b=f.Check();b.Settings.CommonValues["bad"]="changed";Reject(()=>f.Edit(b));});
        Test("exports_use_metric_saved_values_and_owner_routes",()=>{var f=new Fixture();f.Set("land","规划净用地投影面积","1076.39104167","Area","Double",100);var b=f.Check();var plan=f.Engine.BuildExportPlan(b,f.Values,Export(b),DateTime.UtcNow);Assert(plan.TemporaryChanges.Any(c=>c.Value=="100")&&plan.Routes.Any(r=>r.OwnerUniqueIds.Contains("land")),"metric conversion/routes missing");Assert(plan.Options["FileVersion"]=="IFC4DTV"&&!plan.Options.ContainsKey("ElementsForExport")&&plan.Hash==Canonical.ExportHash(plan),"not full native export");});
        Test("unsubmitted_materials_and_server_paths_block_export",()=>{var f=new Fixture();var b=f.Check();var request=Export(b);request.HasPendingMaterials=true;Reject(()=>f.Engine.BuildExportPlan(b,f.Values,request,DateTime.UtcNow));request.HasPendingMaterials=false;request.FileName="C:\\secrets\\target.ifc";Reject(()=>f.Engine.BuildExportPlan(b,f.Values,request,DateTime.UtcNow));});
        Test("workbook_template_previews_without_writing_user_source_path",()=>{var f=new Fixture([Element("building",1,"IfcBuilding")],kind:"单体建筑");var b=f.Check();var bytes=f.Engine.CreateWorkbookTemplate(b);var request=new MaterialPreviewRequest{Context=b.Context,Kind="workbook",FileName="Z:\\never-access\\book.xlsx",ContentBase64=Convert.ToBase64String(bytes)};var preview=f.Engine.PreviewMaterial(b,request);Assert(preview.Tables.Length==1&&preview.Changes.Where(c=>string.IsNullOrEmpty(c.Value)).All(c=>!c.CanSelect)&&preview.DataJson.Contains("never-access"),"template/provenance/blank policy wrong");var plan=f.Edit(b,suggestions:false,materials:[new(){Request=request,Preview=preview,Decision=new(){PreviewId=preview.Id,Accepted=true,RetainReferences=true}}]);Assert(plan.Native.DataWrites.Length==2&&plan.Native.DataWrites.Count(w=>w.SchemaId=="fbdf2a40-b13c-4a9a-aeda-525e010a4785")==1&&plan.Native.DataWrites.Count(w=>w.SchemaId==ModelStorageSchemas.WebSettingsId)==1,"Local archive schema not used");});
        Test("workbook_archive_preserves_old_unrelated_entries",()=>{var old="[{\"Import\":{\"TableId\":\"old\",\"TargetBuildingUniqueId\":\"other\",\"References\":[]},\"Extension\":\"preserve\"}]";var f=new Fixture([Element("building",1,"IfcBuilding")],data:[new(){SchemaId="fbdf2a40-b13c-4a9a-aeda-525e010a4785",SchemaName="HifcReportingWorkbookArchiveV1",Json=old,Hash=Canonical.Hash(old)}],kind:"单体建筑");var b=f.Check();var request=new MaterialPreviewRequest{Context=b.Context,Kind="workbook",FileName="C:\\book.xlsx",ContentBase64=Convert.ToBase64String(f.Engine.CreateWorkbookTemplate(b))};var preview=f.Engine.PreviewMaterial(b,request);var plan=f.Edit(b,suggestions:false,materials:[new(){Request=request,Preview=preview,Decision=new(){PreviewId=preview.Id,Accepted=true}}]);Assert(plan.Native.DataWrites.Single(w=>w.SchemaId=="fbdf2a40-b13c-4a9a-aeda-525e010a4785").Json.Contains("preserve")&&plan.Native.DataWrites.Single(w=>w.SchemaId=="fbdf2a40-b13c-4a9a-aeda-525e010a4785").ExpectedHash==Canonical.Hash(old),"archive lost unrelated record");});
        Test("upload_rejects_zip_traversal_and_dtd",()=>{var f=new Fixture();var b=f.Check();byte[] Zip(string name,string xml){using var ms=new MemoryStream();using(var zip=new ZipArchive(ms,ZipArchiveMode.Create,true)){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(xml);}return ms.ToArray();}
            Reject(()=>f.Engine.PreviewMaterial(b,new(){Context=b.Context,Kind="workbook",FileName="a.xlsx",ContentBase64=Convert.ToBase64String(Zip("../evil.xml","<x/>"))}));
            try{f.Engine.PreviewMaterial(b,new(){Context=b.Context,Kind="workbook",FileName="a.xlsx",ContentBase64=Convert.ToBase64String(Zip("xl/workbook.xml","<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///secret'>]><x>&a;</x>"))});throw new Exception("DTD accepted");}catch(System.Xml.XmlException){} });
        Test("redline_data_requires_confirmation_and_retains_local_schema",()=>{var f=new Fixture();var b=f.Check();var data=new RedlineData{Accepted=true,Unit=RedlineUnit.Meters,Frame=RedlineFrame.Internal,Axes=RedlineAxes.XEastYNorth,Points=[new(){Sequence=1,X=0,Y=0},new(){Sequence=2,X=10,Y=0},new(){Sequence=3,X=10,Y=10},new(){Sequence=4,X=0,Y=10}]};var req=new MaterialPreviewRequest{Context=b.Context,Kind="redline",DataJson=JsonSerializer.Serialize(data)};var preview=f.Engine.PreviewMaterial(b,req);Assert(preview.Issues.Length>0,"missing geometry comparison passed");var plan=f.Edit(b,suggestions:false,materials:[new(){Request=req,Preview=preview,Decision=new(){PreviewId=preview.Id,Accepted=true}}]);Assert(plan.Native.DataWrites.Count(w=>w.SchemaId=="b238a635-91f4-46cc-ac56-f41b7adac784")==1,"redline schema changed");data.Accepted=false;req.DataJson=JsonSerializer.Serialize(data);preview=f.Engine.PreviewMaterial(b,req);Reject(()=>f.Edit(b,suggestions:false,materials:[new(){Request=req,Preview=preview,Decision=new(){PreviewId=preview.Id,Accepted=true}}]));});
        Test("boolean_alias_copies_unselected_category_owners",()=>{
            var f=new Fixture([Element("site",1,"IfcSlab","总平构件"),Element("unselected",2,"IfcSlab")]);
            var one=f.Set("site","建筑密度","1","Number","Double");var two=f.Set("unselected","建筑密度","1","Number","Double");
            f.Definitions=[new(){Source="建筑密度",Guid=ReferencePolicy.SourceGuid("建筑密度").ToString("D"),ParameterId=71,DefinitionType="Number",Origin="instance",CategoryIds=[-2000032],Values=[one,two]}];
            var b=f.Check();var field=b.Fields.Single(x=>x.ElementUniqueId=="site"&&x.Source=="总平构件建筑密度");
            var plan=f.Edit(b,[Draft(field,"0")],false);var operation=plan.Native.Definitions.Single(d=>d.Source==field.Source);
            Assert(operation.ReplaceParameterId==null&&operation.MigrationValues.Length==2&&operation.MigrationValues.Single(v=>v.OwnerUniqueId=="unselected").Value=="1"&&operation.MigrationValues.All(v=>v.Source=="建筑密度"),"unselected legacy true lost");
        });
        Test("control_slab_export_uses_proxy_temporary_overrides",()=>{
            var f=new Fixture([Element("control",1,"IfcSlab","规划控制线")]);var b=f.Check();var plan=f.Engine.BuildExportPlan(b,f.Values,Export(b),DateTime.UtcNow);
            Assert(plan.TemporaryChanges.Any(c=>c.Source=="IfcExportAs"&&c.Value=="IfcBuildingElementProxy")&&plan.TemporaryChanges.Any(c=>c.Source=="IfcExportType"&&c.Value=="NOTDEFINED"),"control slab still exports as floor");
            Assert(plan.Routes.All(r=>r.Entity=="IfcBuildingElementProxy"&&!r.ExactOwnerFilter),"control routing wrong");
        });
        Test("window_routes_are_exact_and_do_not_duplicate_tsv",()=>{
            var f=new Fixture([Element("window",1,"IfcWindow","窗")],kind:"单体建筑");var b=f.Check();var req=Export(b);req.FileName="OUT_测试_报规_综合楼_20261029_V1.ifc";var plan=f.Engine.BuildExportPlan(b,f.Values,req,DateTime.UtcNow);
            Assert(plan.Routes.Length>0&&plan.Routes.All(r=>r.ExactOwnerFilter)&&!plan.MappingText.Contains("Pset_窗信息属性集"),"window route duplicates or leaks");
        });
        Test("multi_sheet_repreview_stable_ids_and_combined_archive_write",()=>{
            var a=Element("a",1,"IfcBuilding");a.Building="A";var bld=Element("b",2,"IfcBuilding");bld.Building="B";var f=new Fixture([a,bld],kind:"单体建筑");var b=f.Check();
            var content=Convert.ToBase64String(f.Engine.CreateWorkbookTemplate(b));var req=new MaterialPreviewRequest{Context=b.Context,Kind="workbook",FileName="C:\\two.xlsx",ContentBase64=content};var first=f.Engine.PreviewMaterial(b,req);
            Assert(first.Tables.Length==2&&first.Changes.Length==0,"multi table chose silently");
            var resolved=first.Tables.Select(table=>{var request=new MaterialPreviewRequest{Context=b.Context,Kind="workbook",FileName=req.FileName,ContentBase64=content,TableId=table.Id};var preview=f.Engine.PreviewMaterial(b,request);Assert(preview.References.Length>0,"temp path changed TableId");return new ResolvedMaterialDecision{Request=request,Preview=preview,Decision=new(){Accepted=true,PreviewId=preview.Id}};}).ToArray();
            var plan=f.Edit(b,suggestions:false,materials:resolved);var write=plan.Native.DataWrites.Single(w=>w.SchemaId=="fbdf2a40-b13c-4a9a-aeda-525e010a4785");Assert(JsonDocument.Parse(write.Json).RootElement.GetArrayLength()==2,"multiple workbook decisions did not merge");
        });
        Test("workbook_conflict_requires_selected_key_and_duplicate_target_rejects",()=>{
            var f=new Fixture([Element("building",1,"IfcBuilding")],kind:"单体建筑");var b=f.Check();var field=b.Fields.Single(x=>x.Property=="建筑面积");
            f.Set("building",field.Source,"70");b=f.Check();
            var path=Path.Combine(Path.GetTempPath(),"hifc-rule-conflict-"+Guid.NewGuid().ToString("N")+".xlsx");
            try {
                File.WriteAllBytes(path,f.Engine.CreateWorkbookTemplate(b));
                using(var zip=ZipFile.Open(path,ZipArchiveMode.Update)){
                    var entry=zip.GetEntry("xl/worksheets/sheet1.xml");System.Xml.Linq.XDocument doc;using(var stream=entry.Open())doc=System.Xml.Linq.XDocument.Load(stream);
                    System.Xml.Linq.XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    var row=doc.Descendants(ns+"row").Single(r=>r.Descendants(ns+"t").Any(t=>t.Value=="建筑面积"));var cell=row.Elements(ns+"c").First(c=>((string)c.Attribute("r")).StartsWith("C"));cell.RemoveNodes();cell.SetAttributeValue("t","n");cell.Add(new System.Xml.Linq.XElement(ns+"v","60"));entry.Delete();using var writer=zip.CreateEntry("xl/worksheets/sheet1.xml").Open();doc.Save(writer);
                }
                var req=new MaterialPreviewRequest{Context=b.Context,Kind="workbook",FileName="C:\\conflict.xlsx",ContentBase64=Convert.ToBase64String(File.ReadAllBytes(path))};var preview=f.Engine.PreviewMaterial(b,req);var change=preview.Changes.Single(c=>c.Property=="建筑面积");Assert(change.IsConflict&&!change.Selected&&change.CanSelect,"conflict preselected or hidden");
                var decision=new ResolvedMaterialDecision{Request=req,Preview=preview,Decision=new(){Accepted=true,PreviewId=preview.Id}};Assert(f.Edit(b,suggestions:false,materials:[decision]).Changes.Length==0,"unselected conflict overwritten");
                decision.Decision.SelectedKeys=[change.Key];Assert(f.Edit(b,suggestions:false,materials:[decision]).Changes.Single(c=>c.Source==field.Source).Value=="60","explicit selection ignored");
                Reject(()=>f.Edit(b,[Draft(b.Fields.Single(x=>x.Key==field.Key),"80")],false,[decision]));
            } finally {File.Delete(path);}
        });
        Test("element_reference_metric_only_allowed_for_window_elevation",()=>{
            var f=new Fixture([Element("window",1,"IfcWindow","窗")],kind:"单体建筑");var fields=f.Check().Fields;var elevation=fields.Single(x=>x.Property=="标高");var width=fields.Single(x=>x.Property=="宽度");
            f.Set("window",elevation.Source,"99","Invalid","ElementId",3.5);f.Set("window",width.Source,"99","Invalid","ElementId",3.5);var b=f.Check();
            Assert(b.Fields.Single(x=>x.Property=="标高").MetricValue==3.5&&b.Fields.Single(x=>x.Property=="标高").State=="有效","level elevation lost");
            Assert(b.Fields.Single(x=>x.Property=="宽度").State=="类型待核验"&&b.Fields.Single(x=>x.Property=="宽度").MetricValue==null,"level reference became width");
        });
        Test("current_upload_sources_are_signed_without_historical_source_dependencies",()=>{
            var f=new Fixture([Element("building",1,"IfcBuilding")],kind:"单体建筑");var b=f.Check();var request=new MaterialPreviewRequest{Context=b.Context,Kind="workbook",FileName="book.xlsx",SourcePath="Z:\\actual-original\\book.xlsx",ContentBase64=Convert.ToBase64String(f.Engine.CreateWorkbookTemplate(b))};var preview=f.Engine.PreviewMaterial(b,request);
            var plan=f.Edit(b,suggestions:false,materials:[new(){Request=request,Preview=preview,Decision=new(){PreviewId=preview.Id,Accepted=true}}]);var source=plan.Native.DataWrites.Single(w=>w.SchemaId=="fbdf2a40-b13c-4a9a-aeda-525e010a4785").SourceFiles.Single();
            Assert(source.Path==request.SourcePath&&source.Sha256==preview.Sha256&&preview.Context==b.Context,"source provenance lost");var hash=plan.Hash;source.Path="Z:\\changed.xlsx";Assert(hash!=Canonical.PlanHash(plan),"source precondition not signed");
        });
        Test("redline_compares_current_double_carriers_and_honors_minimum_gap",()=>{
            var land=Element("land",1,"IfcSlab","规划净用地");var control=Element("control",2,"IfcSlab","规划控制线");land.GeometryFingerprint="land-geometry";control.GeometryFingerprint="control-geometry";
            var f=new Fixture([land,control]);var b=f.Check();foreach(var field in b.Fields.Where(x=>x.ElementUniqueId=="control"&&(x.Property=="名称"||x.Property=="红线类型")))f.Set("control",field.Source,"净用地控制红线");b=f.Check();
            NativePoint3[] Square()=>[new(){X=0,Y=0},new(){X=10,Y=0},new(){X=10,Y=10},new(){X=0,Y=10}];
            var data=new RedlineData{Accepted=true,Unit=RedlineUnit.Meters,Frame=RedlineFrame.Internal,Axes=RedlineAxes.XEastYNorth,Points=Square().Select((p,i)=>new RedlinePoint{Sequence=i+1,X=p.X,Y=p.Y}).ToArray()};
            var req=new MaterialPreviewRequest{Context=b.Context,Kind="redline",DataJson=JsonSerializer.Serialize(data),Evidence=new(){Carriers=[new(){ElementId=1,UniqueId="land",GeometryFingerprint="land-geometry",TopInternal=Square(),BottomInternal=Square(),BottomM=1,TopM=2,HorizontalLinearFaces=true},new(){ElementId=2,UniqueId="control",GeometryFingerprint="control-geometry",TopInternal=Square(),BottomInternal=Square(),BottomM=0,TopM=.9995,HorizontalLinearFaces=true}],Pairs=[new(){LeftUniqueId="land",RightUniqueId="control",IntersectionVolumeM3=0}]} };
            var preview=f.Engine.PreviewMaterial(b,req);Assert(preview.References.Single(r=>r.Field=="双载体红线").Status=="不通过","submillimeter gap falsely passed default .001");req.MinimumGapM=0;preview=f.Engine.PreviewMaterial(b,req);Assert(preview.References.All(r=>r.Status=="通过"),"confirmed gap override/manual comparison not applied");
            req.Evidence.Carriers[0].GeometryFingerprint="stale";Reject(()=>f.Engine.PreviewMaterial(b,req));
        });
        Test("existing_and_new_net_land_cannot_share_projection",()=>{
            var existing=Element("existing",1,"IfcSlab","规划净用地");var next=Element("next",2,"IfcSlab");existing.ProjectionKey=next.ProjectionKey="same-land";next.AssignedSlot="规划净用地";
            var f=new Fixture([existing,next]);var basis=f.Check();
            try {f.Edit(basis,suggestions:false);throw new Exception("duplicate land signed");}
            catch(InvalidOperationException ex){Assert(ex.Message.Contains("规划净用地重复投影"),"duplicate-land Core policy did not reject: "+ex.Message);}
        });
        Test("two_new_net_land_classifications_cannot_share_projection",()=>{
            var first=Element("first",1,"IfcSlab");var second=Element("second",2,"IfcSlab");first.ProjectionKey=second.ProjectionKey="same-land";first.AssignedSlot=second.AssignedSlot="规划净用地";
            var f=new Fixture([first,second]);var basis=f.Check();
            try {f.Edit(basis,suggestions:false);throw new Exception("two duplicate new lands signed");}
            catch(InvalidOperationException ex){Assert(ex.Message.Contains("规划净用地重复投影"),"duplicate-land Core policy did not reject: "+ex.Message);}
        });
        Test("building_overlap_threshold_matches_local_ten_microsquaremeters",()=>{
            var first=Element("first",1,"IfcSlab","建筑区域","floor",10);var second=Element("second",2,"IfcSlab","建筑区域","floor",20);
            first.ProjectionOverlaps=[new(){OtherUniqueId="second",OverlapM2=.000005}];var f=new Fixture([first,second],kind:"单体建筑");
            Assert(!f.Check().CalculationIssues.Any(s=>s.Contains("投影重叠")),"below Local 1e-5 threshold is incorrectly blocked");
            first.ProjectionOverlaps[0].OverlapM2=.00002;f=new Fixture([first,second],kind:"单体建筑");Assert(f.Check().CalculationIssues.Any(s=>s.Contains("投影重叠")),"above Local threshold not reported");
        });
        Test("manual_length_draft_uses_canonical_meters_once",()=>{
            var f=new Fixture([Element("building",1,"IfcBuilding")],kind:"单体建筑");var field=f.Check().Fields.First(x=>PropertyUnits.Canonical(x.Property,x.RawType)=="m");
            f.Set("building",field.Source,"10","Length","Double",3.048);var basis=f.Check();field=basis.Fields.Single(x=>x.Key==field.Key);
            Assert(field.InputUnit=="m","length input unit missing");var change=f.Edit(basis,[Draft(field,"4.2")],false).Changes.Single(c=>c.Source==field.Source);
            Assert(change.Value=="4.2"&&change.Unit=="m"&&change.ExpectedValue=="10"&&change.DefinitionType=="Length","canonical draft converted or baseline overwritten");
        });
        Test("manual_area_number_storage_keeps_canonical_business_quantity",()=>{
            var f=new Fixture();var field=f.Check().Fields.Single(x=>x.Property=="投影面积");f.Set("land",field.Source,"70","Number","Double");
            var basis=f.Check();field=basis.Fields.Single(x=>x.Key==field.Key);Assert(field.InputUnit=="m²","area input unit missing for Number storage");
            var change=f.Edit(basis,[Draft(field,"123.45")],false).Changes.Single(c=>c.Source==field.Source);
            Assert(change.Value=="123.45"&&change.Unit=="m²"&&change.DefinitionType=="Number","Number business area was retyped or scaled");
        });
        Test("dimensionless_manual_input_does_not_acquire_length_or_area_units",()=>{
            var f=new Fixture([Element("region",1,"IfcSlab","建筑区域","floor",50)],kind:"单体建筑");var basis=f.Check();var field=basis.Fields.Single(x=>x.Property=="计容系数");
            Assert(field.InputUnit==null,"dimensionless field acquired units");var change=f.Edit(basis,[Draft(field,"1.5")],false).Changes.Single(c=>c.Source==field.Source);Assert(change.Value=="1.5"&&change.Unit==null,"dimensionless draft changed");
        });
        Test("evaluate_retains_complete_definition_chunks_in_capture_hash",()=>{
            var f=new Fixture();var values=Enumerable.Range(1,300).Select(i=>new ParameterValue{OwnerId=i,OwnerUniqueId="outside-"+i,Source="类目",Origin="instance",Exists=true,HasValue=true,RawValue="value-"+i,StorageType="String",DefinitionType="Text"}).ToArray();
            var definition=new NativeDefinitionSnapshot{Source="类目",ParameterId=99,Guid=ReferencePolicy.SourceGuid("类目").ToString("D"),DefinitionType="Text",Origin="instance",CategoryIds=[-2000032],Values=values};definition.Hash=Canonical.DefinitionHash(definition);
            var header=new NativeDefinitionSnapshot{Source=definition.Source,ParameterId=definition.ParameterId,Guid=definition.Guid,DefinitionType=definition.DefinitionType,Origin=definition.Origin,CategoryIds=definition.CategoryIds,Hash=definition.Hash};
            var pages=new[]{new CapturePage{Context=f.Plan.Context,PageCount=2,PageIndex=0,TotalValues=f.Values.Length,Values=f.Values,PageHash=Canonical.ValuesHash(f.Values),DefinitionChunks=[new(){Header=header,ValueOffset=0,TotalValues=300,Values=values.Take(150).ToArray()}]},new CapturePage{Context=f.Plan.Context,PageCount=2,PageIndex=1,TotalValues=f.Values.Length,PageHash=Canonical.ValuesHash([]),DefinitionChunks=[new(){Header=header,ValueOffset=150,TotalValues=300,Values=values.Skip(150).ToArray()}]}};
            var basis=f.Engine.Evaluate(f.Inventory,f.Plan,pages);Assert(basis.Definitions.Single().Values.Length==300&&basis.CaptureHash==Canonical.CaptureHash(f.Values,[definition]),"outside owners were lost while assembling definitions");
        });
        Test("canonical_area_matching_internal_raw_number_is_not_a_noop",()=>{
            var f=new Fixture();var field=f.Check().Fields.Single(x=>x.Property=="投影面积");f.Set("land",field.Source,"100","Area","Double",9.290304);
            var basis=f.Check();field=basis.Fields.Single(x=>x.Key==field.Key);var plan=f.Edit(basis,[Draft(field,"100")],false);
            Assert(plan.Changes.Any(c=>c.Source==field.Source&&c.Value=="100"&&c.Unit=="m²"&&c.ExpectedValue=="100"),"100 m² incorrectly compared equal to 100 sqft");
        });
        Test("canonical_length_matching_internal_raw_number_is_not_a_noop",()=>{
            var f=new Fixture([Element("building",1,"IfcBuilding")],kind:"单体建筑");var field=f.Check().Fields.First(x=>PropertyUnits.Canonical(x.Property,x.RawType)=="m");f.Set("building",field.Source,"10","Length","Double",3.048);
            var basis=f.Check();field=basis.Fields.Single(x=>x.Key==field.Key);var plan=f.Edit(basis,[Draft(field,"10")],false);
            Assert(plan.Changes.Any(c=>c.Source==field.Source&&c.Value=="10"&&c.Unit=="m"&&c.ExpectedValue=="10"),"10 m incorrectly compared equal to 10 ft");
        });
        Test("same_canonical_metric_and_number_quantity_remain_noops",()=>{
            foreach(var definition in new[]{"Area","Number"}) {
                var f=new Fixture();var field=f.Check().Fields.Single(x=>x.Property=="投影面积");f.Set("land",field.Source,"100",definition,"Double",definition=="Area"?9.290304:null);
                var basis=f.Check();field=basis.Fields.Single(x=>x.Key==field.Key);var plan=f.Edit(basis,[Draft(field,definition=="Area"?"9.290304":"100")],false);
                Assert(plan.Changes.Length==0,"unchanged physical quantity produced a write: "+definition);
            }
        });
        Test("zero_and_native_false_remain_noops",()=>{
            var f=new Fixture();var area=f.Check().Fields.Single(x=>x.Property=="投影面积");f.Set("land",area.Source,"0","Area","Double",0);var basis=f.Check();Assert(f.Edit(basis,[Draft(basis.Fields.Single(x=>x.Key==area.Key),"0")],false).Changes.Length==0,"zero area changed");
            f=new Fixture([Element("building",1,"IfcBuilding")],kind:"单体建筑");var boolean=f.Check().Fields.First(x=>x.RawType=="Boolean");f.Set("building",boolean.Source,"0","YesNo","Integer");basis=f.Check();Assert(f.Edit(basis,[Draft(basis.Fields.Single(x=>x.Key==boolean.Key),"0")],false).Changes.Length==0,"false changed");
        });
        Test("first_write_persists_complete_model_settings_without_property_changes",()=>{
            var settings=new ModelSettings{RuleProfileId=PlatformRules.ProfileId,Roles=[new(){UniqueId="land",Slot="规划净用地",Identity="confirmed-identity"}],CommonValues=new(){["建筑区域|计容系数"]="0",["建筑技术|建筑用途名称"]="中学教学楼"},
                LevelsConfirmed=true,BuildingLevelUniqueIds=["base","upper"],TopBoundaryLevelUniqueId="roof",Underground=new(){Confirmed=true,GroundElevationM=.5,LevelUniqueIds=["basement"],BaseLevelUniqueId="base"}};
            var f=new Fixture(settings:settings);var basis=f.Check();var plan=f.Edit(basis,suggestions:false);var write=plan.Native.DataWrites.Single(w=>w.SchemaId==ModelStorageSchemas.WebSettingsId);
            Assert(plan.Changes.Length==0&&write.SchemaName==ModelStorageSchemas.WebSettingsName&&write.ExpectedHash==Canonical.Hash("")&&write.Hash==Canonical.Hash(write.Json)&&write.SourceFiles.Length==0,"first settings save not isolated/signed");
            var restored=JsonSerializer.Deserialize<ModelSettings>(write.Json);Assert(Canonical.ConfigurationHash(restored,[])==Canonical.ConfigurationHash(settings,[]),"levels/underground/common values/roles not retained");
            Assert(JsonDocument.Parse(write.Json).RootElement.TryGetProperty("RuleProfileId",out _),"settings payload is not direct ModelSettings");
        });
        Test("same_model_settings_do_not_repeat_data_write_even_when_dictionary_order_changes",()=>{
            var settings=new ModelSettings{RuleProfileId=PlatformRules.ProfileId,CommonValues=new(){["z"]="0",["a"]="保留"}};var first=new Fixture(settings:settings);var saved=first.Edit(first.Check(),suggestions:false).Native.DataWrites.Single(w=>w.SchemaId==ModelStorageSchemas.WebSettingsId);
            var reordered=JsonSerializer.Deserialize<ModelSettings>(saved.Json);reordered.CommonValues=reordered.CommonValues.Reverse().ToDictionary(p=>p.Key,p=>p.Value);
            var current=new Fixture(settings:reordered,data:[new(){SchemaId=saved.SchemaId,SchemaName=saved.SchemaName,Json=saved.Json,Hash=saved.Hash}]);
            Assert(current.Edit(current.Check(),suggestions:false).Native.DataWrites.All(w=>w.SchemaId!=ModelStorageSchemas.WebSettingsId),"same settings re-written due to serialization ordering");
        });
        Test("changed_model_settings_bind_saved_hash_and_stale_snapshot_cannot_sign",()=>{
            var settings=new ModelSettings{RuleProfileId=PlatformRules.ProfileId};var first=new Fixture(settings:settings);var saved=first.Edit(first.Check(),suggestions:false).Native.DataWrites.Single(w=>w.SchemaId==ModelStorageSchemas.WebSettingsId);
            settings.CommonValues["建筑技术|建筑名称"]="新名称";var record=new ProjectDataRecord{SchemaId=saved.SchemaId,SchemaName=saved.SchemaName,Json=saved.Json,Hash=saved.Hash};var current=new Fixture(settings:settings,data:[record]);var basis=current.Check();var plan=current.Edit(basis,suggestions:false);
            var write=plan.Native.DataWrites.Single(w=>w.SchemaId==ModelStorageSchemas.WebSettingsId);Assert(write.ExpectedHash==saved.Hash&&write.Hash!=saved.Hash,"saved settings precondition missing");var signedHash=plan.Hash;write.ExpectedHash="tampered";Assert(Canonical.PlanHash(plan)!=signedHash,"settings precondition not covered by signature");
            record.Json="{}";record.Hash=Canonical.Hash(record.Json);Reject(()=>current.Edit(basis,suggestions:false));
        });
        Test("different_window_types_share_one_compatible_pset_route",()=>{
            var first=Element("window-a",1,"IfcWindow","窗");var second=Element("window-b",2,"IfcWindow","窗");var f=new Fixture([first,second],new ModelSettings(),kind:"单体建筑");
            var initial=f.Check();var width=initial.Fields.First(x=>x.PropertySet=="Pset_窗信息属性集"&&x.Property=="宽度");f.Set(first.TypeUniqueId,width.Source,"1");f.Set(second.TypeUniqueId,width.Source,"2");
            var basis=f.Check();var request=Export(basis);request.FileName="OUT_测试_报规_综合楼_20261029_V1.ifc";var plan=f.Engine.BuildExportPlan(basis,f.Values,request,DateTime.UtcNow);var routes=plan.Routes.Where(r=>r.PropertySet=="Pset_窗信息属性集").ToArray();
            Assert(routes.Length==1,"same type Pset still emits one native description per owner");var route=routes.Single();Assert(route.Origin=="type"&&route.ExactOwnerFilter&&route.OwnerUniqueIds.Order().SequenceEqual(new[]{first.TypeUniqueId,second.TypeUniqueId}.Order()),"merged window types lost or widened owners");
            var bridge=route.Properties.Single(p=>p.Property=="宽度").BridgeName;Assert(plan.TemporaryChanges.Where(c=>c.Source==bridge).Select(c=>c.Value).Order().SequenceEqual(new[]{"1","2"}),"merged route overwrote independent type values");Assert(route.Properties.Select(p=>p.Property).Distinct().Count()==route.Properties.Length,"property entries duplicated");
        });
        Test("platform_windows_share_one_exact_instance_route_across_types",()=>{
            var first=Element("window-a",1,"IfcWindow","窗");var second=Element("window-b",2,"IfcWindow","窗");var f=new Fixture([first,second],kind:"单体建筑");var basis=f.Check();var request=Export(basis);request.FileName="OUT_测试_报规_综合楼_20261029_V1.ifc";var plan=f.Engine.BuildExportPlan(basis,f.Values,request,DateTime.UtcNow);
            var routes=plan.Routes.Where(r=>r.PropertySet=="Pset_窗信息属性集").ToArray();Assert(routes.Length==1,"platform window Pset duplicated per instance");Assert(routes[0].Origin=="instance"&&routes[0].ExactOwnerFilter&&routes[0].OwnerUniqueIds.Order().SequenceEqual(new[]{first.UniqueId,second.UniqueId}.Order()),"exact instance owners widened to type owners");
        });
        Test("unclassified_window_sharing_exported_type_still_blocks_route_merge",()=>{
            var selected=Element("selected",1,"IfcWindow","窗");var unclassified=Element("unclassified",2,"IfcWindow");unclassified.TypeId=selected.TypeId;unclassified.TypeUniqueId=selected.TypeUniqueId;
            var f=new Fixture([selected,unclassified],new ModelSettings(),kind:"单体建筑");var basis=f.Check();var request=Export(basis);request.FileName="OUT_测试_报规_综合楼_20261029_V1.ifc";
            try {f.Engine.BuildExportPlan(basis,f.Values,request,DateTime.UtcNow);throw new Exception("shared unclassified window type was exported");}
            catch(InvalidOperationException ex){Assert(ex.Message.Contains("未确认的同类型实例"),"shared type scope not rejected by intended policy: "+ex.Message);}
        });
        Console.WriteLine($"RESULT failures={failures}; tests={tests}");Environment.ExitCode=failures==0?0:1;
    }
}
