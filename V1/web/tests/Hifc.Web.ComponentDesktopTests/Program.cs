using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Update;

internal static class Program
{
    private static int Main(string[] args)
    {
        try { return Execute(args); }
        catch (Exception ex) { for (var error = ex; error != null; error = error.InnerException) { Console.Error.WriteLine(error.GetType().FullName); Console.Error.WriteLine(error.Message); } return 1; }
    }
    private static int Execute(string[] args)
    {
        if (args.Length != 2) throw new InvalidOperationException("Pass the packaging output and a fresh isolated Web test installation root.");
        var package = Path.GetFullPath(args[0]); var root = Path.GetFullPath(args[1]);
        if (Directory.Exists(root)) throw new InvalidOperationException("Use a fresh isolated test root.");
        var locks = JsonWire.Deserialize<ComponentLock[]>(File.ReadAllText(Path.Combine(package, "component-locks.json"))); ComponentLocks.Validate(locks);
        using (var rsa = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false }) {
            var parameters = rsa.ExportParameters(false);
            var key = new PublicSigningKey { Modulus = Convert.ToBase64String(parameters.Modulus), Exponent = Convert.ToBase64String(parameters.Exponent) };
            var handler = new PackageHandler(package, locks, rsa);
            using (var manager = new ComponentManager(root, new Uri(UpdateTrust.Server), key, handler)) using (var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3))) {
                var installed = locks.Select(item => manager.PrepareAsync(item, cancellation.Token).GetAwaiter().GetResult()).ToArray();
                handler.Offline = true;
                foreach (var item in locks) if (manager.PrepareAsync(item, cancellation.Token).GetAwaiter().GetResult() == null) throw new InvalidOperationException("Offline installed component unavailable.");
                var node = installed.Single(x => x.ComponentId == "node"); var sync = installed.Single(x => x.ComponentId == "platform-sync");
                var version = Run(node.EntryPoint, "--version", node.Directory).Trim();
                if (version != "v25.4.0") throw new InvalidOperationException("Installed node version mismatch.");
                var help = Run(node.EntryPoint, "\"" + sync.EntryPoint + "\" help", sync.Directory);
                if (!help.Contains("\"version\":\"0.2.0\"") && !help.Contains("\"version\":\"0.2.1-web.1\"")) throw new InvalidOperationException("Installed CLI version mismatch.");
                var workerStart = new ProcessStartInfo(node.EntryPoint, "-e \"setTimeout(()=>{},60000)\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = node.Directory };
                workerStart.EnvironmentVariables.Remove("NODE_OPTIONS"); workerStart.EnvironmentVariables.Remove("NODE_PATH");
                using (var worker = Process.Start(workerStart)) {
                    try {
                        var next = JsonWire.Deserialize<ComponentLock>(JsonWire.Serialize(locks.Single(x => x.ComponentId == "node")));
                        next.MainReleaseId = "0.5.0-dev-20991231T235959Z"; handler.NextLock = next; handler.Offline = false;
                        var newer = manager.PrepareAsync(next, cancellation.Token).GetAwaiter().GetResult();
                        if (worker.HasExited || newer.Directory == node.Directory || !File.Exists(node.EntryPoint) || manager.GetInstalled(locks.Single(x => x.ComponentId == "node")) == null) throw new InvalidOperationException("Active worker runtime was replaced or removed.");
                    } finally { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); } }
                }
                Console.WriteLine("PASS real net48 signed extraction, offline reuse, Node 25.4.0, CLI help and active Node process retention");
                Console.WriteLine(JsonWire.Serialize(installed));
            }
        }
        return 0;
    }
    private static string Run(string executable, string arguments, string directory)
    {
        var start = new ProcessStartInfo(executable, arguments) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.EnvironmentVariables.Remove("NODE_OPTIONS"); start.EnvironmentVariables.Remove("NODE_PATH");
        using (var process = Process.Start(start)) {
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000)) { process.Kill(); throw new InvalidOperationException("Runtime probe timed out."); }
            if (process.ExitCode != 0) throw new InvalidOperationException("Runtime probe failed: " + error.GetAwaiter().GetResult());
            return output.GetAwaiter().GetResult();
        }
    }
    private sealed class PackageHandler : HttpMessageHandler
    {
        private readonly string package; private readonly ComponentLock[] locks; private readonly RSA rsa;
        public bool Offline; public ComponentLock NextLock;
        public PackageHandler(string package, ComponentLock[] locks, RSA rsa) { this.package = package; this.locks = locks; this.rsa = rsa; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (Offline) throw new InvalidOperationException("Offline proof: no network access is available.");
            var original = locks.Single(value => request.RequestUri.AbsolutePath.Contains("/" + value.ComponentId + "/"));
            var item = NextLock != null && request.RequestUri.AbsolutePath.Contains("/" + NextLock.MainReleaseId + "/") ? NextLock : original;
            HttpContent content;
            if (request.RequestUri.AbsolutePath.EndsWith("/manifest", StringComparison.Ordinal)) {
                var manifest = new SignedComponentManifest { Lock = item, ArchiveUrl = ComponentTrust.ManifestUri(new Uri(UpdateTrust.Server), item).AbsoluteUri.Replace("/manifest", "/archive.zip") };
                manifest.Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(ComponentTrust.Payload(manifest)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                if (!rsa.VerifyData(Encoding.UTF8.GetBytes(ComponentTrust.Payload(manifest)), Convert.FromBase64String(manifest.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new InvalidOperationException("Fixture signature failed its own RSA check.");
                content = new StringContent(JsonWire.Serialize(manifest));
            } else content = new StreamContent(File.OpenRead(Path.Combine(package, "components", original.MainReleaseId, original.ComponentId, original.Version, "archive.zip")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
