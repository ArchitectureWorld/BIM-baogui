using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExternalService;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public sealed class NativeExportInput
    {
        public string DocumentSessionId { get; set; }
        public string OutputDirectory { get; set; }
        public CheckResult Basis { get; set; }
        public CapturePlan Capture { get; set; }
        public ExportPlan Plan { get; set; }
        public ModelSettings Settings { get; set; }
        public IDictionary<string, string> AssignedSlots { get; set; }
    }
    public static class NativeExportExecutor
    {
        public static string OutputPath(string directory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName)
                || fileName != fileName.Trim() || fileName != Path.GetFileName(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !fileName.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase) || fileName.Length > 180
                || fileName.StartsWith(".", StringComparison.Ordinal) || fileName.EndsWith(".", StringComparison.Ordinal))
                throw new InvalidOperationException("请输入有效的单个 IFC 文件名。");
            var stem = Path.GetFileNameWithoutExtension(fileName).Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem))
                throw new InvalidOperationException("IFC 文件名不能使用系统保留名称。");
            var root = Path.GetFullPath(directory); var target = Path.GetFullPath(Path.Combine(root, fileName));
            if (!string.Equals(Path.GetDirectoryName(target).TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("IFC 输出路径越界。");
            if (File.Exists(target)) throw new InvalidOperationException("目标 IFC 已存在，请更改日期或版本，原文件保留。");
            return target;
        }
        public static ExportReceipt Execute(Document document, NativeExportInput input, PublicSigningKey key, DateTime nowUtc)
        {
            if (document == null || !document.IsValidObject || document.IsModifiable || document.IsReadOnly || document.IsFamilyDocument
                || input?.Basis == null || input.DocumentSessionId != input.Basis.Context.DocumentSessionId)
                throw new InvalidOperationException("当前模型不能导出，请重新全面检查。");
            var plan = input.Plan; WriteGuard.Export(plan, input.Basis.Context, key, nowUtc);
            if (plan.CaptureHash != input.Basis.CaptureHash || input.Capture == null || !input.Capture.InventoryComplete)
                throw new InvalidOperationException("导出计划不属于当前完整检查。");
            ProtocolGuard.Match(input.Basis.Context, input.Capture.Context);
            WriteGuard.Capture(input.Basis, NativeInventory.Read(document, input.AssignedSlots), NativeInventory.ReadValues(document, input.Capture.Targets), NativeInventory.ReadDefinitions(document, input.Capture.DefinitionSources));
            if (input.Basis.Context.ConfigurationHash != Canonical.ConfigurationHash(input.Settings, NativeProjectData.Read(document)))
                throw new InvalidOperationException("模型资料或计算设置已变化，请重新检查。");
            if (plan.ExpectedDataHash != Canonical.ProjectDataHash(NativeProjectData.Read(document))) throw new InvalidOperationException("导出资料快照不一致。");
            if (plan.Native.DataWrites.Length != 0) throw new InvalidOperationException("导出不提交资料，请先写入并复检。");
            NativeDefinitions.Validate(document, plan.Native.Definitions);
            if (plan.TemporaryChanges.GroupBy(c => c.OwnerUniqueId + "\n" + c.Source).Any(g => g.Count() != 1)) throw new InvalidOperationException("导出临时目标重复。");
            var originals = NativeInventory.ReadValues(document, plan.TemporaryChanges.Select(c => new CaptureTarget { OwnerId = c.OwnerId, OwnerUniqueId = c.OwnerUniqueId, Origin = c.Origin, Source = c.Source }).ToArray());
            foreach (var change in plan.TemporaryChanges)
            {
                var baseline = originals.Where(v => v.OwnerUniqueId == change.OwnerUniqueId && v.Source == change.Source && v.Origin == change.Origin).ToArray();
                if (baseline.Length != 1 || baseline[0].ReadOnly || baseline[0].StorageType == "ElementId" || change.ExpectedSignature != Canonical.ValuesHash(baseline)
                    || (change.Action == "Create") == baseline[0].Exists) throw new InvalidOperationException("导出临时来源或原值已变化：" + change.Source);
            }
            var target = OutputPath(input.OutputDirectory, plan.FileName);
            var artifactDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HIFC", "0.5.0-Web", "exports", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(artifactDirectory);
            var mapping = Path.Combine(artifactDirectory, "mapping.txt");
            File.WriteAllText(mapping, plan.MappingText ?? "", new UTF8Encoding(false));
            var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.IFCExporterService) as SingleServerService;
            var previousServer = service?.GetActiveServerId();
            var before = Snapshot(document); bool exported = false; TransactionStatus rollback;
            NativeTypePropertyScope scope = null;
            using (var transaction = new Transaction(document, "HIFC Web · 临时 IFC 属性与原生导出（回滚）"))
            {
                if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("导出事务无法启动。");
                try
                {
                    NativeDefinitions.Apply(document, plan.Native.Definitions);
                    foreach (var group in plan.TemporaryChanges.Where(c => c.Action == "Create" && !plan.Native.Definitions.Any(d => d.Source == c.Source)).GroupBy(c => c.Source)) NativeWriter.EnsureDefinition(document, group.ToArray());
                    document.Regenerate();
                    foreach (var change in plan.TemporaryChanges)
                    {
                        if (change.Action != "Create" && change.Action != "Set" && change.Action != "Clear") throw new InvalidOperationException("导出临时操作无效。");
                        if (change.Value != null) NativeWriter.Set(NativeWriter.Writable(document, change), change);
                    }
                    document.Regenerate(); NativeWriter.Verify(document, plan.TemporaryChanges);
                    SuppressFallback(document, plan.SuppressParameterNames, plan.Routes, input.Basis.Elements.Select(e => e.Element.UniqueId).ToArray());
                    using (var options = new IFCExportOptions { FileVersion = IFCVersion.IFC4DTV, ExportBaseQuantities = true })
                    {
                        var settings = new Dictionary<string, string> { ["ExportRoomsInView"] = "true", ["UseActiveViewGeometry"] = "false", ["ExportLinkedFiles"] = "false",
                            ["ExportUserDefinedPsets"] = "true", ["ExportUserDefinedPsetsFileName"] = mapping, ["ExportInternalRevitPropertySets"] = "false",
                            ["ExportIFCCommonPropertySets"] = "true", ["TessellationLevelOfDetail"] = "1", ["SitePlacement"] = "2" };
                        foreach (var option in plan.Options)
                        {
                            if (option.Key == "FileVersion") { if (option.Value != "IFC4DTV") throw new InvalidOperationException("IFC 版本不受支持。"); continue; }
                            if (option.Key == "ExportBaseQuantities") { if (option.Value != "true") throw new InvalidOperationException("导出必须包含原生工程量。"); continue; }
                            if (!settings.ContainsKey(option.Key) || option.Key == "ExportUserDefinedPsetsFileName")
                                throw new InvalidOperationException("服务器导出选项超出允许范围：" + option.Key);
                            if ((option.Key == "ExportLinkedFiles" || option.Key == "UseActiveViewGeometry") && option.Value != settings[option.Key])
                                throw new InvalidOperationException("导出范围选项不得扩大至链接或缩小到当前视图。");
                            settings[option.Key] = option.Value;
                        }
                        foreach (var option in settings) options.AddOption(option.Key, option.Value);
                        using (scope = new NativeTypePropertyScope(document, plan.Routes.Where(r => r.ExactOwnerFilter).ToArray(), service, new FilteredElementCollector(document).OfClass(typeof(Floor)).Any()))
                            exported = document.Export(artifactDirectory, Path.GetFileNameWithoutExtension(plan.FileName), options);
                        if (!scope.Applied || !scope.Restored || !scope.FloorGeometryPassed) throw new InvalidOperationException("原生属性路由或楼板几何未完整执行，未发布 IFC。");
                    }
                }
                finally { rollback = transaction.GetStatus() == TransactionStatus.Started ? transaction.RollBack() : transaction.GetStatus(); }
            }
            var after = Snapshot(document);
            if (rollback != TransactionStatus.RolledBack || before != after || service?.GetActiveServerId() != previousServer
                || scope?.ScheduleIds.Any(id => document.GetElement(id) != null) == true)
                throw new InvalidOperationException("导出后模型或导出器未恢复，未发布 IFC。");
            var staged = Path.Combine(artifactDirectory, plan.FileName);
            if (!exported || !File.Exists(staged) || new FileInfo(staged).Length == 0) throw new InvalidOperationException("原生导出器未产生有效 IFC。");
            var receipt = new ExportReceipt { PlanId = plan.Id, DocumentSessionId = input.DocumentSessionId, IfcPath = target,
                IfcHash = FileHash(staged), ReceiptPath = Path.Combine(artifactDirectory, "receipt.json"), SnapshotBefore = before, SnapshotAfter = after,
                SourceRestored = true, Exported = true, CompletedUtc = DateTime.UtcNow };
            File.WriteAllText(receipt.ReceiptPath, JsonWire.Serialize(receipt), new UTF8Encoding(false));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            // Final directory receives exactly one IFC. Mapping and evidence stay private.
            var publication = target + "." + Guid.NewGuid().ToString("N") + ".staging";
            try { File.Copy(staged, publication, false); if (FileHash(publication) != receipt.IfcHash) throw new IOException("IFC 发布校验失败。"); File.Move(publication, target); }
            finally { if (File.Exists(publication)) File.Delete(publication); }
            return receipt;
        }
        private static void SuppressFallback(Document document, string[] sources, NativeExportRoute[] routes, string[] elementIds)
        {
            foreach (var source in sources.Distinct())
            {
                var definitions = new List<Definition>(); var iterator = document.ParameterBindings.ForwardIterator(); iterator.Reset();
                while (iterator.MoveNext()) if (iterator.Key.Name == source) definitions.Add(iterator.Key);
                foreach (var definition in definitions) if (!document.ParameterBindings.Remove(definition)) throw new InvalidOperationException("无法隔离原生同名回退来源：" + source);
                document.Regenerate();
                var applicable = routes.Where(r => !r.ExactOwnerFilter && r.Origin == "instance" && r.Properties.Any(p => p.Property == source && (p.ExportType == "Real" || p.ExportType == "Boolean"))).ToArray();
                foreach (var id in elementIds)
                {
                    var element = document.GetElement(id) ?? throw new InvalidOperationException("导出对象已删除。");
                    var entity = NativeInventory.Entity(document, element);
                    foreach (var route in applicable.Where(r => r.Entity.Split(',').Contains(entity)))
                    foreach (var property in route.Properties.Where(p => p.Property == source))
                    {
                        var bridge = element.get_Parameter(new Guid(property.BridgeGuid));
                        if (bridge?.StorageType == StorageType.String && bridge.HasValue && !string.IsNullOrWhiteSpace(bridge.AsString())) continue;
                        var type = document.GetElement(element.GetTypeId());
                        if (new[] { element, type }.Where(e => e != null).Any(e => e.GetParameters(source).Any(p => p.HasValue && (p.StorageType != StorageType.String || !string.IsNullOrEmpty(p.AsString())))))
                            throw new InvalidOperationException("族或内置参数存在不能安全隔离的同名回退来源：" + element.Id.IntegerValue + " / " + source);
                    }
                }
            }
        }
        private static string Snapshot(Document document)
        {
            var rows = new FilteredElementCollector(document).WhereElementIsNotElementType().ToArray()
                .Concat(new FilteredElementCollector(document).WhereElementIsElementType().ToArray()).OrderBy(e => e.UniqueId, StringComparer.Ordinal);
            var hashes = rows.Select(element => Canonical.Hash(JsonWire.Serialize(new { element.UniqueId, TypeId = element.GetTypeId().IntegerValue,
                Parameters = element.Parameters.Cast<Parameter>().OrderBy(p => p.Id.IntegerValue).Select(p => new {
                    Id = p.Id.IntegerValue, Name = p.Definition.Name, Type = p.Definition.ParameterType.ToString(), p.HasValue,
                    Raw = !p.HasValue ? null : p.StorageType == StorageType.String ? p.AsString() : p.StorageType == StorageType.Integer ? p.AsInteger().ToString(CultureInfo.InvariantCulture)
                        : p.StorageType == StorageType.Double ? p.AsDouble().ToString("R", CultureInfo.InvariantCulture) : p.StorageType == StorageType.ElementId ? p.AsElementId().IntegerValue.ToString(CultureInfo.InvariantCulture) : null
                }).ToArray() }))).ToArray();
            return Canonical.Hash(string.Join("\n", hashes) + "\n" + JsonWire.Serialize(NativeProjectData.Read(document)) + "\n" + Canonical.InventoryHash(NativeInventory.Read(document)));
        }
        private static string FileHash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2"))); }
    }
}
