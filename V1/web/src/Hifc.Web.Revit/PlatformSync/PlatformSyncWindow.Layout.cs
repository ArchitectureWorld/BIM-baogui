using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Hifc.Revit.Management;

namespace Hifc.Web.Revit.PlatformSync
{
    public sealed partial class PlatformSyncWindow
    {
        readonly Expander settingsExpander = new Expander { Header = "平台设置与诊断", Margin = new Thickness(3, 12, 3, 0) };
        readonly TextBlock settingsHint = Note(""), sourceText = Note("等待读取当前 Revit 模型"), bindingText = Note("尚未确认对应模型"),
            actionHint = Note(""), platformState = Note("尚未读取平台"), diagnostics = Note("选择同步记录可查看任务编号及详细说明。");
        bool configurationValid;

        static TextBlock Note(string text) => new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap,
            Foreground = LiquidGlassTheme.Color("#617B99"), FontSize = 12
        };
        static TextBlock Label(string text) { var label = Note(text); label.Margin = new Thickness(0, 13, 0, 6); return label; }
        static void Value(TextBlock text)
        {
            text.TextWrapping = TextWrapping.Wrap; text.Foreground = LiquidGlassTheme.Color("#28496D");
            text.FontSize = 13; text.LineHeight = 21;
        }
        static Border InputSurface(TextBox box)
        {
            LiquidGlassTheme.SearchBox(box);
            return new Border { Child = box, Background = LiquidGlassTheme.Gradient("#FCFFFFFF", "#EDF5FD"),
                BorderBrush = LiquidGlassTheme.Color("#C6D7E9"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 0, 8, 0) };
        }
        void BuildLayout()
        {
            Title = "HIFC-V1-Web · 平台同步"; Width = 1120; Height = 860; MinWidth = 940; MinHeight = 680;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            LiquidGlassTheme.Apply(this);
            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            Content = new ScrollViewer { Content = root, Background = Background, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

            var header = new Grid { Margin = new Thickness(4, 2, 4, 18) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new Grid { Margin = new Thickness(0, 0, 16, 0) };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); title.ColumnDefinitions.Add(new ColumnDefinition());
            title.Children.Add(new Border { Width = 5, Height = 40, CornerRadius = new CornerRadius(3), Background = LiquidGlassTheme.Gradient("#63ADFF", "#2174DD"), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top });
            var titleStack = new StackPanel(); Grid.SetColumn(titleStack, 1); title.Children.Add(titleStack);
            var heading = LiquidGlassTheme.Title("平台同步"); heading.FontSize = 23; titleStack.Children.Add(heading);
            var subtitle = Note("确认当前 IFC 与平台模型的对应关系，再预览并开始同步。"); subtitle.Margin = new Thickness(0, 5, 0, 0); titleStack.Children.Add(subtitle); header.Children.Add(title);
            platformState.Foreground = LiquidGlassTheme.Color("#29609D"); platformState.MaxWidth = 220;
            var badge = new Border { Child = platformState, Background = LiquidGlassTheme.Gradient("#EEFFFFFF", "#D9EAF9FF"), BorderBrush = LiquidGlassTheme.Color("#FBFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(17, 10, 17, 10), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(badge, 1); header.Children.Add(badge); root.Children.Add(header);

            InspectButton.Content = "1  读取平台"; BindButton.Content = "2  确认对应模型";
            PlanButton.Content = "3  预览同步"; StartButton.Content = "4  开始同步";
            foreach (var button in new[] { LoginButton, InspectButton, BindButton, PlanButton, StartButton }) { button.Margin = new Thickness(0, 0, LiquidGlassTheme.ControlGap, 7); actions.Children.Add(button); }
            LiquidGlassTheme.Primary(StartButton); root.Children.Add(actions);
            actionHint.Margin = new Thickness(3, 1, 3, 14); root.Children.Add(actionHint);

            var workspace = new Grid(); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            var source = new StackPanel(); source.Children.Add(LiquidGlassTheme.Title("当前项目与 IFC"));
            source.Children.Add(Label("Revit 模型")); Value(sourceText); source.Children.Add(sourceText);
            source.Children.Add(Label("本次导出文件")); Value(ExportText); ExportText.Text = "请先通过本插件成功导出当前项目 IFC。"; source.Children.Add(ExportText);
            var sourceHint = Note("使用本次成功导出的完整 IFC。请在右侧确认它对应的平台模型。"); sourceHint.Margin = new Thickness(0, 14, 0, 0); source.Children.Add(sourceHint);
            workspace.Children.Add(LiquidGlassTheme.Card(source, new Thickness(0, 0, 7, 0)));

            var target = new StackPanel(); target.Children.Add(LiquidGlassTheme.Title("平台项目与对应模型"));
            Value(ProjectText); ProjectText.Text = "尚未读取平台项目"; ProjectText.Margin = new Thickness(0, 10, 0, 0); target.Children.Add(ProjectText);
            target.Children.Add(Label("选择平台中的模型")); LiquidGlassTheme.Combo(ModelSelector); ModelSelector.MinWidth = 0; ModelSelector.Margin = new Thickness(0); target.Children.Add(ModelSelector);
            target.Children.Add(Label("已确认的对应关系")); LiquidGlassTheme.Combo(BindingSelector); BindingSelector.Margin = new Thickness(0); target.Children.Add(BindingSelector);
            bindingText.Margin = new Thickness(0, 8, 0, 0); target.Children.Add(bindingText);
            var targetCard = LiquidGlassTheme.Card(target, new Thickness(7, 0, 0, 0)); Grid.SetColumn(targetCard, 1); workspace.Children.Add(targetCard); root.Children.Add(workspace);
            ModelSelector.SetBinding(ToolTipProperty, new Binding("SelectedItem.name") { Source = ModelSelector });
            BindingSelector.SetBinding(ToolTipProperty, new Binding("SelectedItem.remote_model_name") { Source = BindingSelector });

            var history = new StackPanel(); var historyHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            StatusButton.Content = "刷新同步状态"; StatusButton.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(StatusButton, Dock.Right); historyHeader.Children.Add(StatusButton);
            ReconcileButton.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(ReconcileButton, Dock.Right); historyHeader.Children.Add(ReconcileButton);
            historyHeader.Children.Add(LiquidGlassTheme.Title("同步状态")); history.Children.Add(historyHeader);
            LiquidGlassTheme.Table(Runs); Runs.Height = 132; Runs.MinHeight = 100; Runs.CanUserResizeRows = false;
            AddColumn("IFC 文件", "ModelLabel", 2.3); AddColumn("同步状态", "StateLabel", 1.05); AddColumn("计算状态", "CalculationLabel", 0.9); AddColumn("法规审查", "RegulationLabel", 0.9);
            AddColumn("综合报告", "ReportLabel", 0.9);
            history.Children.Add(Runs); StatusText.Text = "尚未创建同步任务。"; Value(StatusText); StatusText.Margin = new Thickness(0, 10, 0, 0); history.Children.Add(StatusText);
            var historyHint = Note("计算完成与法规审查通过分别显示。创建预览后，还需确认开始同步。"); historyHint.Margin = new Thickness(0, 6, 0, 0); history.Children.Add(historyHint);
            root.Children.Add(LiquidGlassTheme.Card(history, new Thickness(0, 14, 0, 0)));
            Runs.SelectionChanged += (s, e) => ShowRunDetails();

            var reports = new StackPanel(); var reportHeader = new DockPanel();
            var reportActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            ReportButton.Margin = new Thickness(0, 0, 8, 0); reportActions.Children.Add(ReportButton); reportActions.Children.Add(OpenReportFolderButton);
            DockPanel.SetDock(reportActions, Dock.Right); reportHeader.Children.Add(reportActions); reportHeader.Children.Add(LiquidGlassTheme.Title("综合报告")); reports.Children.Add(reportHeader);
            Value(ReportText); ReportText.Margin = new Thickness(0, 10, 0, 0); reports.Children.Add(ReportText);
            var reportHint = Note("本次计算完成后自动保存 PDF 和同名 TXT。TXT 记录实际参与审查的模型；同日版本递增，次日从 V1 开始。"); reportHint.Margin = new Thickness(0, 6, 0, 0); reports.Children.Add(reportHint);
            root.Children.Add(LiquidGlassTheme.Card(reports, new Thickness(0, 14, 0, 0))); UpdateReportActions();

            var settings = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            settingsHint.Margin = new Thickness(0, 0, 0, 8); settings.Children.Add(settingsHint);
            settings.Children.Add(PathRow("Node 程序", NodePathBox, "node.exe|node.exe")); settings.Children.Add(PathRow("同步程序", WorkerPathBox, "cli.js|cli.js")); settings.Children.Add(PathRow("平台配置", ConfigPathBox, "JSON 配置|*.json"));
            SaveSettingsButton.HorizontalAlignment = HorizontalAlignment.Left; SaveSettingsButton.Margin = new Thickness(90, 7, 0, 12); settings.Children.Add(SaveSettingsButton);
            settings.Children.Add(Note("访问令牌在专用登录配置中保存，不填写在本窗口。报告下载与模型同步分别记录状态。"));
            var detailsHeading = LiquidGlassTheme.Title("任务详情"); detailsHeading.Margin = new Thickness(0, 14, 0, 7); settings.Children.Add(detailsHeading); settings.Children.Add(diagnostics);
            settingsExpander.Content = LiquidGlassTheme.Card(settings, new Thickness(0, 5, 0, 4)); root.Children.Add(settingsExpander);
            StartButton.IsEnabled = false;
        }
        void AddColumn(string title, string path, double width)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 0, 6, 0)));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(path)));
            if (path == "StateLabel") {
                foreach (var state in new[] { "SUCCEEDED", "NO_CHANGE", "ROLLED_BACK", "FAILED", "BLOCKED", "UNKNOWN_RESULT", "NEEDS_LOGIN", "PAUSED", "UPLOADING", "SUBMITTING", "CONVERTING", "CALCULATING", "VERIFYING" }) {
                    var trigger = new DataTrigger { Binding = new Binding("state"), Value = state };
                    var color = new[] { "SUCCEEDED", "NO_CHANGE", "ROLLED_BACK" }.Contains(state) ? "#23765B" : new[] { "FAILED", "BLOCKED", "UNKNOWN_RESULT", "NEEDS_LOGIN", "PAUSED" }.Contains(state) ? "#B83138" : "#1768C9";
                    trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, LiquidGlassTheme.Color(color))); style.Triggers.Add(trigger);
                }
            }
            Runs.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), ElementStyle = style, Width = new DataGridLength(width, DataGridLengthUnitType.Star) });
        }
        void ShowRunDetails()
        {
            var run = Runs.SelectedItem as SyncRun;
            diagnostics.Text = run == null ? "选择同步记录可查看任务编号及详细说明。" : PlatformSyncClient.SafeText("任务编号：" + run.id + "\n" + run.StateLabel + " · " + run.CalculationLabel + " · " + run.RegulationLabel + (string.IsNullOrWhiteSpace(run.reason) ? "" : "\n说明：" + run.reason));
            UpdateReportActions();
        }
        void UpdateReportActions()
        {
            var run = Runs.SelectedItem as SyncRun;
            ReportButton.IsEnabled = !busy && run?.CanDownloadReport == true;
            ReconcileButton.IsEnabled = !busy && run != null && !new[] { "SUCCEEDED", "NO_CHANGE", "ROLLED_BACK" }.Contains(run.state);
            OpenReportFolderButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(run?.report?.pdf_path) && System.IO.Path.IsPathRooted(run.report.pdf_path) && System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(run.report.pdf_path));
            var destination = !string.IsNullOrWhiteSpace(run?.report?.pdf_path) ? run.report.pdf_path : !string.IsNullOrWhiteSpace(run?.handoff?.document_path) ? System.IO.Path.GetDirectoryName(run.handoff.document_path) : null;
            ReportText.Text = run == null ? "选择同步记录后可查看综合报告状态。" : PlatformSyncClient.SafeText(run.ReportLabel + (string.IsNullOrWhiteSpace(run.report?.reason) ? "" : " · " + run.report.reason) + (string.IsNullOrWhiteSpace(destination) ? "" : "\n保存位置：" + destination));
            ReportText.Foreground = LiquidGlassTheme.Color(new[] { "FAILED", "BLOCKED", "NEEDS_LOGIN" }.Contains(run?.report?.state) ? "#B83138" : "#28496D");
        }
        void SetRuns(SyncRun[] runs) { var selected = (Runs.SelectedItem as SyncRun)?.id; Runs.ItemsSource = runs ?? new SyncRun[0]; Runs.SelectedItem = Runs.Items.Cast<SyncRun>().FirstOrDefault(r => r.id == selected) ?? Runs.Items.Cast<SyncRun>().FirstOrDefault(); ShowRunDetails(); }
        void LoadConfigurationState()
        {
            try { CurrentSettings().Validate(); configurationValid = true; }
            catch (Exception) { configurationValid = false; }
            settingsExpander.IsExpanded = !configurationValid;
            settingsHint.Text = configurationValid ? "平台设置已就绪，日常同步无需重复配置。" : "平台配置未完成或文件缺失，请选择下方三个文件并保存。";
            settingsHint.Foreground = LiquidGlassTheme.Color(configurationValid ? "#617B99" : "#B83138");
        }
        void ShowProject(SyncInspection value)
        {
            inspection = value;
            ProjectText.Text = PlatformSyncClient.SafeText(value.project_name + "\n项目编号：" + value.project_code);
            platformState.Text = value.writable == false ? (value.approval_state == 1 ? "审核中 · 无法替换模型" : "当前不可编辑 · 无法替换模型") : value.writable == true ? "平台项目可编辑" : "平台可编辑状态待核实";
            platformState.Foreground = LiquidGlassTheme.Color(value.writable == false ? "#B83138" : "#29609D");
        }
        void ShowBinding()
        {
            var binding = BindingSelector.SelectedItem as SyncBinding;
            bindingText.Text = binding == null ? "尚未确认对应关系。选中上方模型后，点击“2 确认对应模型”。" : PlatformSyncClient.SafeText("将替换：" + binding.remote_model_name);
            if (binding != null && inspection == null) ProjectText.Text = PlatformSyncClient.SafeText(binding.project_name + "\n项目编号：" + binding.project_code + " · 待读取核对");
        }
        void UpdateActionHint()
        {
            if (inspection?.writable == false) actionHint.Text = platformState.Text + "。当前项目不可编辑，可保留 IFC 与预览计划。";
            else if (!configurationValid) actionHint.Text = "平台配置未完成。请在底部“平台设置与诊断”中补齐设置。";
            else if (plannedRun != null) actionHint.Text = "预览计划已保存。核对项目和模型后，可点击“4 开始同步”。";
            else if (BindingSelector.SelectedItem != null) actionHint.Text = "已确认对应关系，可点击“3 预览同步”核对本次 IFC。";
            else actionHint.Text = "从“1 读取平台”开始，选中对应模型后点击“2 确认对应模型”。";
            actionHint.Foreground = LiquidGlassTheme.Color(inspection?.writable == false ? "#B83138" : "#617B99");
            StartButton.ToolTip = inspection?.writable == false ? "当前项目不可编辑，不能替换模型。" : plannedRun == null ? "请先创建并核对本次 IFC 的同步预览。" : "确认后备份旧 IFC，并开始替换所选模型。";
            ToolTipService.SetShowOnDisabled(StartButton, true);
        }
    }
}
