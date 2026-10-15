using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Hifc.Reference;
using Microsoft.Win32;

namespace Hifc.Revit.Management
{
    internal sealed class ManagerWindow : Window
    {
        private readonly IManagerSession session;
        private ModelSnapshot snapshot;
        private EditPreview preview;
        private string filter = "全部", roleFilter;
        private bool busy, needsRecheck = true;
        private readonly TextBlock project = new TextBlock(), status = new TextBlock(), detail = new TextBlock(), range = new TextBlock();
        private readonly ProgressBar progress = new ProgressBar { Height = 5, Maximum = 100 };
        private readonly Button coverageMetric, presenceMetric, valueMetric, readyMetric, applyButton, cancelButton;
        private readonly ListBox roleList = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch }, conflicts = new ListBox();
        private readonly TabControl rightTabs = new TabControl();
        internal readonly ComboBox StageSelector = new ComboBox { ItemsSource = new[] { "报规", "施工图" }, Width = 100, MinHeight = 29 };
        internal readonly ComboBox KindSelector = new ComboBox { ItemsSource = new[] { "自动识别", "总图", "单体建筑" }, SelectedIndex = 0, Width = 120, MinHeight = 29 };
        internal readonly ComboBox SlotSelector = new ComboBox { MinHeight = 28 };
        internal readonly DataGrid ElementGrid = GridTable(), FieldGrid = GridTable(), PreviewGrid = GridTable();
        internal readonly TextBox NewValue = new TextBox { MinHeight = 28 };
        internal readonly TextBox Search = new TextBox { MinHeight = 28, Width = 165, ToolTip = "名称、ID、类别、楼层、属性组" };
        internal string SelectedStage => StageSelector.SelectedItem as string;
        internal string RequestedKind => KindSelector.SelectedItem as string ?? "自动识别";
        internal ModelSummary Summary { get; private set; }
        internal ModelSnapshot Snapshot => snapshot;
        internal bool Current => snapshot?.Complete == true && !needsRecheck && !busy;
        internal bool CanApply => applyButton.IsEnabled;
        internal int VisibleCount => ElementGrid.Items.Count;
        internal string StatusText => status.Text;
        internal string DetailText => detail.Text;
        internal ManagerWindow(IManagerSession session, string profileDirectory)
        {
            this.session = session;
            Title = "H-IFC V1 · 全面检查与属性管理"; Width = 1380; Height = 850; MinWidth = 1080; MinHeight = 700;
            MaxHeight = SystemParameters.WorkArea.Height; Height = Math.Min(Height, MaxHeight); Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brush("#F2F5F7"); FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 12;
            var root = new Grid { Margin = new Thickness(16), Background = Background };
            foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) root.RowDefinitions.Add(new RowDefinition { Height = height });
            var header = new StackPanel(); header.Children.Add(new TextBlock { Text = "模型检查与属性管理", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brush("#19384C") });
            project.Text = "先确认阶段，再识别模型类型；每次检查重新读取整个当前文档。"; project.TextWrapping = TextWrapping.Wrap; project.Margin = new Thickness(0, 5, 0, 8); header.Children.Add(project); Put(root, header, 0);
            var choices = new WrapPanel { Margin = new Thickness(0, 0, 0, 9) };
            choices.Children.Add(Label("1  人工确认阶段")); choices.Children.Add(StageSelector); choices.Children.Add(Label("2  模型类型")); choices.Children.Add(KindSelector);
            choices.Children.Add(ActionButton("全面检查并补齐属性", StartFullCheck)); choices.Children.Add(ActionButton("重新生成补齐预览", () => { EnsureEditableScope(); session.PrepareAll(); }));
            cancelButton = ActionButton("取消检查", () => session.Cancel()); cancelButton.IsEnabled = false; choices.Children.Add(cancelButton); Put(root, choices, 1);
            StageSelector.SelectionChanged += (s, e) => MarkStale("阶段已变更，请重新全面检查。"); KindSelector.SelectionChanged += (s, e) => MarkStale("模型类型已变更，请重新全面检查。");
            var metrics = new Grid { Margin = new Thickness(0, 0, 0, 10) }; for (int i = 0; i < 4; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
            coverageMetric = Metric("参考项覆盖\n等待检查", () => SetFilter("全部")); presenceMetric = Metric("字段存在\n等待检查", () => SetFilter("缺字段"));
            valueMetric = Metric("属性值\n等待检查", () => SetFilter("缺字段")); readyMetric = Metric("本轮状态\n请确认阶段", () => SetFilter("待处理"));
            var cards = new[] { coverageMetric, presenceMetric, valueMetric, readyMetric }; for (int i = 0; i < cards.Length; i++) { Grid.SetColumn(cards[i], i); metrics.Children.Add(cards[i]); } Put(root, metrics, 2);
            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(Search, Dock.Right); toolbar.Children.Add(Search); Search.TextChanged += (s, e) => RefreshRows();
            var filters = new ComboBox { ItemsSource = new[] { "全部", "待处理", "未分类", "缺字段", "已完成", "普通构件" }, SelectedIndex = 0, Width = 105, MinHeight = 28 };
            filters.SelectionChanged += (s, e) => SetFilter(filters.SelectedItem as string); toolbar.Children.Add(filters);
            toolbar.Children.Add(ActionButton("选取显示项", () => ElementGrid.SelectAll())); toolbar.Children.Add(ActionButton("定位所选", LocateSelected)); range.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(range); Put(root, toolbar, 3);
            var work = new Grid(); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) }); work.ColumnDefinitions.Add(new ColumnDefinition()); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) }; var heading = Label("本阶段 / 类型的参考清单"); DockPanel.SetDock(heading, Dock.Top); left.Children.Add(heading); left.Children.Add(roleList); work.Children.Add(left);
            roleList.SelectionChanged += (s, e) => { if (roleList.SelectedItem is RoleItem item) { roleFilter = item.Role.Slot; filter = "全部"; RefreshRows(); detail.Text = item.Role.Slot + "：" + item.Role.State + "\n" + item.Source; } };
            Column(ElementGrid, "ID", "Id", 65); Column(ElementGrid, "对象名称", "Name", 150); Column(ElementGrid, "类别", "Category", 82); Column(ElementGrid, "楼层", "Level", 90); Column(ElementGrid, "业务 / 类型属性组", "SlotText", 160); Column(ElementGrid, "本轮结果", "StatusText", 215);
            Column(ElementGrid, "识别依据", "Recognition", 420);
            ElementGrid.SelectionMode = DataGridSelectionMode.Extended; ElementGrid.SelectionChanged += (s, e) => RefreshFields(); ElementGrid.MouseDoubleClick += (s, e) => { try { LocateSelected(); } catch (Exception ex) { SetStatus(ex.Message, true); } }; Grid.SetColumn(ElementGrid, 1); work.Children.Add(ElementGrid);
            var right = new Grid { Margin = new Thickness(8, 0, 0, 0) }; right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition()); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detail.Text = "补齐预览自动覆盖本轮全部对象。选中清单可查看识别依据、修正分类或填写业务值。"; detail.TextWrapping = TextWrapping.Wrap; detail.MaxHeight = 150; detail.TextTrimming = TextTrimming.CharacterEllipsis; detail.Margin = new Thickness(0, 0, 0, 8); right.Children.Add(detail);
            Column(FieldGrid, "属性", "Property", 95); Column(FieldGrid, "当前值", "DisplayValue", 90); Column(FieldGrid, "状态", "State", 70); Column(FieldGrid, "属性组", "PropertySet", 180); Column(FieldGrid, "来源参数", "Source", 160); Column(FieldGrid, "来源", "OriginLabel", 60); Column(FieldGrid, "说明", "Detail", 220);
            FieldGrid.SelectionChanged += (s, e) => { if (FieldGrid.SelectedItem is FieldCheck f) { NewValue.Text = f.DisplayValue ?? f.Value ?? ""; detail.Text = f.PropertySet + " / " + f.Property + "\n" + f.Detail; } }; rightTabs.Items.Add(new TabItem { Header = "属性", Content = FieldGrid });
            Column(PreviewGrid, "载体 ID", "Id", 65); Column(PreviewGrid, "参数", "Source", 125); Column(PreviewGrid, "操作", "Action", 75); Column(PreviewGrid, "旧值", "OldValue", 95); Column(PreviewGrid, "新值", "NewValue", 145); Column(PreviewGrid, "范围 / 依据", "Detail", 310);
            rightTabs.Items.Add(new TabItem { Header = "补齐预览", Content = PreviewGrid }); rightTabs.Items.Add(new TabItem { Header = "人工处理", Content = conflicts }); Put(right, rightTabs, 1);
            var editor = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; editor.Children.Add(SlotSelector); editor.Children.Add(ActionButton("预览所选对象分类", PreviewClassification));
            editor.Children.Add(new TextBlock { Text = "所选属性的新值（数值可带项目单位）", Margin = new Thickness(0, 5, 0, 3) }); editor.Children.Add(NewValue); editor.Children.Add(ActionButton("预览属性修改", PreviewValue));
            applyButton = ActionButton("写入已预览项并全面复检", () => { EnsureEditableScope(); if (preview != null) session.Apply(preview); }); applyButton.IsEnabled = false; editor.Children.Add(applyButton); Put(right, editor, 2); Grid.SetColumn(right, 2); work.Children.Add(right); Put(root, work, 4);
            var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; footer.Children.Add(progress); var line = new DockPanel(); var report = ActionButton("保存检查与缺失报告", SaveReport); DockPanel.SetDock(report, Dock.Right); line.Children.Add(report);
            status.Text = "请选择阶段，然后开始全面检查。"; status.TextWrapping = TextWrapping.Wrap; status.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(status); footer.Children.Add(line); Put(root, footer, 5);
            Content = root; Closed += (s, e) => session.Close();
        }
        private static TextBlock Label(string text) => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 3, 9, 3) };
        private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color);
        private static void Put(Grid grid, UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }
        private static DataGrid GridTable() => new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, RowHeight = 30, EnableRowVirtualization = true, EnableColumnVirtualization = true, Background = Brushes.White, SelectionUnit = DataGridSelectionUnit.FullRow };
        private static void Column(DataGrid grid, string title, string path, double width) => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = width });
        private Button ActionButton(string title, Action action) { var button = new Button { Content = title, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(6, 3, 0, 3), MinHeight = 28 }; button.Click += (s, e) => { try { action(); } catch (Exception ex) { SetStatus(ex.Message, true); } }; return button; }
        private Button Metric(string title, Action action) { var b = ActionButton(title, action); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Background = Brushes.White; b.Padding = new Thickness(12); b.FontSize = 14; return b; }
        private static void SetMetric(Button button, string text, double? value) { var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }); panel.Children.Add(new ProgressBar { Height = 4, Value = value ?? 0, Maximum = 100, Margin = new Thickness(0, 7, 0, 0) }); button.Content = panel; }
        internal void StartFullCheck() { if (SelectedStage != "报规" && SelectedStage != "施工图") throw new InvalidOperationException("请先人工选择阶段。"); if (busy) throw new InvalidOperationException("检查进行中。"); session.Scan(); }
        internal void SetBusy(bool value, string message, int done = 0, int total = 0)
        { busy = value; StageSelector.IsEnabled = KindSelector.IsEnabled = !value; cancelButton.IsEnabled = value; progress.Value = total == 0 ? 0 : 100.0 * done / total; if (value) { preview = null; needsRecheck = true; readyMetric.Content = "本轮状态\n全面检查中"; } applyButton.IsEnabled = !value && !needsRecheck && preview?.Changes.Length > 0; SetStatus(message); }
        internal void SetStatus(string message, bool error = false) { status.Text = message; status.Foreground = Brush(error ? "#AE3D38" : "#26465B"); }
        internal void Accept(ModelSnapshot result)
        {
            snapshot = result; preview = null; needsRecheck = !result.Complete; PreviewGrid.ItemsSource = null; Summary = CaseWorkflow.Summarize(result.Stage, result.ModelKind, result.Elements);
            project.Text = result.Title + " · " + Summary.Basis + "\n" + string.Join("；", result.DetectionEvidence);
            var required = Summary.Roles.Count(r => r.Mode == "必要"); var present = Summary.Roles.Count(r => r.Mode == "必要" && r.Count > 0);
            SetMetric(coverageMetric, "参考项覆盖\n" + present + " / " + required, required == 0 ? (double?)null : 100.0 * present / required);
            SetMetric(presenceMetric, "字段存在（类型去重）\n" + Summary.PresentFields + " / " + Summary.FieldCount, Summary.FieldCount == 0 ? (double?)null : 100.0 * Summary.PresentFields / Summary.FieldCount); SetMetric(valueMetric, "有效属性值\n" + Summary.ValidFields + " / " + Summary.FieldCount, Summary.FieldPercent);
            readyMetric.Content = "本轮状态\n" + (Summary.PendingRequirements > 0 ? Summary.Basis : Summary.Ready ? "案例数据检查完成" : "未找到 " + Summary.MissingRequired + " · 待确认类 " + Summary.PendingClassificationRoles + "\n待补属性 " + result.Elements.SelectMany(e => e.Fields).Count(f => !f.Acceptable) + " · 未覆盖 " + Summary.CoverageGaps);
            var rules = CaseWorkflow.ForModel(result.Stage, result.ModelKind); roleList.ItemsSource = Summary.Roles.Select(r => new RoleItem { Role = r, Source = string.Join("、", rules.Single(rule => rule.Slot == r.Slot).Sources) }).ToArray(); roleList.DisplayMemberPath = "Text";
            conflicts.ItemsSource = result.Elements.Where(e => !string.IsNullOrEmpty(e.Issue) || !string.IsNullOrEmpty(e.CoverageNote)).Select(e => e.Id + " / " + e.Name + "：" + e.StatusText).ToArray();
            RefreshRows(); SetBusy(false, result.Stage == "施工图" ? "基础盘点完成；施工图检查规则待建立。" : "全面检查完成，正在整理可补齐项。", result.Elements.Length, result.Elements.Length);
        }
        private sealed class RoleItem { public RoleCoverage Role { get; set; } public string Source { get; set; } public string Text => Role.Slot + "\n" + Role.State + " · 已识别 " + Role.Count + " / 候选 " + Role.Candidates + (Role.Mode == "案例补充" ? "（补充）" : ""); }
        internal void SetFilter(string value) { filter = value ?? "全部"; roleFilter = null; RefreshRows(); }
        private void RefreshRows()
        {
            if (snapshot == null) return; var rows = snapshot.Elements.AsEnumerable();
            if (filter == "未分类") rows = rows.Where(e => e.SuggestedSlots.Length > 0 || !e.InventoryOnly && !e.Classified);
            if (filter == "缺字段") rows = rows.Where(e => e.Fields.Any(f => !f.Acceptable));
            if (filter == "已完成") rows = rows.Where(e => !e.InventoryOnly && e.Classified && e.SuggestedSlots.Length == 0 && e.Fields.Length > 0 && e.Fields.All(f => f.Acceptable) && string.IsNullOrEmpty(e.Issue) && string.IsNullOrEmpty(e.CoverageNote));
            if (filter == "普通构件") rows = rows.Where(e => e.InventoryOnly);
            if (filter == "待处理") rows = rows.Where(e => e.SuggestedSlots.Length > 0 || !string.IsNullOrEmpty(e.Issue) || !string.IsNullOrEmpty(e.CoverageNote) || e.Fields.Any(f => !f.Acceptable));
            if (roleFilter != null) rows = rows.Where(e => e.Slots.Contains(roleFilter) || e.SuggestedSlots.Contains(roleFilter) || e.Slot == roleFilter);
            var text = Search.Text.Trim(); if (text.Length > 0) rows = rows.Where(e => (e.Id + " " + e.Name + " " + e.Category + " " + e.Level + " " + e.SlotText).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            ElementGrid.ItemsSource = rows.ToArray(); range.Text = "显示 " + VisibleCount + " / 全量 " + snapshot.Elements.Length;
        }
        private ModelElementCheck[] Selected() => ElementGrid.SelectedItems.Cast<ModelElementCheck>().ToArray();
        private int[] SelectedIds() { var rows = Selected(); if (rows.Length == 0) throw new InvalidOperationException("请在管理清单中选取需要人工处理的对象。"); return rows.Select(e => e.Id).ToArray(); }
        private void RefreshFields()
        {
            var rows = Selected(); FieldGrid.ItemsSource = rows.FirstOrDefault()?.Fields; if (snapshot == null) return;
            if (rows.Length > 0) { detail.Text = rows[0].Name + " · " + rows[0].StatusText + "\n" + rows[0].Recognition; detail.ToolTip = detail.Text; }
            SlotSelector.ItemsSource = CaseWorkflow.ForModel(snapshot.Stage, snapshot.ModelKind).Where(r => r.Binding == "实例" && rows.Length > 0 && rows.All(e => r.Entities.Contains(e.Entity) && e.Entity != "IfcBuilding" && e.Entity != "IfcBuildingStorey")).Select(r => r.Slot).ToArray();
            var preferred = rows.Length > 0 ? rows[0].Slot ?? (rows[0].SuggestedSlots.Length == 1 ? rows[0].SuggestedSlots[0] : null) : null;
            SlotSelector.SelectedItem = rows.All(r => r.Slot == preferred || r.SuggestedSlots.Contains(preferred)) ? preferred : null;
        }
        private void EnsureEditableScope() { if (!Current) throw new InvalidOperationException("当前结果尚未完成或已过期，请先全面复检。"); if (snapshot.Stage != "报规" || Summary.PendingRequirements > 0) throw new InvalidOperationException("当前阶段/类型的检查规则尚未确定。"); }
        internal void PreviewClassification() { EnsureEditableScope(); if (!(SlotSelector.SelectedItem is string slot)) throw new InvalidOperationException("请选择业务分类。"); session.Preview(new EditRequest { Kind = "分类", Ids = SelectedIds(), Slot = slot }); }
        internal void PreviewValue()
        { EnsureEditableScope(); var field = FieldGrid.SelectedItem as FieldCheck; if (field == null || !field.Editable) throw new InvalidOperationException("请选择可写属性；缺参数请先执行补齐预览。"); session.Preview(new EditRequest { Kind = "赋值", Ids = SelectedIds(), Source = field.Source, PropertySet = field.PropertySet, Binding = field.Binding, Value = NewValue.Text }); }
        internal void ShowPreview(EditPreview value)
        {
            if (!Current || value.ScanId != snapshot.ScanId) throw new InvalidOperationException("补齐预览不属于当前完整检查。");
            preview = value; PreviewGrid.ItemsSource = value.Changes; conflicts.ItemsSource = value.Conflicts; rightTabs.SelectedIndex = value.Changes.Length > 0 ? 1 : value.Conflicts.Length > 0 ? 2 : 0; applyButton.IsEnabled = value.Changes.Length > 0;
            SetStatus("本轮 " + snapshot.Elements.Length + " 个对象已全面读取；可写入 " + value.Changes.Length + " 项，需人工处理 " + value.Conflicts.Length + " 项。核对预览后写入，随后全面复检。");
        }
        internal void InvalidatePreview(string reason) { preview = null; applyButton.IsEnabled = false; PreviewGrid.ItemsSource = null; SetStatus(reason, true); }
        internal void MarkStale(string reason) { needsRecheck = true; if (readyMetric != null) readyMetric.Content = "本轮状态\n需要全面复检"; if (applyButton != null) InvalidatePreview(reason); }
        internal void LocateSelected() => session.Locate(SelectedIds());
        private void SaveReport()
        {
            if (!Current) throw new InvalidOperationException("请先完成当前模型的全面检查，再保存报告。");
            var dialog = new SaveFileDialog { Title = "保存完整检查与缺失报告", Filter = "HTML 报告 (*.html)|*.html", FileName = "HIFC检查-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html" };
            if (dialog.ShowDialog(this) == true) ManagementReport.Write(dialog.FileName, snapshot, Summary, preview);
        }
    }
}
