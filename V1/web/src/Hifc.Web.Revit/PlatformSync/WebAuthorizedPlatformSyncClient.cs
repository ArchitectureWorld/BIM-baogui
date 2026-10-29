using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit.PlatformSync
{
    internal sealed class WebAuthorizedPlatformSyncClient : IPlatformSyncClient
    {
        private readonly WebModelSession session; private readonly PlatformSyncSettings settings; private readonly ComponentLock component; private readonly string root;
        private readonly Dictionary<string, string> previewBindings = new Dictionary<string, string>(StringComparer.Ordinal);
        internal WebAuthorizedPlatformSyncClient(WebModelSession session, PlatformSyncSettings settings, ComponentLock component, string root) { this.session = session; this.settings = settings; this.component = component; this.root = root; }
        private void Match(PlatformSyncSettings actual)
        {
            actual.Validate();
            foreach (var path in new[] { actual.NodePath, actual.WorkerPath, actual.ConfigPath, root }) PlatformSyncHandoff.NoLinks(path);
            if (!new[] { (actual.NodePath, settings.NodePath), (actual.WorkerPath, settings.WorkerPath), (actual.ConfigPath, settings.ConfigPath) }.All(x => Path.GetFullPath(x.Item1).Equals(Path.GetFullPath(x.Item2), StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Web 同步只使用已验签的运行组件及独立配置。");
        }
        public Task<SyncStatus> StatusAsync(PlatformSyncSettings s) => Invoke<SyncStatus>(s, "status");
        internal static string EdgePath => new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("LOCALAPPDATA") }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Path.Combine(x, "Microsoft", "Edge", "Application", "msedge.exe")).FirstOrDefault(File.Exists);
        private static void RequireEdge() { if (EdgePath == null) throw new InvalidOperationException("未检测到 Microsoft Edge。请先安装 Edge，再读取平台或同步；已保存的 IFC 和综合报告仍然保留。"); }
        public Task<SyncInspection> InspectAsync(PlatformSyncSettings s) { RequireEdge(); return Invoke<SyncInspection>(s, "inspect"); }
        public Task<SyncBinding> BindAsync(PlatformSyncSettings s, string id, string handoff) => Invoke<SyncBinding>(s, "bind", null, "--model", id, "--handoff", handoff);
        public async Task<SyncRun> PlanAsync(PlatformSyncSettings s, string id, string handoff)
        {
            var run = await Invoke<SyncRun>(s, "plan", null, "--binding", id, "--handoff", handoff);
            if (run.state == "PLANNED") previewBindings[run.id] = PlatformLaunchTrust.RequestPayload(Request(run));
            return run;
        }
        public Task<SyncWorkerLaunch> ReportAsync(PlatformSyncSettings s, string id) => Invoke<SyncWorkerLaunch>(s, "report-start", null, "--run", id);
        public Task<SyncRun> ReconcileAsync(PlatformSyncSettings s, string id) { RequireEdge(); return Invoke<SyncRun>(s, "resume", null, "--run", id); }
        public async Task ConfigureCredentialAsync(PlatformSyncSettings s, string entry) { if (string.IsNullOrWhiteSpace(entry) || entry.Length > 16000) throw new InvalidOperationException("请输入有效的平台入口链接。"); await Invoke<object>(s, "setup", entry, "--credential-stdin"); }
        public async Task<SyncWorkerLaunch> StartAsync(PlatformSyncSettings s, string id)
        {
            Match(s); var run = (await StatusAsync(s)).runs.SingleOrDefault(x => x.id == id);
            if (run == null || run.state != "PLANNED" || run.baseline == null || run.handoff?.web == null) throw new InvalidOperationException("请重新核对本次 Web 同步预览，历史任务不能自动重放。");
            var request = Request(run);
            if (!previewBindings.TryGetValue(run.id, out var binding) || binding != PlatformLaunchTrust.RequestPayload(request)) throw new InvalidOperationException("确认后同步任务的目标或冻结文件发生变化，请重新预览。");
            var permit = await session.AuthorizePlatformLaunchAsync(request);
            PlatformLaunchTrust.Validate(permit, request, session.Account.Id, session.SigningKey, session.ServerNowUtc);
            var active = await session.ReadPlatformSyncSourceAsync();
            if (active.Receipt == null || active.DocumentSessionId != request.DocumentSessionId || active.Receipt.PlanId != request.ExportPlanId || active.Receipt.IfcHash != request.IfcSha256) throw new InvalidOperationException("启动授权期间文档或本次导出已变化，请重新预览。");
            var configuration = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(s.ConfigPath));
            configuration["web"]["serverClockOffsetSeconds"] = new DateTimeOffset(session.ServerNowUtc).ToUnixTimeSeconds() - new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
            var configTemp = s.ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllText(configTemp, configuration.ToString(), new UTF8Encoding(false)); File.Replace(configTemp, s.ConfigPath, null);
            var directory = Path.Combine(root, "permits"); PlatformSyncHandoff.NoLinks(directory); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, permit.Id + ".json"); File.WriteAllText(path, JsonWire.Serialize(permit), new UTF8Encoding(false));
            return await Invoke<SyncWorkerLaunch>(s, "start", null, "--run", run.id, "--apply", "--web-permit", path);
        }
        private PlatformLaunchRequest Request(SyncRun run)
        {
            if (run.baseline == null || run.handoff?.web == null) throw new InvalidOperationException("同步预览缺少目标和 Web 导出回执。");
            return new PlatformLaunchRequest { ExportPlanId = run.handoff.export_id, DocumentSessionId = run.handoff.web.document_session_id, ProjectId = run.project_id, TargetModelId = run.baseline.id, TargetGeneration = run.baseline.generation, IfcSha256 = run.handoff.ifc_sha256,
                RunId = run.id, MainReleaseId = component.MainReleaseId, MainSourceCommit = component.MainSourceCommit, ComponentVersion = component.Version, NodeVersion = "25.4.0", ConfirmedReplacement = true };
        }
        private async Task<T> Invoke<T>(PlatformSyncSettings s, string command, string input = null, params string[] options) where T : class
        {
            Match(s); var args = new[] { s.WorkerPath, command, "--config", s.ConfigPath }.Concat(options).Select(PlatformSyncClient.QuoteArgument);
            var start = new ProcessStartInfo(s.NodePath, string.Join(" ", args)) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = root, RedirectStandardInput = input != null, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            start.EnvironmentVariables.Remove("NODE_OPTIONS"); start.EnvironmentVariables.Remove("NODE_PATH"); start.EnvironmentVariables.Remove("NODE_TLS_REJECT_UNAUTHORIZED"); start.EnvironmentVariables["NODE_USE_SYSTEM_CA"] = "1"; start.EnvironmentVariables["NODE_USE_ENV_PROXY"] = "0";
            foreach (var variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" }) start.EnvironmentVariables.Remove(variable);
            using (var process = Process.Start(start)) {
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                if (input != null) { await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); }
                var exited = await Task.Run(() => process.WaitForExit(new[] { "status", "start", "report-start", "setup" }.Contains(command) ? 30000 : 300000));
                if (!exited) { try { process.Kill(); } catch (InvalidOperationException) { } throw new InvalidOperationException("平台操作回执超时，请刷新状态核对；已启动的后台转换不会因此终止。"); }
                var text = await output; await error;
                if (process.ExitCode != 0) throw new InvalidOperationException("平台操作未完成（代码 " + process.ExitCode + "）。请刷新状态并核对独立 Web 浏览器；不会重复提交模型。");
                return JsonWire.Deserialize<T>(text) ?? throw new InvalidOperationException("平台操作未返回有效数据。");
            }
        }
    }
}
