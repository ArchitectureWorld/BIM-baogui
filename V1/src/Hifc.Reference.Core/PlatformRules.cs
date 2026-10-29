using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Hifc.Reference
{
    public sealed class PlatformFieldRule
    {
        public string Name { get; internal set; }
        public string InternalName { get; internal set; }
        public bool DisplayRequired { get; internal set; }
        // Source defaults/configured values are metadata, never evidence of a model value.
        public string DefaultValue { get; internal set; }
        public string ConfiguredValue { get; internal set; }
        public string[] Items { get; internal set; }
        public bool ShowProperty { get; internal set; }
        public bool AllowNull { get; internal set; }
        public string Code { get; internal set; }
        public int DataType { get; internal set; }
    }

    public sealed class PlatformMatchRule
    {
        public int Index { get; internal set; }
        public string EntityCode { get; internal set; }
        public string PropertySet { get; internal set; }
        public string SetType { get; internal set; }
        public string ClassificationCode { get; internal set; }
        public string ClassificationFieldOrValue { get; internal set; }
        public string ClassificationMatch { get; internal set; }
        public string ComputeGate { get; internal set; }
        public string ComputeField { get; internal set; }
        public string MatchValue { get; internal set; }
        public PlatformFieldRule[] Fields { get; internal set; }
    }

    public static class PlatformRules
    {
        public const string ProfileId = "hubei-420626-planning-20261020";
        public const string Title = "保康县 420626 · 报规 · SUBTYPE=0 · 2026-10-20 规则快照";
        public const string SnapshotSha256 = "f1868fa3a8f42890bba28192bd418b6a97da1cd194d6bddcba70863be39c5b64";
        private static readonly Lazy<Catalog> Snapshot = new Lazy<Catalog>(Load);

        private sealed class Catalog
        {
            internal PlatformMatchRule[] Rules;
            internal Dictionary<string, PlatformFieldRule[]> Fields;
            internal Dictionary<string, string[]> Functions;
        }

        public static bool IsActive(string profileId) => string.Equals(ProfileId, profileId, StringComparison.Ordinal);

        public static string CanonicalPropertySet(string pset)
        {
            if (pset == null) return null;
            var name = pset.Trim();
            if (name.Length == 0) return name;
            if (!name.StartsWith("Pset_", StringComparison.Ordinal))
                name = "Pset_" + name + (name.EndsWith("属性集", StringComparison.Ordinal) ? "" : "信息属性集");
            return name == "Pset_雨蓬信息属性集" ? "Pset_雨篷信息属性集" : name;
        }

        // Each matching branch is retained. In particular, parking space 104 and 203 share a Pset.
        public static PlatformMatchRule[] Rules(string pset = null)
        {
            var name = CanonicalPropertySet(pset);
            return Snapshot.Value.Rules.Where(rule => pset == null || rule.PropertySet == name).Select(Clone).ToArray();
        }

        // Combined field lists are for display/validation. Branch-specific defaults remain in Rules().
        public static PlatformFieldRule[] Fields(string pset) => FindFields(pset).Select(Clone).ToArray();

        public static string[] RequiredFields(string pset)
            => FindFields(pset).Where(field => field.DisplayRequired).Select(field => field.Name).ToArray();

        public static string[] Choices(string pset, string property, string parentFunction = null)
        {
            var field = FindField(pset, property);
            return field == null ? new string[0] : Choices(pset, field, parentFunction);
        }

        public static string ValueError(string pset, string property, string value, string parentFunction = null)
        {
            var field = FindField(pset, property);
            if (field == null) return null;
            if (string.IsNullOrWhiteSpace(value))
                return field.DisplayRequired ? "缺少平台输入：" + field.Name + "。" : null;

            // The main-function field reports a missing parent. Do not guess a parent for a child.
            if (field.InternalName == "公建小类")
            {
                if (string.IsNullOrWhiteSpace(parentFunction)) return null;
                return Choices(pset, field, parentFunction).Contains(value.Trim(), StringComparer.Ordinal)
                    ? null : field.Name + "“" + value + "”不属于主功能“" + parentFunction + "”。";
            }

            var choices = Choices(pset, field, parentFunction);
            if (choices.Length == 0) return null;
            var candidate = value.Trim();
            if (choices.Length == 2 && choices.Contains("是") && choices.Contains("否"))
            {
                if (candidate == "1" || string.Equals(candidate, "true", StringComparison.OrdinalIgnoreCase)) candidate = "是";
                else if (candidate == "0" || string.Equals(candidate, "false", StringComparison.OrdinalIgnoreCase)) candidate = "否";
            }
            return choices.Contains(candidate, StringComparer.Ordinal)
                ? null : "“" + value + "”不在当前平台规则的" + field.Name + "选项中。";
        }

        private static PlatformFieldRule[] FindFields(string pset)
        {
            PlatformFieldRule[] fields;
            var name = CanonicalPropertySet(pset);
            return name != null && Snapshot.Value.Fields.TryGetValue(name, out fields) ? fields : new PlatformFieldRule[0];
        }

        private static PlatformFieldRule FindField(string pset, string property)
        {
            if (string.IsNullOrWhiteSpace(property)) return null;
            var fields = FindFields(pset);
            var name = property.Trim();
            // Match the effective display name first; only then use an exact internal-name fallback.
            var found = fields.FirstOrDefault(field => field.Name == name);
            if (found != null) return found;
            var canonical = CanonicalPropertySet(pset);
            return Snapshot.Value.Rules.Where(rule => rule.PropertySet == canonical).SelectMany(rule => rule.Fields)
                .FirstOrDefault(field => field.InternalName == name);
        }

        private static string[] Choices(string pset, PlatformFieldRule field, string parentFunction)
        {
            // User-corrected exact net-land redline label (2026-10-22).
            // Supersedes the earlier 净用地规划控制红线 spelling; keep the frozen snapshot intact.
            if (CanonicalPropertySet(pset) == "Pset_规划控制线信息属性集" && field.Name == "红线类型")
                return field.Items.Concat(new[] { "净用地控制红线" }).Distinct(StringComparer.Ordinal).ToArray();
            // The 2026-10-22 single-field platform trial accepted 中学 on IfcBuilding.
            // Extend effective choices only; preserve the signed source snapshot and
            // its original 裙房/塔楼 metadata. Civil-defence participation is independent.
            if (CanonicalPropertySet(pset) == "Pset_建筑技术信息属性集" && field.Name == "建筑用途名称")
                return field.Items.Concat(new[] { "中学" }).Distinct(StringComparer.Ordinal).ToArray();
            if (field.InternalName == "公建大类") return Snapshot.Value.Functions.Keys.ToArray();
            if (field.InternalName == "公建小类")
            {
                string[] children;
                return !string.IsNullOrWhiteSpace(parentFunction) && Snapshot.Value.Functions.TryGetValue(parentFunction.Trim(), out children)
                    ? children.Distinct(StringComparer.Ordinal).ToArray() : new string[0];
            }
            return field.Items.Distinct(StringComparer.Ordinal).ToArray();
        }

        private static Catalog Load()
        {
            using (var stream = typeof(PlatformRules).Assembly.GetManifestResourceStream("Hifc.Reference.Platform420626.20261020.json"))
            {
                if (stream == null) throw new InvalidOperationException("保康县 2026-10-20 平台规则快照资源缺失。");
                byte[] bytes;
                using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); bytes = buffer.ToArray(); }
                using (var hash = SHA256.Create())
                {
                    var actual = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    if (actual != SnapshotSha256) throw new InvalidOperationException("平台规则快照哈希与已核对原附件不一致。");
                }
                var root = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }
                    .DeserializeObject(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
                var rules = Rows(root, "CodeRuleSet").Select((row, index) => ParseRule(row, index + 1)).ToArray();
                var functions = (Dictionary<string, object>)root["CodeComType"];
                return new Catalog
                {
                    Rules = rules,
                    Fields = rules.GroupBy(rule => rule.PropertySet, StringComparer.Ordinal).ToDictionary(group => group.Key,
                        group => group.SelectMany(rule => rule.Fields).GroupBy(field => field.Name, StringComparer.Ordinal).Select(Merge).ToArray(), StringComparer.Ordinal),
                    Functions = functions.ToDictionary(pair => pair.Key,
                        pair => ((object[])pair.Value).Cast<Dictionary<string, object>>().Select(item => Text(item, "sMC")).ToArray(), StringComparer.Ordinal)
                };
            }
        }

        private static PlatformMatchRule ParseRule(Dictionary<string, object> row, int index)
        {
            var rule = new PlatformMatchRule
            {
                Index = index, EntityCode = Text(row, "entCode"), PropertySet = Text(row, "setName"), SetType = Text(row, "setType"),
                ClassificationCode = Text(row, "propCode"), ClassificationFieldOrValue = Text(row, "propName"),
                ClassificationMatch = Text(row, "propNameMatch"), ComputeGate = Text(row, "propSetCompute"), ComputeField = Text(row, "propComputeField"),
                Fields = Rows(row, "propDataList").Select(ParseField).ToArray()
            };
            // This is the source branch predicate, not a value to fill when a model lacks classification.
            if (rule.SetType == "2")
                rule.MatchValue = rule.Fields.FirstOrDefault(field => field.InternalName == rule.ClassificationFieldOrValue)?.ConfiguredValue;
            return rule;
        }

        private static PlatformFieldRule ParseField(Dictionary<string, object> row)
        {
            var displayName = Text(row, "fieldShow");
            var visible = Convert.ToBoolean(row["showProp"]);
            var allowNull = Convert.ToBoolean(row["bAllowNull"]);
            return new PlatformFieldRule
            {
                Name = string.IsNullOrWhiteSpace(displayName) ? Text(row, "sPropName") : displayName,
                InternalName = Text(row, "sPropName"), DisplayRequired = visible && !allowNull,
                DefaultValue = Text(row, "sPropDefault"), ConfiguredValue = Text(row, "sPropValue"),
                Items = Rows(row, "items").Select(item => Text(item, "sMC")).ToArray(),
                ShowProperty = visible, AllowNull = allowNull, Code = Text(row, "sCode"), DataType = Convert.ToInt32(row["eDType"])
            };
        }

        private static PlatformFieldRule Merge(IGrouping<string, PlatformFieldRule> group)
        {
            var fields = group.ToArray();
            var result = Clone(fields[0]);
            result.DisplayRequired = fields.Any(field => field.DisplayRequired);
            result.ShowProperty = fields.Any(field => field.ShowProperty);
            result.AllowNull = fields.All(field => field.AllowNull);
            result.Items = fields.SelectMany(field => field.Items).Distinct(StringComparer.Ordinal).ToArray();
            if (fields.Select(field => field.DefaultValue).Distinct(StringComparer.Ordinal).Count() > 1) result.DefaultValue = null;
            if (fields.Select(field => field.ConfiguredValue).Distinct(StringComparer.Ordinal).Count() > 1) result.ConfiguredValue = null;
            if (fields.Select(field => field.Code).Distinct(StringComparer.Ordinal).Count() > 1) result.Code = null;
            return result;
        }

        private static PlatformFieldRule Clone(PlatformFieldRule field) => new PlatformFieldRule
        {
            Name = field.Name, InternalName = field.InternalName, DisplayRequired = field.DisplayRequired,
            DefaultValue = field.DefaultValue, ConfiguredValue = field.ConfiguredValue, Items = (string[])field.Items.Clone(),
            ShowProperty = field.ShowProperty, AllowNull = field.AllowNull, Code = field.Code, DataType = field.DataType
        };

        private static PlatformMatchRule Clone(PlatformMatchRule rule) => new PlatformMatchRule
        {
            Index = rule.Index, EntityCode = rule.EntityCode, PropertySet = rule.PropertySet, SetType = rule.SetType,
            ClassificationCode = rule.ClassificationCode, ClassificationFieldOrValue = rule.ClassificationFieldOrValue,
            ClassificationMatch = rule.ClassificationMatch, ComputeGate = rule.ComputeGate, ComputeField = rule.ComputeField,
            MatchValue = rule.MatchValue, Fields = rule.Fields.Select(Clone).ToArray()
        };

        private static IEnumerable<Dictionary<string, object>> Rows(Dictionary<string, object> row, string key)
            => ((object[])row[key]).Cast<Dictionary<string, object>>();

        private static string Text(Dictionary<string, object> row, string key)
        {
            object value;
            return row.TryGetValue(key, out value) ? value as string : null;
        }
    }
}
