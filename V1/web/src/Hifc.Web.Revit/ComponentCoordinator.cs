using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hifc.Web.Client;
using Hifc.Web.Update;

namespace Hifc.Web.Revit
{
    internal static class ComponentCoordinator
    {
        private static readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
        internal static void Start()
        {
            var root = Path.GetFullPath(UpdateInstaller.InstallationRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(typeof(WebApp).Assembly.Location).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            Task.Run(async () => {
                try {
                    var installed = await ComponentLocks.PrepareAsync(root, Lifetime.Token).ConfigureAwait(false);
                    Status(root, "ready", "平台同步运行组件已就绪。", installed.Length);
                } catch (OperationCanceledException) { }
                catch (Exception ex) { Status(root, "retry", "运行组件准备未完成，可在平台同步入口重试：" + ex.Message, 0); }
            });
        }
        private static void Status(string root, string state, string detail, int count)
        {
            try { UpdateInstaller.AtomicText(UpdateInstaller.Within(root, Path.Combine(root, "component-status.json")), JsonWire.Serialize(new { state, detail, count, checkedUtc = DateTime.UtcNow })); }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (InvalidOperationException) { }
        }
        internal static void Stop() => Lifetime.Cancel();
    }
}
