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
    public sealed class PersonProfile
    {
        public string Phone { get; set; }
        public string RealName { get; set; }
    }
    public sealed class ProfileStore
    {
        private readonly string root;
        public ProfileStore(string root) { this.root = Path.GetFullPath(root); Directory.CreateDirectory(this.root); }
        public PersonProfile Current => ProtectedFile.Read<PersonProfile>(Path.Combine(root, "person.bin"));
        public string LegacyPhone => ProtectedFile.Read<string>(Path.Combine(root, "active.bin"));
        public static string NormalizePhone(string phone)
        {
            phone = (phone ?? "").Trim().Replace(" ", ""); if (phone.StartsWith("+86", StringComparison.Ordinal)) phone = phone.Substring(3);
            if (!Regex.IsMatch(phone, "^1[3-9][0-9]{9}$")) throw new InvalidOperationException("请填写有效的大陆手机号码。"); return phone;
        }
        public void Save(string phone, string name)
        {
            phone = NormalizePhone(phone); name = (name ?? "").Trim();
            if (name.Length < 2 || name.Length > 40 || name.Any(char.IsControl)) throw new InvalidOperationException("请填写 2–40 个字符的真实姓名。");
            ProtectedFile.Write(Path.Combine(root, "person.bin"), new PersonProfile { Phone = phone, RealName = name });
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
