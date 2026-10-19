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
                if (args.Length != 6 || args[0] != "--apply" || args[2] != "--wait-pid" || args[4] != "--started" || !int.TryParse(args[3], out var pid) || pid < 1 || !long.TryParse(args[5], out var ticks) || ticks < 1) throw new InvalidOperationException("更新参数无效。");
                var pending = UpdateInstaller.Within(root, args[1]);
                using (var mutex = new Mutex(false, "Local\\HIFC.Web.Update." + Canonical.Hash(Path.GetFullPath(root).ToUpperInvariant()))) {
                    if (!mutex.WaitOne(0)) return 0;
                    try { UpdateInstaller.WaitForRevitExit(pid, ticks); UpdateInstaller.Apply(root, UpdateInstaller.AddInManifestPath, pending); }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception ex) {
                try { if (Directory.Exists(root)) UpdateInstaller.AtomicText(UpdateInstaller.Within(root, Path.Combine(root, "update-status.json")), JsonWire.Serialize(new { Status = "failed", Detail = "更新未完成，当前版本保留。", Error = ex.GetType().Name, Diagnostic = ex.ToString() })); } catch { }
                return 1;
            }
        }
    }
}
