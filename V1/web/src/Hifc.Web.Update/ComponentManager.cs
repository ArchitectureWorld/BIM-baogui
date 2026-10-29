using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hifc.Web.Contracts;
using Hifc.Web.Client;

namespace Hifc.Web.Update
{
    public sealed class ComponentInstallation
    {
        public string Directory { get; set; }
        public string EntryPoint { get; set; }
        public string Version { get; set; }
        public string ComponentId { get; set; }
    }
    public sealed class ComponentManager : IDisposable
    {
        public long MaximumExpandedBytes { get; set; } = 768L * 1024 * 1024;
        public int MaximumEntries { get; set; } = 30000;
        public TimeSpan PreparationTimeout { get; set; } = TimeSpan.FromMinutes(5);
        private readonly string root;
        private readonly Uri server;
        private readonly PublicSigningKey key;
        private readonly HttpClient http;
        private const string ArchiveName = ".hifc-archive.zip", ManifestName = ".hifc-manifest.json";
        public ComponentManager(string installationRoot, Uri server, PublicSigningKey key)
            : this(installationRoot, server, key, new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { }
        internal ComponentManager(string installationRoot, Uri server, PublicSigningKey key, HttpMessageHandler handler)
        {
            ComponentTrust.ValidateServer(server);
            root = Path.GetFullPath(Path.Combine(installationRoot, "components")); CheckLinks(root);
            this.server = server; this.key = key;
            http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        }
        private string Within(string relative)
        {
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("组件路径超出 Web 目录。");
            CheckLinks(path); return path;
        }
        private static void CheckLinks(string path)
        {
            for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("组件路径不允许使用链接。");
        }
        private string VersionPath(ComponentLock value) => Within(Path.Combine(value.ComponentId, value.Version + "-" + Canonical.Hash(ComponentTrust.LockPayload(value)).Substring(0, 24)));
        private static string Hash(Stream input) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
        private static string FileHash(string path) { using (var input = File.OpenRead(path)) return Hash(input); }
        public ComponentInstallation GetInstalled(ComponentLock component)
        {
            ComponentTrust.ValidateLock(component); var directory = VersionPath(component);
            if (!Directory.Exists(directory)) return null;
            try {
                var manifestPath = Path.Combine(directory, ManifestName); CheckLinks(manifestPath);
                if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 65536) throw new InvalidOperationException("已安装组件清单无效。");
                var manifest = JsonWire.Deserialize<SignedComponentManifest>(File.ReadAllText(manifestPath));
                ComponentTrust.Validate(manifest, component, key, server);
                VerifyArchive(Path.Combine(directory, ArchiveName), component);
                using (var stream = File.OpenRead(Path.Combine(directory, ArchiveName))) using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) {
                    var entries = Entries(zip);
                    foreach (var entry in entries.Where(e => !e.FullName.EndsWith("/"))) {
                        var path = Path.Combine(directory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)); CheckLinks(path);
                        if (!File.Exists(path) || new FileInfo(path).Length != entry.Length) throw new InvalidOperationException("已安装组件内容缺失。");
                        using (var expected = entry.Open()) if (Hash(expected) != FileHash(path)) throw new InvalidOperationException("已安装组件内容已变化，请重试安装。");
                    }
                    var names = new HashSet<string>(entries.Select(e => e.FullName), StringComparer.OrdinalIgnoreCase);
                    foreach (var path in InstalledPaths(directory)) {
                        var relative = path.Substring(directory.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                        if (File.Exists(path) && relative != ArchiveName && relative != ManifestName && !names.Contains(relative)) throw new InvalidOperationException("已安装组件含未签名文件。");
                    }
                }
                var entryPoint = Path.Combine(directory, component.EntryPoint.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(entryPoint)) throw new InvalidOperationException("组件入口不存在。");
                return new ComponentInstallation { Directory = directory, EntryPoint = entryPoint, Version = component.Version, ComponentId = component.ComponentId };
            } catch (InvalidDataException ex) { throw new InvalidOperationException("已安装组件归档无效。", ex); }
        }
        private static System.Collections.Generic.IEnumerable<string> InstalledPaths(string directory)
        {
            foreach (var path in Directory.GetFileSystemEntries(directory)) {
                CheckLinks(path); yield return path;
                if (Directory.Exists(path)) foreach (var nested in InstalledPaths(path)) yield return nested;
            }
        }
        public async Task<ComponentInstallation> PrepareAsync(ComponentLock component, CancellationToken cancellation)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(PreparationTimeout); cancellation = deadline.Token;
            cancellation.ThrowIfCancellationRequested(); ComponentTrust.ValidateLock(component);
            // A verified immutable version is usable without contacting the server.
            var installed = GetInstalled(component); if (installed != null) return installed;
            CheckLinks(root); Directory.CreateDirectory(root);
            using (var lease = await AcquireAsync(cancellation).ConfigureAwait(false)) {
                installed = GetInstalled(component); if (installed != null) return installed;
                var staging = Within(".staging-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
                try {
                    SignedComponentManifest manifest;
                    using (var response = await http.GetAsync(ComponentTrust.ManifestUri(server, component), HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false)) {
                        CheckResponse(response, 65536, false);
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) using (var output = new MemoryStream()) {
                            await CopyBounded(input, output, 65536, cancellation).ConfigureAwait(false);
                            manifest = JsonWire.Deserialize<SignedComponentManifest>(Encoding.UTF8.GetString(output.ToArray()));
                        }
                    }
                    ComponentTrust.Validate(manifest, component, key, server);
                    var archive = Path.Combine(staging, ArchiveName);
                    using (var response = await http.GetAsync(manifest.ArchiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false)) {
                        CheckResponse(response, component.ArchiveLength, true);
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) using (var output = File.Create(archive)) await CopyBounded(input, output, component.ArchiveLength, cancellation).ConfigureAwait(false);
                    }
                    VerifyArchive(archive, component);
                    using (var input = File.OpenRead(archive)) using (var zip = new ZipArchive(input, ZipArchiveMode.Read)) {
                        foreach (var entry in Entries(zip)) {
                            cancellation.ThrowIfCancellationRequested(); var path = Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                            if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(path); continue; }
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                            using (var source = entry.Open()) using (var output = new FileStream(path, FileMode.CreateNew)) await CopyBounded(source, output, entry.Length, cancellation).ConfigureAwait(false);
                            if (new FileInfo(path).Length != entry.Length) throw new InvalidOperationException("组件解压长度不符。");
                        }
                    }
                    if (!File.Exists(Path.Combine(staging, component.EntryPoint.Replace('/', Path.DirectorySeparatorChar)))) throw new InvalidOperationException("组件入口不存在。");
                    File.WriteAllText(Path.Combine(staging, ManifestName), JsonWire.Serialize(manifest), new UTF8Encoding(false));
                    cancellation.ThrowIfCancellationRequested(); var destination = VersionPath(component); Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    Directory.Move(staging, destination); // Same-volume atomic publication; never overwrite any existing version.
                    installed = GetInstalled(component);
                    AtomicText(Within(component.ComponentId + ".active.json"), JsonWire.Serialize(component));
                    return installed;
                } catch (InvalidDataException ex) { throw new InvalidOperationException("组件归档无效。", ex); }
                finally { if (Directory.Exists(staging)) { CheckLinks(staging); Directory.Delete(staging, true); } }
            }
        }
        private async Task<FileStream> AcquireAsync(CancellationToken cancellation)
        {
            var until = DateTime.UtcNow.AddMinutes(5);
            while (true) {
                cancellation.ThrowIfCancellationRequested();
                try { return new FileStream(Within(".prepare.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { if (DateTime.UtcNow >= until) throw new InvalidOperationException("另一个组件准备任务仍在运行，请稍后重试。"); await Task.Delay(100, cancellation).ConfigureAwait(false); }
            }
        }
        private void VerifyArchive(string path, ComponentLock component)
        {
            CheckLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length != component.ArchiveLength || FileHash(path) != component.ArchiveSha256) throw new InvalidOperationException("组件归档大小或 SHA256 校验失败。");
        }
        private List<ZipArchiveEntry> Entries(ZipArchive zip)
        {
            if (MaximumEntries < 1 || zip.Entries.Count < 1 || zip.Entries.Count > MaximumEntries) throw new InvalidOperationException("组件归档条目数超限。");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
            foreach (var entry in zip.Entries) {
                var name = entry.FullName; var clean = name.TrimEnd('/'); var mode = (entry.ExternalAttributes >> 16) & 0xF000;
                if (name.Length > 220 || name.Contains('\\') || name.StartsWith("/") || clean.Length == 0 || (mode != 0 && mode != 0x8000 && mode != 0x4000) || (entry.ExternalAttributes & 0x400) != 0 || !seen.Add(clean)) throw new InvalidOperationException("组件归档包含链接或重复路径。");
                foreach (var part in clean.Split('/')) {
                    if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") || part.Any(c => c < 32 || "<>:\"|?*".IndexOf(c) >= 0) || Regex.IsMatch(part, "^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\\.|$)", RegexOptions.IgnoreCase)) throw new InvalidOperationException("组件归档路径无效。");
                }
                if (clean.Split('/')[0].Equals(ArchiveName, StringComparison.OrdinalIgnoreCase) || clean.Split('/')[0].Equals(ManifestName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("组件归档使用保留文件名。");
                if (entry.Length < 0 || entry.Length > MaximumExpandedBytes - expanded) throw new InvalidOperationException("组件解压总大小超限。"); expanded += entry.Length;
            }
            var files = new HashSet<string>(zip.Entries.Where(e => !e.FullName.EndsWith("/")).Select(e => e.FullName), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries) {
                var parent = entry.FullName.TrimEnd('/');
                while (parent.LastIndexOf('/') >= 0) { parent = parent.Substring(0, parent.LastIndexOf('/')); if (files.Contains(parent)) throw new InvalidOperationException("组件归档文件与父目录路径冲突。"); }
            }
            return zip.Entries.ToList();
        }
        private static void CheckResponse(HttpResponseMessage response, long size, bool exact)
        {
            if (response.StatusCode != System.Net.HttpStatusCode.OK || (response.Content.Headers.ContentLength.HasValue && (exact ? response.Content.Headers.ContentLength.Value != size : response.Content.Headers.ContentLength.Value > size))) throw new InvalidOperationException("组件下载被重定向、失败或大小不符。");
        }
        private static async Task CopyBounded(Stream input, Stream output, long maximum, CancellationToken cancellation)
        {
            var buffer = new byte[65536]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0) { if (read > maximum - total) throw new InvalidOperationException("组件响应超出允许大小。"); total += read; await output.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false); }
        }
        private static void AtomicText(string path, string text)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, text, new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void Dispose() => http.Dispose();
    }
}
