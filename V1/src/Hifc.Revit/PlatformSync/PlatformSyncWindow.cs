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
using Microsoft.Win32;

namespace Hifc.Revit.PlatformSync
{
    public sealed class PlatformSyncWindow : Window
    {
        readonly IPlatformSyncHost host;
        readonly IPlatformSyncClient client;
        readonly string settingsPath, spoolRoot;
        readonly StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
        bool busy;
        bool closed;
        SyncInspection inspection;
        public TextBox NodePathBox { get; } = new TextBox();
        public TextBox WorkerPathBox { get; } = new TextBox();
        public TextBox ConfigPathBox { get; } = new TextBox();
        public ComboBox ModelSelector { get; } = new ComboBox { MinWidth = 400, DisplayMemberPath = "name" };
        public ComboBox BindingSelector { get; } = new ComboBox { MinWidth = 400 };
        public Button SaveSettingsButton { get; } = new Button { Content = "保存平台设置" };
        public Button InspectButton { get; } = new Button { Content = "读取平台模型" };
        public Button BindButton { get; } = new Button { Content = "确认绑定选中模型" };
        public Button PlanButton { get; } = new Button { Content = "同步本次 IFC（预览）" };
        public Button StatusButton { get; } = new Button { Content = "刷新同步状态" };
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
            Title = "H-IFC V1 · 平台同步预开发"; Width = 1040; Height = 780; MinWidth = 940; MinHeight = 680;
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)); FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
            var root = new StackPanel { Margin = new Thickness(22) }; Content = new ScrollViewer { Content = root, Background = Background, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(new TextBlock { Text = "平台同步", FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            root.Children.Add(new TextBlock { Text = "预开发：真实平台只读。这里会检查并保存同步计划，不会上传、删除或替换平台模型。原有“导出项目 IFC”可单独使用。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkSlateBlue, Margin = new Thickness(0, 0, 0, 14) });
            var settings = new StackPanel();
            settings.Children.Add(PathRow("Node 程序", NodePathBox, "node.exe|node.exe")); settings.Children.Add(PathRow("同步程序", WorkerPathBox, "cli.js|cli.js")); settings.Children.Add(PathRow("平台配置", ConfigPathBox, "JSON 配置|*.json"));
            SaveSettingsButton.HorizontalAlignment = HorizontalAlignment.Left; SaveSettingsButton.Margin = new Thickness(0, 5, 0, 5); settings.Children.Add(SaveSettingsButton);
            settings.Children.Add(new TextBlock { Text = "同步程序位置：platform-sync/dist/src/cli.js。选择 setup 已生成的配置；登录在专用浏览器完成，访问令牌不填写在此界面。", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap });
            root.Children.Add(new Expander { Header = "平台设置", Content = settings, IsExpanded = true, Margin = new Thickness(0, 0, 0, 14) });
            root.Children.Add(ExportText); ExportText.Margin = new Thickness(0, 0, 0, 12); ExportText.Text = "请先通过本插件成功导出当前项目 IFC。";
            var modelRow = new StackPanel { Orientation = Orientation.Horizontal }; modelRow.Children.Add(ModelSelector); modelRow.Children.Add(InspectButton); modelRow.Children.Add(BindButton); root.Children.Add(modelRow);
            root.Children.Add(ProjectText); ProjectText.Margin = new Thickness(0, 7, 0, 7);
            root.Children.Add(new TextBlock { Text = "当前模型的平台绑定（另存为或复制后需重新确认）", Margin = new Thickness(0, 4, 0, 7) });
            root.Children.Add(BindingSelector); BindingSelector.HorizontalAlignment = HorizontalAlignment.Left;
            actions.Children.Add(PlanButton); actions.Children.Add(StatusButton); actions.Margin = new Thickness(0, 12, 0, 10); root.Children.Add(actions);
            root.Children.Add(StatusText); StatusText.Margin = new Thickness(0, 0, 0, 12);
            foreach (var pair in new[] { new[] { "任务", "id" }, new[] { "同步状态", "StateLabel" }, new[] { "计算状态", "CalculationLabel" }, new[] { "法规审查", "RegulationLabel" } })
                Runs.Columns.Add(new DataGridTextColumn { Header = pair[0], Binding = new System.Windows.Data.Binding(pair[1]), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            root.Children.Add(Runs);
            root.Children.Add(new TextBlock { Text = "计算完成与法规审查通过分别显示。计划通过不代表模型已提交，也不代表法规审查通过。", Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
            foreach (var button in new[] { SaveSettingsButton, InspectButton, BindButton, PlanButton, StatusButton }) { button.Padding = new Thickness(12, 7, 12, 7); button.Margin = new Thickness(0, 0, 9, 0); }
            SaveSettingsButton.Click += (s, e) => Start(async () => { CurrentSettings().Save(settingsPath); await Task.CompletedTask; SetStatus("平台设置已保存。", false); });
            InspectButton.Click += (s, e) => Start(InspectAsync); BindButton.Click += (s, e) => Start(BindAsync); PlanButton.Click += (s, e) => Start(PlanAsync); StatusButton.Click += (s, e) => Start(RefreshAsync);
            Closed += (s, e) => { closed = true; host.Close(); };
            try { var saved = PlatformSyncSettings.Load(settingsPath); NodePathBox.Text = saved.NodePath; WorkerPathBox.Text = saved.WorkerPath; ConfigPathBox.Text = saved.ConfigPath; }
            catch (Exception) { SetStatus("平台设置暂存未能读取，请重新选择程序及配置文件。", true); }
            foreach (var box in new[] { NodePathBox, WorkerPathBox, ConfigPathBox }) box.TextChanged += (s, e) => { inspection = null; ModelSelector.ItemsSource = null; BindingSelector.ItemsSource = null; ProjectText.Text = "配置已变化，请重新读取平台模型与同步状态。"; };
        }
        UIElement PathRow(string title, TextBox box, string filter)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) }; var label = new TextBlock { Text = title, Width = 85, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(label, Dock.Left); row.Children.Add(label);
            var browse = new Button { Content = "选择…", Width = 65, Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(box);
            browse.Click += (s, e) => { if (busy) return; var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true }; if (dialog.ShowDialog(this) == true) box.Text = dialog.FileName; }; return row;
        }
        PlatformSyncSettings CurrentSettings() => new PlatformSyncSettings { NodePath = NodePathBox.Text.Trim(), WorkerPath = WorkerPathBox.Text.Trim(), ConfigPath = ConfigPathBox.Text.Trim() };
        void SetStatus(string text, bool failed) { StatusText.Text = PlatformSyncClient.SafeText(text); StatusText.Foreground = failed ? Brushes.Firebrick : Brushes.DarkSlateGray; }
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
        }
        async Task<PlatformExportContext> CurrentAsync(bool requireExport)
        {
            var context = await host.ReadCurrentAsync();
            if (requireExport) context.RequireMatchingExport();
            ExportText.Text = PlatformSyncClient.SafeText(context.Completed == null ? context.ExportStatus : (context.IsSimulation ? "模拟宿主，仅用于本机界面与交接演练。样本 IFC：" : "本次成功导出：") + Path.GetFileName(context.Completed.IfcPath));
            return context;
        }
        async Task RefreshAsync()
        {
            var context = await CurrentAsync(false); var status = await client.StatusAsync(CurrentSettings()); var previous = (BindingSelector.SelectedItem as SyncBinding)?.id;
            var bindings = (status.bindings ?? new SyncBinding[0]).Where(b => PlatformSyncHandoff.SameDocument(b.document_path, b.project_unique_id, context.DocumentPath, context.ProjectUniqueId)).ToArray();
            BindingSelector.ItemsSource = bindings; BindingSelector.SelectedItem = bindings.FirstOrDefault(b => b.id == previous) ?? (bindings.Length == 1 ? bindings[0] : null);
            Runs.ItemsSource = status.runs ?? new SyncRun[0]; SetStatus(bindings.Length == 0 ? "当前项目尚无已确认的平台绑定，请先读取平台模型并确认绑定。" : "同步状态已刷新。", false);
        }
        async Task InspectAsync()
        {
            inspection = null; ModelSelector.ItemsSource = null; ModelSelector.SelectedItem = null;
            await CurrentAsync(false); inspection = await client.InspectAsync(CurrentSettings());
            if (string.IsNullOrWhiteSpace(inspection.project_id) || inspection.project_id != inspection.scene_project_id) { inspection = null; throw new InvalidOperationException("编辑项目与三维场景项目不一致，不能确认绑定。"); }
            ModelSelector.ItemsSource = inspection.models ?? new SyncRemoteModel[0]; ModelSelector.SelectedItem = null;
            ProjectText.Text = PlatformSyncClient.SafeText(inspection.project_name + " · " + inspection.project_code); SetStatus("已读取平台模型。请按完整名称选中要绑定的模型，再确认绑定。", false);
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
            var binding = BindingSelector.SelectedItem as SyncBinding;
            if (binding == null) throw new InvalidOperationException("请先确认当前项目的平台模型绑定。");
            var context = await CurrentAsync(true);
            if (!PlatformSyncHandoff.SameDocument(binding.document_path, binding.project_unique_id, context.DocumentPath, context.ProjectUniqueId))
                throw new InvalidOperationException("平台绑定与当前模型不一致，未创建计划。");
            var frozen = await FreezeAsync(context); var run = await client.PlanAsync(CurrentSettings(), binding.id, frozen);
            Runs.ItemsSource = new[] { run };
            SetStatus("同步预览：" + run.StateLabel + "。" + (run.reason ?? "") + "\n平台模型尚未替换；计算状态与法规审查以任务列分别显示。", run.state == "BLOCKED" || run.state == "FAILED" || run.state == "NEEDS_LOGIN");
        }
    }
}
