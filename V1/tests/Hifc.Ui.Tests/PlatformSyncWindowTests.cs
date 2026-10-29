using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
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

internal static class PlatformSyncWindowTests
{
    static void Assert(bool ok, string why) { if (!ok) throw new Exception(why); }
    static IEnumerable<T> Visible<T>(DependencyObject root) where T : FrameworkElement
    {
        if (root is T item && item.IsVisible) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visible<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    static void ScaledScreenshot(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        root.LayoutTransform = new ScaleTransform(1.5, 1.5); Pump(); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 1.5), (int)Math.Ceiling(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
        root.LayoutTransform = Transform.Identity; Pump();
    }
    static void Pump() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { })); }
    static void Await(Task operation) { var watch = Stopwatch.StartNew(); while (!operation.IsCompleted && watch.ElapsedMilliseconds < 15000) { Pump(); System.Threading.Thread.Sleep(5); } Assert(operation.IsCompleted, "Window operation did not finish"); operation.GetAwaiter().GetResult(); Pump(); }
    static void Click(PlatformSyncWindow window, Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Await(window.LastOperation); }
    sealed class Host : IPlatformSyncHost
    {
        internal PlatformExportContext Context; internal int Reads; internal Action<int> OnRead;
        public Task<PlatformExportContext> ReadCurrentAsync() { Reads++; OnRead?.Invoke(Reads); return Task.FromResult(Context); }
        public void Close() { }
    }
    sealed class Client : IPlatformSyncClient
    {
        internal SyncStatus Status; internal SyncInspection Inspection; internal int Plans, Binds, Starts; internal string LastModel, LastBinding, LastHandoff, LastRun, PlannedName;
        internal TaskCompletionSource<SyncRun> Pending; internal int Reports;
        public Task<SyncWorkerLaunch> ReportAsync(PlatformSyncSettings settings, string runId) { Reports++; LastRun = runId; return Task.FromResult(new SyncWorkerLaunch { started = true, run_id = runId, pid = 43 }); }
        public Task<SyncStatus> StatusAsync(PlatformSyncSettings settings) => Task.FromResult(Status);
        public Task<SyncInspection> InspectAsync(PlatformSyncSettings settings) => Task.FromResult(Inspection);
        public Task<SyncBinding> BindAsync(PlatformSyncSettings settings, string modelId, string handoff) { Binds++; LastModel = modelId; LastHandoff = handoff; return Task.FromResult(Status.bindings[0]); }
        public Task<SyncRun> PlanAsync(PlatformSyncSettings settings, string bindingId, string handoff) { Plans++; LastBinding = bindingId; LastHandoff = handoff; return Pending?.Task ?? Task.FromResult(new SyncRun { id = "run-test", state = "PLANNED", calculation_status = "NOT_STARTED", regulation_status = "NOT_EVALUATED", handoff = new SyncRunHandoff { original_name = PlannedName } }); }
        public Task<SyncWorkerLaunch> StartAsync(PlatformSyncSettings settings, string runId) { Starts++; LastRun = runId; return Task.FromResult(new SyncWorkerLaunch { started = true, run_id = runId, pid = 42 }); }
    }
    sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "hifc-sync-ui-" + Guid.NewGuid().ToString("N"));
        internal Host Host; internal Client Client; internal PlatformSyncWindow Window; internal string Ifc, Receipt, Document;
        internal Fixture(bool configured = false, bool missingWorker = false, string building = "总图")
        {
            Directory.CreateDirectory(Root); Ifc = Path.Combine(Root, "OUT_学校_报规_" + building + "_20261026_V1.ifc"); Receipt = Path.Combine(Root, "native.receipt.json"); Document = Path.Combine(Root, "学校_" + building + ".rvt");
            File.WriteAllText(Ifc, "ISO-10303-21;\nDATA;\nENDSEC;\nEND-ISO-10303-21;", new UTF8Encoding(false)); var id = Guid.NewGuid().ToString("D");
            var receipt = new { schema_version = "hifc.native-export-receipt/1.5", export_id = id, ifc_path = Ifc, ifc_sha256 = PlatformSyncHandoff.Sha256(Ifc), source_snapshot_before = "snapshot", source_snapshot_after = "snapshot", model_transaction = "RolledBack", export_scope = "CURRENT_PROJECT", doc_export_calls = 1, ifc_postprocessed = false, original_export_server_restored = true, temporary_type_schedules_rolled_back = true };
            File.WriteAllText(Receipt, new JavaScriptSerializer().Serialize(receipt), new UTF8Encoding(false));
            Host = new Host { Context = new PlatformExportContext { DocumentPath = Document, ProjectUniqueId = "project-1", Completed = PlatformSyncHandoff.RegisterCompleted(Ifc, Receipt, id, Document, "project-1"), ExportStatus = "已成功导出" } };
            Client = new Client { Status = new SyncStatus { bindings = new[] { new SyncBinding { id = "binding-1", project_id = "project", project_code = "code", project_name = "学校", remote_model_id = "model-1", remote_model_name = "总图全名", document_path = Document, project_unique_id = "project-1" }, new SyncBinding { id = "other", remote_model_name = "另一个项目", document_path = Path.Combine(Root, "other.rvt"), project_unique_id = "project-2" } } },
                Inspection = new SyncInspection { project_id = "project", scene_project_id = "project", project_code = "code", project_name = "学校", models = new[] { new SyncRemoteModel { id = "model-1", name = "总图全名", generation = "1" } } } };
            Client.PlannedName = Path.GetFileName(Ifc);
            if (configured) {
                var settings = new PlatformSyncSettings { NodePath = Path.Combine(Root, "node.exe"), WorkerPath = Path.Combine(Root, "cli.js"), ConfigPath = Path.Combine(Root, "config.json") };
                File.WriteAllText(settings.NodePath, "test placeholder"); File.WriteAllText(settings.WorkerPath, "test placeholder"); File.WriteAllText(settings.ConfigPath, "{}"); settings.Save(Path.Combine(Root, "ui-settings.json"));
                if (missingWorker) File.Delete(settings.WorkerPath);
            }
            Window = new PlatformSyncWindow(Host, Client, Path.Combine(Root, "ui-settings.json"), Path.Combine(Root, "frozen"));
            Window.Show(); Pump();
        }
        public void Dispose() { Window.Close(); Directory.Delete(Root, true); }
    }
    internal static void Run(Action<string, Action> check, string output)
    {
        check("sync_report_interrupted_states_allow_retry_without_model_upload", () => {
            foreach (var state in new[] { "PENDING", "DOWNLOADING", "PUBLISHING" }) using (var f = new Fixture(true)) {
                f.Client.Status.runs = new[] { new SyncRun { id = "interrupted-report", state = "SUCCEEDED", calculation_status = "completed", report = new SyncReport { state = state } } };
                Click(f.Window, f.Window.StatusButton);
                Assert(f.Window.ReportButton.IsEnabled, state + " permanently prevents report recovery");
                Click(f.Window, f.Window.ReportButton);
                Assert(f.Client.Reports == 1 && f.Client.Starts == 0, "Recovery started model upload instead of report retry");
            }
        });
        check("sync_comprehensive_report_card_has_independent_download_status", () => { using (var f = new Fixture(true)) {
            var property = typeof(PlatformSyncWindow).GetProperty("ReportButton"); Assert(property != null, "综合报告 download control missing");
            var button = (Button)property.GetValue(f.Window); Assert(!button.IsEnabled, "Unfinished task permits report download");
            f.Client.Status.runs = new[] { new JavaScriptSerializer().Deserialize<SyncRun>("{\"id\":\"successful-run\",\"state\":\"SUCCEEDED\",\"calculation_status\":\"completed\",\"report\":{\"state\":\"FAILED\",\"reason\":\"下载连接中断\"}}") };
            Click(f.Window, f.Window.StatusButton); Assert(button.IsEnabled, "Successful task cannot retry report");
            Assert(Visible<TextBlock>(f.Window).Any(t => t.Text.Contains("下载连接中断")), "Report failure is not independently explained");
            Click(f.Window, button); Assert(f.Client.Reports == 1 && f.Client.Starts == 0 && f.Client.LastRun == "successful-run", "Report retry starts model upload or wrong task");
            ((ScrollViewer)f.Window.Content).ScrollToBottom(); Pump(); Program.Screenshot(f.Window, Path.Combine(output, "platform-comprehensive-report.png"));
        } });
        check("sync_window_valid_saved_settings_stay_collapsed", () => { using (var f = new Fixture(true)) { Assert(!Visible<Expander>(f.Window).Single().IsExpanded, "Valid technical settings still dominate the primary flow"); } });
        check("sync_window_missing_or_stale_settings_are_explained_and_expanded", () => { foreach (var stale in new[] { false, true }) using (var f = new Fixture(stale, stale)) { Assert(Visible<Expander>(f.Window).Single().IsExpanded && Visible<TextBlock>(f.Window).Any(x => x.Text.Contains("配置") && (x.Text.Contains("缺失") || x.Text.Contains("未完成"))), "Missing setup is not explained beside the expanded settings"); ((ScrollViewer)f.Window.Content).ScrollToBottom(); Pump(); Program.Screenshot(f.Window, Path.Combine(output, stale ? "platform-sync-stale-settings.png" : "platform-sync-missing-settings.png")); } });
        check("sync_window_known_review_state_disables_start_and_shows_reason", () => { using (var f = new Fixture(true)) { f.Client.Inspection.writable = false; Click(f.Window, f.Window.InspectButton); Click(f.Window, f.Window.StatusButton); Click(f.Window, f.Window.PlanButton); Assert(!f.Window.StartButton.IsEnabled && Convert.ToString(f.Window.StartButton.ToolTip).Contains("不可编辑"), "Known non-editable project still offers a start button without its reason"); } });
        check("sync_window_task_list_shows_file_name_and_keeps_internal_id_in_details", () => { using (var f = new Fixture(true)) { var name = "OUT_学校_报规_大门_20261026_V1.ifc"; f.Client.Status.runs = new[] { new JavaScriptSerializer().Deserialize<SyncRun>("{\"id\":\"internal-operation-id\",\"state\":\"CALCULATING\",\"handoff\":{\"original_name\":\"" + name + "\"}}") }; Click(f.Window, f.Window.StatusButton); Pump(); var texts = Visible<TextBlock>(f.Window).Select(x => x.Text).ToArray(); Assert(texts.Contains(name) && !texts.Contains("internal-operation-id"), "Task list still emphasizes an internal ID instead of the IFC filename"); } });
        check("sync_window_layout_captures_ready_review_running_failure_and_narrow_states", () => {
            using (var f = new Fixture(true, false, "大门")) {
                var name = "OUT_歇马镇中心学校_报规_大门及附属管理用房_20261026_V1.ifc";
                f.Host.Context.IsSimulation = true;
                f.Client.Inspection.project_name = f.Client.Status.bindings[0].project_name = "保康县歇马镇中心学校综合楼建设项目";
                f.Client.Inspection.project_code = f.Client.Status.bindings[0].project_code = "2606-420626-04-01-549144";
                f.Client.Status.bindings[0].remote_model_name = f.Client.Inspection.models[0].name = name;
                f.Client.Inspection.writable = true; Click(f.Window, f.Window.InspectButton); Click(f.Window, f.Window.StatusButton); Click(f.Window, f.Window.PlanButton);
                Program.Screenshot(f.Window, Path.Combine(output, "platform-sync-ready.png"));
                ScaledScreenshot(f.Window, Path.Combine(output, "platform-sync-scaled-150.png"));
                f.Client.Inspection.writable = false; f.Client.Inspection.approval_state = 1; Click(f.Window, f.Window.InspectButton); Program.Screenshot(f.Window, Path.Combine(output, "platform-sync-review.png"));
                f.Window.Width = 940; f.Window.Height = 720; Pump(); Program.Screenshot(f.Window, Path.Combine(output, "platform-sync-narrow.png"));
                f.Window.Width = 1120; f.Window.Height = 860; f.Client.Inspection.writable = true; f.Client.Inspection.approval_state = 0; Click(f.Window, f.Window.InspectButton);
                foreach (var state in new[] { "CALCULATING", "FAILED" }) {
                    f.Client.Status.runs = new[] { new SyncRun { id = "fixture-task-id", state = state, calculation_status = state == "FAILED" ? "failed" : "running", regulation_status = "unknown", reason = state == "FAILED" ? "连接中断，保留证据并等待核对。" : "等待本轮计算。", handoff = new SyncRunHandoff { original_name = name } } };
                    Click(f.Window, f.Window.StatusButton); Program.Screenshot(f.Window, Path.Combine(output, state == "FAILED" ? "platform-sync-failed.png" : "platform-sync-running.png"));
                }
                Visible<Expander>(f.Window).Single().IsExpanded = true; Pump(); ((ScrollViewer)f.Window.Content).ScrollToBottom(); Pump(); Program.Screenshot(f.Window, Path.Combine(output, "platform-sync-details.png"));
            }
        });
        check("sync_window_starts_only_confirmed_plan_in_background", () => { using (var f = new Fixture()) { f.Client.Inspection.writable = true; f.Window.ConfirmStart = run => true; Click(f.Window, f.Window.StatusButton); Click(f.Window, f.Window.PlanButton); Click(f.Window, f.Window.StartButton); Assert(f.Client.Starts == 1 && f.Client.LastRun == "run-test" && f.Window.StatusText.Text.Contains("后台"), "Confirmed plan did not launch independently"); Click(f.Window, f.Window.StartButton); Assert(f.Client.Starts == 1, "Duplicate worker launch permitted"); } });
        check("sync_window_review_state_blocks_worker_launch", () => { using (var f = new Fixture()) { f.Client.Inspection.writable = false; f.Window.ConfirmStart = run => true; Click(f.Window, f.Window.StatusButton); Click(f.Window, f.Window.PlanButton); Click(f.Window, f.Window.StartButton); Assert(f.Client.Starts == 0 && f.Window.StatusText.Text.Contains("可编辑"), "Review-state start was not blocked clearly"); } });
        check("sync_window_rechecks_document_before_start", () => { using (var f = new Fixture()) { f.Client.Inspection.writable = true; f.Window.ConfirmStart = run => true; Click(f.Window, f.Window.StatusButton); Click(f.Window, f.Window.PlanButton); f.Host.Context.DocumentPath = Path.Combine(f.Root, "another.rvt"); Click(f.Window, f.Window.StartButton); Assert(f.Client.Starts == 0, "Changed document could start old plan"); } });
        check("sync_window_cancelled_confirmation_never_starts_worker", () => { using (var f = new Fixture()) { f.Client.Inspection.writable = true; f.Window.ConfirmStart = run => false; Click(f.Window, f.Window.StatusButton); Click(f.Window, f.Window.PlanButton); Click(f.Window, f.Window.StartButton); Assert(f.Client.Starts == 0, "Cancel was ignored"); } });
        check("sync_preview_window_has_independent_settings_plan_and_status_controls", () => {
            using (var f = new Fixture()) {
                Assert((string)f.Window.PlanButton.Content == "3  预览同步" && (string)f.Window.StatusButton.Content == "刷新同步状态", "Preview actions missing");
                Assert(!f.Window.ModelSelector.IsEditable && !f.Window.BindingSelector.IsEditable, "Internal IDs can be typed");
                Assert(((ScrollViewer)f.Window.Content).Background != null, "Preview background is transparent in isolated capture");
                Program.Screenshot(f.Window, Path.Combine(output, "platform-sync-preview.png"));
            }
        });
        check("sync_window_filters_bindings_by_saved_document_and_project_identity", () => { using (var f = new Fixture()) { Click(f.Window, f.Window.StatusButton); Assert(f.Window.BindingSelector.Items.Count == 1 && ((SyncBinding)f.Window.BindingSelector.SelectedItem).id == "binding-1", "Other document binding exposed"); } });
        check("sync_window_plan_freezes_verified_success_and_only_creates_a_plan", () => { using (var f = new Fixture()) { Click(f.Window, f.Window.StatusButton); Click(f.Window, f.Window.PlanButton); Assert(f.Client.Plans == 1 && f.Client.LastBinding == "binding-1" && File.Exists(f.Client.LastHandoff), "Frozen plan not submitted"); Assert(f.Window.StatusText.Text.Contains("平台模型尚未替换") && ((SyncRun)f.Window.Runs.Items[0]).regulation_status == "NOT_EVALUATED", "Preview reported platform success"); } });
        check("sync_window_without_complete_export_cannot_plan", () => { using (var f = new Fixture()) { Click(f.Window, f.Window.StatusButton); f.Host.Context.Completed = null; f.Host.Context.ExportStatus = "本轮导出尚未完成"; Click(f.Window, f.Window.PlanButton); Assert(f.Client.Plans == 0 && f.Window.StatusText.Text.Contains("尚未完成"), "Incomplete export accepted"); } });
        check("sync_window_modified_export_is_not_queued", () => { using (var f = new Fixture()) { Click(f.Window, f.Window.StatusButton); File.AppendAllText(f.Ifc, "tampered"); Click(f.Window, f.Window.PlanButton); Assert(f.Client.Plans == 0, "Modified file queued"); } });
        check("sync_window_saved_as_model_requires_new_export_and_binding", () => { using (var f = new Fixture()) { f.Host.Context.DocumentPath = Path.Combine(f.Root, "copied.rvt"); Click(f.Window, f.Window.StatusButton); Assert(f.Window.BindingSelector.Items.Count == 0, "Save As inherited binding"); Click(f.Window, f.Window.PlanButton); Assert(f.Client.Plans == 0, "Copied model queued"); } });
        check("sync_window_active_document_is_rechecked_after_freeze", () => { using (var f = new Fixture()) { Click(f.Window, f.Window.StatusButton); f.Host.OnRead = count => { if (count == 3) f.Host.Context.DocumentPath = Path.Combine(f.Root, "switched.rvt"); }; Click(f.Window, f.Window.PlanButton); Assert(f.Client.Plans == 0, "Active document switch during freeze accepted"); } });
        check("sync_window_requires_explicit_remote_model_selection", () => { using (var f = new Fixture()) { Click(f.Window, f.Window.InspectButton); Assert(f.Window.ModelSelector.SelectedItem == null, "Model was guessed"); Click(f.Window, f.Window.BindButton); Assert(f.Client.Binds == 0, "No-selection bind proceeded"); f.Window.ModelSelector.SelectedIndex = 0; Click(f.Window, f.Window.BindButton); Assert(f.Client.Binds == 1 && f.Client.LastModel == "model-1" && File.Exists(f.Client.LastHandoff), "Selected model binding failed"); } });
        check("sync_window_wrong_scene_cannot_offer_binding", () => { using (var f = new Fixture()) { f.Client.Inspection.scene_project_id = "other"; Click(f.Window, f.Window.InspectButton); Assert(f.Window.ModelSelector.Items.Count == 0 && f.Window.StatusText.Text.Contains("不一致"), "Wrong scene allowed"); } });
        check("sync_window_failed_reinspection_clears_previous_remote_model_selection", () => { using (var f = new Fixture()) { Click(f.Window, f.Window.InspectButton); f.Window.ModelSelector.SelectedIndex = 0; f.Client.Inspection.scene_project_id = "other"; Click(f.Window, f.Window.InspectButton); Assert(f.Window.ModelSelector.Items.Count == 0 && f.Window.ModelSelector.SelectedItem == null, "Stale model selection survived failed reinspection"); Click(f.Window, f.Window.BindButton); Assert(f.Client.Binds == 0, "Failed reinspection could bind a stale model"); } });
        check("sync_window_changed_config_invalidates_previous_platform_selections", () => { using (var f = new Fixture()) { Click(f.Window, f.Window.StatusButton); Click(f.Window, f.Window.InspectButton); f.Window.ConfigPathBox.Text = Path.Combine(f.Root, "different.json"); Assert(f.Window.BindingSelector.Items.Count == 0 && f.Window.ModelSelector.Items.Count == 0, "Stale platform selections survived config change"); } });
        check("sync_window_double_click_cannot_create_duplicate_plan", () => { using (var f = new Fixture()) { Click(f.Window, f.Window.StatusButton); f.Client.Pending = new TaskCompletionSource<SyncRun>(); f.Window.PlanButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var watch = Stopwatch.StartNew(); while (f.Client.Plans == 0 && watch.ElapsedMilliseconds < 5000) { Pump(); System.Threading.Thread.Sleep(5); } f.Window.PlanButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert(f.Client.Plans == 1 && !f.Window.PlanButton.IsEnabled, "Duplicate plan allowed"); f.Client.Pending.SetResult(new SyncRun { id = "one", state = "PLANNED" }); Await(f.Window.LastOperation); } });
        check("sync_client_redacts_credentials_and_urls_before_display", () => { var text = PlatformSyncClient.SafeText("token=secret123 https://example.invalid/?token=secret456 cookie=private"); Assert(!text.Contains("secret123") && !text.Contains("secret456") && !text.Contains("private"), "Credentials leaked"); });
        check("sync_client_uses_literal_unicode_arguments_without_shell", () => {
            using (var f = new Fixture()) {
                var node = @"C:\Program Files\nodejs\node.exe"; Assert(File.Exists(node), "Node runtime missing");
                var entry = Path.Combine(f.Root, "cli.js"); var config = Path.Combine(f.Root, "平台 & test.json"); File.WriteAllText(config, "{}");
                File.WriteAllText(entry, "process.stdout.write(JSON.stringify({bindings:[],runs:[{id:process.argv[process.argv.indexOf('--config')+1],state:'PLANNED',calculation_status:'未运行',regulation_status:'未审查'}]}));", new UTF8Encoding(false));
                var task = new PlatformSyncClient().StatusAsync(new PlatformSyncSettings { NodePath = node, WorkerPath = entry, ConfigPath = config }); Await(task);
                Assert(task.Result.runs[0].id == config && task.Result.runs[0].calculation_status == "未运行", "Arguments or UTF-8 were corrupted");
            }
        });
        check("sync_window_run_labels_distinguish_calculation_from_regulation", () => {
            var run = new SyncRun { state = "SUCCEEDED", calculation_status = "completed", regulation_status = "failed" };
            Assert(run.StateLabel == "同步完成" && run.CalculationLabel == "计算完成" && run.RegulationLabel == "审查未通过", "User-facing status confused calculation and regulation");
        });
    }
}
