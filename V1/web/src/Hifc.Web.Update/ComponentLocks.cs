using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Update
{
    public static class ComponentLocks
    {
        public const string ResourceName = "HIFC.Web.ComponentLocks.json";
        public static ComponentLock[] Load()
        {
            using (var input = typeof(ComponentLocks).Assembly.GetManifestResourceStream(ResourceName)) {
                if (input == null || input.Length > 65536) throw new InvalidOperationException("此 Web 更新器未嵌入组件锁，请安装完整新版发行包。");
                using (var reader = new StreamReader(input)) {
                    var locks = JsonWire.Deserialize<ComponentLock[]>(reader.ReadToEnd()); Validate(locks); return locks;
                }
            }
        }
        public static void Validate(ComponentLock[] locks)
        {
            if (locks == null || locks.Length != 2 || locks.Select(x => x?.ComponentId).Distinct().Count() != 2) throw new InvalidOperationException("Web 运行组件锁必须包含 Node 和平台同步程序。");
            foreach (var item in locks) ComponentTrust.ValidateLock(item);
            if (!locks.Any(x => x.ComponentId == "node" && x.Version == "25.4.0" && x.EntryPoint == "node.exe")
                || !locks.Any(x => x.ComponentId == "platform-sync" && x.Version == "0.2.1-web.1" && x.EntryPoint == "dist/src/cli.js")
                || locks.Any(x => x.MainReleaseId != locks[0].MainReleaseId || x.MainSourceCommit != locks[0].MainSourceCommit)) throw new InvalidOperationException("Web 运行组件版本或主包来源不一致。");
        }
        public static void ValidatePackage(ComponentLock[] locks, string installationRoot, string candidatePackagePath = null)
        {
            Validate(locks);
            var root = Path.GetFullPath(installationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var supplied = candidatePackagePath ?? Path.Combine(root, "package.json");
            var path = Path.GetFullPath(Path.IsPathRooted(supplied) ? supplied : Path.Combine(root, supplied));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("组件候选 metadata 必须是当前 Web 安装目录内的 package.json。");
            for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("组件候选 metadata 不接受链接或重定向目录。");
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) throw new InvalidOperationException("组件候选发行信息不存在或过大。");
            ClientPackageInfo package;
            try { package = JsonWire.Deserialize<ClientPackageInfo>(File.ReadAllText(path)); }
            catch (Newtonsoft.Json.JsonException ex) { throw new InvalidOperationException("组件候选发行信息格式无效。", ex); }
            if (package == null || package.Version != "0.5.0-Web" || package.AddInId != UpdateTrust.AddInId || package.ReleaseId != locks[0].MainReleaseId || package.SourceCommit != locks[0].MainSourceCommit
                || package.Files == null || package.Files.Length != UpdateTrust.PayloadNames.Length || package.Files.Any(x => x == null || x.Length < 1 || x.Length > 50L * 1024 * 1024 || !Regex.IsMatch(x.Sha256 ?? "", "^[a-f0-9]{64}$"))
                || !new HashSet<string>(package.Files.Select(x => x.Name), StringComparer.Ordinal).SetEquals(UpdateTrust.PayloadNames)) throw new InvalidOperationException("组件候选发行信息与六文件主包或 EXE 嵌入锁不一致，当前入口未变更。");
        }
        public static Task<ComponentInstallation[]> PrepareAsync(string installationRoot, CancellationToken cancellation) => PrepareAsync(installationRoot, cancellation, null);
        public static async Task<ComponentInstallation[]> PrepareAsync(string installationRoot, CancellationToken cancellation, string candidatePackagePath)
        {
            var locks = Load();
            ValidatePackage(locks, installationRoot, candidatePackagePath);
            var keyPath = Path.Combine(Path.GetFullPath(installationRoot), "update-public-key.json");
            if (!File.Exists(keyPath) || (File.GetAttributes(keyPath) & FileAttributes.ReparsePoint) != 0 || new FileInfo(keyPath).Length > 65536) throw new InvalidOperationException("未找到现有 Web 安装信任公钥。");
            var key = JsonWire.Deserialize<PublicSigningKey>(File.ReadAllText(keyPath));
            var result = new ComponentInstallation[locks.Length];
            using (var manager = new ComponentManager(installationRoot, new Uri(UpdateTrust.Server), key))
                for (var i = 0; i < locks.Length; i++) result[i] = await manager.PrepareAsync(locks[i], cancellation).ConfigureAwait(false);
            return result;
        }
    }
}
