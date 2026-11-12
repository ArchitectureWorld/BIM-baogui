using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    // The host performs all Revit and workbook operations; this window only displays snapshots.
    internal sealed class SiteStatisticsWindow : Window
    {
        sealed class Display : IValueConverter, IMultiValueConverter
        {
            readonly string format, missing;
            internal Display(bool signed = false, string missing = "待补") { format = signed ? "+0.00;-0.00;0.00" : "0.00"; this.missing = missing; }
            public object Convert(object value, Type type, object parameter, CultureInfo culture)
            {
                if (value == null || value == DependencyProperty.UnsetValue) return missing;
                if (value is decimal number) return number.ToString(format, CultureInfo.InvariantCulture);
                return string.IsNullOrWhiteSpace(value.ToString()) ? missing : value.ToString();
            }
            public object Convert(object[] values, Type type, object parameter, CultureInfo culture)
            {
                if (values.Length > 1 && values[1] is bool applicable && !applicable) return "—";
                return Convert(values.Length > 0 ? values[0] : null, type, parameter, culture);
            }
            public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
            public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
        }

        readonly TextBlock summary = new TextBlock { Margin = new Thickness(0, 5, 0, 5), TextWrapping = TextWrapping.Wrap };
        readonly TextBlock source = new TextBlock { Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBox reportNotes = DetailBox();
        SiteStatisticsSource[] sources = new SiteStatisticsSource[0];
        DataGrid selectedGrid;
        bool loading, busy, stale;
        int changes, refreshChanges;

        internal WrapPanel SourcesPanel { get; } = new WrapPanel();
        internal Button RefreshButton { get; } = new Button { Content = "刷新统计" };
        internal Button LocateButton { get; } = new Button { Content = "定位所选构件" };
        internal Button ExportButton { get; } = new Button { Content = "导出 Excel" };
        internal DataGrid MetricGrid { get; } = NewGrid(true);
        internal DataGrid RowGrid { get; } = NewGrid();
        internal DataGrid ComponentGrid { get; } = NewGrid();
        internal DataGrid PendingGrid { get; } = NewGrid();
        internal TabControl DetailTabs { get; } = new TabControl();
        internal TextBox SelectionDetails { get; } = DetailBox();
        internal TextBlock StatusText { get; } = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        internal SiteStatisticsReport CurrentReport { get; private set; }
        internal string[] SelectedSourceKeys => sources.Where(s => s.Available && (s.IsHost || s.Selected)).Select(s => s.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        string HostKey => sources.FirstOrDefault(s => s.IsHost)?.Key;
        public event Action<string[]> RefreshRequested;
        public event Action<SiteStatisticsSelection[]> LocateRequested;
        public event Action ExportRequested;

        internal SiteStatisticsWindow()
        {
            Title = "总图统计"; Width = 1200; Height = 840; MinWidth = 980; MinHeight = 660; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            LiquidGlassTheme.Apply(this); LiquidGlassTheme.Primary(RefreshButton);
            var root = new Grid { Margin = new Thickness(18), Background = Background }; Content = root;
            for (int i = 0; i < 8; i++) root.RowDefinitions.Add(new RowDefinition { Height = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var toolbar = new WrapPanel();
            toolbar.Children.Add(LiquidGlassTheme.Title("总图统计"));
            foreach (var button in new[] { RefreshButton, LocateButton, ExportButton }) { button.Margin = new Thickness(12, 0, 0, 0); toolbar.Children.Add(button); }
            root.Children.Add(toolbar);
            var sourceArea = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            sourceArea.Children.Add(new TextBlock { Text = "统计来源：当前总图必选；可追加已加载链接，调整后点击刷新。", Margin = new Thickness(0, 0, 0, 5) });
            sourceArea.Children.Add(new ScrollViewer { Content = SourcesPanel, MaxHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            Grid.SetRow(sourceArea, 1); root.Children.Add(sourceArea); Grid.SetRow(summary, 2); root.Children.Add(summary); Grid.SetRow(source, 3); root.Children.Add(source);

            TextColumn(MetricGrid, "指标", "Name", 180, true); NumberColumn(MetricGrid, "填报口径", "DeclaredValue", 110); NumberColumn(MetricGrid, "几何口径", "GeometricValue", 110, applicable: "GeometricApplicable");
            TextColumn(MetricGrid, "单位", "Unit", 55); TextColumn(MetricGrid, "计算口径", "Formula", 420, true); TextColumn(MetricGrid, "备注", "Notes", 150);
            AddRowColumns(RowGrid); AddComponentColumns(ComponentGrid); AddComponentColumns(PendingGrid);
            MetricGrid.MaxHeight = 214; MetricGrid.MinHeight = 110; MetricGrid.Margin = new Thickness(0, 0, 0, 8);
            var overview = new DockPanel(); DockPanel.SetDock(MetricGrid, Dock.Top); overview.Children.Add(MetricGrid); overview.Children.Add(RowGrid);
            DetailTabs.Items.Add(new TabItem { Header = "指标与分类小计", Content = overview });
            DetailTabs.Items.Add(new TabItem { Header = "构件明细", Content = ComponentGrid });
            DetailTabs.Items.Add(new TabItem { Header = "待完善项目", Content = PendingGrid });
            Grid.SetRow(DetailTabs, 4); root.Children.Add(DetailTabs);
            var details = new StackPanel(); details.Children.Add(reportNotes); details.Children.Add(SelectionDetails);
            var expander = new Expander { Header = "范围、提示与选中行完整明细", Content = details, Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(expander, 5); root.Children.Add(expander); Grid.SetRow(StatusText, 6); root.Children.Add(StatusText);
            var hint = new TextBlock { Text = "面积 m²；停车数量按辆计；比例按 % 显示。总用地与净用地分别列示，不相加。红色为超出报告阈值，缺项保留已知小计。", TextWrapping = TextWrapping.Wrap, Foreground = LiquidGlassTheme.Color("#60758C"), Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(hint, 7); root.Children.Add(hint);
            RefreshButton.Click += (s, e) => RequestRefresh(); LocateButton.Click += (s, e) => RequestLocate(); ExportButton.Click += (s, e) => RequestExport();
            foreach (var grid in new[] { RowGrid, ComponentGrid, PendingGrid }) grid.SelectionChanged += (s, e) => {
                if (loading) return; selectedGrid = grid; DescribeSelection(grid); UpdateActions();
            };
            MetricGrid.SelectionChanged += (s, e) => { if (!loading) { selectedGrid = null; DescribeSelection(MetricGrid); UpdateActions(); } };
            DetailTabs.SelectionChanged += (s, e) => {
                if (e.Source != DetailTabs) return;
                selectedGrid = DetailTabs.SelectedIndex == 0 ? RowGrid : DetailTabs.SelectedIndex == 1 ? ComponentGrid : PendingGrid;
                DescribeSelection(selectedGrid); UpdateActions();
            };
            SetStatus("请选择来源并刷新统计。"); UpdateActions();
        }

        static TextBox DetailBox() => new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Padding = new Thickness(7), MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        static DataGrid NewGrid(bool metrics = false)
        {
            var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserReorderColumns = false,
                CanUserResizeColumns = false, CanUserSortColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, FrozenColumnCount = 1,
                SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow, RowHeight = double.NaN, MinRowHeight = 36, ColumnHeaderHeight = 48,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderThickness = new Thickness(0), Background = Brushes.White, AlternatingRowBackground = LiquidGlassTheme.Color("#F6F9FD"),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var headerText = new FrameworkElementFactory(typeof(TextBlock)); headerText.SetBinding(TextBlock.TextProperty, new Binding());
            headerText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); headerText.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, new DataTemplate { VisualTree = headerText })); headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(3)));
            grid.ColumnHeaderStyle = headerStyle;
            var style = new Style(typeof(DataGridRow));
            if (metrics) {
                var incomplete = new DataTrigger { Binding = new Binding("Complete"), Value = false };
                incomplete.Setters.Add(new Setter(Control.ForegroundProperty, LiquidGlassTheme.Color("#986015"))); style.Triggers.Add(incomplete); grid.RowStyle = style; return grid;
            }
            var subtotal = new DataTrigger { Binding = new Binding("IsSubtotal"), Value = true };
            subtotal.Setters.Add(new Setter(Control.BackgroundProperty, LiquidGlassTheme.Color("#E3EEF9"))); subtotal.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); style.Triggers.Add(subtotal);
            var warning = new DataTrigger { Binding = new Binding("ExceedsTolerance"), Value = true };
            warning.Setters.Add(new Setter(Control.ForegroundProperty, LiquidGlassTheme.Color("#AE2B29"))); warning.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); style.Triggers.Add(warning);
            grid.RowStyle = style; return grid;
        }
        static void TextColumn(DataGrid grid, string title, string path, double width, bool wrap = false)
        {
            var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(5, 6, 5, 6)));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center)); style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, wrap ? TextWrapping.Wrap : TextWrapping.NoWrap));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis)); style.Setters.Add(new Setter(ToolTipService.ToolTipProperty, new Binding(path)));
            grid.Columns.Add(new DataGridTextColumn { Header = title, Width = width, ElementStyle = style, Binding = new Binding(path) { Converter = new Display(missing: path == "Notes" || path == "Formula" ? "—" : "待补") } });
        }
        static void NumberColumn(DataGrid grid, string title, string path, double width = 84, bool signed = false, string applicable = null)
        {
            TextColumn(grid, title, path, width); var converter = new Display(signed);
            BindingBase binding = new Binding(path) { Converter = converter };
            if (applicable != null) { var multiple = new MultiBinding { Converter = converter }; multiple.Bindings.Add(new Binding(path)); multiple.Bindings.Add(new Binding(applicable)); binding = multiple; }
            ((DataGridTextColumn)grid.Columns[grid.Columns.Count - 1]).Binding = binding;
        }
        static void AddRowColumns(DataGrid grid)
        {
            TextColumn(grid, "分类及小计", "Name", 150, true); TextColumn(grid, "来源", "SourceNames", 86);
            NumberColumn(grid, "填报投影\nm²", "DeclaredArea", applicable: "DeclaredAreaApplicable"); NumberColumn(grid, "实际几何\nm²", "GeometricArea");
            NumberColumn(grid, "填报折算\nm²", "DeclaredConvertedArea", applicable: "ConvertedAreaApplicable"); NumberColumn(grid, "几何折算\nm²", "GeometricConvertedArea", applicable: "ConvertedAreaApplicable");
            NumberColumn(grid, "机动车\n辆", "MotorCount", 58, applicable: "CountApplicable"); NumberColumn(grid, "非机动车\n辆", "NonMotorCount", 68, applicable: "CountApplicable");
            NumberColumn(grid, "差额\nm²", "Difference", signed: true, applicable: "DeclaredAreaApplicable"); NumberColumn(grid, "差异\n%", "DifferencePercent", 68, true, "DeclaredAreaApplicable");
            TextColumn(grid, "备注", "Notes", 130);
        }
        static void AddComponentColumns(DataGrid grid)
        {
            AddRowColumns(grid); TextColumn(grid, "元素 ID", "ElementId", 78); TextColumn(grid, "分类", "Slot", 110);
            NumberColumn(grid, "折算系数", "Factor", 80, applicable: "ConvertedAreaApplicable"); TextColumn(grid, "状态", "Status", 130);
        }

        public void SetSources(SiteStatisticsSource[] values)
        {
            var oldHost = HostKey; var oldKeys = SelectedSourceKeys;
            loading = true;
            try {
                sources = (values ?? new SiteStatisticsSource[0]).Where(s => s != null && !string.IsNullOrWhiteSpace(s.Key)).GroupBy(s => s.Key, StringComparer.Ordinal).Select(g => g.First())
                    .Select(s => new SiteStatisticsSource { Key = s.Key, Name = s.Name, Path = s.Path, IsHost = s.IsHost, Selected = s.IsHost || (s.Available && s.Selected), Available = s.Available, Notes = s.Notes }).ToArray();
                SourcesPanel.Children.Clear();
                foreach (var choice in sources) {
                    var box = new CheckBox { Tag = choice, Content = (choice.Name ?? choice.Key) + (choice.IsHost ? "（当前，必选）" : choice.Available ? "" : "（未加载）"),
                        IsChecked = choice.Selected, Margin = new Thickness(0, 0, 18, 5), ToolTip = (choice.Path ?? "") + "\n" + (choice.Notes ?? "") };
                    box.Checked += SourceChanged; box.Unchecked += SourceChanged; SourcesPanel.Children.Add(box);
                }
            } finally { loading = false; }
            if (sources.Count(s => s.IsHost) != 1) { ClearReport(); SetStatus("未取得唯一的当前总图来源，请重新打开统计。"); }
            else if (!string.Equals(oldHost, HostKey, StringComparison.Ordinal) || !oldKeys.SequenceEqual(SelectedSourceKeys)) { ClearReport(); busy = false; }
            UpdateActions();
        }
        void SourceChanged(object sender, RoutedEventArgs e)
        {
            if (loading) return; var box = (CheckBox)sender; var choice = (SiteStatisticsSource)box.Tag;
            if (busy || choice.IsHost || !choice.Available) {
                loading = true; try { box.IsChecked = choice.IsHost || (choice.Available && choice.Selected); } finally { loading = false; } return;
            }
            choice.Selected = box.IsChecked == true; ClearReport(); SetStatus("统计来源已调整，请刷新。可继续选择其它链接后一次刷新。"); UpdateActions();
        }
        bool ReportMatches(SiteStatisticsReport report)
        {
            if (report == null || sources.Count(s => s.IsHost) != 1 || !string.Equals(report.DocumentKey, HostKey, StringComparison.Ordinal)) return false;
            var keys = (report.Sources ?? new SiteStatisticsSource[0]).Where(s => s.Available && (s.IsHost || s.Selected)).Select(s => s.Key).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            return keys.SequenceEqual(SelectedSourceKeys);
        }
        public void ShowReport(SiteStatisticsReport report)
        {
            if (!ReportMatches(report)) return;
            loading = true;
            try {
                CurrentReport = report; busy = false; stale = changes != refreshChanges; selectedGrid = null;
                MetricGrid.ItemsSource = report.Metrics ?? new SiteStatisticsMetric[0]; RowGrid.ItemsSource = report.Rows ?? new SiteStatisticsRow[0];
                ComponentGrid.ItemsSource = report.Components ?? new SiteStatisticsComponent[0]; PendingGrid.ItemsSource = report.Pending ?? new SiteStatisticsComponent[0];
                ((TabItem)DetailTabs.Items[2]).Header = "待完善项目（" + PendingGrid.Items.Count + "）";
                summary.Text = (report.DocumentTitle ?? "总图") + " · " + (report.Complete ? "统计完成" : "范围或资料待完善，显示已知小计") + " · " + SelectedSourceKeys.Length + " 个来源 · 差异提示阈值 " + report.TolerancePercent.ToString("0.00", CultureInfo.InvariantCulture) + "%";
                summary.Foreground = LiquidGlassTheme.Color(report.Complete ? "#234B74" : "#986015");
                DateTime captured; var time = DateTime.TryParse(report.CapturedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out captured) ? captured.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : report.CapturedUtc ?? "待补";
                source.Text = "来源：" + (report.SourcePath ?? "未保存模型") + "    更新：" + time; source.ToolTip = source.Text;
                reportNotes.Text = "所选来源：" + string.Join("、", sources.Where(s => s.IsHost || s.Selected).Select(s => s.Name ?? s.Key)) + "\n" + string.Join("\n", report.Notes ?? new string[0]);
                SelectionDetails.Text = "选择分类、构件或指标后，可在此查看完整来源、计算口径和备注。";
            } finally { loading = false; }
            SetStatus(stale ? "读取期间模型已变化，请刷新后再导出。" : report.Complete ? "统计已更新；可选择分类或构件定位。" : "统计已更新，存在缺项或范围提示；可导出保留说明的已知小计。"); UpdateActions();
        }
        void ClearReport()
        {
            CurrentReport = null; selectedGrid = null; stale = false; changes = refreshChanges = 0;
            loading = true; try { foreach (var grid in new[] { MetricGrid, RowGrid, ComponentGrid, PendingGrid }) grid.ItemsSource = null; } finally { loading = false; }
            summary.Text = "当前来源尚未统计"; source.Text = ""; source.ToolTip = null; reportNotes.Text = ""; SelectionDetails.Text = ""; ((TabItem)DetailTabs.Items[2]).Header = "待完善项目";
        }
        public void SetBusy(bool value, string message) { busy = value; if (message != null) SetStatus(message); UpdateActions(); }
        public void MarkStale(string message) { changes++; stale = true; SetStatus(string.IsNullOrWhiteSpace(message) ? "模型已变化，请刷新统计后再导出。" : message); UpdateActions(); }
        public void SetStatus(string message) { StatusText.Text = message ?? ""; }
        void UpdateActions()
        {
            foreach (var box in SourcesPanel.Children.OfType<CheckBox>()) { var choice = (SiteStatisticsSource)box.Tag; box.IsEnabled = !busy && choice.Available && !choice.IsHost; }
            RefreshButton.IsEnabled = !busy && sources.Count(s => s.IsHost) == 1 && sources.Any(s => s.IsHost && s.Available);
            ExportButton.IsEnabled = !busy && !stale && ReportMatches(CurrentReport); ExportButton.ToolTip = stale ? "模型已变化，请先刷新。" : "保留范围和缺项说明导出。";
            LocateButton.IsEnabled = !busy && ReportMatches(CurrentReport) && Selections().Length > 0;
        }
        void RequestRefresh()
        {
            if (!RefreshButton.IsEnabled || RefreshRequested == null) return;
            refreshChanges = changes; var keys = SelectedSourceKeys; SetBusy(true, "正在读取所选总图及链接来源……"); RefreshRequested(keys);
        }
        SiteStatisticsSelection[] Selections()
        {
            if (CurrentReport == null || selectedGrid == null) return new SiteStatisticsSelection[0];
            var selected = selectedGrid.SelectedItems.Cast<SiteStatisticsRow>().SelectMany(row => row is SiteStatisticsComponent component
                ? new[] { new SiteStatisticsSelection { SourceKey = component.SourceKey, ElementIds = new[] { component.ElementId } } } : row.Selection ?? new SiteStatisticsSelection[0]);
            var allowed = SelectedSourceKeys;
            return selected.Where(s => s != null && allowed.Contains(s.SourceKey, StringComparer.Ordinal)).GroupBy(s => s.SourceKey, StringComparer.Ordinal)
                .Select(g => new SiteStatisticsSelection { SourceKey = g.Key, ElementIds = g.SelectMany(s => s.ElementIds ?? new int[0]).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray() })
                .Where(s => s.ElementIds.Length > 0).OrderBy(s => s.SourceKey, StringComparer.Ordinal).ToArray();
        }
        void RequestLocate()
        {
            if (busy || !ReportMatches(CurrentReport) || LocateRequested == null) return;
            var selection = Selections(); if (selection.Length == 0) return; SetBusy(true, "正在定位所选来源中的构件……"); LocateRequested(selection);
        }
        void RequestExport()
        {
            if (busy || stale || !ReportMatches(CurrentReport) || ExportRequested == null) return; SetBusy(true, "正在导出总图统计……"); ExportRequested();
        }
        void DescribeSelection(DataGrid grid)
        {
            SelectionDetails.Text = grid == null ? "" : string.Join("\n\n", grid.SelectedItems.Cast<object>().Select(item => {
                if (item is SiteStatisticsMetric metric) return metric.Name + "（" + metric.Unit + "）\n计算口径：" + metric.Formula + "\n备注：" + (string.IsNullOrWhiteSpace(metric.Notes) ? "—" : metric.Notes);
                if (item is SiteStatisticsRow row) return row.Name + "\n分类：" + row.Kind + " · " + row.Subtype + "\n来源：" + row.SourceNames + (row is SiteStatisticsComponent component ? "\n源键：" + component.SourceKey + " · 元素 " + component.ElementId + " · 状态：" + component.Status : "") + "\n备注：" + (string.IsNullOrWhiteSpace(row.Notes) ? "—" : row.Notes);
                return "";
            }));
        }
    }
}
