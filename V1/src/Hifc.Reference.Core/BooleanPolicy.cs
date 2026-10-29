using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hifc.Reference
{
    public static class BooleanPolicy
    {
        public static string Normalize(string value)
        {
            var text = (value ?? "").Trim();
            if (text.Length == 0 || text == "0" || text == "否" || text.Equals("false", StringComparison.OrdinalIgnoreCase)) return "0";
            if (text == "1" || text == "是" || text.Equals("true", StringComparison.OrdinalIgnoreCase)) return "1";
            throw new InvalidOperationException("是否属性必须为是或否：" + value);
        }
        public static string DefaultValue(string source, string nativeName, string ownerUniqueId = null, string baseLevelUniqueId = null)
        {
            if (source != "建筑基底") return "0";
            if (!string.IsNullOrEmpty(baseLevelUniqueId)) return ownerUniqueId == baseLevelUniqueId ? "1" : "0";
            return Regex.IsMatch((nativeName ?? "").Trim().ToUpperInvariant(), @"^(一层|首层|1层|1F(?![0-9])|F1(?![0-9]))") ? "1" : "0";
        }
        public static bool IsSource(string source, string profileId)
        {
            var properties = CaseExportPolicy.SourceProperties(profileId).Where(p => p.SourceParameter == source).ToArray();
            // Type comes from the contract, never from a value that happens to be 0/1.
            return properties.Length > 0 && properties.All(p => p.ExportType == "Boolean");
        }
        public static Guid SourceGuid(string source) => ReferencePolicy.SourceGuid("YesNo/v1/" + source);
        // Exact aliases permit controlled legacy-value copies without changing the old definitions.
        public static string LegacySource(string physicalSource)
        {
            switch (physicalSource)
            {
                case "总平构件建筑密度": return "建筑密度";
                case "楼梯段间距审查": return "间距审查";
                case "楼梯段退让审查": return "退让审查";
                default: return null;
            }
        }
    }
}
