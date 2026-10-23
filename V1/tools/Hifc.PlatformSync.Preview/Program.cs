using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hifc.Reference;
using Hifc.Revit.PlatformSync;

/// <summary>Runs the actual modeless sync window without loading any Revit assembly.
/// Its artificial export is visibly marked and cannot certify a real Revit invocation.
/// </summary>
internal static class Program
{
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    sealed class PreviewHost : IPlatformSyncHost
    {
        readonly PlatformExportContext context;
        internal PreviewHost(CompletedPlatformExport export) { context = new PlatformExportContext { Completed = export, DocumentPath = export.DocumentPath, ProjectUniqueId = export.ProjectUniqueId, IsSimulation = true, ExportStatus = "人工生成的本机演练样本，非 Revit 实机导出。" }; }
        public Task<PlatformExportContext> ReadCurrentAsync() => Task.FromResult(context);
        public void Close() { }
    }
    static Dictionary<string, string> Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++) {
            var flag = args[i]; if (flag == "--smoke" || flag == "--sample-only") { values.Add(flag, "true"); continue; }
            if (!new[] { "--node", "--worker", "--config", "--evidence" }.Contains(flag) || ++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new InvalidOperationException("预览参数无效。支持 --node、--worker、--config、--evidence、--sample-only、--smoke。");
            values.Add(flag, args[i]);
        }
        return values;
    }
    static string Required(Dictionary<string, string> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || !Path.IsPathRooted(value)) throw new InvalidOperationException("请提供绝对路径参数 " + key + "。"); return Path.GetFullPath(value);
    }
    static void WriteJson(string path, object data) { File.WriteAllText(path, Json.Serialize(data), Utf8); }
    static CompletedPlatformExport CreateSample(string root, out string handoff)
    {
        var directory = Path.Combine(root, "artificial-csharp-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var ifc = Path.Combine(directory, "OUT_本机演练_报规_总图_20261023_V1.ifc");
        var receipt = Path.ChangeExtension(ifc, ".receipt.json"); var id = Guid.NewGuid().ToString("D");
        File.WriteAllText(ifc, "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('Artificial handoff test fixture; not a Revit model'),'2;1');\nFILE_NAME('local-demo.ifc','2026-10-23T00:00:00',('Test fixture'),('HIFC'),'C# handoff test','Local preview','');\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;", Utf8);
        WriteJson(receipt, new { schema_version = "hifc.native-export-receipt/1.5", export_id = id, ifc_path = ifc, ifc_sha256 = PlatformSyncHandoff.Sha256(ifc), source_snapshot_before = "artificial-test-snapshot", source_snapshot_after = "artificial-test-snapshot", model_transaction = "RolledBack", export_scope = "CURRENT_PROJECT", doc_export_calls = 1, ifc_postprocessed = false, original_export_server_restored = true, temporary_type_schedules_rolled_back = true, fixture_scope = "ARTIFICIAL_TEST_ONLY_NOT_REVIT_VERIFICATION" });
        var export = PlatformSyncHandoff.RegisterCompleted(ifc, receipt, id, Path.Combine(directory, "3D_本机演练_总图.rvt"), "artificial-preview-project-" + id);
        handoff = PlatformSyncHandoff.Freeze(export, Path.Combine(root, "handoffs"), export.DocumentPath, export.ProjectUniqueId);
        WriteJson(Path.Combine(root, "csharp-sample.json"), new { scope = "Artificial C# receipt/frozen handoff for cross-language verification; not Revit E2E", manifest_path = handoff, export_id = id, ifc_sha256 = export.IfcHash, receipt_sha256 = export.ReceiptHash });
        return export;
    }
    static void Screenshot(Window window, string path)
    {
        window.UpdateLayout(); var element = (FrameworkElement)window.Content;
        if (element.ActualWidth < 900 || element.ActualHeight < 500) throw new InvalidOperationException("预览窗口尚未完成布局。");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(path)) encoder.Save(file);
    }
    static async Task Click(PlatformSyncWindow window, Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation; }
    static async Task Smoke(PlatformSyncWindow window, string root)
    {
        window.ReadExportStatus(); await window.LastOperation;
        await Click(window, window.InspectButton);
        if (window.ModelSelector.Items.Count != 1) throw new InvalidOperationException("本机演练应恰有一个明确的远端样本。" + window.StatusText.Text);
        window.ModelSelector.SelectedIndex = 0; await Click(window, window.BindButton);
        if (window.BindingSelector.Items.Count != 1) throw new InvalidOperationException("真实 CLI 未保存当前样本绑定。" + window.StatusText.Text);
        await Click(window, window.PlanButton);
        var runs = window.Runs.Items.Cast<SyncRun>().ToArray();
        if (runs.Length != 1 || runs[0].state != "PLANNED" || string.IsNullOrEmpty(window.LastHandoffPath)) throw new InvalidOperationException("真实 CLI 未返回同步预览计划。" + window.StatusText.Text);
        if (!window.ExportText.Text.Contains("模拟宿主")) throw new InvalidOperationException("样本宿主未明确显示模拟状态。");
        Screenshot(window, Path.Combine(root, "standalone-preview.png"));
        WriteJson(Path.Combine(root, "standalone-smoke.json"), new { passed = true, scope = "Actual WPF window plus real Node CLI; artificial local fixture only; no Revit or real platform write", plan_id = runs[0].id, state = runs[0].state, calculation_status = runs[0].calculation_status, regulation_status = runs[0].regulation_status, manifest_path = window.LastHandoffPath });
    }
    [STAThread]
    static int Main(string[] args)
    {
        string root = null;
        try {
            var parsed = Parse(args); root = Required(parsed, "--evidence"); Directory.CreateDirectory(root);
            var export = CreateSample(root, out var manifest);
            if (parsed.ContainsKey("--sample-only")) { Console.OutputEncoding = Utf8; Console.WriteLine(Json.Serialize(new { manifest_path = manifest, scope = "Artificial C# fixture" })); return 0; }
            var settings = new PlatformSyncSettings { NodePath = Required(parsed, "--node"), WorkerPath = Required(parsed, "--worker"), ConfigPath = Required(parsed, "--config") }; settings.Validate();
            if (parsed.ContainsKey("--smoke")) {
                var config = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(settings.ConfigPath, Encoding.UTF8));
                if (!config.TryGetValue("adapter", out var adapter) || !Equals(adapter, "fixture")) throw new InvalidOperationException("自动界面演练仅允许本机 fixture 配置。");
            }
            var settingsFile = Path.Combine(root, "ui-settings.json"); settings.Save(settingsFile);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }; var result = 0;
            var window = new PlatformSyncWindow(new PreviewHost(export), new PlatformSyncClient(), settingsFile, Path.Combine(root, "handoffs")) { Title = "H-IFC V1 · 平台同步（模拟宿主演练）" };
            window.Loaded += (sender, e) => {
                if (!parsed.ContainsKey("--smoke")) { window.ReadExportStatus(); return; }
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => {
                    try { await Smoke(window, root); }
                    catch (Exception error) { result = 1; WriteJson(Path.Combine(root, "standalone-smoke.json"), new { passed = false, message = PlatformSyncClient.SafeText(error.Message) }); }
                    finally { window.Close(); }
                }));
            };
            app.Run(window); return result;
        }
        catch (Exception error) { if (root != null && Directory.Exists(root)) WriteJson(Path.Combine(root, "preview-error.json"), new { message = PlatformSyncClient.SafeText(error.Message) }); return 2; }
    }
}
