using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class ManualWrite
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public string PreviousValue { get; set; }
        public string WrittenUtc { get; set; }
        public int Count { get; set; }
    }
    internal sealed class ManagementState
    {
        public int Version { get; set; } = 1;
        public string DocumentKey { get; set; }
        public string Stage { get; set; }
        public string Kind { get; set; }
        public ModelSnapshot Snapshot { get; set; }
        public EditRequest[] Drafts { get; set; } = new EditRequest[0];
        public ManualWrite[] ManualWrites { get; set; } = new ManualWrite[0];
        public UndergroundSettings Underground { get; set; } = new UndergroundSettings();
    }
    internal sealed class ManagementStateStore
    {
        readonly string path, key;
        static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
        internal ManagementStateStore(string directory, string key)
        {
            this.key = key;
            using (var sha = SHA256.Create()) path = Path.Combine(directory, BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant() + ".json");
        }
        internal ManagementState Load()
        {
            if (!File.Exists(path)) return new ManagementState { DocumentKey = key };
            var value = Serializer().Deserialize<ManagementState>(File.ReadAllText(path, Encoding.UTF8));
            if (value == null || value.Version != 1 || value.DocumentKey != key) throw new InvalidOperationException("暂存记录与当前模型不匹配。");
            value.Drafts = value.Drafts ?? new EditRequest[0]; value.ManualWrites = value.ManualWrites ?? new ManualWrite[0];
            value.Underground=value.Underground??new UndergroundSettings();
            return value;
        }
        internal void Save(ManagementState value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, Serializer().Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
}
