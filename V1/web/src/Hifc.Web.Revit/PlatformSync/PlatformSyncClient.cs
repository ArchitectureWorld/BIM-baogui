using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Hifc.Web.Client;

namespace Hifc.Web.Revit.PlatformSync
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
            return JsonWire.Deserialize<PlatformSyncSettings>(File.ReadAllText(path, Encoding.UTF8)) ?? new PlatformSyncSettings();
        }
        public void Save(string path)
        {
            Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonWire.Serialize(this), new UTF8Encoding(false));
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
    public sealed class SyncWebExport {public string document_session_id {get;set;} public string receipt_identity_hash {get;set;}}
    public sealed class SyncRunHandoff { public string export_id {get;set;} public string ifc_sha256 {get;set;} public SyncWebExport web {get;set;} public string original_name { get; set; } public string document_path { get; set; } }
    public sealed class SyncReport
    {
        public string state { get; set; } public string reason { get; set; }
        public string pdf_path { get; set; } public string txt_path { get; set; }
    }
    public sealed class SyncRun
    {
        public string id { get; set; } public string state { get; set; } public string reason { get; set; } public string project_id {get;set;} public SyncRemoteModel baseline {get;set;}
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
        Task ConfigureCredentialAsync(PlatformSyncSettings settings, string entry);
        Task<SyncInspection> InspectAsync(PlatformSyncSettings settings);
        Task<SyncBinding> BindAsync(PlatformSyncSettings settings, string modelId, string handoff);
        Task<SyncRun> PlanAsync(PlatformSyncSettings settings, string bindingId, string handoff);
        Task<SyncWorkerLaunch> StartAsync(PlatformSyncSettings settings, string runId);
        Task<SyncWorkerLaunch> ReportAsync(PlatformSyncSettings settings, string runId);
        Task<SyncRun> ReconcileAsync(PlatformSyncSettings settings, string runId);
    }
    /// <summary>Paths and IDs go directly to Node; apply launches an independent worker without exposing credentials.</summary>
    public static class PlatformSyncClient
    {
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
    }
}
