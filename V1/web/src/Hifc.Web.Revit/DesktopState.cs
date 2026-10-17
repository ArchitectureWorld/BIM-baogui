using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public sealed class DeviceCredential
    {
        public string Phone { get; set; }
        public string Credential { get; set; }
    }
    public sealed class DeviceStore
    {
        private readonly string root;
        public DeviceStore(string root) { this.root = Path.GetFullPath(root); Directory.CreateDirectory(this.root); }
        public string ActivePhone => ProtectedFile.Read<string>(Path.Combine(root, "active.bin"));
        public static string NormalizePhone(string phone)
        {
            phone = (phone ?? "").Trim().Replace(" ", ""); if (phone.StartsWith("+86", StringComparison.Ordinal)) phone = phone.Substring(3);
            if (!Regex.IsMatch(phone, "^1[3-9][0-9]{9}$")) throw new InvalidOperationException("请填写有效的大陆手机号码。"); return phone;
        }
        private string FileFor(string phone, bool pending) => Path.Combine(root, Canonical.Hash(NormalizePhone(phone)) + (pending ? ".pending.bin" : ".bin"));
        public DeviceCredential[] Candidates(string phone) => new[] { ProtectedFile.Read<DeviceCredential>(FileFor(phone, true)), ProtectedFile.Read<DeviceCredential>(FileFor(phone, false)) }.Where(c => c != null).ToArray();
        public DeviceCredential Stage(string phone)
        {
            phone = NormalizePhone(phone); var existing = ProtectedFile.Read<DeviceCredential>(FileFor(phone, true)); if (existing != null) return existing;
            var bytes = new byte[32]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var credential = new DeviceCredential { Phone = phone, Credential = Convert.ToBase64String(bytes) };
            ProtectedFile.Write(FileFor(phone, true), credential); return credential;
        }
        public void Accept(DeviceCredential credential)
        {
            if (credential == null || !Candidates(credential.Phone).Any(c => c.Credential == credential.Credential)) throw new InvalidOperationException("本机凭证与登录结果不匹配。");
            ProtectedFile.Write(FileFor(credential.Phone, false), credential); ProtectedFile.Write(Path.Combine(root, "active.bin"), credential.Phone);
            var pending = FileFor(credential.Phone, true); if (File.Exists(pending)) File.Delete(pending);
        }
    }
    public sealed class SavedWorkspace
    {
        public string DocumentKey { get; set; }
        public string ModelKind { get; set; } = "自动识别";
        public Dictionary<string, string> AssignedSlots { get; set; } = new Dictionary<string, string>();
        public EditDraft[] Drafts { get; set; } = new EditDraft[0];
        public CheckResult LastCheck { get; set; }
        public WriteReceipt PendingReceipt { get; set; }
        public WriteReceipt LastReceipt { get; set; }
    }
    public sealed class WorkspaceStore
    {
        private readonly string root;
        public WorkspaceStore(string root) { this.root = Path.GetFullPath(root); Directory.CreateDirectory(this.root); }
        private string FileFor(string key)
        {
            if (key == null || !Regex.IsMatch(key, "^[a-f0-9]{64}$")) throw new InvalidOperationException("草稿文档标识无效。"); return Path.Combine(root, key + ".bin");
        }
        public void Save(SavedWorkspace workspace) { if (workspace == null) throw new InvalidOperationException("草稿为空。"); ProtectedFile.Write(FileFor(workspace.DocumentKey), workspace); }
        public SavedWorkspace Load(string key) => ProtectedFile.Read<SavedWorkspace>(FileFor(key)) ?? new SavedWorkspace { DocumentKey = key };
    }
    internal static class ProtectedFile
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HIFC.0.5.0.Web.DesktopState.v1");
        internal static T Read<T>(string path)
        {
            if (!File.Exists(path)) return default(T);
            try { return JsonWire.Deserialize<T>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser))); }
            catch (Exception ex) when (ex is CryptographicException || ex is Newtonsoft.Json.JsonException || ex is DecoderFallbackException)
            { throw new InvalidOperationException("本机凭证或草稿无法读取，原文件已保留；请联系管理员，勿重新登记以重置期限。"); }
        }
        internal static void Write<T>(string path, T value)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonWire.Serialize(value)), Entropy, DataProtectionScope.CurrentUser));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
}
