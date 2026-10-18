using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Hifc.Revit.Management;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public sealed class WebModelWindow : Window
    {
        private readonly WebModelSession session;
        private readonly TextBox phone = new TextBox(), name = new TextBox(), code = new TextBox();
        private readonly TextBox search = new TextBox { Width = 210 }, fieldSearch = new TextBox(), newValue = new TextBox();
        private readonly ComboBox kind = new ComboBox { Width = 125 }, slots = new ComboBox(), filter = new ComboBox { Width = 105 };
        private readonly ListBox categories = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly TextBlock status = Text(), account = Text(), context = Text(), selection = Text(), propertyDetail = Text(), connectionStatus = Text(), previewSummary = Text();
        private readonly DataGrid elements = Table(), fields = Table(), preview = Table();
        private readonly ObservableCollection<Editor> editors = new ObservableCollection<Editor>();
        private readonly List<Button> buttons = new List<Button>();
        private readonly CheckBox suggestions = Check("补齐缺失参数并采用本轮自动值", true), skipInvalid = Check("跳过不再适用的草稿，仍保留在本机", false);
        private readonly TabControl centerTabs = new TabControl { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        private readonly ProgressBar progress = new ProgressBar { Height = 4, IsIndeterminate = true, Visibility = Visibility.Collapsed };
        private readonly TextBlock objectCount = MetricValue(), issueCount = MetricValue(), draftCount = MetricValue(), changeCount = MetricValue();
        private readonly Button scanButton, previewButton, writeButton, assignButton, locateButton;
        private CheckResult shownResult;
        private EditPlan shownPlan;
        private Window connectionWindow;
        private bool restoring, loadingValue, closing;

        public WebModelWindow(WebModelSession session)
        {
            this.session = session;
            Title = "HIFC-V1-Web · 模型检查与属性管理";
            Width = Math.Min(1430, SystemParameters.WorkArea.Width); Height = Math.Min(870, SystemParameters.WorkArea.Height);
            MinWidth = 1080; MinHeight = 700; MaxHeight = SystemParameters.WorkArea.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            LiquidGlassTheme.Apply(this);
            foreach (var combo in new[] { kind, slots, filter }) LiquidGlassTheme.Combo(combo);
            foreach (var input in new[] { search, fieldSearch, newValue, phone, name, code }) LiquidGlassTheme.SearchBox(input);
            var root = new Grid { Margin = new Thickness(21, 17, 21, 15), Background = Background };
            foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                root.RowDefinitions.Add(new RowDefinition { Height = height });
            Content = root;

            var header = new DockPanel { Margin = new Thickness(4, 2, 4, 16) };
            var accountArea = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 410 };
            account.HorizontalAlignment = HorizontalAlignment.Right; account.Margin = new Thickness(0, 0, 0, 5); accountArea.Children.Add(account);
            var connectionButton = Action("账号与连接", () => { ShowAccountWindow(); return Task.CompletedTask; });
            connectionButton.HorizontalAlignment = HorizontalAlignment.Right; accountArea.Children.Add(connectionButton);
            DockPanel.SetDock(accountArea, Dock.Right); header.Children.Add(accountArea);
            var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
            titleLine.Children.Add(new Border { Width = 5, Height = 40, CornerRadius = new CornerRadius(3), Background = LiquidGlassTheme.Gradient("#63ADFF", "#2174DD"), Margin = new Thickness(0, 0, 14, 0) });
            var titles = new StackPanel(); titles.Children.Add(new TextBlock { Text = "模型检查与属性管理", FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = LiquidGlassTheme.Color("#16375B") });
            titles.Children.Add(Text("HIFC-V1-Web  ·  " + session.Title, new Thickness(0, 5, 0, 0))); titleLine.Children.Add(titles); header.Children.Add(titleLine); Put(root, header, 0);

            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 13) };
            toolbar.Children.Add(LiquidGlassTheme.Pill("报规")); toolbar.Children.Add(kind);
            scanButton = Action("全面检查", session.ScanAsync, true); toolbar.Children.Add(scanButton);
            previewButton = Action("生成写入预览", async () => { session.IncludeSuggestions = suggestions.IsChecked == true; session.SkipInvalidDrafts = skipInvalid.IsChecked == true; await session.PreviewAsync(); if (session.Plan != null) ShowWritePreview(); }); toolbar.Children.Add(previewButton);
            writeButton = Action("写入并全面复检", session.WriteAsync, true); toolbar.Children.Add(writeButton);
            toolbar.Children.Add(Action("保存当前模型", session.SaveModelAsync));
            kind.Margin = new Thickness(8, 0, 8, 0); context.Margin = new Thickness(10, 0, 0, 0); context.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(context); Put(root, toolbar, 1);

            var metrics = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            var values = new[] { objectCount, issueCount, draftCount, changeCount }; var labels = new[] { "模型对象", "待核对字段", "人工草稿", "预览修改" };
            for (int i = 0; i < values.Length; i++) {
                metrics.ColumnDefinitions.Add(new ColumnDefinition()); var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(values[i]); panel.Children.Add(Text(labels[i], new Thickness(10, 0, 0, 0)));
                var card = LiquidGlassTheme.Card(panel, new Thickness(i == 0 ? 0 : 5, 0, i == 3 ? 0 : 5, 0)); card.Padding = new Thickness(15, 10, 15, 10); Grid.SetColumn(card, i); metrics.Children.Add(card);
            }
            Put(root, metrics, 2);

            var work = new Grid(); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) }); work.ColumnDefinitions.Add(new ColumnDefinition()); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(335) });
            var left = new DockPanel(); var leftTitle = LiquidGlassTheme.Title("模型分类"); leftTitle.Margin = new Thickness(6, 4, 0, 15); DockPanel.SetDock(leftTitle, Dock.Top); left.Children.Add(leftTitle);
            LiquidGlassTheme.Categories(categories); left.Children.Add(categories); var leftCard = LiquidGlassTheme.Card(left, new Thickness(0, 0, 9, 0)); work.Children.Add(leftCard);

            var center = new DockPanel();
            var tableHeading = new DockPanel { Margin = new Thickness(3, 0, 1, 9) };
            var searchHost = SearchHost(search, "搜索 ID / 名称 / 类别 / 楼层"); DockPanel.SetDock(searchHost, Dock.Right); tableHeading.Children.Add(searchHost); tableHeading.Children.Add(LiquidGlassTheme.Title("模型对象列表")); DockPanel.SetDock(tableHeading, Dock.Top); center.Children.Add(tableHeading);
            var objectTools = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; filter.ItemsSource = new[] { "全部对象", "待核对", "有草稿" }; filter.SelectedIndex = 0; objectTools.Children.Add(filter);
            locateButton = Action("在模型中定位", () => session.LocateAsync(elements.SelectedItems.Cast<ElementResult>().Select(row => row.Element.Id).ToArray())); DockPanel.SetDock(locateButton, Dock.Right); objectTools.Children.Add(locateButton);
            selection.Margin = new Thickness(10, 0, 5, 0); selection.VerticalAlignment = VerticalAlignment.Center; objectTools.Children.Add(selection); DockPanel.SetDock(objectTools, Dock.Top); center.Children.Add(objectTools);
            Column(elements, "ID", "Element.Id", 68); Column(elements, "名称", "Element.Name", 180); Column(elements, "类别", "Element.Category", 100); Column(elements, "楼层", "Element.Level", 95); Column(elements, "已有分类", "Element.DeclaredSlot", 130); Column(elements, "检查结果", "Status", 200);
            centerTabs.Items.Add(new TabItem { Header = "模型对象", Content = elements, Padding = new Thickness(14, 7, 14, 7) });
            var planPanel = new DockPanel(); previewSummary.Margin = new Thickness(5, 9, 5, 10); DockPanel.SetDock(previewSummary, Dock.Top); planPanel.Children.Add(previewSummary);
            Column(preview, "构件 ID", "OwnerId", 75); Column(preview, "修改项", "Source", 210); Column(preview, "模型原值", "ExpectedValue", 130); Column(preview, "拟写值", "Value", 140); Column(preview, "范围", "AffectedInstances", 70); Column(preview, "操作", "Action", 75); Column(preview, "依据", "Reason", 230); planPanel.Children.Add(preview);
            centerTabs.Items.Add(new TabItem { Header = "写入预览", Content = planPanel, Padding = new Thickness(14, 7, 14, 7) }); center.Children.Add(centerTabs);
            var centerCard = LiquidGlassTheme.Card(center, new Thickness(0, 0, 9, 0)); Grid.SetColumn(centerCard, 1); work.Children.Add(centerCard);

            var right = new Grid(); foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) right.RowDefinitions.Add(new RowDefinition { Height = height });
            Put(right, LiquidGlassTheme.Title("属性编辑"), 0);
            var classification = new StackPanel { Margin = new Thickness(0, 9, 0, 9) }; slots.ToolTip = "为中间列表中选中的构件指定业务分类"; classification.Children.Add(slots);
            assignButton = Action("设置所选构件分类", async () => {
                if (!(slots.SelectedItem is string slot)) throw new InvalidOperationException("请先选择业务分类。");
                var selected = elements.SelectedItems.Cast<ElementResult>().ToArray(); if (selected.Length == 0) throw new InvalidOperationException("请先选择构件。");
                foreach (var row in selected) session.Assign(row.Element.UniqueId, slot); await session.ScanAsync();
            }); assignButton.Margin = new Thickness(0, 7, 0, 0); classification.Children.Add(assignButton);
            Put(right, new Expander { Header = "调整业务分类", Content = classification, Margin = new Thickness(0, 12, 0, 10) }, 1);
            Put(right, SearchHost(fieldSearch, "查找属性名称 / 来源"), 2);
            Column(fields, "属性", "Field.Property", 135); Column(fields, "当前模型值", "Current", 150); fields.ItemsSource = editors; fields.Margin = new Thickness(0, 9, 0, 7); Put(right, fields, 3);
            var editArea = new StackPanel(); propertyDetail.FontSize = 11; propertyDetail.MaxHeight = 90; editArea.Children.Add(propertyDetail);
            editArea.Children.Add(Text("拟写值 · 自动保留为本机草稿", new Thickness(0, 8, 0, 5)));
            newValue.ToolTip = "数值按当前项目单位输入；可附带单位。类型字段会影响同类型实例。"; editArea.Children.Add(SearchHost(newValue, "选中属性后输入拟写值"));
            var draftActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 9) };
            draftActions.Children.Add(Action("采用模型值", () => Resolve(false))); draftActions.Children.Add(Action("确认保留草稿", () => Resolve(true))); editArea.Children.Add(draftActions);
            editArea.Children.Add(suggestions); editArea.Children.Add(skipInvalid); Put(right, editArea, 4);
            var rightCard = LiquidGlassTheme.Card(right, new Thickness(0)); Grid.SetColumn(rightCard, 2); work.Children.Add(rightCard); Put(root, work, 3);

            var footer = new StackPanel { Margin = new Thickness(5, 12, 5, 0) }; footer.Children.Add(progress); status.Margin = new Thickness(0, 6, 0, 0); footer.Children.Add(status); Put(root, footer, 4);
            kind.ItemsSource = new[] { "自动识别", "总图", "单体建筑" }; kind.SelectedItem = session.Workspace.ModelKind;
            kind.SelectionChanged += (s, e) => { if (!restoring && !session.Busy && kind.SelectedItem is string selected) session.SelectModelKind(selected); };
            categories.SelectionChanged += (s, e) => { if (!restoring) RefreshRows(); };
            filter.SelectionChanged += (s, e) => RefreshRows(); search.TextChanged += (s, e) => RefreshRows();
            elements.SelectionChanged += (s, e) => { if (!restoring) RebuildEditors(); }; fieldSearch.TextChanged += (s, e) => RebuildEditors();
            fields.SelectionChanged += (s, e) => LoadSelectedField();
            newValue.TextChanged += (s, e) => { if (!loadingValue && !session.Busy && fields.SelectedItem is Editor editor && editor.Editable) editor.Input = newValue.Text; };
            suggestions.Checked += InvalidateOptions; suggestions.Unchecked += InvalidateOptions; skipInvalid.Checked += InvalidateOptions; skipInvalid.Unchecked += InvalidateOptions;
            phone.Text = session.ActivePhone ?? ""; session.Changed += Refresh; Refresh();
            Loaded += async (s, e) => { if (!string.IsNullOrEmpty(phone.Text)) await session.RunAsync(() => session.ConnectAsync(phone.Text, "", false)); };
            Closing += (s, e) => {
                if (session.Busy) { e.Cancel = true; return; }
                try { session.Flush(); } catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, "草稿暂未保存，请保留窗口并重试。\n" + ex.Message, "HIFC Web"); return; }
                closing = true; connectionWindow?.Close(); session.Changed -= Refresh; session.Dispose();
            };
        }

        private static TextBlock Text(string value = "", Thickness? margin = null) => new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Foreground = LiquidGlassTheme.Color("#617B99"), Margin = margin ?? new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        private static TextBlock MetricValue() => new TextBlock { Text = "—", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = LiquidGlassTheme.Color("#205EAB"), VerticalAlignment = VerticalAlignment.Center };
        private static CheckBox Check(string label, bool value) => new CheckBox { Content = Text(label), IsChecked = value, Margin = new Thickness(0, 4, 0, 4), VerticalContentAlignment = VerticalAlignment.Center };
        private static DataGrid Table() { var table = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow, EnableRowVirtualization = true, EnableColumnVirtualization = true }; LiquidGlassTheme.Table(table); return table; }
        private static void Column(DataGrid table, string title, string property, double width) => table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        private static void Put(Grid parent, UIElement child, int row) { Grid.SetRow(child, row); parent.Children.Add(child); }
        private static Border SearchHost(TextBox input, string hint) {
            var host = new Grid(); host.Children.Add(input);
            var watermark = Text(hint, new Thickness(8, 0, 7, 0)); watermark.IsHitTestVisible = false; watermark.TextWrapping = TextWrapping.NoWrap; watermark.TextTrimming = TextTrimming.CharacterEllipsis; host.Children.Add(watermark);
            input.TextChanged += (s, e) => watermark.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            return new Border { Child = host, Background = Brushes.White, BorderBrush = LiquidGlassTheme.Color("#D7E4F1"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
        }
        private Button Action(string title, Func<Task> action, bool primary = false) {
            var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 0) };
            if (primary) LiquidGlassTheme.Primary(button);
            button.Click += async (s, e) => await session.RunAsync(action); buttons.Add(button); return button;
        }
        private Task Resolve(bool keep) { if (fields.SelectedItem is Editor editor) { var key = editor.Field.Key; session.ResolveDraft(editor.Field, keep); RebuildEditors(); SelectField(key); } return Task.CompletedTask; }
        private void InvalidateOptions(object sender, RoutedEventArgs e) { session.IncludeSuggestions = suggestions.IsChecked == true; session.SkipInvalidDrafts = skipInvalid.IsChecked == true; session.InvalidatePlan(); }

        public void ShowAccountWindow()
        {
            if (connectionWindow == null) {
                connectionWindow = new Window { Title = "HIFC-V1-Web · 账号与连接", Width = 500, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, MaxHeight = SystemParameters.WorkArea.Height };
                LiquidGlassTheme.Apply(connectionWindow); var form = new StackPanel { Margin = new Thickness(24) };
                form.Children.Add(LiquidGlassTheme.Title("登记与连接")); form.Children.Add(Text("首次登记填写手机号和真实姓名，后续在当前电脑自动登录。", new Thickness(0, 7, 0, 10)));
                Input(form, "手机号", phone); Input(form, "真实姓名（首次登记）", name);
                var connect = Action("登记 / 自动登录", async () => { await session.ConnectAsync(phone.Text, name.Text, true); if (session.Account != null) connectionWindow.Hide(); }, true); connect.Margin = new Thickness(0, 14, 0, 8); form.Children.Add(connect);
                var rebind = new StackPanel(); Input(rebind, "管理员提供的一次性绑定码", code); var bind = Action("绑定当前电脑", async () => { await session.ConnectAsync(phone.Text, name.Text, false, code.Text); code.Clear(); if (session.Account != null) connectionWindow.Hide(); }); bind.Margin = new Thickness(0, 12, 0, 0); rebind.Children.Add(bind);
                form.Children.Add(new Expander { Header = "已有账号 / 更换电脑", Content = rebind, Margin = new Thickness(0, 12, 0, 12) });
                form.Children.Add(Text("账号初始有效 168 小时；换机保留原期限。到期后仍可查看和编辑本机草稿，延期请联系管理员。", new Thickness(0, 4, 0, 14))); form.Children.Add(connectionStatus);
                connectionWindow.Content = form; connectionWindow.Closing += (s, e) => { if (!closing) { e.Cancel = true; connectionWindow.Hide(); } };
            }
            Refresh(); connectionWindow.Show(); connectionWindow.Activate();
        }
        private static void Input(StackPanel form, string label, TextBox input) { form.Children.Add(Text(label, new Thickness(0, 9, 0, 5))); form.Children.Add(SearchHost(input, "")); }
        public void ShowWritePreview() { centerTabs.SelectedIndex = 1; UpdateLayout(); }
        public void ShowObjects() { centerTabs.SelectedIndex = 0; UpdateLayout(); }
        public void SelectElement(int id) { categories.SelectedIndex = 0; filter.SelectedIndex = 0; search.Clear(); var row = elements.Items.Cast<ElementResult>().FirstOrDefault(item => item.Element.Id == id); elements.SelectedItem = row; if (row != null) elements.ScrollIntoView(row); }
        public void SelectField(string key) { fieldSearch.Clear(); fields.SelectedItem = editors.FirstOrDefault(editor => editor.Field.Key == key); if (fields.SelectedItem != null) fields.ScrollIntoView(fields.SelectedItem); }
        public void EditSelectedValue(string value) { if (!(fields.SelectedItem is Editor editor) || !editor.Editable) throw new InvalidOperationException("请先选择可编辑属性。"); newValue.Text = value; }

        private void Refresh()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Refresh)); return; }
            status.Text = connectionStatus.Text = session.Status; progress.Visibility = session.Busy ? Visibility.Visible : Visibility.Collapsed;
            account.Text = session.Account == null ? "尚未连接 · 点击“账号与连接”开始" : session.Account.RealName + "  ·  " + (session.Account.IsActive ? "有效至 " + new DateTimeOffset(session.Account.ExpiresAtUtc).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm") : "已到期，请联系管理员延期");
            if (session.Account != null) name.Text = session.Account.RealName;
            context.Text = session.Result == null ? "等待全面检查" : session.Result.ModelKind + (session.LiveCheck ? " · 本轮检查" : " · 待重新检查");
            objectCount.Text = session.Result?.Elements.Length.ToString() ?? "—";
            issueCount.Text = session.Result?.Fields.Count(Problem).ToString() ?? "—"; draftCount.Text = session.Workspace.Drafts.Length.ToString(); changeCount.Text = session.Plan?.Changes.Length.ToString() ?? "—";
            foreach (var button in buttons) button.IsEnabled = !session.Busy;
            var active = session.Account?.IsActive == true;
            scanButton.IsEnabled = !session.Busy && active; previewButton.IsEnabled = !session.Busy && active && session.Result != null;
            writeButton.IsEnabled = !session.Busy && active && session.Plan?.Changes.Length > 0;
            assignButton.IsEnabled = !session.Busy && active && elements.SelectedItems.Count > 0 && slots.Items.Count > 0;
            locateButton.IsEnabled = !session.Busy && elements.SelectedItems.Count > 0;
            fields.IsEnabled = elements.IsEnabled = categories.IsEnabled = kind.IsEnabled = slots.IsEnabled = suggestions.IsEnabled = skipInvalid.IsEnabled = !session.Busy;
            phone.IsEnabled = name.IsEnabled = code.IsEnabled = !session.Busy;
            if (!ReferenceEquals(shownResult, session.Result)) {
                var selectedSlot = (categories.SelectedItem as Category)?.Role.Slot; shownResult = session.Result; restoring = true;
                var rows = shownResult?.Elements ?? new ElementResult[0]; var choices = new List<Category> { new Category("全部对象", rows.Length, "当前文档") };
                if (shownResult != null) { choices.AddRange(shownResult.AvailableSlots.Select(slot => new Category(slot, rows.Count(row => InSlot(row, slot)), "业务分类"))); choices.Add(new Category("待分类", rows.Count(row => row.Slots.Length == 0), "需核对分类")); }
                categories.ItemsSource = choices; categories.SelectedItem = choices.FirstOrDefault(choice => choice.Role.Slot == selectedSlot) ?? choices[0]; slots.ItemsSource = shownResult?.AvailableSlots; restoring = false; RefreshRows();
            }
            if (categories.Items.Count == 0) { categories.ItemsSource = new[] { new Category("全部对象", 0, "请先全面检查") }; categories.SelectedIndex = 0; }
            if (!ReferenceEquals(shownPlan, session.Plan)) { shownPlan = session.Plan; preview.ItemsSource = shownPlan?.Changes; }
            previewSummary.Text = shownPlan == null ? "尚未生成预览。修改草稿后，请点击“生成写入预览”。" : shownPlan.Changes.Length + " 项修改 · 核对原值、拟写值与影响范围后，点击“写入并全面复检”。";
            RefreshFieldDetail();
        }
        private static bool Problem(FieldResult field) => field.State != "有效" && !(field.AllowedEmpty && field.State == "未填写");
        private static bool InSlot(ElementResult row, string slot) => row.Slots.Contains(slot) || row.Element.DeclaredSlot == slot;
        private void RefreshRows()
        {
            if (restoring) return; var selected = elements.SelectedItems.Cast<ElementResult>().Select(row => row.Element.Id).ToArray(); var slot = (categories.SelectedItem as Category)?.Role.Slot; var query = search.Text.Trim(); var mode = filter.SelectedIndex;
            var rows = (session.Result?.Elements ?? new ElementResult[0]).Where(row => (slot == null || slot == "全部对象" || (slot == "待分类" ? row.Slots.Length == 0 : InSlot(row, slot)))
                && (query.Length == 0 || (row.Element.Id + " " + row.Element.Name + " " + row.Element.Category + " " + row.Element.Level + " " + string.Join(" ", row.Slots)).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                && (mode != 1 || row.Slots.Length == 0 || session.Result.Fields.Any(field => field.ElementId == row.Element.Id && Problem(field)))
                && (mode != 2 || session.Result.Fields.Any(field => field.ElementId == row.Element.Id && session.Workspace.Drafts.Any(draft => draft.FieldKey == field.Key)))).ToArray();
            restoring = true; elements.ItemsSource = rows; foreach (var row in rows.Where(row => selected.Contains(row.Element.Id))) elements.SelectedItems.Add(row);
            if (elements.SelectedItems.Count == 0 && rows.Length > 0) elements.SelectedIndex = 0; restoring = false; RebuildEditors();
        }
        private void RebuildEditors()
        {
            var key = (fields.SelectedItem as Editor)?.Field.Key; var selected = elements.SelectedItems.Cast<ElementResult>().Select(row => row.Element.Id).ToArray(); var query = fieldSearch.Text.Trim(); editors.Clear();
            if (session.Result != null) foreach (var field in session.Result.Fields.Where(field => selected.Contains(field.ElementId) && (query == "" || (field.Property + " " + field.Source).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))) editors.Add(new Editor(session, field));
            selection.Text = "可见 " + elements.Items.Count + " 项 · 已选 " + selected.Length + " 项";
            fields.SelectedItem = editors.FirstOrDefault(editor => editor.Field.Key == key) ?? editors.FirstOrDefault();
            assignButton.IsEnabled = !session.Busy && session.Account?.IsActive == true && selected.Length > 0 && slots.Items.Count > 0;
            locateButton.IsEnabled = !session.Busy && selected.Length > 0; LoadSelectedField();
        }
        private void LoadSelectedField()
        {
            loadingValue = true; newValue.Text = (fields.SelectedItem as Editor)?.Input ?? ""; loadingValue = false; RefreshFieldDetail();
        }
        private void RefreshFieldDetail()
        {
            var editor = fields.SelectedItem as Editor; newValue.IsEnabled = !session.Busy && editor?.Editable == true;
            propertyDetail.Text = editor == null ? "在中间列表选中构件，再选择需要编辑的属性。" : editor.Field.Property + " · " + editor.Scope + (editor.Unit == "" ? "" : " · " + editor.Unit) + "\n" + editor.State + (editor.Original == "" ? "" : " · 草稿原值：" + editor.Original) + "\n" + editor.Field.Detail;
            propertyDetail.ToolTip = propertyDetail.Text;
        }

        public sealed class Category
        {
            public CategoryRole Role { get; }
            public string Subtitle { get; }
            public Brush Foreground => LiquidGlassTheme.Color("#28496D");
            public Category(string slot, int count, string detail) { Role = new CategoryRole { Slot = slot }; Subtitle = count + " 项 · " + detail; }
        }
        public sealed class CategoryRole { public string Slot { get; set; } }
        public sealed class Editor : INotifyPropertyChanged
        {
            private readonly WebModelSession session; private string input;
            public FieldResult Field { get; }
            public string Current => Field.DisplayValue ?? Field.Value ?? "（" + Field.State + "）";
            public string Original => session.Workspace.Drafts.SingleOrDefault(draft => draft.FieldKey == Field.Key) is EditDraft draft ? draft.OriginalValue ?? "（原未填写）" : "";
            public bool Editable => Field.Editable;
            public string Scope => Field.Binding == "类型" ? "同类型 " + Field.AffectedInstances + " 项" : "当前构件";
            public string Unit => Field.SuggestionUnit == "internal" ? "项目单位" : Field.SuggestionUnit ?? (Field.RawType == "Boolean" ? "是 / 否" : "");
            public string State => session.IsConflict(Field.Key) ? "原值变化，需确认" : Field.ManualInputRequired && string.IsNullOrWhiteSpace(input) ? "道路宽度待手填" : Field.State;
            public string Input { get => input; set { if (input == value) return; input = value; session.SetDraft(Field, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Input))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State))); } }
            public event PropertyChangedEventHandler PropertyChanged;
            public Editor(WebModelSession session, FieldResult field) { this.session = session; Field = field; input = session.Workspace.Drafts.SingleOrDefault(draft => draft.FieldKey == field.Key)?.Value ?? field.DisplayValue ?? field.Value ?? ""; }
        }
    }
}
