using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Hifc.Reference
{
    public sealed class ReferenceCatalog
    {
        public const string BaselineSha256 = "c1f3d74e17229aee4ed10bc4980532d2c7c61a5a783061c6e979562b808052c9";
        public ReferenceProperty[] Properties { get; private set; }

        public static ReferenceCatalog FromEmbeddedBaseline()
        {
            using (var stream = typeof(ReferenceCatalog).Assembly.GetManifestResourceStream("Hifc.Reference.Contract.json"))
            {
                if (stream == null) throw new InvalidOperationException("参考契约资源缺失。");
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    var bytes = buffer.ToArray();
                    using (var hash = SHA256.Create())
                    {
                        var actual = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                        if (actual != BaselineSha256) throw new InvalidOperationException("参考契约哈希与冻结版本不一致。");
                    }
                    return Parse(Encoding.UTF8.GetString(bytes));
                }
            }
        }

        public static ReferenceCatalog Parse(string json)
        {
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null || Text(root, "schema_version") != "hifc.reference-contract/1.0")
                    throw new InvalidOperationException("参考契约版本不受支持。");
                object data;
                if (!root.TryGetValue("entries", out data) || !(data is object[]) || ((object[])data).Length == 0)
                    throw new InvalidOperationException("参考契约无属性定义。");
                var properties = ((object[])data).Select(item =>
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) throw new InvalidOperationException("参考契约条目格式错误。");
                    return new ReferenceProperty
                    {
                        PropertySet = Text(row, "property_set"), Binding = Text(row, "binding"),
                        Entity = Text(row, "ifc_entity"), Property = Text(row, "ifc_property"),
                        RawType = Text(row, "raw_data_type"), SourceParameter = Text(row, "revit_source_parameter"),
                        NativeGroup = Convert.ToBoolean(row["exporter_native_group"])
                    };
                }).ToArray();
                if (properties.Any(p => p.Binding != "实例" && p.Binding != "类型"))
                    throw new InvalidOperationException("参考契约驱动方式无效。");
                if (properties.GroupBy(p => p.PropertySet + "|" + p.Binding + "|" + p.Entity + "|" + p.Property).Any(g => g.Count() != 1))
                    throw new InvalidOperationException("参考契约存在重复属性键。");
                return new ReferenceCatalog { Properties = properties };
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex) { throw new InvalidOperationException("无法加载参考契约。", ex); }
        }

        private static string Text(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value) || !(value is string) || string.IsNullOrWhiteSpace((string)value))
                throw new InvalidOperationException("参考契约字段缺失：" + key);
            return (string)value;
        }

        public ReferenceProperty[] ForSlot(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot)) throw new InvalidOperationException("类目为空，请先准备映射参数并设置类目。");
            var value = slot.Trim();
            var candidates = value.StartsWith("Pset_", StringComparison.Ordinal)
                ? new[] { value }
                : new[] { "Pset_" + value + "信息属性集", "Pset_" + value };
            var found = Properties.Where(p => !p.NativeGroup && candidates.Contains(p.PropertySet, StringComparer.Ordinal)).ToArray();
            if (found.Length == 0) throw new InvalidOperationException("类目不在参考工规配置中：" + value);
            return found;
        }

        public string[] Slots()
        {
            return Properties.Where(p => !p.NativeGroup).Select(p => LegacySlot(p.PropertySet))
                .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }

        public static string LegacySlot(string pset)
        {
            var value = pset.StartsWith("Pset_", StringComparison.Ordinal) ? pset.Substring(5) : pset;
            const string tail = "信息属性集";
            return value.EndsWith(tail, StringComparison.Ordinal) ? value.Substring(0, value.Length - tail.Length) : value;
        }

        public string BuildExportConfiguration()
            =>BuildSelectedExportConfiguration(Properties);
        public string BuildSelectedExportConfiguration(IEnumerable<ReferenceProperty> selected)
        {
            var output = new StringBuilder("# Reference profile; raw types preserved; isolated temporary value sources.\n");
            foreach (var group in selected.Where(p => !p.NativeGroup).GroupBy(p=>p.PropertySet+"|"+p.Binding+"|"+p.Entity+"|"+p.Property).Select(g=>g.First()).GroupBy(p => new { p.PropertySet, p.Binding, p.Entity }))
            {
                output.Append("PropertySet:\t").Append(group.Key.PropertySet).Append('\t')
                    .Append(group.Key.Binding == "类型" ? "T" : "I").Append('\t')
                    .Append(string.Join(",", group.Key.Entity.Split(',').Select(e => group.Key.Binding == "类型"
                        && (e == "IfcSlab" || e == "IfcWindow") ? e + "Type" : e))).Append('\n');
                foreach (var property in group)
                    output.Append(property.Property).Append('\t').Append(property.ExportType).Append('\t')
                        .Append(ReferencePolicy.BridgeName(property)).Append('\n');
            }
            return output.ToString();
        }
    }
}
