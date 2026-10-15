using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel;
using System.Windows.Threading;
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
        private readonly string profileDirectory;
        private ManagementStateStore stateStore;
        private ManagementState state;
        private bool restoring;
        private readonly TextBlock diagnosticText = new TextBlock { TextWrapping = TextWrapping.Wrap };
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
        internal string DiagnosticText => diagnosticText.Text;
        internal EditRequest[] DraftRequests => state?.Drafts ?? new EditRequest[0];
        internal bool HasRememberedStage => SelectedStage == "报规" || SelectedStage == "施工图";
        internal ManagerWindow(IManagerSession session, string profileDirectory)
        {
            this.session = session;
            this.profileDirectory = profileDirectory;
            Title = "H-IFC V1 · 全面检查与属性管理"; Width = 1380; Height = 850; MinWidth = 1080; MinHeight = 700;
            MaxHeight = SystemParameters.WorkArea.Height; Height = Math.Min(Height, MaxHeight); Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brush("#F2F5F7"); FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 12;
            var root = new Grid { Margin = new Thickness(16), Background = Background };
            foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) root.RowDefinitions.Add(new RowDefinition { Height = height });
            var header = new StackPanel(); header.Children.Add(new TextBlock { Text = "模型检查与属性管理", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brush("#19384C") });
            project.Text = "选择阶段和类型，全面检查；直接修改右侧属性，最后统一写入 Revit。"; project.TextWrapping = TextWrapping.Wrap; project.Margin = new Thickness(0, 5, 0, 8); header.Children.Add(project); Put(root, header, 0);
            var choices = new WrapPanel { Margin = new Thickness(0, 0, 0, 9) };
            choices.Children.Add(Label("1  人工确认阶段")); choices.Children.Add(StageSelector); choices.Children.Add(Label("2  模型类型")); choices.Children.Add(KindSelector);
            choices.Children.Add(ActionButton("3  全面检查", StartFullCheck));
            applyButton = ActionButton("4  写入 Revit", WriteChanges); applyButton.IsEnabled = false; choices.Children.Add(applyButton);
            cancelButton = ActionButton("取消检查", () => session.Cancel()); cancelButton.IsEnabled = false; choices.Children.Add(cancelButton); Put(root, choices, 1);
            StageSelector.SelectionChanged += (s, e) => SettingsChanged(); KindSelector.SelectionChanged += (s, e) => SettingsChanged();
            var metrics = new Grid { Margin = new Thickness(0, 0, 0, 10) }; for (int i = 0; i < 4; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
            coverageMetric = Metric("参考项覆盖\n等待检查", () => SetFilter("全部")); presenceMetric = Metric("字段存在\n等待检查", () => SetFilter("缺字段"));
            valueMetric = Metric("属性值\n等待检查", () => SetFilter("缺字段")); readyMetric = Metric("本轮状态\n请确认阶段", () => SetFilter("待处理"));
            var cards = new[] { coverageMetric, presenceMetric, valueMetric, readyMetric }; for (int i = 0; i < cards.Length; i++) { Grid.SetColumn(cards[i], i); metrics.Children.Add(cards[i]); }
            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(Search, Dock.Right); toolbar.Children.Add(Search); Search.TextChanged += (s, e) => RefreshRows();
            var filters = new ComboBox { ItemsSource = new[] { "全部", "待处理", "未分类", "缺字段", "已完成", "普通构件" }, SelectedIndex = 0, Width = 105, MinHeight = 28 };
            filters.SelectionChanged += (s, e) => SetFilter(filters.SelectedItem as string); toolbar.Children.Add(filters);
            toolbar.Children.Add(ActionButton("全选列表", () => ElementGrid.SelectAll())); toolbar.Children.Add(ActionButton("定位所选", LocateSelected)); range.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(range); Put(root, toolbar, 2);
            var work = new Grid(); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) }); work.ColumnDefinitions.Add(new ColumnDefinition()); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) }; var heading = Label("模型分类"); DockPanel.SetDock(heading, Dock.Top); left.Children.Add(heading); left.Children.Add(roleList); work.Children.Add(left);
            roleList.SelectionChanged += (s, e) => { if (roleList.SelectedItem is RoleItem item) { roleFilter = item.Role.Slot; filter = "全部"; RefreshRows(); detail.Text = item.Role.Slot + "：" + item.Role.State + "\n" + item.Source; } };
            Column(ElementGrid, "ID", "Id", 65); Column(ElementGrid, "对象名称", "Name", 150); Column(ElementGrid, "类别", "Category", 82); Column(ElementGrid, "楼层", "Level", 90); Column(ElementGrid, "业务分类", "SlotText", 160);
            ElementGrid.Columns.Add(new DataGridTextColumn { Header = "本轮结果", Binding = new Binding("StatusText") { Converter = new ProductStatus() }, Width = 215 });
            ElementGrid.SelectionMode = DataGridSelectionMode.Extended; ElementGrid.SelectionChanged += (s, e) => RefreshFields(); ElementGrid.MouseDoubleClick += (s, e) => { try { LocateSelected(); } catch (Exception ex) { SetStatus(ex.Message, true); } }; Grid.SetColumn(ElementGrid, 1); work.Children.Add(ElementGrid);
            var right = new Grid { Margin = new Thickness(8, 0, 0, 0) }; right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition()); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detail.Text = "选中构件后，直接在下面的属性值中修改。\n红色：待写入；橙色：已手工写入。"; detail.TextWrapping = TextWrapping.Wrap; detail.MaxHeight = 90; detail.Margin = new Thickness(0, 0, 0, 8); right.Children.Add(detail);
            FieldGrid.IsReadOnly = false;
            Column(FieldGrid, "属性", "Property", 125); FieldGrid.Columns[0].IsReadOnly = true;
            var valueColumn = new DataGridTextColumn { Header = "属性值（可修改）", Width = 125, IsReadOnly = false, Binding = new Binding("Value") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, ValidatesOnExceptions = true } };
            var cellStyle = new Style(typeof(DataGridCell)); cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, new Binding("Background"))); cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#233746"))); cellStyle.Setters.Add(new Setter(ToolTipProperty, new Binding("SourceDetail"))); valueColumn.CellStyle = cellStyle; FieldGrid.Columns.Add(valueColumn);
            FieldGrid.Columns.Add(new DataGridTextColumn { Header = "状态", Width = 130, IsReadOnly = true, Binding = new Binding("State") });
            FieldGrid.BeginningEdit += (s, e) => { var f = (e.Row.Item as PropertyEditor)?.Field; if (!Current || f == null || !f.Editable && f.State != "缺参数") { e.Cancel = true; SetStatus("该属性当前不可编辑；请先完成全面检查，或核对只读/冲突原因。", true); } };
            Put(right, FieldGrid, 1);
            Column(PreviewGrid, "载体 ID", "Id", 65); Column(PreviewGrid, "参数", "Source", 125); Column(PreviewGrid, "操作", "Action", 75); Column(PreviewGrid, "旧值", "OldValue", 95); Column(PreviewGrid, "新值", "NewValue", 145); Column(PreviewGrid, "范围 / 依据", "Detail", 310);
            var basisPanel = new StackPanel(); basisPanel.Children.Add(diagnosticText); basisPanel.Children.Add(ActionButton("清除未写入的暂存修改", DiscardDrafts));
            rightTabs.Items.Add(new TabItem { Header = "检查依据", Content = new ScrollViewer { Content = basisPanel } }); rightTabs.Items.Add(new TabItem { Header = "补齐与变更明细", Content = PreviewGrid }); rightTabs.Items.Add(new TabItem { Header = "核验详情", Content = conflicts });
            var editor = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; editor.Children.Add(new TextBlock { Text = "需要调整业务分类时：" }); editor.Children.Add(SlotSelector); editor.Children.Add(ActionButton("设置所选分类", PreviewClassification));
            editor.Children.Add(new TextBlock { Text = "红色待写入　橙色已手工写入\n修改完成后点顶部“4 写入 Revit”。", Foreground = Brush("#6F4C28"), Margin = new Thickness(0, 7, 0, 0) });
            Put(right, editor, 2); Grid.SetColumn(right, 2); work.Children.Add(right); Put(root, work, 3);
            var diagnostics = new DockPanel { MaxHeight = 270 }; DockPanel.SetDock(metrics, Dock.Top); diagnostics.Children.Add(metrics); diagnostics.Children.Add(rightTabs);
            Put(root, new Expander { Header = "开发与核验信息（可展开）", IsExpanded = false, Content = diagnostics, Margin = new Thickness(0, 8, 0, 0) }, 4);
            var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; footer.Children.Add(progress); var line = new DockPanel(); var report = ActionButton("保存检查与缺失报告", SaveReport); DockPanel.SetDock(report, Dock.Right); line.Children.Add(report);
            status.Text = "请选择阶段，然后开始全面检查。"; status.TextWrapping = TextWrapping.Wrap; status.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(status); footer.Children.Add(line); Put(root, footer, 5);
            Content = root;
            Closing += (s, e) => {
                if (!FieldGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !FieldGrid.CommitEdit(DataGridEditingUnit.Row, true))
                { e.Cancel = true; SetStatus("当前属性未能确认，请修正输入后再关闭。", true); return; }
                PersistState();
            };
            Closed += (s, e) => session.Close();
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
        { busy = value; StageSelector.IsEnabled = KindSelector.IsEnabled = FieldGrid.IsEnabled = !value; cancelButton.IsEnabled = value; progress.Value = total == 0 ? 0 : 100.0 * done / total; if (value) { preview = null; needsRecheck = true; readyMetric.Content = "本轮状态\n全面检查中"; } UpdateWriteButton(); SetStatus(message); }
        internal void SetStatus(string message, bool error = false) { status.Text = message; status.Foreground = Brush(error ? "#AE3D38" : "#26465B"); }
        internal void Accept(ModelSnapshot result)
        {
            snapshot = result; preview = null; needsRecheck = !result.Complete; PreviewGrid.ItemsSource = null; Summary = CaseWorkflow.Summarize(result.Stage, result.ModelKind, result.Elements);
            project.Text = result.Title + " · " + result.Stage + " / " + result.ModelKind + " · 已检查 " + result.Elements.Length + " 个对象";
            diagnosticText.Text = Summary.Basis + "\n" + string.Join("；", result.DetectionEvidence);
            var required = Summary.Roles.Count(r => r.Mode == "必要"); var present = Summary.Roles.Count(r => r.Mode == "必要" && r.Count > 0);
            SetMetric(coverageMetric, "参考项覆盖\n" + present + " / " + required, required == 0 ? (double?)null : 100.0 * present / required);
            SetMetric(presenceMetric, "字段存在（类型去重）\n" + Summary.PresentFields + " / " + Summary.FieldCount, Summary.FieldCount == 0 ? (double?)null : 100.0 * Summary.PresentFields / Summary.FieldCount); SetMetric(valueMetric, "有效属性值\n" + Summary.ValidFields + " / " + Summary.FieldCount, Summary.FieldPercent);
            readyMetric.Content = "本轮状态\n" + (Summary.PendingRequirements > 0 ? Summary.Basis : Summary.Ready ? "案例数据检查完成" : "未找到 " + Summary.MissingRequired + " · 待确认类 " + Summary.PendingClassificationRoles + "\n待补属性 " + result.Elements.SelectMany(e => e.Fields).Count(f => !f.Acceptable) + " · 未覆盖 " + Summary.CoverageGaps);
            var rules = CaseWorkflow.ForModel(result.Stage, result.ModelKind); roleList.ItemsSource = Summary.Roles.Select(r => new RoleItem { Role = r, Source = string.Join("、", rules.Single(rule => rule.Slot == r.Slot).Sources) }).ToArray(); roleList.DisplayMemberPath = "Text";
            conflicts.ItemsSource = result.Elements.Where(e => !string.IsNullOrEmpty(e.Issue) || !string.IsNullOrEmpty(e.CoverageNote)).Select(e => e.Id + " / " + e.Name + "：" + e.StatusText).ToArray();
            RefreshRows(); SetBusy(false, result.Stage == "施工图" ? "基础盘点完成；施工图检查规则待建立。" : "全面检查完成，正在整理可补齐项。", result.Elements.Length, result.Elements.Length);
            if (state == null) InitializeState(result.DocumentKey);
            state.Snapshot = result; PersistState(); UpdateWriteButton();
        }
        private sealed class RoleItem { public RoleCoverage Role { get; set; } public string Source { get; set; } public string Text => Role.Slot + "\n" + ProductStatus.Text(Role.State) + " · 已识别 " + Role.Count + " / 候选 " + Role.Candidates; }
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
            var rows = Selected(); FieldGrid.ItemsSource = rows.FirstOrDefault()?.Fields.Select(f => new PropertyEditor(this, f, rows)).ToArray(); if (snapshot == null) return;
            if (rows.Length > 0) { detail.Text = rows[0].Name + " · " + ProductStatus.Text(rows[0].StatusText) + (rows.Length > 1 ? "\n已选 " + rows.Length + " 个对象，共同属性修改将应用到所选对象。" : "\n直接修改属性值，完成后点顶部“4 写入 Revit”。"); detail.ToolTip = detail.Text; diagnosticText.Text = Summary.Basis + "\n" + string.Join("；", snapshot.DetectionEvidence) + "\n" + rows[0].Recognition; }
            SlotSelector.ItemsSource = CaseWorkflow.ForModel(snapshot.Stage, snapshot.ModelKind).Where(r => r.Binding == "实例" && rows.Length > 0 && rows.All(e => r.Entities.Contains(e.Entity) && e.Entity != "IfcBuilding" && e.Entity != "IfcBuildingStorey")).Select(r => r.Slot).ToArray();
            var preferred = rows.Length > 0 ? rows[0].Slot ?? (rows[0].SuggestedSlots.Length == 1 ? rows[0].SuggestedSlots[0] : null) : null;
            SlotSelector.SelectedItem = rows.All(r => r.Slot == preferred || r.SuggestedSlots.Contains(preferred)) ? preferred : null;
        }
        private void EnsureEditableScope() { if (!Current) throw new InvalidOperationException("当前结果尚未完成或已过期，请先全面复检。"); if (snapshot.Stage != "报规" || Summary.PendingRequirements > 0) throw new InvalidOperationException("当前阶段/类型的检查规则尚未确定。"); }
        internal void PreviewClassification()
        {
            EnsureEditableScope(); if (!(SlotSelector.SelectedItem is string slot)) throw new InvalidOperationException("请选择业务分类。");
            foreach (var row in Selected())
            {
                if (row.Slot == slot) { if (state != null) state.Drafts = state.Drafts.Where(r => r.Kind != "分类" || r.Baselines[0].ElementUniqueId != row.UniqueId).ToArray(); continue; }
                StageRequest(new EditRequest { Kind = "分类", Ids = new[] { row.Id }, Slot = slot, Baselines = new[] { new EditBaseline { ElementId = row.Id, ElementUniqueId = row.UniqueId, OwnerUniqueId = row.UniqueId, Value = row.Slot } } });
            }
            PersistState(); UpdateWriteButton(); session.Preview(new EditRequest { Kind = "分类", Ids = SelectedIds(), Slot = slot });
        }
        internal void PreviewValue()
        { EnsureEditableScope(); var field = (FieldGrid.SelectedItem as PropertyEditor)?.Field; if (field == null || !field.Editable) throw new InvalidOperationException("请选择可写属性；缺参数请先执行补齐预览。"); session.Preview(new EditRequest { Kind = "赋值", Ids = SelectedIds(), Source = field.Source, PropertySet = field.PropertySet, Binding = field.Binding, Value = NewValue.Text }); }
        internal void ShowPreview(EditPreview value)
        {
            if (!Current || value.ScanId != snapshot.ScanId) throw new InvalidOperationException("补齐预览不属于当前完整检查。");
            preview = value; PreviewGrid.ItemsSource = value.Changes; conflicts.ItemsSource = value.Conflicts; rightTabs.SelectedIndex = value.Changes.Length > 0 ? 1 : value.Conflicts.Length > 0 ? 2 : 0; UpdateWriteButton();
            SetStatus("全面检查完成；可补齐 " + value.Changes.Length + " 项，手工待写入 " + DraftRequests.Length + " 项。修改完成后点顶部“4 写入 Revit”。");
            var clashes = DraftRequests.Count(r => !Matches(r));
            if (clashes > 0) SetStatus("有 " + clashes + " 项暂存修改与模型现值冲突；请重新确认红色属性。失效对象的暂存可在底部开发信息中清除。", true);
        }
        internal void InvalidatePreview(string reason) { preview = null; applyButton.IsEnabled = false; PreviewGrid.ItemsSource = null; SetStatus(reason, true); }
        internal void MarkStale(string reason) { needsRecheck = true; if (readyMetric != null) readyMetric.Content = "本轮状态\n需要全面复检"; if (applyButton != null) InvalidatePreview(reason); }
        internal void LocateSelected() => session.Locate(SelectedIds());
        private void InitializeState(string key)
        {
            stateStore = new ManagementStateStore(profileDirectory, key); state = new ManagementState { DocumentKey = key };
        }
        internal void RestoreState(string key)
        {
            InitializeState(key); restoring = true;
            try
            {
                state = stateStore.Load(); StageSelector.SelectedItem = state.Stage; KindSelector.SelectedItem = state.Kind ?? "自动识别";
                if (state.Snapshot != null) { Accept(state.Snapshot); MarkStale("已恢复上次检查及未写入修改，正在等待全面复核当前模型。"); }
            }
            catch (Exception ex) { stateStore = null; SetStatus("上次暂存无法读取，保留原记录；请重新选择并检查：" + ex.Message, true); }
            finally { restoring = false; }
        }
        private void SettingsChanged()
        {
            if (restoring) return;
            MarkStale("阶段或类型已变化，请点“3 全面检查”。"); PersistState();
        }
        private void PersistState()
        {
            if (state == null || stateStore == null || restoring) return;
            state.Stage = SelectedStage; state.Kind = RequestedKind;
            try { stateStore.Save(state); } catch (Exception ex) { SetStatus("本地暂存保存失败，窗口内修改仍保留：" + ex.Message, true); }
        }
        private static string Key(string owner, string pset, string binding, string source) => owner + "|" + pset + "|" + binding + "|" + source;
        private string FieldKey(FieldCheck field) => Key(field.OwnerUniqueId, field.PropertySet, field.Binding, field.Source);
        private static string DraftKey(EditRequest request) => request.Kind == "分类" ? request.Baselines[0].ElementUniqueId + "|分类" : Key(request.Baselines[0].OwnerUniqueId, request.PropertySet, request.Binding, request.Source);
        private void StageRequest(EditRequest request)
        {
            if (state == null) InitializeState(snapshot.DocumentKey);
            state.Drafts = state.Drafts.Where(r => DraftKey(r) != DraftKey(request)).Concat(new[] { request }).ToArray();
        }
        internal void StageValue(FieldCheck field, string value)
            => StageTargets(field, value, Selected());
        private void StageTargets(FieldCheck field, string value, ModelElementCheck[] selected)
        {
            EnsureEditableScope();
            if (selected.Length == 0) throw new InvalidOperationException("请先选中构件。");
            foreach (var row in selected)
            {
                if (!snapshot.Elements.Contains(row)) throw new InvalidOperationException("属性对应的检查结果已更新，请重新选择构件。");
                var target = row.Fields.SingleOrDefault(f => f.PropertySet == field.PropertySet && f.Source == field.Source && f.Binding == field.Binding);
                if (target == null || !target.Editable && target.State != "缺参数") throw new InvalidOperationException("部分所选对象没有可写的共同属性，请缩小选择范围。");
            }
            foreach (var row in selected)
            {
                var target = row.Fields.Single(f => f.PropertySet == field.PropertySet && f.Source == field.Source && f.Binding == field.Binding);
                var request = new EditRequest { Kind = "赋值", Ids = new[] { row.Id }, Source = target.Source, PropertySet = target.PropertySet, Binding = target.Binding, Value = value,
                    Baselines = new[] { new EditBaseline { ElementId = row.Id, ElementUniqueId = row.UniqueId, OwnerUniqueId = target.OwnerUniqueId, Value = target.Value, State = target.State, SourceSignature = target.SourceSignature } } };
                if (state == null) InitializeState(snapshot.DocumentKey);
                if (value == ModelFieldValue(target)) state.Drafts = state.Drafts.Where(r => DraftKey(r) != DraftKey(request)).ToArray();
                else StageRequest(request);
            }
            PersistState(); UpdateWriteButton(); SetStatus("手工待写入 " + DraftRequests.Length + " 项；红色内容尚未写入 Revit。");
        }
        private bool Matches(EditRequest request)
        {
            if (snapshot == null || request.Baselines == null || request.Baselines.Length == 0) return false;
            return request.Baselines.All(b => {
                var row = snapshot.Elements.SingleOrDefault(r => r.Id == b.ElementId && r.UniqueId == b.ElementUniqueId);
                if (row == null) return false;
                if (request.Kind == "分类") return row.Slot == b.Value;
                var f = row.Fields.SingleOrDefault(x => x.PropertySet == request.PropertySet && x.Source == request.Source && x.Binding == request.Binding);
                return f != null && f.OwnerUniqueId == b.OwnerUniqueId && f.Value == b.Value && f.State == b.State && f.SourceSignature == b.SourceSignature;
            });
        }
        internal string FieldMark(FieldCheck field)
        {
            var draft = DraftRequests.FirstOrDefault(r => r.Kind == "赋值" && DraftKey(r) == FieldKey(field));
            if (draft != null) return Matches(draft) ? "待写入" : "待写入（冲突）";
            var saved = state?.ManualWrites.FirstOrDefault(r => r.Key == FieldKey(field));
            if (saved != null && saved.Value == field.Value) return "已手工写入";
            if ((field.State == "缺参数" || field.State == "未填写") && !string.IsNullOrWhiteSpace(field.SuggestedValue)) return "自动待写入";
            return field.State;
        }
        private static string ModelFieldValue(FieldCheck field) => !string.IsNullOrWhiteSpace(field.Value) ? field.DisplayValue ?? field.Value : (field.State == "缺参数" || field.State == "未填写") ? field.SuggestedValue ?? "" : field.DisplayValue ?? field.Value ?? "";
        private string FieldValue(FieldCheck field) => DraftRequests.FirstOrDefault(r => r.Kind == "赋值" && DraftKey(r) == FieldKey(field))?.Value ?? ModelFieldValue(field);
        private void UpdateWriteButton()
        {
            applyButton.IsEnabled = Current && (preview?.Changes.Length > 0 || DraftRequests.Length > 0) && DraftRequests.All(Matches);
            applyButton.Content = "4  写入 Revit" + (DraftRequests.Length > 0 ? "（" + DraftRequests.Length + " 项手工修改）" : "");
        }
        internal void WriteChanges()
        {
            if (!FieldGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !FieldGrid.CommitEdit(DataGridEditingUnit.Row, true)) throw new InvalidOperationException("当前属性未能确认，请修正输入后再写入。");
            EnsureEditableScope();
            if (!DraftRequests.All(Matches)) throw new InvalidOperationException("暂存修改与模型现值有冲突，请重新确认红色属性。");
            session.Write(DraftRequests);
        }
        private void DiscardDrafts()
        {
            if (state == null) return;
            state.Drafts = new EditRequest[0]; PersistState(); RefreshFields(); UpdateWriteButton(); SetStatus("未写入的暂存修改已清除，Revit 中的值保持原状。");
        }
        internal void RecordCommitted(EditPreview applied, EditRequest[] requests)
        {
            if (state == null) InitializeState(snapshot.DocumentKey);
            var completed = new List<EditRequest>(); var records = state.ManualWrites.ToList();
            foreach (var request in requests ?? new EditRequest[0])
            {
                if (request.Kind == "分类") { if (applied.Changes.Any(c => c.Action == "分类" && request.Ids.Contains(c.Id) && c.ExpectedRaw == request.Slot)) completed.Add(request); continue; }
                foreach (var baseline in request.Baselines)
                {
                    var change = applied.Changes.LastOrDefault(c => c.Action == "赋值" && c.OwnerUniqueId == baseline.OwnerUniqueId && c.Source == request.Source);
                    if (change == null) continue;
                    var key = Key(baseline.OwnerUniqueId, request.PropertySet, request.Binding, request.Source); var prior = records.FirstOrDefault(r => r.Key == key);
                    records.RemoveAll(r => r.Key == key); records.Add(new ManualWrite { Key = key, Value = change.ExpectedRaw, PreviousValue = baseline.Value, WrittenUtc = DateTime.UtcNow.ToString("O"), Count = (prior?.Count ?? 0) + 1 }); completed.Add(request);
                }
            }
            state.ManualWrites = records.ToArray();
            state.Drafts = state.Drafts.Where(d => !completed.Any(r => DraftKey(r) == DraftKey(d) && r.Value == d.Value && r.Slot == d.Slot)).ToArray();
            PersistState(); UpdateWriteButton();
        }
        internal void SelectProperty(FieldCheck field) => FieldGrid.SelectedItem = FieldGrid.Items.Cast<PropertyEditor>().FirstOrDefault(r => r.Field == field);
        private sealed class ProductStatus : IValueConverter
        {
            internal static string Text(string value) => (value ?? "").Replace("案例字段已齐", "本轮属性已齐").Replace("无案例专用要求", "本轮无专用属性要求").Replace("参考项未找到", "本轮未找到").Replace("案例差异项", "按需检查");
            public object Convert(object value, Type type, object parameter, CultureInfo culture) => Text(value as string);
            public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => Binding.DoNothing;
        }
        internal sealed class PropertyEditor : INotifyPropertyChanged
        {
            readonly ManagerWindow window;
            readonly ModelElementCheck[] targets;
            public FieldCheck Field { get; }
            public string Property => Field.Property;
            public string SourceDetail => Field.SuggestionReason ?? Field.Detail;
            public string State => window.FieldMark(Field);
            public Brush Background => Brush(State.StartsWith("待写入") ? "#FFFFDADA" : State == "已手工写入" ? "#FFFFD9AD" : "#FFFFFFFF");
            public string Value
            {
                get => window.FieldValue(Field);
                set { window.StageTargets(Field, value, targets); foreach (var name in new[] { "Value", "State", "Background" }) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
            }
            internal PropertyEditor(ManagerWindow window, FieldCheck field, ModelElementCheck[] targets) { this.window = window; Field = field; this.targets = targets; }
            public event PropertyChangedEventHandler PropertyChanged;
        }
        private void SaveReport()
        {
            if (!Current) throw new InvalidOperationException("请先完成当前模型的全面检查，再保存报告。");
            var dialog = new SaveFileDialog { Title = "保存完整检查与缺失报告", Filter = "HTML 报告 (*.html)|*.html", FileName = "HIFC检查-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html" };
            if (dialog.ShowDialog(this) == true) ManagementReport.Write(dialog.FileName, snapshot, Summary, preview);
        }
    }
}
