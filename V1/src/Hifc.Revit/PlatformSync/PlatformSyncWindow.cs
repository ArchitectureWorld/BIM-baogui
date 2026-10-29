using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hifc.Reference;
using Hifc.Revit.Management;
using Microsoft.Win32;

namespace Hifc.Revit.PlatformSync
{
    public sealed partial class PlatformSyncWindow : Window
    {
        readonly IPlatformSyncHost host;
        readonly IPlatformSyncClient client;
        readonly string settingsPath, spoolRoot;
        readonly WrapPanel actions = new WrapPanel();
        bool busy;
        bool closed;
        SyncInspection inspection;
        SyncRun plannedRun; SyncBinding plannedBinding;
        string plannedDocument, plannedProjectUniqueId, plannedExportId;
        public TextBox NodePathBox { get; } = new TextBox();
        public TextBox WorkerPathBox { get; } = new TextBox();
        public TextBox ConfigPathBox { get; } = new TextBox();
        public ComboBox ModelSelector { get; } = new ComboBox { DisplayMemberPath = "name" };
        public ComboBox BindingSelector { get; } = new ComboBox { DisplayMemberPath = "remote_model_name" };
        public Button SaveSettingsButton { get; } = new Button { Content = "保存平台设置" };
        public Button InspectButton { get; } = new Button { Content = "读取平台模型" };
        public Button BindButton { get; } = new Button { Content = "确认绑定选中模型" };
        public Button PlanButton { get; } = new Button { Content = "同步本次 IFC（预览）" };
        public Button StatusButton { get; } = new Button { Content = "刷新同步状态" };
        public Button StartButton { get; } = new Button { Content = "确认并开始同步" };
        public Button ReportButton { get; } = new Button { Content = "下载／重试" };
        public Button OpenReportFolderButton { get; } = new Button { Content = "打开目录" };
        public TextBlock ReportText { get; } = new TextBlock { TextWrapping = TextWrapping.Wrap };
        public Action<string> OpenReportDirectory { get; set; } = path => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", PlatformSyncClient.QuoteArgument(path)) { UseShellExecute = false });
        public Func<SyncRun, bool> ConfirmStart { get; set; }
        public TextBlock StatusText { get; } = new TextBlock { TextWrapping = TextWrapping.Wrap };
        public TextBlock ExportText { get; } = new TextBlock { TextWrapping = TextWrapping.Wrap };
        public TextBlock ProjectText { get; } = new TextBlock { TextWrapping = TextWrapping.Wrap };
        public DataGrid Runs { get; } = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, MinHeight = 125, CanUserAddRows = false };
        public Task LastOperation { get; private set; } = Task.CompletedTask;
        public string LastHandoffPath { get; private set; }
        public bool IsBusy => busy;
        public PlatformSyncWindow(IPlatformSyncHost host, IPlatformSyncClient client, string settingsPath, string spoolRoot)
        {
            this.host = host; this.client = client; this.settingsPath = settingsPath; this.spoolRoot = spoolRoot;
            BuildLayout();
            SaveSettingsButton.Click += (s, e) => Start(async () => { CurrentSettings().Save(settingsPath); LoadConfigurationState(); await Task.CompletedTask; SetStatus("平台设置已保存。", false); });
            InspectButton.Click += (s, e) => Start(InspectAsync); BindButton.Click += (s, e) => Start(BindAsync); PlanButton.Click += (s, e) => Start(PlanAsync); StatusButton.Click += (s, e) => Start(RefreshAsync);
            StartButton.Click += (s, e) => Start(StartSyncAsync);
            ReportButton.Click += (s, e) => Start(DownloadReportAsync);
            OpenReportFolderButton.Click += (s, e) => Start(async () => {
                var path = (Runs.SelectedItem as SyncRun)?.report?.pdf_path;
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !Directory.Exists(Path.GetDirectoryName(path))) throw new InvalidOperationException("综合报告目录尚不可用。");
                OpenReportDirectory(Path.GetDirectoryName(path)); await Task.CompletedTask;
            });
            ConfirmStart = run => MessageBox.Show(this, "项目：" + plannedBinding.project_name + "\n编号：" + plannedBinding.project_code + "\n替换模型：" + plannedBinding.remote_model_name + "\n新文件：" + ExportText.Text + "\n\n程序会先备份旧 IFC，再替换这个模型。是否开始？", "确认平台同步", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            Closed += (s, e) => { closed = true; host.Close(); };
            try { var saved = PlatformSyncSettings.Load(settingsPath); NodePathBox.Text = saved.NodePath; WorkerPathBox.Text = saved.WorkerPath; ConfigPathBox.Text = saved.ConfigPath; }
            catch (Exception) { SetStatus("平台设置暂存未能读取，请重新选择程序及配置文件。", true); }
            LoadConfigurationState(); UpdateActionHint();
            foreach (var box in new[] { NodePathBox, WorkerPathBox, ConfigPathBox }) box.TextChanged += (s, e) => { plannedRun = null; StartButton.IsEnabled = false; inspection = null; ModelSelector.ItemsSource = null; BindingSelector.ItemsSource = null; ProjectText.Text = "配置已变化，请重新读取平台模型与同步状态。"; platformState.Text = "配置已变化 · 待核对"; configurationValid = false; settingsExpander.IsExpanded = true; settingsHint.Text = "配置已修改，请保存后重新读取平台。"; ShowBinding(); UpdateActionHint(); };
            BindingSelector.SelectionChanged += (s, e) => { if (!busy) { plannedRun = null; StartButton.IsEnabled = false; } ShowBinding(); UpdateActionHint(); };
        }
        UIElement PathRow(string title, TextBox box, string filter)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) }; var label = new TextBlock { Text = title, Width = 85, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(label, Dock.Left); row.Children.Add(label);
            var browse = new Button { Content = "选择…", Width = 80, Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(InputSurface(box));
            browse.Click += (s, e) => { if (busy) return; var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true }; if (dialog.ShowDialog(this) == true) box.Text = dialog.FileName; }; return row;
        }
        PlatformSyncSettings CurrentSettings() => new PlatformSyncSettings { NodePath = NodePathBox.Text.Trim(), WorkerPath = WorkerPathBox.Text.Trim(), ConfigPath = ConfigPathBox.Text.Trim() };
        void SetStatus(string text, bool failed) { StatusText.Text = PlatformSyncClient.SafeText(text); StatusText.Foreground = LiquidGlassTheme.Color(failed ? "#B83138" : "#26465B"); }
        public void ReadExportStatus() { Start(async () => { await CurrentAsync(false); }); }
        void Start(Func<Task> action)
        {
            if (busy || closed) return;
            // Revit can invoke a modeless window without installing WPF's context.
            // Explicitly capture this dispatcher before any asynchronous file/process work.
            var previous = SynchronizationContext.Current;
            try { SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher)); LastOperation = ExecuteAsync(action); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        async Task ExecuteAsync(Func<Task> action)
        {
            busy = true; SetEnabled(false);
            try { await action(); } catch (Exception error) { SetStatus(error.Message, true); }
            finally { busy = false; SetEnabled(true); }
        }
        void SetEnabled(bool value)
        {
            foreach (var c in new Control[] { SaveSettingsButton, InspectButton, BindButton, PlanButton, StatusButton, ModelSelector, BindingSelector, NodePathBox, WorkerPathBox, ConfigPathBox }) c.IsEnabled = value;
            StartButton.IsEnabled = value && plannedRun != null && inspection?.writable != false;
            UpdateActionHint(); UpdateReportActions();
        }
        async Task<PlatformExportContext> CurrentAsync(bool requireExport)
        {
            var context = await host.ReadCurrentAsync();
            if (requireExport) context.RequireMatchingExport();
            sourceText.Text = PlatformSyncClient.SafeText(string.IsNullOrWhiteSpace(context.DocumentPath) ? "当前未打开已保存的 Revit 模型" : Path.GetFileNameWithoutExtension(context.DocumentPath));
            sourceText.ToolTip = PlatformSyncClient.SafeText(context.DocumentPath);
            ExportText.Text = PlatformSyncClient.SafeText(context.Completed == null ? context.ExportStatus : (context.IsSimulation ? "模拟宿主，仅用于本机界面与交接演练。样本 IFC：" : "本次成功导出：") + Path.GetFileName(context.Completed.IfcPath));
            ExportText.ToolTip = PlatformSyncClient.SafeText(context.Completed?.IfcPath ?? context.ExportStatus);
            return context;
        }
        async Task RefreshAsync()
        {
            var context = await CurrentAsync(false); var status = await client.StatusAsync(CurrentSettings()); var previous = (BindingSelector.SelectedItem as SyncBinding)?.id;
            var bindings = (status.bindings ?? new SyncBinding[0]).Where(b => PlatformSyncHandoff.SameDocument(b.document_path, b.project_unique_id, context.DocumentPath, context.ProjectUniqueId)).ToArray();
            BindingSelector.ItemsSource = bindings; BindingSelector.SelectedItem = bindings.FirstOrDefault(b => b.id == previous) ?? (bindings.Length == 1 ? bindings[0] : null);
            SetRuns(status.runs); ShowBinding(); SetStatus(bindings.Length == 0 ? "当前项目尚无已确认的平台绑定，请先读取平台模型并确认绑定。" : "同步状态已刷新。", false);
        }
        async Task InspectAsync()
        {
            inspection = null; ModelSelector.ItemsSource = null; ModelSelector.SelectedItem = null;
            await CurrentAsync(false); inspection = await client.InspectAsync(CurrentSettings());
            if (string.IsNullOrWhiteSpace(inspection.project_id) || inspection.project_id != inspection.scene_project_id) { inspection = null; throw new InvalidOperationException("编辑项目与三维场景项目不一致，不能确认绑定。"); }
            ModelSelector.ItemsSource = inspection.models ?? new SyncRemoteModel[0]; ModelSelector.SelectedItem = null;
            ShowProject(inspection); SetStatus("已读取平台模型。请按完整名称选中要绑定的模型，再确认绑定。", false);
        }
        async Task<string> FreezeAsync(PlatformExportContext context)
        {
            var handoff = await Task.Run(() => PlatformSyncHandoff.Freeze(context.Completed, spoolRoot, context.DocumentPath, context.ProjectUniqueId));
            var active = await CurrentAsync(true);
            if (closed) throw new InvalidOperationException("窗口已关闭，未提交同步计划。");
            if (!PlatformSyncHandoff.SameDocument(context.DocumentPath, context.ProjectUniqueId, active.DocumentPath, active.ProjectUniqueId)
                || active.Completed.ExportId != context.Completed.ExportId) throw new InvalidOperationException("当前文档或本次导出已变化，未提交计划，请重新操作。");
            LastHandoffPath = handoff; return handoff;
        }
        async Task BindAsync()
        {
            var model = ModelSelector.SelectedItem as SyncRemoteModel;
            if (inspection == null || model == null || (inspection.models ?? new SyncRemoteModel[0]).Count(m => m.id == model.id) != 1)
                throw new InvalidOperationException("请先读取平台模型，并明确选择一个完整模型名称。");
            var context = await CurrentAsync(true); var frozen = await FreezeAsync(context);
            await client.BindAsync(CurrentSettings(), model.id, frozen); await RefreshAsync(); SetStatus("绑定已保存。可创建本次 IFC 的同步预览计划。", false);
        }
        async Task PlanAsync()
        {
            plannedRun = null;
            var binding = BindingSelector.SelectedItem as SyncBinding;
            if (binding == null) throw new InvalidOperationException("请先确认当前项目的平台模型绑定。");
            var context = await CurrentAsync(true);
            if (!PlatformSyncHandoff.SameDocument(binding.document_path, binding.project_unique_id, context.DocumentPath, context.ProjectUniqueId))
                throw new InvalidOperationException("平台绑定与当前模型不一致，未创建计划。");
            var frozen = await FreezeAsync(context); var run = await client.PlanAsync(CurrentSettings(), binding.id, frozen);
            if (run.state == "PLANNED") { plannedRun = run; plannedBinding = binding; plannedDocument = context.DocumentPath; plannedProjectUniqueId = context.ProjectUniqueId; plannedExportId = context.Completed.ExportId; }
            SetRuns(new[] { run });
            SetStatus("同步预览：" + run.StateLabel + "。平台模型尚未替换，确认无误后再开始同步。", run.state == "BLOCKED" || run.state == "FAILED" || run.state == "NEEDS_LOGIN");
        }
        async Task StartSyncAsync()
        {
            if (plannedRun == null) throw new InvalidOperationException("请先为当前 IFC 创建并核对同步预览。");
            var context = await CurrentAsync(true);
            if (!PlatformSyncHandoff.SameDocument(plannedDocument, plannedProjectUniqueId, context.DocumentPath, context.ProjectUniqueId) || context.Completed.ExportId != plannedExportId)
                throw new InvalidOperationException("当前模型或本次导出已变化，请重新创建同步预览。");
            var fresh = await client.InspectAsync(CurrentSettings());
            if (fresh.project_id != plannedBinding.project_id || fresh.scene_project_id != fresh.project_id || fresh.project_code != plannedBinding.project_code || fresh.project_name != plannedBinding.project_name)
                throw new InvalidOperationException("当前平台项目与预览计划不一致，未启动同步。");
            ShowProject(fresh);
            if (fresh.writable != true) throw new InvalidOperationException("项目当前不可编辑或可编辑状态尚未核实，不能替换模型。可保留 IFC 与同步计划。");
            if (closed || ConfirmStart == null || !ConfirmStart(plannedRun)) { SetStatus("未开始同步，预览计划已保留。", false); return; }
            // Recheck the Revit document after the potentially long browser read and confirmation.
            context = await CurrentAsync(true);
            if (closed || !PlatformSyncHandoff.SameDocument(plannedDocument, plannedProjectUniqueId, context.DocumentPath, context.ProjectUniqueId) || context.Completed.ExportId != plannedExportId)
                throw new InvalidOperationException("确认期间当前模型或导出已变化，请重新预览。");
            var run = plannedRun; plannedRun = null;
            var launch = await client.StartAsync(CurrentSettings(), run.id);
            if (!launch.started || launch.run_id != run.id) throw new InvalidOperationException("后台启动结果尚未确认，请刷新同步状态。");
            SetStatus("后台同步已启动。可关闭本窗口；请保留独立浏览器，等待转换和计算完成，再刷新同步状态。", false);
        }
        async Task DownloadReportAsync()
        {
            var run = Runs.SelectedItem as SyncRun;
            if (run == null || !run.CanDownloadReport) throw new InvalidOperationException("请先选择一条计算已核验完成的同步记录。");
            var launch = await client.ReportAsync(CurrentSettings(), run.id);
            if (!launch.started || launch.run_id != run.id) throw new InvalidOperationException("综合报告后台下载尚未确认，请刷新状态。");
            run.report = new SyncReport { state = "PENDING", reason = "后台下载已启动。PDF 与同名 TXT 将保存到该任务来源 RVT 所在目录。", pdf_path = run.report?.pdf_path, txt_path = run.report?.txt_path };
            Runs.Items.Refresh(); ShowRunDetails();
            SetStatus("综合报告下载已启动；刷新状态可查看结果。模型同步状态保持不变。", false);
        }
    }
}
