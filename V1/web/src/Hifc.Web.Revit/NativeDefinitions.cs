using System;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    internal static class NativeDefinitions
    {
        internal static ParameterType Type(string value)
        {
            switch (value ?? "Text")
            {
                case "Text": return ParameterType.Text;
                case "YesNo": return ParameterType.YesNo;
                case "Number": return ParameterType.Number;
                case "Integer": return ParameterType.Integer;
                case "Area": return ParameterType.Area;
                case "Length": return ParameterType.Length;
                default: throw new InvalidOperationException("参数定义类型不受支持：" + value);
            }
        }
        internal static void Validate(Document document, NativeDefinitionOperation[] operations)
        {
            if (operations == null || operations.GroupBy(o => o.Source).Any(g => g.Count() > 1)) throw new InvalidOperationException("定义计划有重复来源。");
            var snapshots = NativeInventory.ReadDefinitions(document, operations.Select(o => o.Source).Distinct().ToArray());
            foreach (var operation in operations)
            {
                Type(operation.DefinitionType);
                if (!Guid.TryParse(operation.Guid, out _) || string.IsNullOrWhiteSpace(operation.Source)
                    || operation.Origin != "instance" && operation.Origin != "type" || operation.CategoryIds.Length == 0)
                    throw new InvalidOperationException("参数定义计划不完整。");
                var matches = snapshots.Where(s => s.Source == operation.Source).ToArray();
                if (matches.Length > 1) throw new InvalidOperationException("同名定义有歧义，不能迁移：" + operation.Source);
                var original = matches.SingleOrDefault();
                if (Canonical.DefinitionSetHash(operation.Source, snapshots) != operation.ExpectedBindingHash)
                    throw new InvalidOperationException("参数绑定或未选构件的原值已变化，请重新检查：" + operation.Source);
                if (original != null && (original.Origin != operation.Origin || original.CategoryIds.Except(operation.CategoryIds).Any()))
                    throw new InvalidOperationException("参数迁移不允许缩小绑定或变更实例/类型范围。");
                if (operation.ReplaceParameterId.HasValue)
                {
                    if (original == null || original.ParameterId != operation.ReplaceParameterId.Value || original.ParameterId < 1)
                        throw new InvalidOperationException("旧参数身份不一致，已阻止删除。");
                    if (original.Dependencies == null || original.Dependencies.Length != 0) throw new InvalidOperationException("旧参数仍有明细表、过滤器或全局参数依赖，不能自动迁移：" + string.Join("；", original.Dependencies ?? new[] { "依赖状态未知" }));
                    var values = original.Values;
                    if (values.Length != operation.MigrationValues.Length || operation.MigrationValues.GroupBy(v => v.OwnerUniqueId).Any(g => g.Count() != 1))
                        throw new InvalidOperationException("迁移必须覆盖全部绑定载体，不能仅迁移所选构件。");
                }
                else if (original != null && (original.Guid != operation.Guid || original.DefinitionType != operation.DefinitionType))
                    throw new InvalidOperationException("变更定义类型需要显式的旧定义迁移计划。");
                if (operation.MigrationValues.GroupBy(v => v.OwnerUniqueId).Any(g => g.Count() != 1)) throw new InvalidOperationException("迁移载体重复。");
                foreach (var migrated in operation.MigrationValues)
                {
                    var source = migrated.Source ?? operation.Source;
                    var owner = document.GetElement(migrated.OwnerUniqueId);
                    if (owner?.Category == null || !operation.CategoryIds.Contains(owner.Category.Id.IntegerValue)) throw new InvalidOperationException("迁移载体不属于签名类别绑定。");
                    var previous = NativeInventory.ReadValues(document, new[] { new CaptureTarget { OwnerId = migrated.OwnerId, OwnerUniqueId = migrated.OwnerUniqueId, Origin = operation.Origin, Source = source } });
                    if (previous.Length != 1 || !previous[0].Exists || migrated.ExpectedSignature != Canonical.ValuesHash(previous)
                        || operation.ReplaceParameterId.HasValue && (source != operation.Source || previous[0].ReadOnly)
                        || previous[0].HasValue && !migrated.HasValue)
                        throw new InvalidOperationException("迁移来源、原值或目标状态不一致。");
                    if (source != operation.Source && owner.GetParameters(operation.Source).Any(p => p.HasValue))
                        throw new InvalidOperationException("别名迁移不能覆盖已有目标值，需单列明确的属性编辑。");
                }
                if (!operation.ReplaceParameterId.HasValue && operation.MigrationValues.Any(v => v.Source != null && v.Source != operation.Source))
                {
                    var aliases = operation.MigrationValues.Select(v => v.Source).Where(s => s != null && s != operation.Source).Distinct().ToArray();
                    var collector = new FilteredElementCollector(document);
                    if (operation.Origin == "type") collector.WhereElementIsElementType(); else collector.WhereElementIsNotElementType();
                    foreach (var owner in collector.Where(e => e.Category != null && operation.CategoryIds.Contains(e.Category.Id.IntegerValue)))
                    {
                        if (owner.GetParameters(operation.Source).Any(p => p.HasValue)) continue;
                        if (aliases.Any(alias => owner.GetParameters(alias).Any(p => p.HasValue)) && !operation.MigrationValues.Any(v => v.OwnerUniqueId == owner.UniqueId))
                            throw new InvalidOperationException("别名迁移遗漏同类别未选载体，已阻止绑定新默认值。");
                    }
                }
            }
        }
        internal static void Apply(Document document, NativeDefinitionOperation[] operations)
        {
            foreach (var operation in operations)
            {
                if (operation.ReplaceParameterId.HasValue)
                {
                    // Revit 2020 may return the definition AND its anonymous binding
                    // infrastructure when deleting a still-bound parameter. Unbind
                    // explicitly, as the verified Local native executor does, and
                    // never accept deletion of a public/model dependent.
                    using (var probe = new SubTransaction(document))
                    {
                        probe.Start();
                        try { RemoveOldDefinition(document, operation); }
                        finally { probe.RollBack(); }
                    }
                    RemoveOldDefinition(document, operation); document.Regenerate();
                }
                NativeWriter.EnsureDefinition(document, new[] { new WriteChange { Source = operation.Source, SourceGuid = operation.Guid,
                    DefinitionType = operation.DefinitionType, Origin = operation.Origin } }, operation.CategoryIds);
                document.Regenerate();
                foreach (var value in operation.MigrationValues.Where(v => v.HasValue))
                {
                    var change = MigrationChange(operation, value);
                    NativeWriter.Set(NativeWriter.Writable(document, change), change);
                }
            }
        }
        private static void RemoveOldDefinition(Document document, NativeDefinitionOperation operation)
        {
            var id = new ElementId(operation.ReplaceParameterId.Value);
            var parameter = document.GetElement(id) as SharedParameterElement
                ?? throw new InvalidOperationException("旧定义不再是可迁移的共享参数：" + operation.Source);
            var dependents = parameter.GetDependentElements(null).Where(dependent => dependent.IntegerValue != id.IntegerValue)
                .Select(dependent => document.GetElement(dependent)).Where(element => element != null)
                .Select(element => new { Id = element.Id.IntegerValue, Type = element.GetType().FullName, Name = element.Name,
                    BindingNode = element.GetType() == typeof(Element) && element.Category == null && string.IsNullOrEmpty(element.Name) }).ToArray();
            if (!document.ParameterBindings.Remove(parameter.GetDefinition())) throw new InvalidOperationException("旧参数解绑失败，整轮已回滚：" + operation.Source);
            document.Regenerate();
            var removedPublic = dependents.Where(element => !element.BindingNode && document.GetElement(new ElementId(element.Id)) == null).ToArray();
            if (removedPublic.Length > 0) throw new InvalidOperationException("旧参数解绑删除了非绑定依赖，整轮已回滚：" + operation.Source + " / " + string.Join("；", removedPublic.Select(e => e.Id + ":" + e.Type + ":" + e.Name)));
            if (document.GetElement(id) == null) throw new InvalidOperationException("解绑后旧定义意外消失，整轮已回滚：" + operation.Source + " / " + id.IntegerValue);
            var deleted = document.Delete(id).Select(element => element.IntegerValue).ToArray();
            // Do not equate the returned collection's count with successful deletion.
            // Check actual absence as well, while rejecting every unexpected ID.
            if (deleted.Any(element => element != id.IntegerValue) || document.GetElement(id) != null
                || dependents.Any(element => !element.BindingNode && document.GetElement(new ElementId(element.Id)) == null))
                throw new InvalidOperationException("旧定义删除范围不符，整轮已回滚：" + operation.Source + " / requested=" + id.IntegerValue + " / returned=" + string.Join(",", deleted));
        }
        private static WriteChange MigrationChange(NativeDefinitionOperation operation, NativeMigrationValue value) => new WriteChange {
            OwnerId = value.OwnerId, OwnerUniqueId = value.OwnerUniqueId, Source = operation.Source, SourceGuid = operation.Guid,
            Origin = operation.Origin, Action = "Set", DefinitionType = operation.DefinitionType, Value = value.Value };
        internal static void Verify(Document document, NativeDefinitionOperation[] operations, WriteChange[] changes, NativeDefinitionSnapshot[] originals = null)
        {
            var snapshots = NativeInventory.ReadDefinitions(document, operations.Select(o => o.Source).Distinct().ToArray());
            foreach (var operation in operations)
            {
                var found = snapshots.SingleOrDefault(s => s.Source == operation.Source);
                if (found == null || found.Guid != operation.Guid || found.DefinitionType != operation.DefinitionType
                    || found.Origin != operation.Origin || !found.CategoryIds.SequenceEqual(operation.CategoryIds.Distinct().OrderBy(x => x)))
                    throw new InvalidOperationException("迁移后参数定义或绑定回读不一致。");
                NativeWriter.Verify(document, operation.MigrationValues.Where(v => v.HasValue && !changes.Any(c => c.OwnerUniqueId == v.OwnerUniqueId && c.Source == operation.Source))
                    .Select(v => MigrationChange(operation, v)).ToArray());
                foreach (var unset in operation.MigrationValues.Where(v => !v.HasValue && !changes.Any(c => c.OwnerUniqueId == v.OwnerUniqueId && c.Source == operation.Source)))
                    if (NativeWriter.Writable(document, MigrationChange(operation, unset)).HasValue) throw new InvalidOperationException("未设置值迁移后变成默认值，已回滚。");
                if (!operation.ReplaceParameterId.HasValue)
                foreach (var before in (originals ?? new NativeDefinitionSnapshot[0]).Where(d => d.Source == operation.Source).SelectMany(d => d.Values)
                    .Where(v => v.Exists && !changes.Any(c => c.OwnerUniqueId == v.OwnerUniqueId && c.Source == operation.Source)
                        && !operation.MigrationValues.Any(m => m.OwnerUniqueId == v.OwnerUniqueId)))
                {
                    var actual = found.Values.Where(v => v.OwnerUniqueId == before.OwnerUniqueId && v.Origin == before.Origin).ToArray();
                    if (actual.Length != 1 || Canonical.ValuesHash(actual) != Canonical.ValuesHash(new[] { before }))
                        throw new InvalidOperationException("扩展绑定改变了未选载体的原值，已回滚：" + operation.Source);
                }
            }
        }
    }
}
