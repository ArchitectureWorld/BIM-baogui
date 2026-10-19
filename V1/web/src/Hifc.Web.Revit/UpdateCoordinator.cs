using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hifc.Web.Contracts;
using Hifc.Web.Update;

namespace Hifc.Web.Revit
{
    public static class UpdateCoordinator
    {
        private static int checking;
        private static DateTime checkedUtc;
        public static string Status { get; private set; } = "更新服务准备中。";
        public static event Action Changed;
        private static void Say(string value) { Status = value; Changed?.Invoke(); }
        public static void Start()
        {
            if (DateTime.UtcNow - checkedUtc < TimeSpan.FromMinutes(1) || Interlocked.CompareExchange(ref checking, 1, 0) != 0) return;
            checkedUtc = DateTime.UtcNow;
            var installed = Path.GetDirectoryName(typeof(WebApp).Assembly.Location);
            int pid; long started;
            using (var process = Process.GetCurrentProcess()) { pid = process.Id; started = process.StartTime.ToUniversalTime().Ticks; }
            Task.Run(async () => {
                try {
                    Say("正在检查客户端更新……");
                    var pending = await UpdateClient.StageAsync(UpdateInstaller.InstallationRoot, installed, new Uri(UpdateTrust.Server), CancellationToken.None).ConfigureAwait(false);
                    if (pending == null) { Say("客户端已是最新版本。"); return; }
                    UpdateClient.StartWorker(installed, pending, pid, started)?.Dispose();
                    Say("新版已下载，关闭 Revit 后自动更新，下次启动生效。");
                } catch { Say("暂未完成更新检查，继续使用当前版本。"); }
                finally { Interlocked.Exchange(ref checking, 0); }
            });
        }
    }
}
