using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Hifc.Reference
{
    // RVT inputs remain native YesNo parameters. The current county's calculator
    // compares enum and branch values literally (including parking 104/203).
    // Only registered Boolean fields with an explicit 是/否 choice are adapted.
    public static class PlatformNativeEncoding
    {
        public const string PolicyId = "hbbim-420626-boolean-label/20261105";
        private static readonly Lazy<ReferenceProperty[]> LocalizedFields = new Lazy<ReferenceProperty[]>(LoadFields);
        private static ReferenceProperty[] LoadFields()
        {
            using (var stream = typeof(PlatformNativeEncoding).Assembly.GetManifestResourceStream("Hifc.Reference.Platform420626.20261105.BooleanEncoding.json"))
            {
                if (stream == null) throw new InvalidOperationException("当前平台布尔出口证据缺失。");
                byte[] bytes; using (var copy = new MemoryStream()) { stream.CopyTo(copy); bytes = copy.ToArray(); }
                using (var hash = SHA256.Create())
                    if (BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != "f93cd1f9a41575bae56165642a9fff517eeddf0a68a084b08eb87ecd6972b54c")
                        throw new InvalidOperationException("当前平台布尔出口证据哈希不符。");
                var json = new JavaScriptSerializer();
                var data = json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes));
                if ((string)data["policy_id"] != PolicyId || (string)data["profile_id"] != PlatformRules.ProfileId)
                    throw new InvalidOperationException("当前平台布尔出口方案不符。");
                return json.Deserialize<ReferenceProperty[]>(json.Serialize(data["fields"]));
            }
        }
        public static bool UsesLocalizedBoolean(ReferenceProperty p, string profileId)
        {
            if (p == null || !PlatformRules.IsActive(profileId) || p.NativeGroup || p.Binding != "实例" || p.ExportType != "Boolean") return false;
            return LocalizedFields.Value.Any(f => f.PropertySet == p.PropertySet && f.Property == p.Property
                && f.Entity == p.Entity && f.SourceParameter == p.SourceParameter);
        }
        public static ReferenceProperty ConfigurationProperty(ReferenceProperty p, string profileId)
        {
            if (!UsesLocalizedBoolean(p, profileId)) return p;
            var result = CaseExportPolicy.WithBinding(p, p.Binding, p.Entity);
            result.RawType = "Label";
            return result;
        }
        public static string Value(ReferenceProperty p, string value, string profileId)
        {
            if (!UsesLocalizedBoolean(p, profileId)) return value;
            if (string.IsNullOrWhiteSpace(value)) return null;
            return BooleanPolicy.Normalize(value) == "1" ? "是" : "否";
        }
    }
}
