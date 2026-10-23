using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hifc.Reference;
using Microsoft.Win32;

namespace Hifc.Revit.Management
{
    internal sealed class ReportingWorkbookWindow : Window
    {
        sealed class TableOption
        {
            public ReportingWorkbookTable Table { get; set; }
            public string Title { get; set; }
        }
        sealed class BuildingOption
        {
            public string UniqueId { get; set; }
            public string Title { get; set; }
        }
        readonly ModelElementCheck[] elements;
        readonly string kind;
        readonly List<ReportingWorkbookDocument> documents = new List<ReportingWorkbookDocument>();
        readonly Dictionary<string, ReportingWorkbookPlan> plans = new Dictionary<string, ReportingWorkbookPlan>(StringComparer.Ordinal);
        readonly TextBlock source = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        readonly TextBlock issues = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        readonly TextBlock error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        readonly TextBlock summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 6) };
        readonly CheckBox retain = new CheckBox { Content = "保留当前表原始资料和来源（未映射字段仅作资料）", IsChecked = true, Margin = new Thickness(0, 8, 0, 8) };
        readonly DataGrid referenceGrid = Table();
        bool updating;
        internal readonly ComboBox TableSelector = new ComboBox { DisplayMemberPath = "Title", MinWidth = 360 };
        internal readonly ComboBox BuildingSelector = new ComboBox { DisplayMemberPath = "Title", SelectedValuePath = "UniqueId", MinWidth = 260 };
        internal readonly DataGrid ChangeGrid = Table();
        internal ReportingWorkbookPlan Plan { get; private set; } = new ReportingWorkbookPlan();
        internal ReportingWorkbookDecision Decision { get; private set; }
        internal ReportingWorkbookWindow(ModelElementCheck[] elements, string kind, ReportingWorkbookDecision[] saved = null)
        {
            this.elements = elements ?? new ModelElementCheck[0]; this.kind = kind;
            Title = "报规表导入 · 预览并暂存"; Width = 1190; Height = 790; MinWidth = 960; MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; LiquidGlassTheme.Apply(this);
            var root = new Grid { Margin = new Thickness(20), Background = Background }; Content = root;
            foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
                root.RowDefinitions.Add(new RowDefinition { Height = height });
            var intro = new StackPanel();
            intro.Children.Add(new TextBlock { Text = "一栋一张表，可从多个文件或多个工作表中选择当前楼栋。空白保留现值，冲突默认不勾选。组合值不自动拆分。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
            var open = new Button { Content = "添加 Excel（可多文件）", Margin = new Thickness(0, 0, 10, 0) };
            open.Click += (s, e) => { var dialog = new OpenFileDialog { Filter = "Excel 报规表 (*.xlsx)|*.xlsx", Multiselect = true }; if (dialog.ShowDialog(this) == true) Try(() => LoadFiles(dialog.FileNames)); };
            toolbar.Children.Add(open);
            var template = new Button { Content = "导出一栋一表模板" };
            template.Click += (s, e) => Try(ExportTemplate); toolbar.Children.Add(template); intro.Children.Add(toolbar); root.Children.Add(intro);
            var choices = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            var tablePanel = new StackPanel { Margin = new Thickness(0, 0, 14, 0) }; tablePanel.Children.Add(new TextBlock { Text = "当前表（每次只暂存当前表）", Margin = new Thickness(0, 0, 0, 5) }); tablePanel.Children.Add(TableSelector); choices.Children.Add(tablePanel);
            var buildingPanel = new StackPanel(); buildingPanel.Children.Add(new TextBlock { Text = "对应模型楼栋", Margin = new Thickness(0, 0, 0, 5) }); buildingPanel.Children.Add(BuildingSelector); Grid.SetColumn(buildingPanel, 1); choices.Children.Add(buildingPanel);
            Grid.SetRow(choices, 1); root.Children.Add(choices); Grid.SetRow(source, 2); root.Children.Add(source);
            var warning = new ScrollViewer { Content = issues, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(warning, 3); root.Children.Add(warning);
            var tabs = new TabControl { Background = Background, BorderThickness = new Thickness(0) }; Grid.SetRow(tabs, 4); root.Children.Add(tabs);
            var check = new FrameworkElementFactory(typeof(CheckBox)); check.SetBinding(CheckBox.IsCheckedProperty, new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            check.SetBinding(IsEnabledProperty, new Binding("CanSelect")); check.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); check.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            ChangeGrid.Columns.Add(new DataGridTemplateColumn { Header = "选用", CellTemplate = new DataTemplate { VisualTree = check }, Width = 54 });
            Column(ChangeGrid, "模型对象", "ElementName", 150); Column(ChangeGrid, "属性", "Property", 135); Column(ChangeGrid, "当前值", "CurrentDisplayValue", 125); Column(ChangeGrid, "Excel 值", "PreviewValue", 125);
            Column(ChangeGrid, "源单元格", "SourceLocation", 120); Column(ChangeGrid, "核对提示", "Issue", 330);
            var rowStyle = new Style(typeof(DataGridRow)); var conflict = new DataTrigger { Binding = new Binding("IsConflict"), Value = true };
            conflict.Setters.Add(new Setter(BackgroundProperty, LiquidGlassTheme.Color("#FFF1D9"))); rowStyle.Triggers.Add(conflict); ChangeGrid.RowStyle = rowStyle;
            tabs.Items.Add(new TabItem { Header = "可映射属性 · 当前值与 Excel 对照", Content = ChangeGrid });
            Column(referenceGrid, "属性", "Field", 165); Column(referenceGrid, "楼层", "LevelName", 100); Column(referenceGrid, "原始值", "Value", 190); Column(referenceGrid, "单位", "Unit", 65);
            Column(referenceGrid, "源单元格", "SourceLocation", 120); Column(referenceGrid, "处理说明", "Status", 330); Column(referenceGrid, "原备注", "Note", 190);
            tabs.Items.Add(new TabItem { Header = "当前表原始资料", Content = referenceGrid });
            var savedGrid=Table();Column(savedGrid,"属性","Field",165);Column(savedGrid,"楼层","LevelName",100);Column(savedGrid,"原始值","Value",190);
            Column(savedGrid,"源单元格","SourceLocation",130);Column(savedGrid,"处理说明","Status",330);
            savedGrid.ItemsSource=(saved??new ReportingWorkbookDecision[0]).SelectMany(d=>d.References).ToArray();
            tabs.Items.Add(new TabItem {Header="模型已保存资料",Content=savedGrid});
            var footer = new StackPanel(); footer.Children.Add(summary); footer.Children.Add(retain); Grid.SetRow(footer, 5); root.Children.Add(footer);
            error.Foreground = LiquidGlassTheme.Color("#A13C2D"); Grid.SetRow(error, 6); root.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetRow(buttons, 7); root.Children.Add(buttons);
            var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 10, 0) }; cancel.Click += (s, e) => Close(); buttons.Children.Add(cancel);
            var save = new Button { Content = "暂存当前表选中项", MinWidth = 180 }; LiquidGlassTheme.Primary(save); buttons.Children.Add(save);
            save.Click += (s, e) => Try(() => { Decision = BuildDecision(); DialogResult = true; });
            TableSelector.SelectionChanged += (s, e) => { if (!updating) SelectTable(); };
            BuildingSelector.SelectionChanged += (s, e) => { if (!updating) RefreshPlan(); };
            ShowPlan();
        }
        internal void LoadFiles(IEnumerable<string> paths)
        {
            var added = (paths ?? new string[0]).Select(ReportingWorkbookReader.Read).ToArray();
            foreach (var book in added) if (!documents.Any(d => d.Path == book.Path && d.Sha256 == book.Sha256)) documents.Add(book);
            var options = documents.SelectMany(d => d.Tables.Select(t => new TableOption { Table = t, Title = Path.GetFileName(d.Path) + " / " + t.Title })).ToArray();
            updating = true;
            try { TableSelector.ItemsSource = options; TableSelector.SelectedIndex = options.Length == 1 ? 0 : -1; }
            finally { updating = false; }
            SelectTable();
        }
        void SelectTable()
        {
            var selected = TableSelector.SelectedItem as TableOption;
            var targets = elements.Where(e => e.Entity == "IfcBuilding").Select(e => new BuildingOption { UniqueId = e.UniqueId, Title = BuildingName(e) + " · ID " + e.Id }).ToArray();
            var matched = selected == null ? new ModelElementCheck[0] : elements.Where(e => e.Entity == "IfcBuilding" &&
                new[] { e.Name, e.Building }.Concat((e.Fields ?? new FieldCheck[0]).Where(f => f.Property == "建筑名称" && f.PropertySet == "Pset_建筑技术信息属性集").Select(f => f.Value)).Any(n => !string.IsNullOrWhiteSpace(n) && n == selected.Table.BuildingName)).ToArray();
            updating = true;
            try {
                BuildingSelector.ItemsSource = new[] { new BuildingOption { UniqueId = "", Title = "请选择模型楼栋" } }.Concat(targets).ToArray();
                BuildingSelector.SelectedValue = matched.Length == 1 ? matched[0].UniqueId : ""; BuildingSelector.IsEnabled = selected?.Table.Kind == "单体建筑" && kind == "单体建筑";
            } finally { updating = false; }
            RefreshPlan();
        }
        void RefreshPlan()
        {
            var selected = TableSelector.SelectedItem as TableOption;
            if (selected == null) { Plan = new ReportingWorkbookPlan(); ShowPlan(); return; }
            var target = BuildingSelector.SelectedValue as string;
            var key = selected.Table.Id + "|" + target;
            if (!plans.TryGetValue(key, out var plan)) plans[key] = plan = ReportingWorkbookPlanner.Build(selected.Table, elements, kind, target ?? "");
            Plan = plan; ShowPlan();
        }
        void ShowPlan()
        {
            error.Text = ""; ChangeGrid.ItemsSource = Plan.Changes; referenceGrid.ItemsSource = Plan.References;
            source.Text = Plan.SourceFiles.Length == 0 ? "添加 Excel 后请选择一张表。" : string.Join("\n", Plan.SourceFiles.Select(f => f.Path + "\nSHA-256  " + f.Sha256));
            issues.Text = string.Join("\n", Plan.Issues); issues.Foreground = LiquidGlassTheme.Color("#8A602B");
            summary.Text = Plan.References.Length == 0 ? "尚未选择来源表。" : Plan.Changes.Count(c => c.CanSelect) + " 项可选属性；" + Plan.Changes.Count(c => c.IsConflict) + " 项与现值冲突；" + Plan.References.Length + " 条原始资料。暂存后仍需点击“4 写入 Revit”。楼层面积通过原有面积同步流程处理。";
        }
        internal ReportingWorkbookDecision BuildDecision()
        {
            ChangeGrid.CommitEdit(DataGridEditingUnit.Cell, true); ChangeGrid.CommitEdit(DataGridEditingUnit.Row, true);
            return ReportingWorkbookPlanner.Decide(Plan, retain.IsChecked == true);
        }
        void ExportTemplate()
        {
            var dialog = new SaveFileDialog { Filter = "Excel 报规表模板 (*.xlsx)|*.xlsx", FileName = "报规属性表_一栋一表.xlsx", OverwritePrompt = false };
            if (dialog.ShowDialog(this) != true) return;
            ReportingWorkbookTemplate.Write(dialog.FileName, elements.Where(e => e.Entity == "IfcBuilding").Select(BuildingName));
            error.Text = "模板已保存：" + dialog.FileName;
        }
        static string BuildingName(ModelElementCheck element) => (element.Fields ?? new FieldCheck[0]).FirstOrDefault(f => f.PropertySet == "Pset_建筑技术信息属性集" && f.Property == "建筑名称" && !string.IsNullOrWhiteSpace(f.Value))?.Value
            ?? (!string.IsNullOrWhiteSpace(element.Building) ? element.Building : element.Name);
        void Try(Action action) { try { error.Text = ""; action(); } catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException) { error.Text = ex.Message; } }
        static DataGrid Table()
        {
            var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Single, RowHeight = double.NaN, MinRowHeight = 38, EnableRowVirtualization = true };
            LiquidGlassTheme.Table(grid); return grid;
        }
        static void Column(DataGrid grid, string title, string path, double width)
        {
            var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), IsReadOnly = true, Width = width, ElementStyle = style });
        }
    }
}
