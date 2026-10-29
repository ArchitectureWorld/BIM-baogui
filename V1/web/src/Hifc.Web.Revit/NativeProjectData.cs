using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public static class NativeProjectData
    {
        // The existing Local archive schemas remain byte compatible.
        private static readonly string[][] Schemas = {
            new[] { "fbdf2a40-b13c-4a9a-aeda-525e010a4785", "HifcReportingWorkbookArchiveV1", "Json" },
            new[] { "b238a635-91f4-46cc-ac56-f41b7adac784", "HifcManualRedlineV1", "ManualRedlineJson" },
            new[] { ModelStorageSchemas.WebSettingsId, ModelStorageSchemas.WebSettingsName, ModelStorageSchemas.JsonField }
        };
        private static string[] Contract(string id, string name)
        {
            var value = Schemas.SingleOrDefault(s => s[0] == id && s[1] == name);
            return value ?? throw new InvalidOperationException("服务器资料计划包含不支持的模型 Schema。");
        }
        private static Schema GetSchema(string[] contract, bool create)
        {
            var schema = Schema.Lookup(new Guid(contract[0]));
            if (schema != null)
            {
                if (schema.SchemaName != contract[1] || schema.GetField(contract[2])?.ValueType != typeof(string))
                    throw new InvalidOperationException("模型资料 Schema 不兼容，已保留原资料。");
                return schema;
            }
            if (!create) return null;
            var builder = new SchemaBuilder(new Guid(contract[0])); builder.SetSchemaName(contract[1]);
            if (contract[0] == "b238a635-91f4-46cc-ac56-f41b7adac784" || contract[0] == ModelStorageSchemas.WebSettingsId) builder.SetVendorId("HIFC");
            builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(contract[2], typeof(string)); return builder.Finish();
        }
        public static ProjectDataRecord[] Read(Document document)
        {
            return Schemas.Select(contract => {
                var schema = GetSchema(contract, false); string json = null;
                if (schema != null) { var entity = document.ProjectInformation.GetEntity(schema); if (entity.IsValid()) json = entity.Get<string>(contract[2]); }
                return new ProjectDataRecord { SchemaId = contract[0], SchemaName = contract[1], Json = json, Hash = Canonical.Hash(json ?? "") };
            }).ToArray();
        }
        public static void Validate(Document document, NativeDataWrite[] writes)
        {
            if (writes == null || writes.GroupBy(w => w.SchemaId).Any(g => g.Count() != 1)) throw new InvalidOperationException("资料写入计划有重复 Schema。");
            var current = Read(document);
            foreach (var write in writes)
            {
                Contract(write.SchemaId, write.SchemaName);
                if (write.Json == null || write.Json.Length > 64 * 1024 * 1024 || write.Hash != Canonical.Hash(write.Json)
                    || write.ExpectedHash != current.Single(r => r.SchemaId == write.SchemaId).Hash)
                    throw new InvalidOperationException("模型资料已变化，请重新预览。");
                if (write.SchemaId == ModelStorageSchemas.WebSettingsId) NativeSettingsState.Decode(new ProjectDataRecord { SchemaId = write.SchemaId, SchemaName = write.SchemaName, Json = write.Json, Hash = write.Hash });
                foreach (var file in write.SourceFiles)
                {
                    if (string.IsNullOrWhiteSpace(file.Path) || !Path.IsPathRooted(file.Path) || !File.Exists(file.Path)) throw new InvalidOperationException("本轮资料来源文件无法读取，请重新导入。");
                    using (var stream = File.OpenRead(file.Path)) using (var sha = SHA256.Create())
                        if (!string.Equals(string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2"))), file.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("本轮资料来源文件已改变，请重新导入：" + Path.GetFileName(file.Path));
                }
            }
        }
        public static void Apply(Document document, NativeDataWrite[] writes)
        {
            if (!document.IsModifiable) throw new InvalidOperationException("资料必须与属性在同一个事务内保存。");
            Validate(document, writes);
            foreach (var write in writes)
            {
                var contract = Contract(write.SchemaId, write.SchemaName); var schema = GetSchema(contract, true);
                var entity = new Entity(schema); entity.Set(contract[2], write.Json); document.ProjectInformation.SetEntity(entity);
            }
            Verify(document, writes);
        }
        public static void Verify(Document document, NativeDataWrite[] writes)
        {
            var actual = Read(document);
            if (writes.Any(w => actual.Single(r => r.SchemaId == w.SchemaId).Json != w.Json))
                throw new InvalidOperationException("模型资料回读不一致，整轮修改已回滚。");
        }
    }
}
