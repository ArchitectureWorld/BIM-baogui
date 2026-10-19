using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Update;

internal static class UpdateInstallCases
{
    internal static void Run(Action<string, Action> test)
    {
        void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe client update was accepted."); }
        test("verified update activates a version directory and retains old client account drafts and Local manifest", () => {
            using (var f = new Fixture()) {
                if (!UpdateInstaller.Apply(f.Root, f.Addin, f.Pending)) throw new Exception("Valid update was not activated.");
                if (!File.ReadAllText(f.Addin).Contains(f.Offer.ReleaseId) || File.ReadAllText(Path.Combine(f.Root, "Hifc.Web.Revit.dll")) != "old client") throw new Exception("Activation overwrote the loaded client or missed the new version.");
                if (File.ReadAllText(f.Draft) != "remembered draft" || File.ReadAllText(f.Person) != "remembered person" || File.ReadAllText(f.Local) != "Local protected") throw new Exception("Update changed user state or the Local add-in.");
                foreach (var file in f.Package.Files) if (Hash(File.ReadAllBytes(Path.Combine(f.Root, "releases", f.Offer.ReleaseId, file.Name))) != file.Sha256) throw new Exception("Activated payload differs from the package.");
            }
        });
        test("tampered download retains the active client and manifest", () => {
            using (var f = new Fixture()) {
                File.AppendAllText(f.Archive, "tampered"); var before = File.ReadAllText(f.Addin);
                Reject(() => UpdateInstaller.Apply(f.Root, f.Addin, f.Pending));
                if (File.ReadAllText(f.Addin) != before) throw new Exception("Tampered update changed the active manifest.");
            }
        });
        test("an activation write failure rolls the manifest back to the previous client", () => {
            using (var f = new Fixture()) {
                var before = File.ReadAllText(f.Addin); var metadata = Path.Combine(f.Root, "package.json"); var old = File.ReadAllText(metadata);
                using (var locked = new FileStream(metadata, FileMode.Open, FileAccess.Read, FileShare.Read)) Reject(() => UpdateInstaller.Apply(f.Root, f.Addin, f.Pending));
                if (File.ReadAllText(f.Addin) != before || File.ReadAllText(metadata) != old) throw new Exception("Failed activation left a new or mixed active version.");
            }
        });
        test("signed path traversal or mismatched package metadata cannot escape the update directory", () => {
            foreach (var mode in new[] { "traversal", "wrong-release" }) using (var f = new Fixture(mode)) {
                var before = File.ReadAllText(f.Addin); Reject(() => UpdateInstaller.Apply(f.Root, f.Addin, f.Pending));
                if (File.ReadAllText(f.Addin) != before || File.Exists(Path.Combine(f.Root, "escaped.txt"))) throw new Exception("Rejected package changed installed data.");
            }
        });
        test("newer installed releases and an unowned manifest are protected", () => {
            using (var f = new Fixture()) {
                File.WriteAllText(Path.Combine(f.Root, "package.json"), JsonWire.Serialize(new ClientPackageInfo { ReleaseId = "0.5.0-dev-20261020T120000Z" }));
                var before = File.ReadAllText(f.Addin); if (UpdateInstaller.Apply(f.Root, f.Addin, f.Pending) || File.ReadAllText(f.Addin) != before) throw new Exception("Updater downgraded a newer client.");
            }
            using (var f = new Fixture()) {
                File.WriteAllText(f.Addin, "<RevitAddIns><AddIn><AddInId>other</AddInId></AddIn></RevitAddIns>");
                Reject(() => UpdateInstaller.Apply(f.Root, f.Addin, f.Pending));
            }
        });
        test("the update worker refuses to wait on an unrelated running process", () => {
            using (var current = System.Diagnostics.Process.GetCurrentProcess()) Reject(() => UpdateInstaller.WaitForRevitExit(current.Id, current.StartTime.ToUniversalTime().Ticks));
        });
        test("the standalone update executable activates the signed package with its isolated user directories", () => {
            using (var f = new Fixture()) using (var worker = UpdateClient.StartWorker(Path.GetDirectoryName(typeof(UpdateInstaller).Assembly.Location), f.Pending, int.MaxValue, 1, f.LocalAppData, f.AppData)) {
                if (!worker.WaitForExit(20000)) throw new Exception("Standalone update worker did not finish.");
                if (worker.ExitCode != 0) throw new Exception("Standalone update worker failed: " + File.ReadAllText(Path.Combine(f.Root, "update-status.json")));
                if (!File.ReadAllText(f.Addin).Contains(f.Offer.ReleaseId)) throw new Exception("Standalone update worker did not activate the verified version.");
            }
        });
        test("actual update HTTP download stages the signed package without changing the active client", () => {
            using (var f = new Fixture()) using (var server = new HttpFixture(f, false, false)) {
                var before = File.ReadAllText(f.Addin);
                var pending = UpdateClient.StageAsync(f.Root, f.Root, server.Address, CancellationToken.None).GetAwaiter().GetResult();
                if (pending != f.Pending || !File.Exists(pending) || UpdateInstaller.HashFile(f.Archive) != f.Offer.Sha256 || server.Requests != 2 || File.ReadAllText(f.Addin) != before) throw new Exception("HTTP update was not staged independently from activation.");
            }
        });
        test("redirected or corrupted update responses keep the current version", () => {
            foreach (var redirect in new[] { true, false }) using (var f = new Fixture()) using (var server = new HttpFixture(f, redirect, !redirect)) {
                var before = File.ReadAllText(f.Addin); bool rejected = false;
                try { UpdateClient.StageAsync(f.Root, f.Root, server.Address, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.Net.Http.HttpRequestException) { rejected = true; }
                if (!rejected || File.Exists(f.Pending) || File.ReadAllText(f.Addin) != before || redirect && server.Requests != 1) throw new Exception("Unsafe update response was followed or activated.");
            }
        });
    }
    private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root, Addin, Pending, Archive, Draft, Person, Local;
        internal readonly string LocalAppData, AppData;
        internal readonly ClientUpdateManifest Offer;
        internal readonly ClientPackageInfo Package;
        private readonly RSA rsa = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
        internal Fixture(string mode = "valid")
        {
            var fixture = Path.Combine(Environment.CurrentDirectory, ".local", "updater-tests", Guid.NewGuid().ToString("N"));
            LocalAppData = Path.Combine(fixture, "localappdata"); AppData = Path.Combine(fixture, "appdata");
            Root = Path.Combine(LocalAppData, "HIFC", "0.5.0-Web", "Revit2020"); Directory.CreateDirectory(Root);
            Addin = Path.Combine(AppData, "Autodesk", "Revit", "Addins", "2020", "Hifc.Web.addin"); Directory.CreateDirectory(Path.GetDirectoryName(Addin));
            File.WriteAllText(Addin, "<RevitAddIns><AddIn Type=\"Application\"><AddInId>" + UpdateTrust.AddInId + "</AddInId><Assembly>" + Path.Combine(Root, "Hifc.Web.Revit.dll") + "</Assembly></AddIn></RevitAddIns>");
            File.WriteAllText(Path.Combine(Root, "Hifc.Web.Revit.dll"), "old client");
            File.WriteAllText(Path.Combine(Root, "package.json"), JsonWire.Serialize(new ClientPackageInfo { ReleaseId = "0.5.0-dev-20261018T120000Z" }));
            Draft = Path.Combine(Root, "drafts.bin"); Person = Path.Combine(Root, "person.bin"); Local = Path.Combine(Path.GetDirectoryName(Addin), "Hifc.V1.addin");
            File.WriteAllText(Draft, "remembered draft"); File.WriteAllText(Person, "remembered person"); File.WriteAllText(Local, "Local protected");
            var parameters = rsa.ExportParameters(false);
            File.WriteAllText(Path.Combine(Root, "update-public-key.json"), JsonWire.Serialize(new PublicSigningKey { Modulus = Convert.ToBase64String(parameters.Modulus), Exponent = Convert.ToBase64String(parameters.Exponent) }));
            Offer = new ClientUpdateManifest { ReleaseId = "0.5.0-dev-20261019T120000Z", SourceCommit = new string('a', 40), PackageUrl = UpdateTrust.Server + "updates/packages/0.5.0-dev-20261019T120000Z.zip" };
            Package = new ClientPackageInfo { Version = "0.5.0-Web", AddInId = UpdateTrust.AddInId, ReleaseId = mode == "wrong-release" ? "0.5.0-dev-20261019T130000Z" : Offer.ReleaseId, SourceCommit = Offer.SourceCommit,
                Files = UpdateTrust.PayloadNames.Select(name => { var bytes = Encoding.UTF8.GetBytes("MZ update fixture " + name); return new ClientPackageFile { Name = name, Length = bytes.Length, Sha256 = Hash(bytes) }; }).ToArray() };
            var cache = Path.Combine(Root, "update-cache", Offer.ReleaseId); Directory.CreateDirectory(cache); Archive = Path.Combine(cache, "package.zip"); Pending = Path.Combine(cache, "pending.json");
            using (var zip = new ZipArchive(File.Create(Archive), ZipArchiveMode.Create)) {
                foreach (var file in Package.Files) Write(zip, "HIFC-0.5.0-Web/client/" + file.Name, Encoding.UTF8.GetBytes("MZ update fixture " + file.Name));
                Write(zip, "HIFC-0.5.0-Web/package.json", Encoding.UTF8.GetBytes(JsonWire.Serialize(Package)));
                if (mode == "traversal") Write(zip, "HIFC-0.5.0-Web/../../escaped.txt", new byte[] { 1 });
            }
            Offer.Length = new FileInfo(Archive).Length; Offer.Sha256 = Hash(File.ReadAllBytes(Archive));
            Offer.Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(UpdateTrust.Payload(Offer)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            File.WriteAllText(Pending, JsonWire.Serialize(new { Manifest = Offer }));
        }
        private static void Write(ZipArchive archive, string path, byte[] bytes) { using (var stream = archive.CreateEntry(path).Open()) stream.Write(bytes, 0, bytes.Length); }
        internal void ForDownload(Uri server) {
            Offer.PackageUrl = new Uri(server, "updates/packages/" + Offer.ReleaseId + ".zip").AbsoluteUri;
            Offer.Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(UpdateTrust.Payload(Offer)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            File.Delete(Archive); File.Delete(Pending);
        }
        public void Dispose() => rsa.Dispose();
    }
    private sealed class HttpFixture : IDisposable
    {
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly Task serving;
        internal readonly Uri Address;
        internal int Requests;
        internal HttpFixture(Fixture fixture, bool redirect, bool corrupt)
        {
            var package = File.ReadAllBytes(fixture.Archive); if (corrupt) package[0] ^= 1;
            listener.Start(); Address = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/"); fixture.ForDownload(Address);
            serving = Task.Run(() => {
                try { while (true) using (var client = listener.AcceptTcpClient()) using (var stream = client.GetStream()) {
                    string request;
                    using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true)) { request = reader.ReadLine(); while (!string.IsNullOrEmpty(reader.ReadLine())) { } }
                    Interlocked.Increment(ref Requests); var body = request.Contains("/updates/latest") ? Encoding.UTF8.GetBytes(JsonWire.Serialize(fixture.Offer)) : package;
                    var header = redirect ? "HTTP/1.1 302 Found\r\nLocation: /redirect-target\r\nContent-Length: 0\r\nConnection: close\r\n\r\n" : "HTTP/1.1 200 OK\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
                    var bytes = Encoding.ASCII.GetBytes(header); stream.Write(bytes, 0, bytes.Length); if (!redirect) stream.Write(body, 0, body.Length);
                } } catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException || ex is IOException) { }
            });
        }
        public void Dispose() { listener.Stop(); serving.GetAwaiter().GetResult(); }
    }
}
