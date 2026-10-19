using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Update
{
    public sealed class PendingUpdate { public ClientUpdateManifest Manifest { get; set; } }
    public static class UpdateInstaller
    {
        public static string InstallationRoot => Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HIFC", "0.5.0-Web", "Revit2020");
        public static string AddInManifestPath => Path.Combine(Environment.GetEnvironmentVariable("APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2020", "Hifc.Web.addin");
        public static string HashFile(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        public static string Within(string root, string path)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            path = Path.GetFullPath(path);
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("更新路径超出 Web 安装目录。");
            var current = path;
            while (current != null && (current.Equals(root, StringComparison.OrdinalIgnoreCase) || current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("更新目录不允许使用链接。");
                current = Path.GetDirectoryName(current);
            }
            return path;
        }
        public static T Read<T>(string path) { try { return JsonWire.Deserialize<T>(File.ReadAllText(path)); } catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is ArgumentException) { throw new InvalidOperationException("更新资料格式无效。", ex); } }
        public static void AtomicText(string path, string value)
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temp, value, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void WaitForRevitExit(int pid, long startedUtcTicks)
        {
            Process process;
            try { process = Process.GetProcessById(pid); } catch (ArgumentException) { return; }
            using (process) {
                if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != startedUtcTicks) return;
                if (!string.Equals(process.ProcessName, "Revit", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("更新等待目标不是本轮 Revit 进程。");
                process.WaitForExit();
            }
        }
        private static void CheckOwnedManifest(string root, string manifest)
        {
            if (!File.Exists(manifest) || (File.GetAttributes(manifest) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("未找到所属 Web 插件清单。");
            XDocument document;
            try { using (var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document = XDocument.Load(reader); }
            catch (XmlException ex) { throw new InvalidOperationException("已有 Web 清单无效。", ex); }
            var addins = document.Root?.Elements("AddIn").ToArray();
            if (document.Root?.Name != "RevitAddIns" || addins == null || addins.Length != 1 || (string)addins[0].Element("AddInId") != UpdateTrust.AddInId) throw new InvalidOperationException("已有清单不属于 Web 插件。");
            var assembly = Within(root, (string)addins[0].Element("Assembly") ?? "");
            if (!string.Equals(Path.GetFileName(assembly), "Hifc.Web.Revit.dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("已有清单的程序集不属于 Web 插件。");
        }
        private static void CheckPackage(ClientPackageInfo package, ClientUpdateManifest offer)
        {
            if (package == null || package.Version != "0.5.0-Web" || package.AddInId != UpdateTrust.AddInId || package.ReleaseId != offer.ReleaseId || package.SourceCommit != offer.SourceCommit
                || package.Files == null || package.Files.Length != UpdateTrust.PayloadNames.Length
                || !package.Files.Select(file => file.Name).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(UpdateTrust.PayloadNames.OrderBy(name => name, StringComparer.Ordinal))) throw new InvalidOperationException("更新包的产品、版本或文件清单不匹配。");
            if (package.Files.Any(file => file.Length < 1 || file.Length > 40 * 1024 * 1024 || !System.Text.RegularExpressions.Regex.IsMatch(file.Sha256 ?? "", "^[a-f0-9]{64}$"))) throw new InvalidOperationException("更新文件大小或散列无效。");
        }
        private static void CheckFiles(string root, ClientPackageInfo package)
        {
            foreach (var file in package.Files) {
                var path = Within(root, Path.Combine(root, file.Name));
                if (!File.Exists(path) || new FileInfo(path).Length != file.Length || HashFile(path) != file.Sha256) throw new InvalidOperationException("更新文件校验失败：" + file.Name);
            }
        }
        private static string ManifestXml(string assembly) => new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("RevitAddIns", new XElement("AddIn", new XAttribute("Type", "Application"),
            new XElement("Name", "HIFC-V1-Web"), new XElement("Assembly", assembly), new XElement("AddInId", UpdateTrust.AddInId), new XElement("FullClassName", "Hifc.Web.Revit.WebApp"), new XElement("VendorId", "HIFC"), new XElement("VendorDescription", "HIFC Web")))).ToString();
        public static bool Apply(string installationRoot, string addInManifestPath, string pendingPath)
        {
            var root = Path.GetFullPath(installationRoot); pendingPath = Within(root, pendingPath);
            var pending = Read<PendingUpdate>(pendingPath); var offer = pending?.Manifest;
            var key = Read<PublicSigningKey>(Within(root, Path.Combine(root, "update-public-key.json"))); UpdateTrust.Validate(offer, key);
            var cache = Within(root, Path.Combine(root, "update-cache", offer.ReleaseId));
            if (!pendingPath.Equals(Path.Combine(cache, "pending.json"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("更新缓存路径不匹配。");
            var currentPath = Within(root, Path.Combine(root, "package.json")); var current = Read<ClientPackageInfo>(currentPath);
            if (!UpdateTrust.IsRelease(current?.ReleaseId)) throw new InvalidOperationException("当前安装版本资料无效，请重新安装 Web 版。");
            if (string.CompareOrdinal(current.ReleaseId, offer.ReleaseId) >= 0) return false;
            CheckOwnedManifest(root, addInManifestPath);
            var archive = Within(root, Path.Combine(cache, "package.zip"));
            if (new FileInfo(archive).Length != offer.Length || HashFile(archive) != offer.Sha256) throw new InvalidOperationException("更新包散列或大小校验失败，保留当前版本。");
            var releases = Within(root, Path.Combine(root, "releases")); Directory.CreateDirectory(releases);
            var staging = Within(root, Path.Combine(releases, offer.ReleaseId + ".staging-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(staging);
            var target = Within(root, Path.Combine(releases, offer.ReleaseId)); ClientPackageInfo package;
            try {
                using (var zip = new ZipArchive(File.OpenRead(archive), ZipArchiveMode.Read)) {
                    var entries = new System.Collections.Generic.Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in zip.Entries) {
                        var name = entry.FullName.Replace('\\', '/');
                        if (!name.StartsWith("HIFC-0.5.0-Web/", StringComparison.Ordinal) || name.Split('/').Any(part => part == ".." || part == "." || part.Contains(":")) || name.Length > 240 || entry.Length > 40 * 1024 * 1024 || entries.ContainsKey(name)) throw new InvalidOperationException("更新压缩包路径或文件无效。");
                        entries.Add(name, entry);
                    }
                    if (!entries.TryGetValue("HIFC-0.5.0-Web/package.json", out var metadata) || metadata.Length > 65536) throw new InvalidOperationException("更新包缺少版本资料。");
                    using (var reader = new StreamReader(metadata.Open(), Encoding.UTF8, true)) package = JsonWire.Deserialize<ClientPackageInfo>(reader.ReadToEnd());
                    CheckPackage(package, offer);
                    foreach (var file in package.Files) {
                        if (!entries.TryGetValue("HIFC-0.5.0-Web/client/" + file.Name, out var entry) || entry.Length != file.Length) throw new InvalidOperationException("更新文件缺失或大小不符。");
                        using (var input = entry.Open()) using (var output = File.Create(Within(root, Path.Combine(staging, file.Name)))) input.CopyTo(output);
                    }
                    CheckFiles(staging, package); AtomicText(Path.Combine(staging, "package.json"), JsonWire.Serialize(package));
                }
                if (Directory.Exists(target)) {
                    var retained = Read<ClientPackageInfo>(Within(root, Path.Combine(target, "package.json"))); CheckPackage(retained, offer); CheckFiles(target, package);
                } else Directory.Move(staging, target);
                var previousManifest = File.ReadAllText(addInManifestPath); var previousPackage = File.ReadAllText(currentPath);
                try {
                    CheckOwnedManifest(root, addInManifestPath);
                    AtomicText(addInManifestPath, ManifestXml(Path.Combine(target, "Hifc.Web.Revit.dll")));
                    AtomicText(currentPath, JsonWire.Serialize(package));
                } catch {
                    AtomicText(addInManifestPath, previousManifest); AtomicText(currentPath, previousPackage); throw;
                }
                try { AtomicText(Within(root, Path.Combine(root, "update-status.json")), JsonWire.Serialize(new { Status = "updated", ReleaseId = offer.ReleaseId, Detail = "新版已安装，下次启动生效。" })); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException) { }
                return true;
            } catch (Exception ex) when (!(ex is InvalidOperationException)) { throw new InvalidOperationException("更新未完成，保留当前版本。", ex); }
            finally {
                // Only this invocation's checked, generated staging directory may be removed.
                if (Directory.Exists(staging)) { Within(root, staging); Directory.Delete(staging, true); }
            }
        }
    }
}
