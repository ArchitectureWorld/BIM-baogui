using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Update
{
    public static class UpdateClient
    {
        public static async Task<string> StageAsync(string installationRoot, string installedDirectory, Uri server, CancellationToken cancellation)
        {
            var root = Path.GetFullPath(installationRoot);
            var current = UpdateInstaller.Read<ClientPackageInfo>(UpdateInstaller.Within(root, Path.Combine(installedDirectory, "package.json")));
            if (!UpdateTrust.IsRelease(current?.ReleaseId)) throw new InvalidOperationException("重新安装本次新版后即可启用自动更新。");
            var key = UpdateInstaller.Read<PublicSigningKey>(UpdateInstaller.Within(root, Path.Combine(root, "update-public-key.json")));
            using (var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(90) }) {
                if (server.AbsoluteUri != UpdateTrust.Server && !(server.IsLoopback && server.Scheme == "http" && server.AbsolutePath == "/" && server.Query == "" && server.Fragment == "" && server.UserInfo == "")) throw new InvalidOperationException("更新源必须是 zetta HTTPS 服务。");
                ClientUpdateManifest offer;
                using (var response = await http.GetAsync(new Uri(server, "updates/latest"), HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false)) {
                    response.EnsureSuccessStatusCode();
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) using (var output = new MemoryStream()) {
                        await CopyBounded(input, output, 65536, cancellation).ConfigureAwait(false); offer = JsonWire.Deserialize<ClientUpdateManifest>(Encoding.UTF8.GetString(output.ToArray()));
                    }
                }
                UpdateTrust.Validate(offer, key, server);
                if (string.CompareOrdinal(current.ReleaseId, offer.ReleaseId) >= 0) return null;
                var cache = UpdateInstaller.Within(root, Path.Combine(root, "update-cache", offer.ReleaseId)); Directory.CreateDirectory(cache);
                var archive = UpdateInstaller.Within(root, Path.Combine(cache, "package.zip"));
                if (!File.Exists(archive) || new FileInfo(archive).Length != offer.Length || UpdateInstaller.HashFile(archive) != offer.Sha256) {
                    var temporary = UpdateInstaller.Within(root, archive + "." + Guid.NewGuid().ToString("N") + ".tmp");
                    try {
                        using (var response = await http.GetAsync(offer.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false)) {
                            response.EnsureSuccessStatusCode(); if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != offer.Length) throw new InvalidOperationException("更新下载大小与签名清单不符。");
                            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) using (var output = File.Create(temporary)) await CopyBounded(input, output, offer.Length, cancellation).ConfigureAwait(false);
                        }
                        if (new FileInfo(temporary).Length != offer.Length || UpdateInstaller.HashFile(temporary) != offer.Sha256) throw new InvalidOperationException("更新包校验失败，保留当前版本。");
                        if (File.Exists(archive)) File.Replace(temporary, archive, null); else File.Move(temporary, archive);
                    } finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                var pending = UpdateInstaller.Within(root, Path.Combine(cache, "pending.json")); UpdateInstaller.AtomicText(pending, JsonWire.Serialize(new PendingUpdate { Manifest = offer })); return pending;
            }
        }
        private static async Task CopyBounded(Stream input, Stream output, long maximum, CancellationToken cancellation)
        {
            var buffer = new byte[16384]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0) { total += read; if (total > maximum) throw new InvalidOperationException("更新响应超出允许大小。"); await output.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false); }
        }
        public static Process StartWorker(string installedDirectory, string pending, int pid, long startedUtcTicks, string isolatedLocalAppData = null, string isolatedAppData = null)
        {
            var start = new ProcessStartInfo(Path.Combine(installedDirectory, "Hifc.Web.Update.exe"), "--apply \"" + pending + "\" --wait-pid " + pid + " --started " + startedUtcTicks) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            // Optional roots are used only by the owned integration harness; no global environment is modified.
            if (isolatedLocalAppData != null) start.EnvironmentVariables["LOCALAPPDATA"] = isolatedLocalAppData;
            if (isolatedAppData != null) start.EnvironmentVariables["APPDATA"] = isolatedAppData;
            return Process.Start(start);
        }
    }
}
