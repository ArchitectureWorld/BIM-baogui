using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public sealed class WebModelWindow : Window
    {
        private readonly WebModelSession session;
        private readonly TextBox phone = new TextBox(), name = new TextBox(), code = new TextBox();
        private readonly TextBox search = new TextBox { MinWidth = 180 };
        private readonly ComboBox kind = new ComboBox { MinWidth = 130 }, slots = new ComboBox { MinWidth = 200 };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap }, account = new TextBlock(), summary = new TextBlock();
        private readonly DataGrid elements = Grid(), fields = Grid(), preview = Grid();
        private readonly ObservableCollection<Editor> editors = new ObservableCollection<Editor>();
        private readonly System.Collections.Generic.List<Button> buttons = new System.Collections.Generic.List<Button>();
        private readonly CheckBox suggestions = new CheckBox { Content = "补齐缺失参数并采用本轮自动值", IsChecked = true, Margin = new Thickness(8) };
        private readonly CheckBox skipInvalid = new CheckBox { Content = "本轮跳过不再适用的草稿，仍保留在本机", Margin = new Thickness(8) };
        private readonly TabControl tabs = new TabControl();
        private CheckResult shownResult;
        private EditPlan shownPlan;
        public WebModelWindow(WebModelSession session)
        {
            this.session = session; Title = "HIFC 0.5.0-Web · " + session.Title; Width = 1320; Height = 870; MinWidth = 1000; MinHeight = 680;
            Background = Brush("#F2F5F8"); Foreground = Brush("#18324C"); FontFamily = new FontFamily("Microsoft YaHei"); FontSize = 13;
            var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            header.Children.Add(new TextBlock { Text = "HIFC 0.5.0-Web", FontSize = 25, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = session.Title + "  ·  报规模型属性管理", Margin = new Thickness(0, 5, 0, 8), Foreground = Brush("#60758A") });
            header.Children.Add(account); header.Children.Add(summary);
            var footer = new Border { Background = Brush("#E5EEF6"), Padding = new Thickness(14), Margin = new Thickness(0, 14, 0, 0), CornerRadius = new CornerRadius(7), Child = status };
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
            BuildConnection(); BuildInventory(); BuildFields(); BuildPreview();
            kind.ItemsSource = new[] { "自动识别", "总图", "单体建筑" }; kind.SelectedItem = session.Workspace.ModelKind;
            kind.SelectionChanged += (s, e) => { if (!session.Busy && kind.SelectedItem is string selected) session.SelectModelKind(selected); };
            elements.SelectionChanged += (s, e) => RebuildEditors(); search.TextChanged += (s, e) => RebuildEditors();
            phone.Text = session.ActivePhone ?? ""; session.Changed += Refresh; Refresh();
            Loaded += async (s, e) => { if (!string.IsNullOrEmpty(phone.Text)) await session.RunAsync(() => session.ConnectAsync(phone.Text, "", false)); };
            Closing += (s, e) => {
                try { Commit(); session.Flush(); } catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, "草稿暂未保存，请保留窗口并重试。\n" + ex.Message, "HIFC Web"); return; }
                session.Changed -= Refresh; session.Dispose();
            };
        }
        private static SolidColorBrush Brush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        private static DataGrid Grid() => new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, RowHeaderWidth = 0, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(218, 227, 236)), RowHeight = 34, SelectionMode = DataGridSelectionMode.Extended, Margin = new Thickness(8) };
        private static void Column(DataGrid grid, string label, string property, double width = 130, bool editable = false)
        {
            grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property) { Mode = editable ? BindingMode.TwoWay : BindingMode.OneWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = width, IsReadOnly = !editable });
        }
        private DockPanel Page(string title, string detail)
        {
            var panel = new DockPanel { Margin = new Thickness(10), LastChildFill = true };
            var header = new StackPanel { Margin = new Thickness(8) }; DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
            header.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 7) });
            header.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#60758A") });
            return panel;
        }
        private void Tab(string header, UIElement content) => tabs.Items.Add(new TabItem { Header = header, Content = content, Padding = new Thickness(20, 11, 20, 11) });
        private Button Action(string title, Func<Task> action)
        {
            var button = new Button { Content = title, Padding = new Thickness(15, 8, 15, 8), Margin = new Thickness(7), Background = Brush("#245E8F"), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            button.Click += async (s, e) => { Commit(); await session.RunAsync(action); }; buttons.Add(button); return button;
        }
        private static void Input(StackPanel panel, string label, TextBox input)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 15, 0, 5) }); input.Padding = new Thickness(10, 7, 10, 7); panel.Children.Add(input);
        }
        private void BuildConnection()
        {
            var page = Page("连接与登记", "首次登记填写手机号和真实姓名即可。账号默认有效 168 小时，重装或换机不会重置期限。");
            var form = new StackPanel { MaxWidth = 500, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(15), MinWidth = 380 };
            Input(form, "手机号", phone); Input(form, "真实姓名（首次登记）", name);
            var actions = new WrapPanel(); actions.Children.Add(Action("登记 / 自动登录", () => session.ConnectAsync(phone.Text, name.Text, true))); form.Children.Add(actions);
            var rebind = new StackPanel(); Input(rebind, "管理员提供的一次性绑定码", code); rebind.Children.Add(Action("绑定当前电脑", () => session.ConnectAsync(phone.Text, name.Text, false, code.Text)));
            form.Children.Add(new Expander { Header = "已有账号 / 更换电脑", Content = rebind, Margin = new Thickness(0, 20, 0, 0) });
            form.Children.Add(new TextBlock { Text = "本机自动保存登录凭证。到期后仍可查看和编辑已有草稿；请通过既有联系渠道向管理员申请延期。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 22, 0, 0), Foreground = Brush("#60758A") });
            page.Children.Add(form); Tab("1 连接与登记", page);
        }
        private void BuildInventory()
        {
            var page = Page("全面检查", "每次重新盘点整个当前文档。类型来源会显示影响范围；链接模型需要在源文档单独检查。");
            var tools = new WrapPanel(); DockPanel.SetDock(tools, Dock.Top); page.Children.Add(tools);
            tools.Children.Add(new TextBlock { Text = "模型类型", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) }); tools.Children.Add(kind);
            tools.Children.Add(Action("开始全面检查", session.ScanAsync)); tools.Children.Add(slots);
            tools.Children.Add(Action("设置所选构件分类", async () => {
                if (!(slots.SelectedItem is string slot)) throw new InvalidOperationException("请先选择业务分类。");
                var selected = elements.SelectedItems.Cast<ElementResult>().ToArray(); if (selected.Length == 0) throw new InvalidOperationException("请先选择需要分类的构件。");
                foreach (var row in selected) session.Assign(row.Element.UniqueId, slot); await session.ScanAsync();
            }));
            tools.Children.Add(Action("在模型中定位", () => session.LocateAsync(elements.SelectedItems.Cast<ElementResult>().Select(r => r.Element.Id).ToArray())));
            Column(elements, "构件 ID", "Element.Id", 85); Column(elements, "名称", "Element.Name", 260); Column(elements, "Revit 类别", "Element.Category", 150);
            Column(elements, "楼层", "Element.Level", 150); Column(elements, "已有分类", "Element.DeclaredSlot", 180); Column(elements, "检查结果", "Status", 300);
            page.Children.Add(elements); Tab("2 全面检查", page);
        }
        private void BuildFields()
        {
            var page = Page("属性编辑", "修改立即保留为本机草稿。数值按当前项目单位输入，可附带单位；类型值会影响同类型实例。只对道路宽度提示手填。");
            var tools = new WrapPanel(); DockPanel.SetDock(tools, Dock.Top); page.Children.Add(tools);
            tools.Children.Add(new TextBlock { Text = "查找字段 / 构件", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) }); tools.Children.Add(search);
            tools.Children.Add(Action("保留所选草稿并确认原值变化", () => { foreach (var row in fields.SelectedItems.Cast<Editor>().ToArray()) session.ResolveDraft(row.Field, true); RebuildEditors(); return Task.CompletedTask; }));
            tools.Children.Add(Action("所选字段采用模型现值", () => { foreach (var row in fields.SelectedItems.Cast<Editor>().ToArray()) session.ResolveDraft(row.Field, false); RebuildEditors(); return Task.CompletedTask; }));
            var bottom = new WrapPanel(); DockPanel.SetDock(bottom, Dock.Bottom); page.Children.Add(bottom); bottom.Children.Add(suggestions); bottom.Children.Add(skipInvalid);
            bottom.Children.Add(Action("生成写入预览", async () => { session.IncludeSuggestions = suggestions.IsChecked == true; session.SkipInvalidDrafts = skipInvalid.IsChecked == true; await session.PreviewAsync(); tabs.SelectedIndex = 3; }));
            fields.IsReadOnly = false;
            Column(fields, "构件", "Element", 180); Column(fields, "字段", "Field.Property", 150); Column(fields, "草稿开始时的原值", "Original", 160); Column(fields, "当前模型值", "Current", 170); Column(fields, "拟写值", "Input", 190, true);
            var inputStyle = new Style(typeof(DataGridCell)); var readOnly = new DataTrigger { Binding = new Binding("Editable"), Value = false }; readOnly.Setters.Add(new Setter(IsEnabledProperty, false)); inputStyle.Triggers.Add(readOnly); fields.Columns[4].CellStyle = inputStyle;
            Column(fields, "范围", "Scope", 120); Column(fields, "单位 / 输入", "Unit", 110); Column(fields, "状态", "State", 170); Column(fields, "说明", "Field.Detail", 260);
            fields.ItemsSource = editors; page.Children.Add(fields); Tab("3 编辑与预览", page);
        }
        private void BuildPreview()
        {
            var page = Page("核对写入与复检", "确认拟修改内容后写入。全部修改在一个事务组中完成并回读；失败整体回滚，写后重新全面检查。保存模型由你决定。");
            var tools = new WrapPanel(); DockPanel.SetDock(tools, Dock.Top); page.Children.Add(tools);
            tools.Children.Add(Action("刷新写入预览", async () => { session.IncludeSuggestions = suggestions.IsChecked == true; session.SkipInvalidDrafts = skipInvalid.IsChecked == true; await session.PreviewAsync(); }));
            tools.Children.Add(Action("写入并全面复检", session.WriteAsync)); tools.Children.Add(Action("重新全面检查", session.ScanAsync)); tools.Children.Add(Action("保存当前模型", session.SaveModelAsync));
            Column(preview, "构件 ID", "OwnerId", 90); Column(preview, "修改项", "Source", 240); Column(preview, "模型原值", "ExpectedValue", 190);
            Column(preview, "拟写值", "Value", 190); Column(preview, "操作", "Action", 85); Column(preview, "影响实例数", "AffectedInstances", 110); Column(preview, "依据", "Reason", 300);
            page.Children.Add(preview); Tab("4 写入与复检", page);
        }
        private void Commit() { fields.CommitEdit(DataGridEditingUnit.Cell, true); fields.CommitEdit(DataGridEditingUnit.Row, true); }
        private void Refresh()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Refresh)); return; }
            status.Text = session.Status;
            account.Text = session.Account == null ? "尚未登录 · 已有草稿可离线编辑" : session.Account.RealName + "  ·  " + (session.Account.IsAdmin ? "管理员" : session.Account.IsActive ? "有效至 " + new DateTimeOffset(session.Account.ExpiresAtUtc).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm") + "（北京时间）" : "已到期，请联系管理员延期");
            if (session.Account != null) name.Text = session.Account.RealName;
            summary.Text = session.Result == null ? "尚未全面检查" : session.Result.ModelKind + "  ·  " + session.Result.Elements.Length + " 个模型记录  ·  待核对字段 " + session.Result.Fields.Count(f => f.State != "有效" && !(f.AllowedEmpty && f.State == "未填写")) + "  ·  人工草稿 " + session.Workspace.Drafts.Length + (session.ConflictCount > 0 ? "  ·  原值冲突 " + session.ConflictCount : "") + (session.InvalidDraftCount > 0 ? "  ·  不再适用草稿 " + session.InvalidDraftCount : "");
            foreach (var button in buttons) button.IsEnabled = !session.Busy; fields.IsEnabled = !session.Busy; elements.IsEnabled = !session.Busy; kind.IsEnabled = !session.Busy; phone.IsEnabled = name.IsEnabled = code.IsEnabled = !session.Busy;
            if (!ReferenceEquals(shownResult, session.Result)) { shownResult = session.Result; elements.ItemsSource = shownResult?.Elements; slots.ItemsSource = shownResult?.AvailableSlots; RebuildEditors(); }
            if (!ReferenceEquals(shownPlan, session.Plan)) { shownPlan = session.Plan; preview.ItemsSource = shownPlan?.Changes; }
        }
        private void RebuildEditors()
        {
            if (session.Result == null) return; var selected = elements.SelectedItems.Cast<ElementResult>().Select(r => r.Element.Id).ToArray(); var query = search.Text.Trim(); editors.Clear();
            foreach (var field in session.Result.Fields.Where(f => (selected.Length == 0 || selected.Contains(f.ElementId)) && (query == "" || (f.ElementName + f.Property + f.Source + f.ElementId).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
                editors.Add(new Editor(session, field));
        }
        public sealed class Editor : INotifyPropertyChanged
        {
            private readonly WebModelSession session; private string input;
            public FieldResult Field { get; }
            public string Element => Field.ElementName + " · " + Field.ElementId;
            public string Current => Field.DisplayValue ?? Field.Value ?? "（" + Field.State + "）";
            public string Original => session.Workspace.Drafts.SingleOrDefault(d => d.FieldKey == Field.Key) is EditDraft draft ? draft.OriginalValue ?? "（原未填写）" : "";
            public bool Editable => Field.Editable;
            public string Scope => Field.Binding == "类型" ? "同类型 " + Field.AffectedInstances + " 项" : "当前构件";
            public string Unit => Field.SuggestionUnit == "internal" ? "项目单位" : Field.SuggestionUnit ?? (Field.RawType == "Boolean" ? "是 / 否" : "");
            public string State => session.IsConflict(Field.Key) ? "原值变化，需确认" : Field.ManualInputRequired && string.IsNullOrWhiteSpace(input) ? "道路宽度待手填" : Field.State;
            public string Input { get => input; set { if (input == value) return; input = value; session.SetDraft(Field, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Input))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State))); } }
            public event PropertyChangedEventHandler PropertyChanged;
            public Editor(WebModelSession session, FieldResult field) { this.session = session; Field = field; input = session.Workspace.Drafts.SingleOrDefault(d => d.FieldKey == field.Key)?.Value ?? field.DisplayValue ?? field.Value ?? ""; }
        }
    }
}
