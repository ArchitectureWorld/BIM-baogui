using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Update;
using Newtonsoft.Json.Linq;

namespace Hifc.Web.Revit.PlatformSync
{
    public static class WebPlatformSyncController
    {
        public static async Task<Window> OpenAsync(WebModelSession session, Window owner)
        {
            if (session.Account == null) throw new InvalidOperationException("请先连接 Web 账号，再打开平台同步。");
            var locks = ComponentLocks.Load();
            var installed = await Task.Run(() => ComponentLocks.PrepareAsync(UpdateInstaller.InstallationRoot, CancellationToken.None));
            var node = installed.Single(x => x.ComponentId == "node"); var sync = installed.Single(x => x.ComponentId == "platform-sync");
            var root = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HIFC", "0.5.0-Web", "PlatformSync", Canonical.Hash(session.Account.Id));
            PlatformSyncHandoff.NoLinks(root); Directory.CreateDirectory(root);
            var config = Path.Combine(root, "config.json"); var component = locks.Single(x => x.ComponentId == "platform-sync");
            var state = Path.Combine(root, "state"); var browser = Path.Combine(root, "browser"); var entry = Path.Combine(root, "entry.dpapi");
            JObject value;
            if (File.Exists(config)) {
                PlatformSyncHandoff.NoLinks(config); if (new FileInfo(config).Length > 65536) throw new InvalidOperationException("Web 平台配置过大。"); value = JObject.Parse(File.ReadAllText(config));
                if ((string)value["state_dir"] != state || (string)value["browser_profile"] != browser || (string)value["entry_secret_file"] != entry || (string)(value["web"]?["accountId"]) != session.Account.Id || (int?)value["web"]?["schemaVersion"] != 1 || (int?)value["web"]?["minimumReaderVersion"] != 1) throw new InvalidOperationException("Web 平台配置与当前账号或受支持版本不符，未迁移 Local/新版状态。");
            } else value = JObject.FromObject(new { schema = "hifc.platform-sync-config/1", adapter = "real", state_dir = state, browser_profile = browser, entry_secret_file = entry, headless = false });
            value["web"] = JObject.FromObject(new { schemaVersion = 1, minimumReaderVersion = 1, accountId = session.Account.Id, componentVersion = component.Version, nodeVersion = "25.4.0", mainReleaseId = component.MainReleaseId, mainSourceCommit = component.MainSourceCommit, serverClockOffsetSeconds = new DateTimeOffset(session.ServerNowUtc).ToUnixTimeSeconds() - new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() });
            var temporary = config + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllText(temporary, value.ToString(), new UTF8Encoding(false)); if (File.Exists(config)) File.Replace(temporary, config, null); else File.Move(temporary, config);
            var settings = new PlatformSyncSettings { NodePath = node.EntryPoint, WorkerPath = sync.EntryPoint, ConfigPath = config };
            var settingsPath = Path.Combine(root, "settings.json"); settings.Save(settingsPath);
            var window = new PlatformSyncWindow(new Host(session), new WebAuthorizedPlatformSyncClient(session, settings, component, root), settingsPath, Path.Combine(root, "spool")) { Owner = owner, EnvironmentNotice = WebAuthorizedPlatformSyncClient.EdgePath == null ? "环境缺项：未检测到 Microsoft Edge，请安装后再读取平台或同步。" : null };
            window.Show(); window.RefreshInitial(); return window;
        }
        private sealed class Host : IPlatformSyncHost
        {
            private readonly WebModelSession session; internal Host(WebModelSession session) { this.session = session; }
            public async Task<PlatformExportContext> ReadCurrentAsync()
            {
                PlatformSyncSource source;
                try { source = await session.ReadPlatformSyncSourceAsync(); }
                catch (InvalidOperationException) { return new PlatformExportContext { ExportStatus = "当前模型不可用；历史同步记录和已冻结的综合报告仍可查看。" }; }
                var result = new PlatformExportContext { DocumentPath = source.DocumentPath, ProjectUniqueId = source.ProjectUniqueId, ExportStatus = "请先完成当前模型的 IFC 导出及服务器回执登记。" };
                if (source.Receipt != null) {
                    try { result.Completed = PlatformSyncHandoff.FromAcceptedSource(source); }
                    catch (Exception error) when (error is InvalidOperationException || error is IOException || error is UnauthorizedAccessException || error is Newtonsoft.Json.JsonException) { result.ExportStatus = "当前导出回执已失效，请重新导出；历史同步与报告记录保留。"; }
                }
                return result;
            }
            public void Close() { }
        }
    }
}
