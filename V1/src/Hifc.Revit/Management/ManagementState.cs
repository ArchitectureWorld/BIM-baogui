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
        public ReportingWorkbookDecision[] PendingWorkbooks { get; set; } = new ReportingWorkbookDecision[0];
        public UndergroundSettings Underground { get; set; } = new UndergroundSettings();
        public PlanningSettings Planning { get; set; } = new PlanningSettings();
        internal void RebindDocumentKey(string key)
        {
            DocumentKey=key;
            if(Snapshot!=null)Snapshot.DocumentKey=key;
        }
        internal void EnsureExportReady()
        {
            if (Drafts?.Length > 0 || PendingWorkbooks?.Length > 0)
                throw new InvalidOperationException("管理窗口还有 " + (Drafts?.Length??0) + " 项属性修改及 "+(PendingWorkbooks?.Length??0)+" 张报规资料未写入。请打开模型属性管理，点“4 写入 Revit”并复检后再导出；若不需要这些修改，可在底部清除暂存。IFC 尚未生成。");
        }
    }
    internal sealed class ManagementStateStore
    {
        readonly string path, key;
        internal bool Exists => File.Exists(path);
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
            value.Planning=value.Planning??new PlanningSettings();
            value.PendingWorkbooks=value.PendingWorkbooks??new ReportingWorkbookDecision[0];
            return value;
        }
        internal void Save(ManagementState value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, Serializer().Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) ReplaceWithRetry(temporary, path); else File.Move(temporary, path);
        }
        internal static void ReplaceWithRetry(string temporary, string path, Action<string,string> replace = null, Action<int> delay = null)
        {
            var replaceFile = replace ?? ((source, target) => File.Replace(source, target, null));
            var wait = delay ?? new Action<int>(System.Threading.Thread.Sleep);
            for (int retry = 0; ; retry++)
            {
                try { replaceFile(temporary, path); return; }
                catch (IOException error)
                {
                    // Retry only this failed atomic replacement while both recovery files remain.
                    if (error.HResult != unchecked((int)0x80070497) || retry >= 3 ||
                        !File.Exists(temporary) || !File.Exists(path)) throw;
                    wait(100 << retry);
                }
            }
        }
    }
}
