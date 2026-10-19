using Hifc.Web.Contracts;
using System.Security.Cryptography;

namespace Hifc.Web.Server;

public static class ClientUpdateEndpoints
{
    public static void Map(WebApplication app, string directory)
    {
        var root = Path.GetFullPath(directory);
        var gate = new object(); ClientUpdateManifest cached = null;
        ClientUpdateManifest Manifest(PlanSigner signer) {
            lock (gate) {
                if (cached != null) return cached;
                var archive = Path.Combine(root, "HIFC-0.5.0-Web.zip"); var metadata = Path.Combine(root, "package.json");
                if (!File.Exists(archive) || !File.Exists(metadata)) throw new WebOperationException("UPDATE_NOT_READY", 503, "更新包暂未就绪，当前版本可以继续使用。");
                var package = System.Text.Json.JsonSerializer.Deserialize<ClientPackageInfo>(File.ReadAllText(metadata), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (package?.Version != "0.5.0-Web" || package.AddInId != UpdateTrust.AddInId || !UpdateTrust.IsRelease(package.ReleaseId)) throw new InvalidOperationException("客户端发行包标识无效。");
                using var stream = File.OpenRead(archive);
                var value = new ClientUpdateManifest { ReleaseId = package.ReleaseId, SourceCommit = package.SourceCommit,
                    PackageUrl = UpdateTrust.Server + "updates/packages/" + package.ReleaseId + ".zip", Length = stream.Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() };
                signer.Sign(value); UpdateTrust.Validate(value, signer.PublicKey); return cached = value;
            }
        }
        app.MapGet("/updates/latest", (PlanSigner signer) => Results.Ok(Manifest(signer)));
        app.MapGet("/updates/packages/{release}.zip", (string release, PlanSigner signer) => {
            if (release != Manifest(signer).ReleaseId) return Results.NotFound();
            return Results.File(Path.Combine(root, "HIFC-0.5.0-Web.zip"), "application/zip", "HIFC-0.5.0-Web.zip");
        });
    }
}
