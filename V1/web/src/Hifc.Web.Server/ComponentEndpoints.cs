using Hifc.Web.Contracts;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hifc.Web.Server;

public static class ComponentEndpoints
{
    // Retain versioned directories when deploying. Older clients/workers still reference them.
    public static void Map(WebApplication app, string directory)
    {
        var root = Path.GetFullPath(directory);
        (SignedComponentManifest Manifest, string Archive)? Read(string release, string id, string version, PlanSigner signer)
        {
            if (!UpdateTrust.IsRelease(release) || !Regex.IsMatch(id, "^[a-z][a-z0-9-]{0,47}$") || !Regex.IsMatch(version, "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[a-z0-9]+(?:[.-][a-z0-9]+)*)?$")) return null;
            var path = Path.GetFullPath(Path.Combine(root, release, id, version));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
            for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("组件发布目录不能使用链接。");
            var metadata = Path.Combine(path, "lock.json"); var archive = Path.Combine(path, "archive.zip");
            if (!File.Exists(metadata) || !File.Exists(archive)) return null;
            if (new FileInfo(metadata).Length > 65536 || (File.GetAttributes(metadata) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(archive) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("组件发布文件无效。");
            var component = JsonSerializer.Deserialize<ComponentLock>(File.ReadAllText(metadata), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            ComponentTrust.ValidateLock(component);
            if (component.MainReleaseId != release || component.ComponentId != id || component.Version != version) throw new InvalidOperationException("组件发布路径与锁定信息不一致。");
            using (var input = File.OpenRead(archive))
                if (input.Length != component.ArchiveLength || Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant() != component.ArchiveSha256) throw new InvalidOperationException("组件发布包散列与锁定信息不符。");
            var manifest = new SignedComponentManifest { Lock = component, ArchiveUrl = new Uri(ComponentTrust.ManifestUri(new Uri(UpdateTrust.Server), component).AbsoluteUri.Replace("/manifest", "/archive.zip")).AbsoluteUri };
            signer.Sign(manifest); return (manifest, archive);
        }
        app.MapGet("/components/{release}/{id}/{version}/manifest", (string release, string id, string version, PlanSigner signer) => {
            var value = Read(release, id, version, signer); return value.HasValue ? Results.Ok(value.Value.Manifest) : Results.NotFound();
        });
        app.MapGet("/components/{release}/{id}/{version}/archive.zip", (string release, string id, string version, PlanSigner signer) => {
            var value = Read(release, id, version, signer); return value.HasValue ? Results.File(value.Value.Archive, "application/zip", "archive.zip") : Results.NotFound();
        });
    }
}
