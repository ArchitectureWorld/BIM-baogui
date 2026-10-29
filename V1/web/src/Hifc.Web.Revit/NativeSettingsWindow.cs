using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hifc.Revit.Management;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    internal sealed class NativeSettingsWindow : NativeDialogWindow
    {
        private readonly ModelSettings previous;
        private readonly ComboBox profile = new ComboBox { DisplayMemberPath = "Title", SelectedValuePath = "Id", MinWidth = 300 };
        private readonly ComboBox top = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "UniqueId", MinWidth = 240 };
        private readonly CheckBox manualLevels = new CheckBox { Content = "手工指定实际建筑楼层" };
        private readonly TextBlock error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly RoleRow[] roleRows;
        private readonly LevelRow[] levelRows;
        private readonly CommonRow[] commonRows;
        private readonly DataGrid roles, levels, values;
        internal ModelSettings Result { get; private set; }
        internal NativeSettingsWindow(CheckResult result, ModelSettings settings, Func<int[], System.Threading.Tasks.Task> locate)
        {
            previous = JsonWire.Deserialize<ModelSettings>(JsonWire.Serialize(settings ?? new ModelSettings()));
            Title = "报规计算设置"; Width = 1020; Height = 710; MinWidth = 780; MinHeight = 530; WindowStartupLocation = WindowStartupLocation.CenterOwner; LiquidGlassTheme.Apply(this);
            var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            header.Children.Add(LiquidGlassTheme.Title("计算范围、项目共用值与实际楼层"));
            profile.ItemsSource = result.RuleProfiles; profile.SelectedValue = previous.RuleProfileId ?? ""; NativeUi.NamedCombo(profile, "Title"); header.Children.Add(profile);
            var scope = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) }; header.Children.Add(scope);
            header.Children.Add(new TextBlock { Text = "本窗口先暂存设置并重新检查；经写入预览和“写入并全面复检”后，设置与属性同事务写入模型。保存 RVT 后，其他电脑可恢复这些设置。已有人工属性值保留。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; footer.Children.Add(buttons);
            buttons.Children.Add(new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) }); var save = new Button { Content = "暂存设置并全面检查" }; LiquidGlassTheme.Primary(save); buttons.Children.Add(save);
            var tabs = new TabControl { BorderThickness = new Thickness(0), Background = Background }; NativeUi.Tabs(tabs); root.Children.Add(tabs);
            roleRows = result.Elements.Where(e => string.IsNullOrWhiteSpace(e.Element.DeclaredSlot) && string.IsNullOrWhiteSpace(e.Element.AssignedSlot) && e.CandidateSlots.Length > 0).Select(e => new RoleRow {
                Element = e, Slot = previous.Roles.FirstOrDefault(r => r.UniqueId == e.Element.UniqueId && r.Identity == e.Identity)?.Slot ?? e.CandidateSlots[0],
                Included = previous.Roles.Any(r => r.UniqueId == e.Element.UniqueId && r.Identity == e.Identity), Choices = e.CandidateSlots
            }).ToArray();
            roles = Table(roleRows); roles.Columns.Add(CheckColumn("确认用途", "Included", 85)); Column(roles, "ID", "Element.Element.Id", 75); Column(roles, "名称", "Element.Element.Name", 170);
            var roleEditor = new FrameworkElementFactory(typeof(ComboBox)); roleEditor.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Choices")); roleEditor.SetBinding(ComboBox.SelectedItemProperty, new Binding("Slot") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            roles.Columns.Add(new DataGridTemplateColumn { Header = "拟定类目", CellTemplate = new DataTemplate { VisualTree = roleEditor }, Width = 160 });
            Column(roles, "所属楼层", "Element.Element.Level", 100); Column(roles, "投影面积 m²", "Element.Element.ProjectedAreaM2", 120); Column(roles, "依据 / 提示", "Evidence", 300);
            var rolePanel = new DockPanel(); var locateButton = new Button { Content = "定位选中对象", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 10) };
            locateButton.Click += async (s, e) => { try { await locate(roles.SelectedItems.Cast<RoleRow>().Select(r => r.Element.Element.Id).ToArray()); } catch (Exception ex) { error.Text = ex.Message; } };
            DockPanel.SetDock(locateButton, Dock.Top); rolePanel.Children.Add(locateButton); rolePanel.Children.Add(roles); tabs.Items.Add(new TabItem { Header = "计算范围", Content = rolePanel });
            commonRows = result.CommonFields.Select(f => new CommonRow { Field = f, Value = previous.CommonValues.TryGetValue(f.Slot + "|" + f.Property, out var value) ? value : "" }).ToArray();
            values = Table(commonRows); Column(values, "应用类目", "Field.Slot", 170); Column(values, "共用属性", "Field.Property", 190);
            var editor = new FrameworkElementFactory(typeof(ComboBox)); editor.SetBinding(ComboBox.IsEditableProperty, new Binding("CanType")); editor.SetValue(ComboBox.IsTextSearchEnabledProperty, false);
            editor.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Choices")); editor.SetBinding(ComboBox.TextProperty, new Binding("EditorValue") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            values.Columns.Add(new DataGridTemplateColumn { Header = "统一补齐值（仅填空值，可输入）", CellTemplate = new DataTemplate { VisualTree = editor }, MinWidth = 320, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            tabs.Items.Add(new TabItem { Header = "项目共用值", Content = values });
            levelRows = result.Elements.Where(e => e.Element.Entity == "IfcBuildingStorey").OrderBy(e => e.Element.LevelElevationM).Select(e => new LevelRow { UniqueId = e.Element.UniqueId, Name = e.Element.Name, ElevationM = e.Element.LevelElevationM, Included = previous.BuildingLevelUniqueIds.Contains(e.Element.UniqueId) }).ToArray();
            levels = Table(levelRows); levels.Columns.Add(CheckColumn("实际楼层", "Included", 95)); Column(levels, "楼层 / 标高", "Name", 330); Column(levels, "项目标高 m", "ElevationM", 170);
            manualLevels.IsChecked = previous.LevelsConfirmed; levels.IsEnabled = previous.LevelsConfirmed; manualLevels.Margin = new Thickness(0, 10, 0, 12);
            manualLevels.Checked += (s, e) => levels.IsEnabled = true; manualLevels.Unchecked += (s, e) => levels.IsEnabled = false;
            top.ItemsSource = new[] { new LevelRow { UniqueId = "", Name = "未确认最高层上边界" } }.Concat(levelRows); top.SelectedValue = previous.TopBoundaryLevelUniqueId ?? ""; NativeUi.NamedCombo(top, "Name");
            var levelPanel = new DockPanel(); DockPanel.SetDock(manualLevels, Dock.Top); levelPanel.Children.Add(manualLevels); DockPanel.SetDock(top, Dock.Top); levelPanel.Children.Add(top); levelPanel.Children.Add(levels);
            tabs.Items.Add(new TabItem { Header = "实际楼层", Content = levelPanel });
            tabs.Items.Add(new TabItem { Header = "本轮计算核对", Content = new ScrollViewer { Content = new TextBlock { Text = string.Join("\n\n", result.CalculationIssues), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) } } });
            Action changed = () => { scope.Text = (profile.SelectedItem as RuleProfileInfo)?.Scope ?? "请选择服务器支持的规则方案"; values.IsEnabled = (profile.SelectedValue as string ?? "") == (previous.RuleProfileId ?? ""); if (!values.IsEnabled) scope.Text += "\n方案已切换：保存并重新检查后更新共用值选项。"; };
            profile.SelectionChanged += (s, e) => changed(); changed();
            save.Click += (s, e) => { try { Result = Build(); Complete(); } catch (Exception ex) { error.Text = ex.Message; } };
        }
        private ModelSettings Build()
        {
            foreach (var grid in new[] { roles, levels, values }) { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); }
            if (!(profile.SelectedItem is RuleProfileInfo selected)) throw new InvalidOperationException("请选择规则方案。");
            if (manualLevels.IsChecked == true && !levelRows.Any(l => l.Included)) throw new InvalidOperationException("请至少选择一个实际楼层，或取消手工指定。");
            var common = new Dictionary<string, string>(previous.CommonValues);
            foreach (var row in commonRows) { var key = row.Field.Slot + "|" + row.Field.Property; if (row.Value == "") common.Remove(key); else common[key] = row.Value; }
            previous.RuleProfileId = selected.Id; previous.CommonValues = common; previous.LevelsConfirmed = manualLevels.IsChecked == true;
            previous.BuildingLevelUniqueIds = levelRows.Where(r => r.Included).Select(r => r.UniqueId).ToArray(); previous.TopBoundaryLevelUniqueId = top.SelectedValue as string;
            previous.Roles = roleRows.Where(r => r.Included).Select(r => new ModelRoleChoice { UniqueId = r.Element.Element.UniqueId, Identity = r.Element.Identity, Slot = r.Slot }).ToArray();
            return previous;
        }
        internal static DataGrid Table(object rows) { var table = new DataGrid { ItemsSource = (System.Collections.IEnumerable)rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Extended }; LiquidGlassTheme.Table(table); return table; }
        internal static void Column(DataGrid grid, string name, string path, double width) {
            var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(path)));
            grid.Columns.Add(new DataGridTextColumn { Header = name, Binding = new Binding(path), ElementStyle = style, IsReadOnly = true, Width = width, MinWidth = width });
        }
        internal static DataGridCheckBoxColumn CheckColumn(string name, string path, double width) => new DataGridCheckBoxColumn { Header = name, Binding = new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = width, MinWidth = width };
        internal sealed class RoleRow { public ElementResult Element { get; set; } public bool Included { get; set; } public string Slot { get; set; } public string[] Choices { get; set; } public string Evidence => string.Join("；", Element.CandidateEvidence) + " " + Element.Element.AreaAggregationIssue; }
        internal sealed class LevelRow { public string UniqueId { get; set; } public string Name { get; set; } public double? ElevationM { get; set; } public bool Included { get; set; } }
        internal sealed class CommonRow { public FieldResult Field { get; set; } public string Value { get; set; } public bool CanType => Field.RawType != "Boolean" && Field.InputKind != "choice"; public string[] Choices => Field.RawType == "Boolean" ? new[] { "", "是", "否" } : Field.Options; public string EditorValue { get => Field.RawType == "Boolean" ? Value == "1" ? "是" : Value == "0" ? "否" : Value : Value; set => Value = Field.RawType == "Boolean" ? value == "是" ? "1" : value == "否" ? "0" : value : value; } }
    }

    internal sealed class NativeUndergroundWindow : NativeDialogWindow
    {
        internal UndergroundSelection Result { get; private set; }
        internal NativeUndergroundWindow(CheckResult result, UndergroundSelection settings)
        {
            settings = settings ?? new UndergroundSelection(); Title = "整栋建筑：楼层、基底与地坪基准"; Width = 740; Height = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner; LiquidGlassTheme.Apply(this);
            var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
            var head = new StackPanel(); DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
            head.Children.Add(new TextBlock { Text = "勾选实际地下楼层，排除重复结构和辅助标高。标高统一采用项目标高（m）；确认后暂存并计算，写入及保存 RVT 后可在其他电脑恢复。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
            var rows = result.Elements.Where(e => e.Element.Entity == "IfcBuildingStorey").OrderBy(e => e.Element.LevelElevationM).Select(e => new NativeSettingsWindow.LevelRow { UniqueId = e.Element.UniqueId, Name = e.Element.Name, ElevationM = e.Element.LevelElevationM, Included = settings.LevelUniqueIds.Contains(e.Element.UniqueId) }).ToArray();
            var datum = new TextBox { Text = settings.GroundElevationM?.ToString("0.######", CultureInfo.InvariantCulture) ?? "", Width = 140 }; LiquidGlassTheme.SearchBox(datum);
            var line = new WrapPanel(); line.Children.Add(new TextBlock { Text = "地坪基准（m）", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }); line.Children.Add(datum);
            var copy = new ComboBox { ItemsSource = rows, DisplayMemberPath = "Name", Width = 190, Margin = new Thickness(12, 0, 0, 0), ToolTip = "从标高复制地坪基准，再手工调整" }; NativeUi.NamedCombo(copy, "Name"); copy.SelectionChanged += (s, e) => { if (copy.SelectedItem is NativeSettingsWindow.LevelRow row) datum.Text = row.ElevationM?.ToString("0.######", CultureInfo.InvariantCulture) ?? ""; }; line.Children.Add(copy); head.Children.Add(line);
            var basis = new ComboBox { ItemsSource = new[] { new NativeSettingsWindow.LevelRow { UniqueId = "", Name = "未确认基底楼层" } }.Concat(rows), DisplayMemberPath = "Name", SelectedValuePath = "UniqueId", SelectedValue = settings.BaseLevelUniqueId ?? "", Margin = new Thickness(0, 12, 0, 14) }; NativeUi.NamedCombo(basis, "Name"); head.Children.Add(basis);
            var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) }; footer.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(buttons); buttons.Children.Add(new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) }); var save = new Button { Content = "确认并全面检查" }; LiquidGlassTheme.Primary(save); buttons.Children.Add(save);
            var grid = NativeSettingsWindow.Table(rows); grid.Columns.Add(NativeSettingsWindow.CheckColumn("计入地下", "Included", 95)); NativeSettingsWindow.Column(grid, "楼层 / 标高名称", "Name", 330); NativeSettingsWindow.Column(grid, "项目标高（m）", "ElevationM", 170); root.Children.Add(grid);
            save.Click += (s, e) => { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
                if (!double.TryParse(datum.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ground) || double.IsNaN(ground) || double.IsInfinity(ground)) { error.Text = "请输入有限数值的地坪基准，单位 m。"; return; }
                Result = new UndergroundSelection { Confirmed = true, GroundElevationM = ground, BaseLevelUniqueId = basis.SelectedValue as string, LevelUniqueIds = rows.Where(r => r.Included).Select(r => r.UniqueId).ToArray() }; Complete();
            };
        }
    }
}
