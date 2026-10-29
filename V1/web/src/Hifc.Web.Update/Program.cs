using System;
using System.IO;
using System.Net;
using System.Threading;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Update
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var root = UpdateInstaller.InstallationRoot;
            try {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                if (args.Length == 1 && args[0] == "--component-locks") { Console.WriteLine(JsonWire.Serialize(ComponentLocks.Load())); return 0; }
                if ((args.Length == 2 || args.Length == 3) && args[0] == "--prepare-components") {
                    if (!string.Equals(Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("组件准备目录必须是当前用户 Web 安装目录。");
                    using (var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(12))) {
                        var installations = ComponentLocks.PrepareAsync(root, cancellation.Token, args.Length == 3 ? args[2] : null).GetAwaiter().GetResult();
                        UpdateInstaller.AtomicText(UpdateInstaller.Within(root, Path.Combine(root, "component-status.json")), JsonWire.Serialize(new { Status = "ready", Components = installations }));
                        Console.WriteLine(JsonWire.Serialize(installations));
                    }
                    return 0;
                }
                if (args.Length != 6 || args[0] != "--apply" || args[2] != "--wait-pid" || args[4] != "--started" || !int.TryParse(args[3], out var pid) || pid < 1 || !long.TryParse(args[5], out var ticks) || ticks < 1) throw new InvalidOperationException("更新参数无效。");
                var pending = UpdateInstaller.Within(root, args[1]);
                using (var mutex = new Mutex(false, "Local\\HIFC.Web.Update." + Canonical.Hash(Path.GetFullPath(root).ToUpperInvariant()))) {
                    if (!mutex.WaitOne(0)) return 0;
                    try { UpdateInstaller.WaitForRevitExit(pid, ticks); UpdateInstaller.Apply(root, UpdateInstaller.AddInManifestPath, pending); }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception ex) {
                var componentPreparation = args.Length > 0 && args[0] == "--prepare-components";
                var diagnosticsOnly = args.Length > 0 && args[0] == "--component-locks";
                try { if (!diagnosticsOnly && Directory.Exists(root)) UpdateInstaller.AtomicText(UpdateInstaller.Within(root, Path.Combine(root, componentPreparation ? "component-status.json" : "update-status.json")), JsonWire.Serialize(new { Status = "failed", Detail = componentPreparation ? "组件准备未完成，已安装版本保留，可稍后重试。" : "更新未完成，当前版本保留。", Error = ex.GetType().Name, Diagnostic = ex.ToString() })); } catch { }
                if (diagnosticsOnly || componentPreparation) Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
