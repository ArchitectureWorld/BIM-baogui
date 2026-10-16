using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Hifc.Web.Contracts
{
    public static class Canonical
    {
        private static string Pack(params object[] values)
        {
            var output = new StringBuilder();
            foreach (var value in values)
            {
                if (value == null) { output.Append("-1:"); continue; }
                var date = value as DateTime?;
                var text = date.HasValue ? DateTime.SpecifyKind(date.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)
                    : Convert.ToString(value, CultureInfo.InvariantCulture);
                output.Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text);
            }
            return output.ToString();
        }
        public static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))).Replace("-", "").ToLowerInvariant();
        }
        public static string InventoryHash(ElementRecord[] elements) => Hash(string.Concat((elements ?? new ElementRecord[0]).OrderBy(x => x.UniqueId, StringComparer.Ordinal).Select(x => Pack(
            x.Id, x.UniqueId, x.TypeId, x.TypeUniqueId, x.Name, x.TypeName, x.Category, x.Entity, x.DeclaredSlot, x.AssignedSlot,
            x.Level, x.Building, x.AreaScheme, x.CoverageNote, x.NativeAreaM2, x.ProjectedAreaM2))));
        public static string ValuesHash(ParameterValue[] values) => Hash(string.Concat((values ?? new ParameterValue[0]).Select(x => Pack(
            x.OwnerUniqueId, x.OwnerId, x.Source, x.Origin, x.Exists, x.HasValue, x.RawValue, x.DisplayValue, x.StorageType, x.Unit, x.ReadOnly, x.SharedGuid))
            .OrderBy(x => x, StringComparer.Ordinal)));
        public static string ContextKey(RequestContext context) => context == null ? null : Pack(context.ProtocolMajor, context.ClientVersion, context.RuleVersion,
            context.RequestId, context.DocumentSessionId, context.ScanId, context.SnapshotHash);
        public static string PlanHash(EditPlan plan) => Hash(Pack("HIFC-WEB-EDIT-1", ContextKey(plan.Context), plan.Id, plan.CaptureHash, plan.ExpiresAtUtc) + string.Concat(
            plan.Changes.OrderBy(c => c.OwnerUniqueId, StringComparer.Ordinal).ThenBy(c => c.Source, StringComparer.Ordinal).ThenBy(c => c.Action, StringComparer.Ordinal)
            .Select(c => Pack(c.FieldKey, c.OwnerId, c.OwnerUniqueId, c.Source, c.SourceGuid, c.Origin, c.PropertySet, c.RawType,
                c.ExpectedSignature, c.ExpectedValue, c.Value, c.Unit, c.Action, c.Reason, c.AffectedInstances, c.Manual))));
        public static string PermitPayload(WritePermit permit) => Pack("HIFC-WEB-PERMIT-1", ContextKey(permit.Context), permit.Id, permit.PlanId, permit.PlanHash, permit.ExpiresAtUtc);
    }
    public static class ProtocolGuard
    {
        public static void Match(RequestContext expected, RequestContext actual)
        {
            if (expected == null || actual == null || expected.ProtocolMajor != 1 || actual.ProtocolMajor != 1 || Canonical.ContextKey(expected) != Canonical.ContextKey(actual))
                throw new InvalidOperationException("响应的协议、文档、扫描或快照已不匹配，请重新检查。");
        }
        private static void Pages(int[] indexes, int[] counts, int total, int actual)
        {
            if (indexes.Length == 0 || counts.Any(c => c < 1 || c > 10000 || c != counts[0]) || counts[0] != indexes.Length
                || indexes.Distinct().Count() != indexes.Length || indexes.Any(i => i < 0 || i >= counts[0]) || total < 0 || total > 2000000 || actual != total)
                throw new InvalidOperationException("分页缺失、重复或总数不一致，不能产生写入方案。");
        }
        public static void InventoryPages(InventoryPage[] pages)
        {
            if (pages == null || pages.Length == 0 || pages.Any(p => p == null || p.Elements == null || p.Context == null))
                throw new InvalidOperationException("模型盘点页为空。");
            var first = pages[0];
            var elements = pages.SelectMany(p => p.Elements).ToArray();
            Pages(pages.Select(p => p.PageIndex).ToArray(), pages.Select(p => p.PageCount).ToArray(), first.TotalElements, elements.Length);
            foreach (var page in pages)
            {
                Match(first.Context, page.Context);
                if (page.TotalElements != first.TotalElements || page.Stage != first.Stage || page.ModelKind != first.ModelKind || page.Elements.Length > 256
                    || page.Elements.Any(e => e == null) || page.PageHash != Canonical.InventoryHash(page.Elements))
                    throw new InvalidOperationException("盘点页内容、阶段或散列不一致。");
            }
            if (elements.Any(e => string.IsNullOrWhiteSpace(e.UniqueId)) || elements.Select(e => e.UniqueId).Distinct().Count() != elements.Length)
                throw new InvalidOperationException("盘点构件标识为空或重复。");
            if (first.Context.SnapshotHash != Canonical.InventoryHash(elements)) throw new InvalidOperationException("完整模型快照散列不一致。");
        }
        private static string Key(string owner, string origin, string source) => owner + "\n" + origin + "\n" + source;
        public static void CapturePages(CapturePage[] pages, CaptureTarget[] required)
        {
            if (pages == null || pages.Length == 0 || pages.Any(p => p == null || p.Values == null || p.Context == null) || required == null)
                throw new InvalidOperationException("字段取数不完整。");
            var values = pages.SelectMany(p => p.Values).ToArray();
            Pages(pages.Select(p => p.PageIndex).ToArray(), pages.Select(p => p.PageCount).ToArray(), pages[0].TotalValues, values.Length);
            foreach (var page in pages)
            {
                Match(pages[0].Context, page.Context);
                if (page.TotalValues != pages[0].TotalValues || page.Values.Length > 1024 || page.Values.Any(v => v == null)
                    || page.PageHash != Canonical.ValuesHash(page.Values)) throw new InvalidOperationException("字段页散列或总数不一致。");
            }
            var keys = new HashSet<string>(required.Select(t => Key(t.OwnerUniqueId, t.Origin, t.Source)), StringComparer.Ordinal);
            var groups = values.GroupBy(v => Key(v.OwnerUniqueId, v.Origin, v.Source)).ToArray();
            if (groups.Any(g => !keys.Contains(g.Key) || g.Any(v => !v.Exists) && g.Count() != 1) || groups.Length != keys.Count)
                throw new InvalidOperationException("未收齐指定字段，或上传了范围外字段。");
            foreach (var target in required)
                if (values.Where(v => Key(v.OwnerUniqueId, v.Origin, v.Source) == Key(target.OwnerUniqueId, target.Origin, target.Source)).Any(v => v.OwnerId != target.OwnerId))
                    throw new InvalidOperationException("字段载体标识与盘点不一致。");
        }
    }
    public static class DraftRebaser
    {
        public static DraftRebaseResult Rebase(EditDraft[] drafts, FieldResult[] fields)
        {
            if (drafts == null || fields == null || drafts.Any(d => d == null || string.IsNullOrWhiteSpace(d.FieldKey)) || drafts.GroupBy(d => d.FieldKey).Any(g => g.Count() > 1))
                throw new InvalidOperationException("草稿标识无效或重复。");
            var reusable = new List<EditDraft>(); var conflicts = new List<DraftConflict>(); var invalid = new List<EditDraft>();
            foreach (var draft in drafts)
            {
                var field = fields.SingleOrDefault(f => f.Key == draft.FieldKey);
                if (field == null) invalid.Add(draft);
                else if (field.SourceSignature == draft.OriginalSignature) reusable.Add(draft);
                else conflicts.Add(new DraftConflict { Draft = draft, CurrentValue = field.Value, Reason = "模型原值、来源或参数定义已变化，请确认原值 / 现值 / 拟写值。" });
            }
            return new DraftRebaseResult { Reusable = reusable.ToArray(), Conflicts = conflicts.ToArray(), InvalidTargets = invalid.ToArray() };
        }
    }
}
