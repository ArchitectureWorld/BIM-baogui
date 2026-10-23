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
using System.Windows.Threading;
using Hifc.Reference;
using Hifc.Revit.PlatformSync;

internal static class PlatformSyncWindowTests
{
    static void Assert(bool ok, string why) { if (!ok) throw new Exception(why); }
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
        internal SyncStatus Status; internal SyncInspection Inspection; internal int Plans, Binds; internal string LastModel, LastBinding, LastHandoff;
        internal TaskCompletionSource<SyncRun> Pending;
        public Task<SyncStatus> StatusAsync(PlatformSyncSettings settings) => Task.FromResult(Status);
        public Task<SyncInspection> InspectAsync(PlatformSyncSettings settings) => Task.FromResult(Inspection);
        public Task<SyncBinding> BindAsync(PlatformSyncSettings settings, string modelId, string handoff) { Binds++; LastModel = modelId; LastHandoff = handoff; return Task.FromResult(Status.bindings[0]); }
        public Task<SyncRun> PlanAsync(PlatformSyncSettings settings, string bindingId, string handoff) { Plans++; LastBinding = bindingId; LastHandoff = handoff; return Pending?.Task ?? Task.FromResult(new SyncRun { id = "run-test", state = "PLANNED", calculation_status = "NOT_STARTED", regulation_status = "NOT_EVALUATED" }); }
    }
    sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "hifc-sync-ui-" + Guid.NewGuid().ToString("N"));
        internal Host Host; internal Client Client; internal PlatformSyncWindow Window; internal string Ifc, Receipt, Document;
        internal Fixture()
        {
            Directory.CreateDirectory(Root); Ifc = Path.Combine(Root, "OUT_学校_总图.ifc"); Receipt = Path.Combine(Root, "native.receipt.json"); Document = Path.Combine(Root, "学校_总图.rvt");
            File.WriteAllText(Ifc, "ISO-10303-21;\nDATA;\nENDSEC;\nEND-ISO-10303-21;", new UTF8Encoding(false)); var id = Guid.NewGuid().ToString("D");
            var receipt = new { schema_version = "hifc.native-export-receipt/1.5", export_id = id, ifc_path = Ifc, ifc_sha256 = PlatformSyncHandoff.Sha256(Ifc), source_snapshot_before = "snapshot", source_snapshot_after = "snapshot", model_transaction = "RolledBack", export_scope = "CURRENT_PROJECT", doc_export_calls = 1, ifc_postprocessed = false, original_export_server_restored = true, temporary_type_schedules_rolled_back = true };
            File.WriteAllText(Receipt, new JavaScriptSerializer().Serialize(receipt), new UTF8Encoding(false));
            Host = new Host { Context = new PlatformExportContext { DocumentPath = Document, ProjectUniqueId = "project-1", Completed = PlatformSyncHandoff.RegisterCompleted(Ifc, Receipt, id, Document, "project-1"), ExportStatus = "已成功导出" } };
            Client = new Client { Status = new SyncStatus { bindings = new[] { new SyncBinding { id = "binding-1", project_id = "project", project_code = "code", project_name = "学校", remote_model_id = "model-1", remote_model_name = "总图全名", document_path = Document, project_unique_id = "project-1" }, new SyncBinding { id = "other", remote_model_name = "另一个项目", document_path = Path.Combine(Root, "other.rvt"), project_unique_id = "project-2" } } },
                Inspection = new SyncInspection { project_id = "project", scene_project_id = "project", project_code = "code", project_name = "学校", models = new[] { new SyncRemoteModel { id = "model-1", name = "总图全名", generation = "1" } } } };
            Window = new PlatformSyncWindow(Host, Client, Path.Combine(Root, "ui-settings.json"), Path.Combine(Root, "frozen"));
            Window.Show(); Pump();
        }
        public void Dispose() { Window.Close(); Directory.Delete(Root, true); }
    }
    internal static void Run(Action<string, Action> check, string output)
    {
        check("sync_preview_window_has_independent_settings_plan_and_status_controls", () => {
            using (var f = new Fixture()) {
                Assert((string)f.Window.PlanButton.Content == "同步本次 IFC（预览）" && (string)f.Window.StatusButton.Content == "刷新同步状态", "Preview actions missing");
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
