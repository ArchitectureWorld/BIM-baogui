using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public sealed class NativeWriteInput
    {
        public string DocumentSessionId { get; set; }
        public CheckResult Basis { get; set; }
        public CapturePlan Capture { get; set; }
        public EditPlan Plan { get; set; }
        public WritePermit Permit { get; set; }
        public IDictionary<string, string> AssignedSlots { get; set; }
    }
    public static class NativeWriter
    {
        private static readonly HashSet<string> ConsumedPermits = new HashSet<string>(StringComparer.Ordinal);
        public static WriteReceipt Apply(Document document, NativeWriteInput input, PublicSigningKey key, DateTime serverNowUtc)
        {
            if (document == null || !document.IsValidObject || document.IsReadOnly || document.IsFamilyDocument || document.IsModifiable
                || input?.Basis?.Context == null || input.Capture == null || input.DocumentSessionId != input.Basis.Context.DocumentSessionId)
                throw new InvalidOperationException("文档状态已变化，请重新检查。");
            WriteGuard.Plan(input.Plan, input.Basis.Context, key, serverNowUtc);
            WriteGuard.Permit(input.Permit, input.Plan, key, serverNowUtc);
            ProtocolGuard.Match(input.Basis.Context, input.Capture.Context);
            if (!input.Capture.InventoryComplete || input.Plan.CaptureHash != input.Basis.CaptureHash) throw new InvalidOperationException("检查尚未完整完成。");
            var captured = NativeInventory.ReadValues(document, input.Capture.Targets);
            WriteGuard.Capture(input.Basis, NativeInventory.Read(document, input.AssignedSlots), captured);
            if (input.Plan.Changes.GroupBy(c => c.OwnerUniqueId + "\n" + c.Source).Any(g => g.Count() != 1)) throw new InvalidOperationException("写入目标重复。");
            foreach (var change in input.Plan.Changes)
            {
                if (change.Action != "Create" && change.Action != "Set" && change.Action != "Clear") throw new InvalidOperationException("写入操作无效。");
                var originals = captured.Where(v => v.OwnerUniqueId == change.OwnerUniqueId && v.Origin == change.Origin && v.Source == change.Source).ToArray();
                if (originals.Length != 1 || originals[0].OwnerId != change.OwnerId || change.ExpectedSignature != Canonical.ValuesHash(originals)
                    || originals[0].ReadOnly || originals[0].StorageType == "ElementId" || (change.Action == "Create") == originals[0].Exists)
                    throw new InvalidOperationException("参数来源、原值或可写性已改变：" + change.Source);
                if (change.Action == "Create" && (!Guid.TryParse(change.SourceGuid, out _) || string.IsNullOrWhiteSpace(change.Source))) throw new InvalidOperationException("新增参数的固定标识缺失。");
            }
            lock (ConsumedPermits) if (!ConsumedPermits.Add(input.Permit.Id)) throw new InvalidOperationException("本次写入许可已使用，请重新生成方案。");
            using (var group = new TransactionGroup(document, "HIFC 0.5.0 Web · 写入与回读"))
            {
                group.Start();
                try
                {
                    using (var transaction = new Transaction(document, "按服务器签名方案写入"))
                    {
                        transaction.Start();
                        foreach (var sources in input.Plan.Changes.Where(c => c.Action == "Create").GroupBy(c => c.Source)) EnsureDefinition(document, sources.ToArray());
                        document.Regenerate();
                        foreach (var change in input.Plan.Changes) if (change.Value != null) Set(Writable(document, change), change);
                        document.Regenerate(); Verify(document, input.Plan.Changes);
                        if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("写入事务未提交。");
                    }
                    // Updaters can run during Commit; the group remains rollbackable until this readback passes.
                    Verify(document, input.Plan.Changes);
                    if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("写入事务组未提交。");
                    return new WriteReceipt { PermitId = input.Permit.Id, PlanId = input.Plan.Id, DocumentSessionId = input.DocumentSessionId,
                        TransactionCommitted = true, ReadbackPassed = true, CompletedUtc = DateTime.UtcNow,
                        Detail = "本机写入与回读成功；全面复检尚未完成。模型尚未保存，请复检后自行保存。" };
                }
                catch { if (group.GetStatus() == TransactionStatus.Started) group.RollBack(); throw; }
            }
        }
        private static Parameter Writable(Document document, WriteChange change)
        {
            var owner = document.GetElement(change.OwnerUniqueId);
            if (owner == null || owner.Id.IntegerValue != change.OwnerId || (owner is ElementType) != (change.Origin == "type")) throw new InvalidOperationException("参数载体已改变。");
            var parameters = owner.GetParameters(change.Source);
            if (parameters.Count != 1 || parameters[0].IsReadOnly || parameters[0].StorageType == StorageType.ElementId) throw new InvalidOperationException("参数不可写或有歧义：" + change.Source);
            var parameter = parameters[0];
            if (change.Action == "Create" && (!parameter.IsShared || parameter.GUID != new Guid(change.SourceGuid))) throw new InvalidOperationException("新增参数的 GUID 不一致。");
            return parameter;
        }
        private static double Number(Parameter parameter, WriteChange change)
        {
            if (!double.TryParse(change.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsInfinity(number) || double.IsNaN(number)) throw new InvalidOperationException("数值无效：" + change.Source);
            if (string.IsNullOrEmpty(change.Unit) || change.Unit == "internal") return number;
            if (change.Unit == "m²" && parameter.Definition.UnitType == UnitType.UT_Area) return UnitUtils.ConvertToInternalUnits(number, DisplayUnitType.DUT_SQUARE_METERS);
            if (change.Unit == "m" && parameter.Definition.UnitType == UnitType.UT_Length) return UnitUtils.ConvertToInternalUnits(number, DisplayUnitType.DUT_METERS);
            throw new InvalidOperationException("数值单位与模型参数不匹配：" + change.Source);
        }
        private static void Set(Parameter parameter, WriteChange change)
        {
            bool success;
            switch (parameter.StorageType)
            {
                case StorageType.String: success = parameter.Set(change.Value); break;
                case StorageType.Integer:
                    if (!int.TryParse(change.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) throw new InvalidOperationException("整数无效：" + change.Source);
                    success = parameter.Set(integer); break;
                case StorageType.Double: success = parameter.Set(Number(parameter, change)); break;
                default: throw new InvalidOperationException("参数存储类型不支持写入：" + change.Source);
            }
            if (!success) throw new InvalidOperationException("参数写入失败：" + change.Source);
        }
        private static void Verify(Document document, WriteChange[] changes)
        {
            foreach (var change in changes)
            {
                var parameter = Writable(document, change);
                if (change.Value == null) continue;
                var equal = parameter.StorageType == StorageType.String ? parameter.AsString() == change.Value
                    : parameter.StorageType == StorageType.Integer ? parameter.AsInteger().ToString(CultureInfo.InvariantCulture) == change.Value
                    : parameter.HasValue && Math.Abs(parameter.AsDouble() - Number(parameter, change)) <= 1e-9 * Math.Max(1, Math.Abs(Number(parameter, change)));
                if (!equal) throw new InvalidOperationException("回读与拟写值不同，已回滚全部修改：" + change.Source);
            }
        }
        private static void EnsureDefinition(Document document, WriteChange[] changes)
        {
            var first = changes[0]; var guid = new Guid(first.SourceGuid);
            if (changes.Any(c => c.SourceGuid != first.SourceGuid || c.Origin != first.Origin)) throw new InvalidOperationException("共享参数定义或绑定范围不一致。");
            var owners = changes.Select(c => document.GetElement(c.OwnerUniqueId)).ToArray();
            var categories = owners.Select(o => o.Category).GroupBy(c => c?.Id.IntegerValue).Select(g => g.First()).ToArray();
            if (categories.Any(c => c == null || !c.AllowsBoundParameters)) throw new InvalidOperationException("目标类别不支持共享参数。");
            var collector = new FilteredElementCollector(document).WherePasses(new ElementMulticategoryFilter(categories.Select(c => c.Id).ToList()));
            if (first.Origin == "type") collector.WhereElementIsElementType(); else collector.WhereElementIsNotElementType();
            foreach (var owner in collector) if (owner.GetParameters(first.Source).Any(p => !p.IsShared || p.GUID != guid)) throw new InvalidOperationException("新增类别参数会与已有同名来源冲突：" + first.Source);
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HIFC", "0.5.0-Web", "bindings");
            Directory.CreateDirectory(directory); var filePath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(filePath, "# This is a Revit shared parameter file.\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\nGROUP\t1\tHIFC Web\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\r\n", Encoding.Unicode);
            var previous = document.Application.SharedParametersFilename;
            try
            {
                document.Application.SharedParametersFilename = filePath;
                var file = document.Application.OpenSharedParameterFile() ?? throw new InvalidOperationException("无法创建共享参数定义。");
                var group = file.Groups.get_Item("HIFC Web") ?? file.Groups.Create("HIFC Web");
                Definition definition = SharedParameterElement.Lookup(document, guid)?.GetDefinition();
                if (definition != null && (definition.Name != first.Source || definition.ParameterType != ParameterType.Text)) throw new InvalidOperationException("共享参数 GUID 定义冲突。");
                definition = definition ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(first.Source, ParameterType.Text) { GUID = guid, Visible = true, UserModifiable = true });
                var set = document.Application.Create.NewCategorySet(); var existing = document.ParameterBindings.get_Item(definition) as ElementBinding;
                if (existing != null)
                {
                    if ((existing is TypeBinding) != (first.Origin == "type")) throw new InvalidOperationException("已有参数绑定与方案来源不同。");
                    foreach (Category category in existing.Categories) set.Insert(category);
                }
                foreach (var category in categories) set.Insert(category);
                Binding binding = first.Origin == "type" ? (Binding)document.Application.Create.NewTypeBinding(set) : document.Application.Create.NewInstanceBinding(set);
                if (!document.ParameterBindings.Insert(definition, binding, BuiltInParameterGroup.PG_DATA)
                    && !document.ParameterBindings.ReInsert(definition, binding, BuiltInParameterGroup.PG_DATA)) throw new InvalidOperationException("共享参数绑定失败：" + first.Source);
            }
            finally { document.Application.SharedParametersFilename = previous; File.Delete(filePath); }
        }
    }
}
