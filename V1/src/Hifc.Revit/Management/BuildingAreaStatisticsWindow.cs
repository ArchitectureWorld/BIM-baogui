using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    // Deliberately has no Revit dependency: the host owns document access and export.
    internal sealed class BuildingAreaStatisticsWindow : Window
    {
        sealed class HeaderValue : IValueConverter
        {
            public object Convert(object value, Type type, object parameter, CultureInfo culture) => (value?.ToString() ?? "").Replace(" m²", "\nm²").Replace(" m", "\nm").Replace(" %", "\n%");
            public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
        }
        sealed class DisplayValue : IValueConverter, IMultiValueConverter
        {
            readonly string format, missing;
            internal DisplayValue(string format, string missing = "待补") { this.format = format; this.missing = missing; }
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value == null || value == DependencyProperty.UnsetValue) return missing;
                if (value is decimal number) return number.ToString(format, CultureInfo.InvariantCulture);
                var text = value.ToString();
                return string.IsNullOrWhiteSpace(text) ? missing : text;
            }
            public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
            public object Convert(object[] values, Type type, object parameter, CultureInfo culture)
            {
                return values.Length > 1 && values[1] is bool attributeOnly && attributeOnly ? "—" : Convert(values.Length > 0 ? values[0] : null, type, parameter, culture);
            }
            public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
        }

        readonly TextBlock summary = new TextBlock { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        readonly TextBlock source = new TextBlock { Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBox descriptions = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Padding = new Thickness(8), MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        bool loading, busy, stale;
        int changes, refreshChanges;
        DataGrid selectedGrid;

        internal ComboBox DocumentSelector { get; } = new ComboBox { Width = 300, DisplayMemberPath = "Name", SelectedValuePath = "Key", IsEditable = false };
        internal Button RefreshButton { get; } = new Button { Content = "刷新统计" };
        internal Button LocateButton { get; } = new Button { Content = "定位所选构件" };
        internal Button ExportButton { get; } = new Button { Content = "导出 Excel" };
        internal DataGrid FloorGrid { get; } = NewGrid();
        internal DataGrid TotalGrid { get; } = NewGrid();
        internal DataGrid ComponentGrid { get; } = NewGrid();
        internal DataGrid PendingGrid { get; } = NewGrid();
        internal DataGrid ComparisonGrid { get; } = NewGrid();
        internal TabControl DetailTabs { get; } = new TabControl();
        internal TextBlock StatusText { get; } = new TextBlock { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
        internal TextBox SelectionDetails { get; } = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Padding = new Thickness(8), MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        internal BuildingAreaStatisticsReport CurrentReport { get; private set; }
        // SelectedValue can still hold the previous key inside WPF SelectionChanged.
        internal string SelectedDocumentKey => (DocumentSelector.SelectedItem as BuildingAreaDocumentOption)?.Key;

        public event Action<string> RefreshRequested;
        public event Action<string, int[]> LocateRequested;
        public event Action<string> ExportRequested;

        internal BuildingAreaStatisticsWindow()
        {
            Title = "单体面积统计"; Width = 1200; Height = 820; MinWidth = 980; MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // Supply the selection template explicitly for the shared custom ComboBox template.
            var choiceText = new FrameworkElementFactory(typeof(TextBlock)); choiceText.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            DocumentSelector.DisplayMemberPath = ""; DocumentSelector.ItemTemplate = new DataTemplate { VisualTree = choiceText };
            LiquidGlassTheme.Apply(this); LiquidGlassTheme.Combo(DocumentSelector); LiquidGlassTheme.Primary(RefreshButton);
            var root = new Grid { Margin = new Thickness(18), Background = Background }; Content = root;
            for (int i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            toolbar.Children.Add(new TextBlock { Text = "单体模型", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            toolbar.Children.Add(DocumentSelector);
            foreach (var button in new[] { RefreshButton, LocateButton, ExportButton }) { button.Margin = new Thickness(0, 0, 8, 0); toolbar.Children.Add(button); }
            root.Children.Add(toolbar);
            Grid.SetRow(summary, 1); root.Children.Add(summary); Grid.SetRow(source, 2); root.Children.Add(source);

            AddFloorColumns(FloorGrid, true); AddFloorColumns(TotalGrid, false);
            FloorGrid.RowHeight = double.NaN; FloorGrid.MinRowHeight = 38;
            TotalGrid.HeadersVisibility = DataGridHeadersVisibility.None; TotalGrid.Height = 65;
            TotalGrid.FontWeight = FontWeights.SemiBold; TotalGrid.Background = LiquidGlassTheme.Color("#E3EEF9");
            TotalGrid.RowBackground = LiquidGlassTheme.Color("#E3EEF9"); TotalGrid.BorderBrush = LiquidGlassTheme.Color("#78A6D8");
            TotalGrid.BorderThickness = new Thickness(0, 2, 0, 0); TotalGrid.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            TotalGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            // Same fixed widths and synchronized offsets keep the pinned total under its column.
            FloorGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => SyncScroll(TotalGrid, e)));
            TotalGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => SyncScroll(FloorGrid, e)));
            var floors = new DockPanel(); DockPanel.SetDock(TotalGrid, Dock.Bottom); floors.Children.Add(TotalGrid); floors.Children.Add(FloorGrid);
            AddComponentColumns(ComponentGrid); AddComponentColumns(PendingGrid);
            TextColumn(ComparisonGrid, "项目属性", "Name", 200);
            NumberColumn(ComparisonGrid, "属性填写值 m²", "AttributeValue"); NumberColumn(ComparisonGrid, "统计值 m²", "CalculatedValue", attributeOnlyAware: true);
            NumberColumn(ComparisonGrid, "差值 m²", "Difference", true, attributeOnlyAware: true); NumberColumn(ComparisonGrid, "差异 %", "DifferencePercent", true, true, true);
            TextColumn(ComparisonGrid, "说明", "Notes", 450);
            DetailTabs.Items.Add(new TabItem { Header = "楼层与功能面积", Content = floors });
            DetailTabs.Items.Add(new TabItem { Header = "构件明细", Content = ComponentGrid });
            DetailTabs.Items.Add(new TabItem { Header = "待完善项目", Content = PendingGrid });
            DetailTabs.Items.Add(new TabItem { Header = "项目属性对照", Content = ComparisonGrid });
            Grid.SetRow(DetailTabs, 3); root.Children.Add(DetailTabs);
            var fullDetails = new StackPanel(); fullDetails.Children.Add(descriptions); fullDetails.Children.Add(SelectionDetails);
            var rules = new Expander { Header = "统计范围、计算口径与选中行完整备注", Content = fullDetails, Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(rules, 4); root.Children.Add(rules); Grid.SetRow(StatusText, 5); root.Children.Add(StatusText);
            var hint = new TextBlock { Text = "面积单位 m²；红色表示超出本报告容差。可多选楼层、构件或待完善项目后定位；数据有缺项时仍可导出并保留说明。", Foreground = LiquidGlassTheme.Color("#60758C"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(hint, 6); root.Children.Add(hint);

            DocumentSelector.SelectionChanged += (s, e) => { if (!loading) { ClearReport(); busy = false; RequestRefresh(); } };
            RefreshButton.Click += (s, e) => RequestRefresh(); LocateButton.Click += (s, e) => RequestLocate(); ExportButton.Click += (s, e) => RequestExport();
            foreach (var grid in new[] { FloorGrid, TotalGrid, ComponentGrid, PendingGrid })
                grid.SelectionChanged += (s, e) => { if (!loading && grid.SelectedItems.Count > 0) { selectedGrid = grid; DescribeSelection(); UpdateActions(); } };
            ComparisonGrid.SelectionChanged += (s, e) => { if (!loading) DescribeSelection(ComparisonGrid); };
            DetailTabs.SelectionChanged += (s, e) => {
                if (e.Source != DetailTabs) return;
                selectedGrid = DetailTabs.SelectedIndex == 0 ? (TotalGrid.SelectedItems.Count > 0 ? TotalGrid : FloorGrid)
                    : DetailTabs.SelectedIndex == 1 ? ComponentGrid : DetailTabs.SelectedIndex == 2 ? PendingGrid : null;
                UpdateActions();
            };
            SetStatus("请选择单体模型并刷新统计。"); UpdateActions();
        }

        static DataGrid NewGrid()
        {
            var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
                CanUserReorderColumns = false, CanUserResizeColumns = false, CanUserSortColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow, RowHeight = 38, ColumnHeaderHeight = 44, FrozenColumnCount = 1,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderThickness = new Thickness(0),
                Background = Brushes.White, AlternatingRowBackground = LiquidGlassTheme.Color("#F6F9FD"),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var headerText = new FrameworkElementFactory(typeof(TextBlock));
            headerText.SetBinding(TextBlock.TextProperty, new Binding { Converter = new HeaderValue() });
            headerText.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            headerText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, new DataTemplate { VisualTree = headerText }));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
            grid.ColumnHeaderStyle = headerStyle;
            var rowStyle = new Style(typeof(DataGridRow));
            var warning = new DataTrigger { Binding = new Binding("ExceedsTolerance"), Value = true };
            warning.Setters.Add(new Setter(Control.ForegroundProperty, LiquidGlassTheme.Color("#AE2B29")));
            warning.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            rowStyle.Triggers.Add(warning); grid.RowStyle = rowStyle;
            return grid;
        }
        static void TextColumn(DataGrid grid, string title, string property, double width, bool wrap = false)
        {
            var textStyle = new Style(typeof(TextBlock)); textStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            textStyle.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(7, wrap ? 7 : 0, 7, wrap ? 7 : 0)));
            textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis));
            textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, wrap ? TextWrapping.Wrap : TextWrapping.NoWrap));
            textStyle.Setters.Add(new Setter(ToolTipService.ToolTipProperty, new Binding(property)));
            grid.Columns.Add(new DataGridTextColumn { Header = title, Width = width, Binding = new Binding(property) { Converter = new DisplayValue("0.00", property == "Notes" ? "—" : "待补") }, ElementStyle = textStyle });
        }
        static void NumberColumn(DataGrid grid, string title, string property, bool signed = false, bool percent = false, bool attributeOnlyAware = false)
        {
            TextColumn(grid, title, property, percent ? 68 : 88);
            var converter = new DisplayValue(signed ? "+0.00;-0.00;0.00" : "0.00");
            BindingBase binding = new Binding(property) { Converter = converter };
            if (attributeOnlyAware) {
                var multiple = new MultiBinding { Converter = converter };
                multiple.Bindings.Add(new Binding(property)); multiple.Bindings.Add(new Binding("AttributeOnly")); binding = multiple;
            }
            ((DataGridTextColumn)grid.Columns[grid.Columns.Count - 1]).Binding = binding;
        }
        static void AddAmounts(DataGrid grid)
        {
            NumberColumn(grid, "申报投影 m²", "DeclaredProjectionArea"); NumberColumn(grid, "实际几何 m²", "GeometricArea");
            NumberColumn(grid, "计算面积 m²", "CalculatedArea"); NumberColumn(grid, "计容面积 m²", "FloorRatioArea");
            NumberColumn(grid, "不计容 m²", "NonFloorRatioArea"); NumberColumn(grid, "几何差值 m²", "DifferenceArea", true);
            NumberColumn(grid, "差异 %", "DifferencePercent", true, true);
        }
        static void AddFloorColumns(DataGrid grid, bool wrapFunctions)
        {
            TextColumn(grid, "楼层", "Name", 60); NumberColumn(grid, "层高 m", "HeightM"); grid.Columns[1].Width = 58;
            TextColumn(grid, "使用性质及计算面积", "FunctionSummary", 208, wrapFunctions);
            if (!wrapFunctions) {
                ((DataGridTextColumn)grid.Columns[1]).Binding = new Binding { Source = "—" };
                ((DataGridTextColumn)grid.Columns[2]).Binding = new Binding { Source = "—" };
            }
            AddAmounts(grid); TextColumn(grid, "备注", "Notes", 140);
        }
        static void AddComponentColumns(DataGrid grid)
        {
            TextColumn(grid, "构件", "Name", 210); TextColumn(grid, "元素 ID", "ElementId", 90); TextColumn(grid, "楼层", "LevelName", 110);
            TextColumn(grid, "功能", "Function", 170); TextColumn(grid, "分类", "Slot", 140);
            NumberColumn(grid, "计算系数", "CalculationFactor"); NumberColumn(grid, "计容系数", "FloorRatioFactor");
            AddAmounts(grid); TextColumn(grid, "状态", "Status", 150); TextColumn(grid, "备注", "Notes", 400);
        }
        static ScrollViewer Scroll(DependencyObject parent)
        {
            if (parent is ScrollViewer viewer) return viewer;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var found = Scroll(VisualTreeHelper.GetChild(parent, i)); if (found != null) return found; }
            return null;
        }
        static void SyncScroll(DataGrid target, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.HorizontalChange) > 0.001) Scroll(target)?.ScrollToHorizontalOffset(e.HorizontalOffset);
        }

        public void SetDocuments(BuildingAreaDocumentOption[] documents, string selectedKey)
        {
            var old = SelectedDocumentKey; loading = true;
            try
            {
                DocumentSelector.ItemsSource = documents ?? new BuildingAreaDocumentOption[0];
                DocumentSelector.SelectedValue = selectedKey;
                if (DocumentSelector.SelectedIndex < 0 && DocumentSelector.Items.Count > 0) DocumentSelector.SelectedIndex = 0;
            }
            finally { loading = false; }
            if (!string.Equals(old, SelectedDocumentKey, StringComparison.Ordinal)) { ClearReport(); busy = false; }
            UpdateActions();
        }

        public void ShowReport(BuildingAreaStatisticsReport report)
        {
            if (report == null || SelectedDocumentKey == null || !string.Equals(report.DocumentKey, SelectedDocumentKey, StringComparison.Ordinal)) return;
            loading = true;
            try
            {
                CurrentReport = report; busy = false; stale = changes != refreshChanges; selectedGrid = null; SelectionDetails.Text = "选择一行后查看完整使用性质、状态和备注。";
                FloorGrid.ItemsSource = report.Floors ?? new BuildingAreaFloor[0];
                TotalGrid.ItemsSource = report.Total == null ? new BuildingAreaFloor[0] : new[] { report.Total };
                ComponentGrid.ItemsSource = report.Components ?? new BuildingAreaComponent[0]; PendingGrid.ItemsSource = report.Pending ?? new BuildingAreaComponent[0];
                ComparisonGrid.ItemsSource = report.Comparisons ?? new BuildingAreaComparison[0];
                ((TabItem)DetailTabs.Items[2]).Header = "待完善项目（" + PendingGrid.Items.Count + "）";
                summary.Text = (report.BuildingNumber ?? "") + " " + (report.BuildingName ?? report.DocumentTitle ?? "单体") + "   ·   " + (report.Complete ? "统计完成" : "资料待完善") + "   ·   容差 " + report.TolerancePercent.ToString("0.00", CultureInfo.InvariantCulture) + "%";
                summary.Foreground = LiquidGlassTheme.Color(report.Complete ? "#234B74" : "#986015");
                DateTime captured;
                var time = DateTime.TryParse(report.CapturedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out captured)
                    ? captured.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : (report.CapturedUtc ?? "待核对");
                source.Text = "来源：" + (report.SourcePath ?? "未保存模型") + "    更新：" + time; source.ToolTip = source.Text;
                descriptions.Text = "统计范围：" + (report.ScopeDescription ?? "待核对") + "\n\n计算口径：" + (report.FormulaDescription ?? "待核对") + "\n\n备注：\n" + string.Join("\n", report.Notes ?? new string[0]);
            }
            finally { loading = false; }
            SetStatus(stale ? "模型已变化，此报告待刷新，刷新后方可导出。" : report.Complete ? "统计已更新。选择楼层或构件可定位。" : "统计已更新；存在缺项，请查看待完善项目及备注。可导出带说明的报告。");
            UpdateActions();
        }

        public void SetBusy(bool value, string message) { busy = value; if (message != null) SetStatus(message); UpdateActions(); }
        public void MarkStale(string message)
        {
            changes++; stale = true;
            SetStatus(string.IsNullOrWhiteSpace(message) ? "模型已变化，请刷新统计后再导出。" : message); UpdateActions();
        }
        public void SetStatus(string message) { StatusText.Text = message ?? ""; }

        void ClearReport()
        {
            CurrentReport = null; selectedGrid = null; stale = false; changes = refreshChanges = 0;
            loading = true;
            try { foreach (var grid in new[] { FloorGrid, TotalGrid, ComponentGrid, PendingGrid, ComparisonGrid }) grid.ItemsSource = null; }
            finally { loading = false; }
            summary.Text = "尚未读取当前单体面积"; source.Text = ""; source.ToolTip = null; descriptions.Text = ""; SelectionDetails.Text = "";
            ((TabItem)DetailTabs.Items[2]).Header = "待完善项目"; SetStatus("请刷新当前单体的面积统计。");
        }
        void RequestRefresh()
        {
            if (busy || string.IsNullOrEmpty(SelectedDocumentKey) || RefreshRequested == null) return;
            var key = SelectedDocumentKey; refreshChanges = changes; SetBusy(true, "正在读取当前单体面积……"); RefreshRequested(key);
        }
        int[] SelectedIds()
        {
            if (CurrentReport == null || selectedGrid == null) return new int[0];
            if (selectedGrid == FloorGrid || selectedGrid == TotalGrid)
                return selectedGrid.SelectedItems.Cast<BuildingAreaFloor>().SelectMany(f => f.IsTotal || selectedGrid == TotalGrid
                    ? (CurrentReport.Components ?? new BuildingAreaComponent[0]).Where(c => c.Included).Select(c => c.ElementId)
                    : (f.ElementIds ?? new int[0]).AsEnumerable()).Where(i => i > 0).Distinct().OrderBy(i => i).ToArray();
            if (selectedGrid == ComponentGrid || selectedGrid == PendingGrid)
                return selectedGrid.SelectedItems.Cast<BuildingAreaComponent>().Select(c => c.ElementId).Where(i => i > 0).Distinct().OrderBy(i => i).ToArray();
            return new int[0];
        }
        bool MatchesDocument => CurrentReport != null && string.Equals(CurrentReport.DocumentKey, SelectedDocumentKey, StringComparison.Ordinal);
        void DescribeSelection(DataGrid grid = null)
        {
            grid = grid ?? selectedGrid;
            SelectionDetails.Text = grid == null ? "" : string.Join("\n\n", grid.SelectedItems.Cast<object>().Select(item => {
                if (item is BuildingAreaFloor floor) return floor.Name + "\n" + floor.FunctionSummary + "\n备注：" + (string.IsNullOrWhiteSpace(floor.Notes) ? "—" : floor.Notes);
                if (item is BuildingAreaComponent component) return component.Name + "（" + component.ElementId + "）\n" + component.LevelName + " · " + component.Function + "\n状态：" + component.Status + "\n备注：" + (string.IsNullOrWhiteSpace(component.Notes) ? "—" : component.Notes);
                if (item is BuildingAreaComparison comparison) return comparison.Name + "\n说明：" + (string.IsNullOrWhiteSpace(comparison.Notes) ? "—" : comparison.Notes);
                return "";
            }));
        }
        void UpdateActions()
        {
            DocumentSelector.IsEnabled = !busy; RefreshButton.IsEnabled = !busy && !string.IsNullOrEmpty(SelectedDocumentKey);
            ExportButton.IsEnabled = !busy && !stale && MatchesDocument;
            ExportButton.ToolTip = stale ? "模型已变化，请先刷新统计。" : "导出当前单体报告，保留缺项和备注。";
            LocateButton.IsEnabled = !busy && MatchesDocument && SelectedIds().Length > 0;
        }
        void RequestLocate()
        {
            if (busy || !MatchesDocument || LocateRequested == null) return;
            var ids = SelectedIds(); if (ids.Length == 0) return;
            var key = CurrentReport.DocumentKey; SetBusy(true, "正在定位所选构件……"); LocateRequested(key, ids);
        }
        void RequestExport()
        {
            if (busy || stale || !MatchesDocument || ExportRequested == null) return;
            var key = CurrentReport.DocumentKey; SetBusy(true, "正在导出当前单体报告……"); ExportRequested(key);
        }
    }
}
