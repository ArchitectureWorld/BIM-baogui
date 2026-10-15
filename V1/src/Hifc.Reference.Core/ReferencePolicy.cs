using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Hifc.Reference
{
    public static class ReferencePolicy
    {
        public static Guid SourceGuid(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("参数名为空。");
            return Uuid5("https://hifc.local/reference/source/" + name);
        }
        public static Guid BridgeGuid(ReferenceProperty property)
        {
            return Uuid5("https://hifc.local/reference/bridge/" + property.PropertySet + "/" + property.Binding + "/" + property.Property);
        }
        public static string BridgeName(ReferenceProperty property) { return "__HIFCV1_" + BridgeGuid(property).ToString("N"); }

        private static Guid Uuid5(string name)
        {
            var namespaceBytes = new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8").ToByteArray();
            Swap(namespaceBytes);
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var input = new byte[namespaceBytes.Length + nameBytes.Length];
            Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
            Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);
            using (var hash = SHA1.Create())
            {
                var bytes = hash.ComputeHash(input).Take(16).ToArray();
                bytes[6] = (byte)((bytes[6] & 15) | 80);
                bytes[8] = (byte)((bytes[8] & 63) | 128);
                Swap(bytes);
                return new Guid(bytes);
            }
        }
        private static void Swap(byte[] bytes)
        {
            Action<int, int> swap = (a, b) => { byte tmp = bytes[a]; bytes[a] = bytes[b]; bytes[b] = tmp; };
            swap(0, 3); swap(1, 2); swap(4, 5); swap(6, 7);
        }

        public static ParameterRead Resolve(IEnumerable<ParameterRead> reads, string name)
        {
            var matches = reads.Where(p => p.Name == name).ToArray();
            foreach (var origin in new[] { "instance", "type" })
            {
                var candidates = matches.Where(p => p.Origin == origin).ToArray();
                if (candidates.Length > 1) throw new InvalidOperationException("同名取值参数有歧义：" + name + " (" + origin + ")");
                var value = candidates.SingleOrDefault();
                if (value != null && value.HasValue && value.Value != null && !(value.Value is string && ((string)value.Value).Length == 0))
                    return value;
            }
            return null;
        }

        public static string ToBridgeValue(ParameterRead value)
        {
            if (value == null || !value.HasValue || value.Value == null) return null;
            if (value.Value is bool) return (bool)value.Value ? "1" : "0";
            if (value.Value is double) return ((double)value.Value).ToString("R", CultureInfo.InvariantCulture);
            if (value.Value is float) return ((float)value.Value).ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value.Value, CultureInfo.InvariantCulture);
        }
    }
}
