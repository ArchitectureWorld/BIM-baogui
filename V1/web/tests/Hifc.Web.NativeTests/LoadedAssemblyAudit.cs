using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;
using Hifc.Web.Update;

internal static class LoadedAssemblyAudit
{
    internal sealed class Expected
    {
        public string AcceptanceSuffix { get; set; }
        public Entry[] Assemblies { get; set; }
    }
    internal sealed class Entry { public string Name { get; set; } public string Path { get; set; } public string Sha256 { get; set; } }
    internal static void Verify(string run)
    {
        var manifest = Path.Combine(run, "expected-assemblies.json");
        if (!File.Exists(manifest)) throw new InvalidOperationException("缺少独立验收程序集清单，拒绝运行可能混载旧 DLL 的宿主用例。");
        var expected = JsonWire.Deserialize<Expected>(File.ReadAllText(manifest));
        var assemblies = new[] { typeof(HifcWebNativeHost).Assembly, typeof(WebModelSession).Assembly, typeof(WebApiClient).Assembly, typeof(RequestContext).Assembly, typeof(UpdateInstaller).Assembly }.Distinct().ToArray();
        var actual = assemblies.Select(a => new Entry { Name = a.GetName().Name, Path = a.Location, Sha256 = Hash(a.Location) }).ToArray();
        File.WriteAllText(Path.Combine(run, "loaded-assemblies.json"), JsonWire.Serialize(new { expected.AcceptanceSuffix, Assemblies = actual }));
        if (string.IsNullOrWhiteSpace(expected.AcceptanceSuffix) || expected.Assemblies == null || expected.Assemblies.Length != actual.Length)
            throw new InvalidOperationException("独立验收程序集清单不完整。");
        foreach (var entry in actual)
        {
            var match = expected.Assemblies.SingleOrDefault(e => e.Name == entry.Name);
            if (!entry.Name.EndsWith("." + expected.AcceptanceSuffix, StringComparison.Ordinal) || match == null
                || !string.Equals(Path.GetFullPath(match.Path), Path.GetFullPath(entry.Path), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(match.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("实际加载的验收程序集路径、身份或散列不匹配：" + entry.Name);
        }
    }
    private static string Hash(string path) { using (var input = File.OpenRead(path)) using (var hash = SHA256.Create()) return string.Concat(hash.ComputeHash(input).Select(b => b.ToString("x2"))); }
}
