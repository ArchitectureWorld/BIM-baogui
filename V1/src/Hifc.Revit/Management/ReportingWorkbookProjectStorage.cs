using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class ReportingWorkbookArchiveEntry
    {
        public ReportingWorkbookDecision Import {get;set;}
        public string WrittenUtc {get;set;}
        public EditChange[] AppliedChanges {get;set;}=new EditChange[0];
    }
    internal static class ReportingWorkbookProjectStorage
    {
        static readonly Guid SchemaId=new Guid("fbdf2a40-b13c-4a9a-aeda-525e010a4785");
        const string Payload="Json";
        static JavaScriptSerializer Json()=>new JavaScriptSerializer{MaxJsonLength=64*1024*1024};
        static Schema GetSchema(bool create)
        {
            var schema=Schema.Lookup(SchemaId);if(schema!=null||!create)return schema;
            var builder=new SchemaBuilder(SchemaId);builder.SetSchemaName("HifcReportingWorkbookArchiveV1");
            builder.SetReadAccessLevel(AccessLevel.Public);builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(Payload,typeof(string));return builder.Finish();
        }
        internal static ReportingWorkbookArchiveEntry[] Load(Document document)
        {
            var schema=GetSchema(false);if(schema==null)return new ReportingWorkbookArchiveEntry[0];
            var entity=document.ProjectInformation.GetEntity(schema);if(!entity.IsValid())return new ReportingWorkbookArchiveEntry[0];
            var entries=Json().Deserialize<ReportingWorkbookArchiveEntry[]>(entity.Get<string>(Payload));
            if(entries==null||entries.Any(e=>e?.Import==null))throw new InvalidOperationException("模型报规资料格式损坏，未覆盖原记录。");
            return entries;
        }
        internal static void VerifySources(ReportingWorkbookDecision[] imports)
        {
            foreach(var source in imports.SelectMany(d=>d.SourceFiles).GroupBy(s=>s.Path,StringComparer.OrdinalIgnoreCase))
            {
                if(source.Select(s=>s.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1||!File.Exists(source.Key)||
                    !string.Equals(Evidence.FileHash(source.Key),source.First().Sha256,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Excel 来源已变更或不可读取，请重新导入，暂存内容保留："+source.Key);
            }
        }
        internal static void SaveInTransaction(Document document,ReportingWorkbookDecision[] imports,EditChange[] changes)
        {
            if(imports.Length==0)return;
            if(!document.IsModifiable)throw new InvalidOperationException("报规资料必须与属性在同一 Revit 写入事务内保存。");
            var entries=Load(document).ToList();
            foreach(var import in imports)
            {
                entries.RemoveAll(e=>e.Import.TableId==import.TableId&&e.Import.TargetBuildingUniqueId==import.TargetBuildingUniqueId);
                var keys=import.Changes.Select(c=>c.OwnerUniqueId+"|"+c.Source).ToArray();
                entries.Add(new ReportingWorkbookArchiveEntry {Import=import,WrittenUtc=DateTime.UtcNow.ToString("O"),
                    AppliedChanges=changes.Where(c=>keys.Contains(c.OwnerUniqueId+"|"+c.Source)).ToArray()});
            }
            var json=Json().Serialize(entries.ToArray());var schema=GetSchema(true);var entity=new Entity(schema);entity.Set<string>(Payload,json);
            document.ProjectInformation.SetEntity(entity);
            if(document.ProjectInformation.GetEntity(schema).Get<string>(Payload)!=json)throw new InvalidOperationException("报规资料事务内回读失败，整轮写入已撤销。");
        }
    }
}
