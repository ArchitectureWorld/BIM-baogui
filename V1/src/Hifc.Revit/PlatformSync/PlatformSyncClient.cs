using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Hifc.Reference;

namespace Hifc.Revit.PlatformSync
{
    public sealed class PlatformSyncSettings
    {
        public string NodePath { get; set; } = "";
        public string WorkerPath { get; set; } = "";
        public string ConfigPath { get; set; } = "";
        public void Validate()
        {
            foreach (var path in new[] { NodePath, WorkerPath, ConfigPath })
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path) || path.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                    throw new InvalidOperationException("请选择现有的 Node 程序、同步程序 cli.js 和平台配置 JSON 文件。");
            if (!string.Equals(Path.GetFileName(NodePath), "node.exe", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(WorkerPath), "cli.js", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetExtension(ConfigPath), ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("程序路径应分别指向 node.exe、cli.js 和配置 JSON。");
        }
        public static PlatformSyncSettings Load(string path)
        {
            if (!File.Exists(path)) return new PlatformSyncSettings();
            return new JavaScriptSerializer().Deserialize<PlatformSyncSettings>(File.ReadAllText(path, Encoding.UTF8)) ?? new PlatformSyncSettings();
        }
        public void Save(string path)
        {
            Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
    public sealed class SyncRemoteModel
    { public string id { get; set; } public string name { get; set; } public string generation { get; set; } public override string ToString() => name; }
    public sealed class SyncInspection
    {
        public bool? writable { get; set; }
        public int? approval_state { get; set; }
        public string project_id { get; set; } public string project_code { get; set; } public string project_name { get; set; }
        public string scene_project_id { get; set; } public string revision { get; set; } public SyncRemoteModel[] models { get; set; } = new SyncRemoteModel[0];
    }
    public sealed class SyncBinding
    {
        public string id { get; set; } public string project_id { get; set; } public string project_code { get; set; } public string project_name { get; set; }
        public string remote_model_id { get; set; } public string remote_model_name { get; set; } public string document_path { get; set; } public string project_unique_id { get; set; }
        public override string ToString() => (project_name ?? "") + " · " + remote_model_name;
    }
    public sealed class SyncRunHandoff { public string original_name { get; set; } public string document_path { get; set; } }
    public sealed class SyncReport
    {
        public string state { get; set; } public string reason { get; set; }
        public string pdf_path { get; set; } public string txt_path { get; set; }
    }
    public sealed class SyncRun
    {
        public string id { get; set; } public string state { get; set; } public string reason { get; set; }
        public SyncRunHandoff handoff { get; set; }
        public SyncReport report { get; set; }
        public bool CanDownloadReport => state == "SUCCEEDED" && string.Equals(calculation_status, "completed", StringComparison.OrdinalIgnoreCase);
        public string ReportLabel { get { switch (report?.state) {
            case "PENDING": return "等待下载"; case "DOWNLOADING": return "下载中"; case "PUBLISHING": return "保存中";
            case "SUCCEEDED": return "已保存"; case "NEEDS_LOGIN": return "需要登录"; case "BLOCKED": return "待核对"; case "FAILED": return "下载失败";
            default: return state == "SUCCEEDED" ? "可下载" : "等待计算完成";
        } } }
        public string ModelLabel => string.IsNullOrWhiteSpace(handoff?.original_name) ? "本次 IFC" : PlatformSyncClient.SafeText(handoff.original_name);
        public string calculation_status { get; set; } public string regulation_status { get; set; }
        public string StateLabel {
            get { switch (state) {
                case "QUEUED": return "等待处理"; case "PRECHECK": return "核对中"; case "PLANNED": return "预览计划已保存";
                case "DELETING": return "替换旧模型"; case "UPLOADING": return "上传中"; case "SUBMITTING": return "提交中";
                case "CONVERTING": return "转换中"; case "CALCULATING": return "计算中"; case "VERIFYING": return "核验中";
                case "SUCCEEDED": return "同步完成"; case "NO_CHANGE": return "内容未变化"; case "BLOCKED": return "需要核对";
                case "NEEDS_LOGIN": return "需要登录"; case "FAILED": return "处理失败"; case "UNKNOWN_RESULT": return "结果待核对";
                case "PAUSED": return "已暂停"; case "CANCELLED": return "已取消"; case "ROLLED_BACK": return "已恢复旧模型";
                default: return "尚未处理";
            } }
        }
        public string CalculationLabel { get { switch ((calculation_status ?? "").ToLowerInvariant()) { case "running": return "计算中"; case "completed": return "计算完成"; case "failed": return "计算失败"; default: return "尚未计算"; } } }
        public string RegulationLabel { get { switch ((regulation_status ?? "").ToLowerInvariant()) { case "passed": return "审查通过"; case "failed": return "审查未通过"; default: return "尚未审查"; } } }
    }
    public sealed class SyncStatus
    { public SyncBinding[] bindings { get; set; } = new SyncBinding[0]; public SyncRun[] runs { get; set; } = new SyncRun[0]; }
    public sealed class SyncWorkerLaunch { public bool started { get; set; } public string run_id { get; set; } public int pid { get; set; } }
    public sealed class PlatformExportContext
    {
        public string DocumentPath { get; set; } public string ProjectUniqueId { get; set; }
        public CompletedPlatformExport Completed { get; set; } public string ExportStatus { get; set; }
        public bool IsSimulation { get; set; }
        public void RequireMatchingExport()
        {
            if (Completed == null) throw new InvalidOperationException(ExportStatus ?? "请先成功导出当前项目 IFC。");
            if (!PlatformSyncHandoff.SameDocument(Completed.DocumentPath, Completed.ProjectUniqueId, DocumentPath, ProjectUniqueId))
                throw new InvalidOperationException("模型已另存为或与本次导出不一致，请重新导出并确认绑定。");
        }
    }
    public interface IPlatformSyncHost
    { Task<PlatformExportContext> ReadCurrentAsync(); void Close(); }
    public interface IPlatformSyncClient
    {
        Task<SyncStatus> StatusAsync(PlatformSyncSettings settings);
        Task<SyncInspection> InspectAsync(PlatformSyncSettings settings);
        Task<SyncBinding> BindAsync(PlatformSyncSettings settings, string modelId, string handoff);
        Task<SyncRun> PlanAsync(PlatformSyncSettings settings, string bindingId, string handoff);
        Task<SyncWorkerLaunch> StartAsync(PlatformSyncSettings settings, string runId);
        Task<SyncWorkerLaunch> ReportAsync(PlatformSyncSettings settings, string runId);
    }
    /// <summary>Paths and IDs go directly to Node; apply launches an independent worker without exposing credentials.</summary>
    public sealed class PlatformSyncClient : IPlatformSyncClient
    {
        public Task<SyncStatus> StatusAsync(PlatformSyncSettings s) => Invoke<SyncStatus>(s, "status");
        public Task<SyncInspection> InspectAsync(PlatformSyncSettings s) => Invoke<SyncInspection>(s, "inspect");
        public Task<SyncBinding> BindAsync(PlatformSyncSettings s, string id, string handoff) => Invoke<SyncBinding>(s, "bind", "--model", id, "--handoff", handoff);
        public Task<SyncRun> PlanAsync(PlatformSyncSettings s, string id, string handoff) => Invoke<SyncRun>(s, "plan", "--binding", id, "--handoff", handoff);
        public Task<SyncWorkerLaunch> StartAsync(PlatformSyncSettings s, string id) => Invoke<SyncWorkerLaunch>(s, "start", "--run", id, "--apply");
        public Task<SyncWorkerLaunch> ReportAsync(PlatformSyncSettings s, string id) => Invoke<SyncWorkerLaunch>(s, "report-start", "--run", id);
        public static string QuoteArgument(string value)
        {
            if (value == null || value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new InvalidOperationException("同步参数无效。");
            var result = new StringBuilder("\""); int slash = 0;
            foreach (var c in value) { if (c == '\\') { slash++; continue; } result.Append('\\', c == '"' ? slash * 2 + 1 : slash); result.Append(c); slash = 0; }
            result.Append('\\', slash * 2); return result.Append('"').ToString();
        }
        public static string SafeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var result = Regex.Replace(value, @"(?i)(token|authorization|cookie|password|secret|access_token|refresh_token)\s*[:=]\s*[^\s,;]+", "$1=[已隐藏]");
            result = Regex.Replace(result, @"https?://[^\s]+", "[平台地址]");
            return result.Length > 1600 ? result.Substring(0, 1600) + "…" : result;
        }
        async Task<T> Invoke<T>(PlatformSyncSettings settings, string command, params string[] args) where T : class
        {
            settings.Validate();
            if (!new[] { "status", "inspect", "bind", "plan", "start", "report-start" }.Contains(command)) throw new InvalidOperationException("不支持的同步操作。");
            var all = new[] { settings.WorkerPath, command, "--config", settings.ConfigPath }.Concat(args).Select(QuoteArgument);
            var start = new ProcessStartInfo(settings.NodePath, string.Join(" ", all)) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(settings.ConfigPath), RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = new Process { StartInfo = start })
            {
                process.Start(); var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                var exited = await Task.Run(() => process.WaitForExit(command == "start" || command == "report-start" || command == "status" ? 30000 : 300000));
                if (!exited) { try { process.Kill(); } catch (InvalidOperationException) { } throw new InvalidOperationException(command == "start" ? "后台启动回执超时，请刷新同步状态核对；窗口不会终止后台转换。" : "平台读取超时，请检查独立浏览器和连接状态。"); }
                var text = await stdout; await stderr;
                // Never echo raw stderr or arbitrary JSON on failures: they may contain page credentials.
                if (process.ExitCode != 0) throw new InvalidOperationException("同步程序未完成（代码 " + process.ExitCode + "）。请刷新同步状态并查看独立程序的脱敏记录。");
                try { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Deserialize<T>(text) ?? throw new InvalidOperationException("同步程序未返回有效数据。"); }
                catch (ArgumentException) { throw new InvalidOperationException("同步程序响应格式不正确，未继续操作。"); }
            }
        }
    }
}
