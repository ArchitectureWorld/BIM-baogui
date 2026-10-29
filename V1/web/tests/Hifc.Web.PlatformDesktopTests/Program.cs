using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Revit.PlatformSync;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("Incomplete native export was accepted."); }
        var document = Path.Combine(root, "测试模型.rvt"); var ifc = Path.Combine(root, "测试模型.ifc"); var receiptPath = Path.Combine(root, "receipt.json");
        File.WriteAllText(ifc, "ISO-10303-21; HEADER; FILE_SCHEMA(('IFC2X3')); ENDSEC; DATA; #1=IFCPROJECT('p'); ENDSEC; END-ISO-10303-21;");
        var receipt = new ExportReceipt { PlanId = Guid.NewGuid().ToString("N"), DocumentSessionId = "doc-1", IfcPath = ifc, IfcHash = PlatformSyncHandoff.Sha256(ifc), ReceiptPath = receiptPath, SnapshotBefore = "snapshot", SnapshotAfter = "snapshot", SourceRestored = true, Exported = true, CompletedUtc = DateTime.UtcNow };
        File.WriteAllText(receiptPath, JsonWire.Serialize(receipt));
        var source = new PlatformSyncSource { DocumentPath = document, ProjectUniqueId = "project-unique", DocumentSessionId = "doc-1", Receipt = receipt };
        Reject(() => PlatformSyncHandoff.FromAcceptedSource(new PlatformSyncSource { DocumentPath = document, ProjectUniqueId = "project-unique", DocumentSessionId = "doc-1" }));
        var completed = PlatformSyncHandoff.FromAcceptedSource(source); var handoff = PlatformSyncHandoff.Freeze(completed, Path.Combine(root, "spool"), document, source.ProjectUniqueId);
        Require(File.Exists(handoff), "Frozen Web handoff missing.");
        File.AppendAllText(ifc, "changed"); Reject(() => PlatformSyncHandoff.Freeze(completed, Path.Combine(root, "spool"), document, source.ProjectUniqueId));
        File.WriteAllText(ifc, "ISO-10303-21; HEADER; FILE_SCHEMA(('IFC2X3')); ENDSEC; DATA; #1=IFCPROJECT('p'); ENDSEC; END-ISO-10303-21;");
        Console.WriteLine("PASS Web handoff requires accepted native receipt and preserves frozen hashes");
        var node = Path.Combine(root, "node.exe"); var worker = Path.Combine(root, "cli.js"); var config = Path.Combine(root, "config.json"); File.WriteAllText(node, "fixture only"); File.WriteAllText(worker, "fixture only"); File.WriteAllText(config, "{}");
        var settingsPath = Path.Combine(root, "settings.json"); new PlatformSyncSettings { NodePath = node, WorkerPath = worker, ConfigPath = config }.Save(settingsPath);
        var host = new Host { Context = new PlatformExportContext { DocumentPath = document, ProjectUniqueId = source.ProjectUniqueId, Completed = completed } }; var client = new Client(document, source.ProjectUniqueId);
        var window = new PlatformSyncWindow(host, client, settingsPath, Path.Combine(root, "window-spool")) { ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 1120, Height = 970 };
        window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window.LastOperation); }
        Click(window.InspectButton); window.ModelSelector.SelectedIndex = 0; Click(window.BindButton);
        client.ReturnWrongTarget = true; Click(window.PlanButton); Require(!window.StartButton.IsEnabled, "Preview target escaped the displayed binding."); client.ReturnWrongTarget = false; Click(window.PlanButton);
        Require(client.Starts == 0 && window.StartButton.IsEnabled, "Planning started a write or omitted explicit confirmation.");
        window.ConfirmStart = _ => false; Click(window.StartButton); Require(client.Starts == 0, "Cancelled confirmation started a worker.");
        window.ConfirmStart = _ => { host.Context.DocumentPath = Path.Combine(root, "changed.rvt"); return true; }; Click(window.StartButton); Require(client.Starts == 0, "Document change during confirmation was ignored.");
        host.Context.DocumentPath = document; Click(window.PlanButton); window.ConfirmStart = _ => true; Click(window.StartButton); Require(client.Starts == 1, "Confirmed matching export did not start exactly once.");
        client.Pending = true; Click(window.StatusButton); Click(window.ReconcileButton); Require(client.Reconciles == 1 && client.Starts == 1, "Read-only reconciliation replayed apply."); client.Pending = false;
        client.ReportReady = true; Click(window.StatusButton); Click(window.ReportButton); Require(client.Starts == 1 && client.Reports == 1, "Report retry replayed a model write.");
        Console.WriteLine("PASS Web platform UI preview, confirmation, document race, read-only recovery and independent report retry");
        var content = (FrameworkElement)window.Content; content.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Require(window.Runs.Columns[0].ActualWidth > 250 && window.Runs.Columns[1].ActualWidth > 120, "Platform status columns collapsed during actual WPF layout.");
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(root, "web-platform-window.png"))) encoder.Save(file);
        window.Close(); Console.WriteLine("PASS actual WPF platform window rendered to PNG without Revit or real platform access"); return 0;
    }
    private static void Pump(Task task) { var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher; task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false))); if (!task.IsCompleted) Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult(); }
    private sealed class Host : IPlatformSyncHost { public PlatformExportContext Context; public Task<PlatformExportContext> ReadCurrentAsync() => Task.FromResult(Context); public void Close() { } }
    private sealed class Client : IPlatformSyncClient
    {
        private readonly string document, unique; public int Starts, Reports, Reconciles; public bool ReportReady, Pending, ReturnWrongTarget; private readonly string runId = Guid.NewGuid().ToString();
        public Client(string document, string unique) { this.document = document; this.unique = unique; }
        private SyncBinding Binding => new SyncBinding { id = "binding", project_id = "project", project_name = "Web 平台同步体验检查", project_code = "WEB-2026", remote_model_id = "model", remote_model_name = "测试模型.ifc", document_path = document, project_unique_id = unique };
        public Task<SyncStatus> StatusAsync(PlatformSyncSettings s) => Task.FromResult(new SyncStatus { bindings = new[] { Binding }, runs = Pending ? new[] { new SyncRun { id = runId, state = "UNKNOWN_RESULT", handoff = new SyncRunHandoff { original_name = "测试模型.ifc" } } } : ReportReady ? new[] { new SyncRun { id = runId, state = "SUCCEEDED", calculation_status = "completed", regulation_status = "passed", report = new SyncReport { state = "FAILED", reason = "仅报告重试；模型不重复提交" }, handoff = new SyncRunHandoff { original_name = "测试模型.ifc" } } } : new SyncRun[0] });
        public Task ConfigureCredentialAsync(PlatformSyncSettings s, string entry) => Task.CompletedTask;
        public Task<SyncInspection> InspectAsync(PlatformSyncSettings s) => Task.FromResult(new SyncInspection { project_id = "project", scene_project_id = "project", project_name = Binding.project_name, project_code = Binding.project_code, writable = true, models = new[] { new SyncRemoteModel { id = "model", name = "测试模型.ifc", generation = "generation" } } });
        public Task<SyncBinding> BindAsync(PlatformSyncSettings s, string id, string handoff) => Task.FromResult(Binding);
        public Task<SyncRun> PlanAsync(PlatformSyncSettings s, string id, string handoff) => Task.FromResult(new SyncRun { id = runId, state = "PLANNED", project_id = "project", baseline = new SyncRemoteModel { id = ReturnWrongTarget ? "other-model" : "model", name = "测试模型.ifc", generation = "generation" }, handoff = JsonWire.Deserialize<SyncRunHandoff>(File.ReadAllText(handoff)) });
        public Task<SyncWorkerLaunch> StartAsync(PlatformSyncSettings s, string id) { Starts++; return Task.FromResult(new SyncWorkerLaunch { started = true, run_id = id, pid = 1 }); }
        public Task<SyncWorkerLaunch> ReportAsync(PlatformSyncSettings s, string id) { Reports++; return Task.FromResult(new SyncWorkerLaunch { started = true, run_id = id, pid = 1 }); }
        public Task<SyncRun> ReconcileAsync(PlatformSyncSettings s, string id) { Reconciles++; return Task.FromResult(new SyncRun { id = id, state = "UNKNOWN_RESULT" }); }
    }
}
