using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    // The entity lives in this RVT's ProjectInformation, including Save As/copies.
    // No title/path-derived global JSON identity can leak data to another project.
    internal static class RedlineProjectStorage
    {
        internal static readonly Guid SchemaId=new Guid("b238a635-91f4-46cc-ac56-f41b7adac784");
        const string Payload="ManualRedlineJson";
        static Schema GetSchema(bool create)
        {
            var schema=Schema.Lookup(SchemaId);
            if(schema!=null) {
                if(schema.GetField(Payload)==null)throw new InvalidOperationException("模型内红线资料 Schema 与本版不兼容，已保留原资料。");
                return schema;
            }
            if(!create)return null;
            var builder=new SchemaBuilder(SchemaId);builder.SetSchemaName("HifcManualRedlineV1");builder.SetVendorId("HIFC");
            builder.SetReadAccessLevel(AccessLevel.Public);builder.SetWriteAccessLevel(AccessLevel.Public);builder.AddSimpleField(Payload,typeof(string));return builder.Finish();
        }
        internal static RedlineData Load(Document doc)
        {
            var schema=GetSchema(false);if(schema==null)return new RedlineData();
            var entity=doc.ProjectInformation.GetEntity(schema);
            return !entity.IsValid()?new RedlineData():RedlineDataPolicy.Deserialize(entity.Get<string>(Payload));
        }
        internal static void Save(Document doc,RedlineData data)
        {
            if(doc==null||!doc.IsValidObject||doc.IsReadOnly||doc.IsFamilyDocument)throw new InvalidOperationException("当前项目模型不可写，未保存红线人工资料。");
            if(string.IsNullOrEmpty(doc.PathName))throw new InvalidOperationException("请先另存项目 RVT，再保存本模型的人工红线资料。");
            var json=RedlineDataPolicy.Serialize(data);var schema=GetSchema(true);
            using(var group=new TransactionGroup(doc,"保存人工红线项目资料")) {
                group.Start();
                try {
                    using(var transaction=new Transaction(doc,"人工红线坐标及口径")) {
                        transaction.Start();var entity=new Entity(schema);entity.Set<string>(Payload,json);doc.ProjectInformation.SetEntity(entity);
                        if(doc.ProjectInformation.GetEntity(schema).Get<string>(Payload)!=json)throw new InvalidOperationException("红线资料事务内回读失败。");
                        if(transaction.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("红线资料事务未提交。");
                    }
                    var persisted=doc.ProjectInformation.GetEntity(schema).Get<string>(Payload);
                    if(persisted!=json)throw new InvalidOperationException("红线资料提交后回读失败。");
                    RedlineDataPolicy.Deserialize(persisted);
                    if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("红线资料事务组未提交。");
                }catch {if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
            }
        }
    }
}
