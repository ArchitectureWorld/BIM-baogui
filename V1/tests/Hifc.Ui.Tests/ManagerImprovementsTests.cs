using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class ManagerImprovementsTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    internal static T Visual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) return found;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var item = Visual<T>(VisualTreeHelper.GetChild(root, i)); if (item != null) return item; }
        return null;
    }
    internal static ModelSnapshot Sample()
    {
        var row = new ModelElementCheck { Id = 451959, UniqueId = "total-site-land", Name = "场地", Category = "楼板", Entity = "IfcSlab", Slot = "规划净用地", Classified = true };
        row.Fields = new[] { new FieldCheck { PropertySet = "Pset_规划净用地信息属性集", Property = "名称", Source = "规划净用地名称", RawType = "Label", State = "有效", Value = "场地", DisplayValue = "场地", Editable = true, OwnerId = row.Id, OwnerUniqueId = row.UniqueId, Binding = "实例", SourceSignature = "land-source" } };
        var level = new ModelElementCheck { Id = 2, UniqueId = "level", Name = "一层", Entity = "IfcBuildingStorey", Slot = "建筑楼层", Classified = true, Fields = new[] { new FieldCheck { PropertySet = "Pset_建筑楼层信息属性集", Property = "建筑基底", Source = "建筑基底", RawType = "Boolean", State = "有效", Value = "0", Editable = true, OwnerId = 2, OwnerUniqueId = "level", Binding = "实例" } } };
        var info = new ModelElementCheck { Id = 3, UniqueId = "info", Name = "项目信息", Entity = "IfcBuilding", Slot = "建筑技术", Classified = true, Fields = new[] { "建筑类型名称", "建筑类别" }.Select(n => new FieldCheck { PropertySet = "Pset_建筑技术信息属性集", Property = n, Source = n, RawType = "Label", State = "未填写", Editable = true, OwnerId = 3, OwnerUniqueId = "info", Binding = "实例" }).ToArray() };
        return new ModelSnapshot { DocumentKey = "six-item-site-" + Guid.NewGuid().ToString("N"), Title = "总图 · 鼠标交互验证副本", Stage = "报规", RequestedKind = "总图", ModelKind = "总图", Complete = true, ScanId = Guid.NewGuid().ToString("N"), Elements = new[] { row, level, info } };
    }
    static FrameworkElement Edit(ManagerWindow window, ModelElementCheck row, int index)
    {
        window.FieldGrid.CommitEdit(); window.ElementGrid.SelectedItem = row; window.UpdateLayout();
        var item = window.FieldGrid.Items[index]; window.FieldGrid.CurrentCell = new DataGridCellInfo(item, window.FieldGrid.Columns[1]);
        window.FieldGrid.SelectedItem = item; Assert(window.FieldGrid.BeginEdit(), "Cannot enter property editor"); window.UpdateLayout();
        return window.FieldGrid.Columns[1].GetCellContent(item);
    }
    public static void Run(Action<string, Action> check, string output)
    {
        var fake = new FakeSession { Snapshot = Sample() };
        var window = new ManagerWindow(fake, Path.Combine(output, "six-item-" + Guid.NewGuid().ToString("N"))); fake.Window = window;
        window.Show(); window.StageSelector.SelectedItem = "报规"; window.KindSelector.SelectedItem = "总图"; window.StartFullCheck();
        check("filled_site_value_enters_edit_via_single_mouse_routed_event", () => {
            window.ElementGrid.SelectedItem = fake.Snapshot.Elements[0]; window.UpdateLayout();
            var content = window.FieldGrid.Columns[1].GetCellContent(window.FieldGrid.Items[0]);
            var cell = (DataGridCell)content.Parent;
            window.FieldGrid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = content });
            window.UpdateLayout();
            Assert(cell.IsEditing && Visual<TextBox>(cell) != null, "Single mouse event only selected the filled cell");
            window.FieldGrid.CancelEdit();
        });
        check("boolean_editor_shows_unfilled_yes_no_without_losing_false", () => {
            var content = Edit(window, fake.Snapshot.Elements[1], 0); var combo = Visual<ComboBox>(content);
            Assert(combo != null, "Boolean is still a free text field");
            Assert(combo.Items.Cast<string>().SequenceEqual(new[] { "未填写", "是", "否" }) && (string)combo.SelectedItem == "否", "False not shown explicitly");
            combo.SelectedItem = "是"; window.FieldGrid.CommitEdit();
            Assert(window.DraftRequests.Any(r => r.Value == "1"), "Dropdown did not stage true");
        });
        check("project_building_presets_allow_missing_factory_category", () => {
            window.FieldGrid.CancelEdit(); var content = Edit(window, fake.Snapshot.Elements[2], 0); var combo = Visual<ComboBox>(content);
            Assert(combo != null && combo.Items.Cast<string>().Contains("厂房") && combo.Items.Cast<string>().Contains("生活服务设施"), "Building type presets absent");
            window.FieldGrid.CommitEdit(); content = Edit(window, fake.Snapshot.Elements[2], 1); combo = Visual<ComboBox>(content);
            Assert(combo != null && combo.IsEditable && combo.Items.Cast<string>().Contains("配套车库"), "Building category must accept manual additions");
            combo.Text = "工业厂房"; combo.GetBindingExpression(ComboBox.TextProperty).UpdateSource(); window.FieldGrid.CommitEdit();
            Assert(window.DraftRequests.Any(r => r.Source == "建筑类别" && r.Value == "工业厂房"), "Additional category lost");
        });
        check("existing_building_type_outside_presets_is_not_erased_on_edit", () => {
            var row=fake.Snapshot.Elements[2]; row.Fields[0].State="有效"; row.Fields[0].Value=row.Fields[0].DisplayValue="原模型分类";
            window.ElementGrid.SelectedItem=null;
            var content=Edit(window,row,0); var combo=Visual<ComboBox>(content);
            Assert(combo != null && combo.Text=="原模型分类", "Existing label disappeared because it is outside presets");
            window.FieldGrid.CommitEdit();
            Assert(!window.DraftRequests.Any(r=>r.Source=="建筑类型名称"), "An unchanged label became a clear request");
        });
        check("stale_property_has_local_explanation", () => {
            window.ElementGrid.SelectedItem = fake.Snapshot.Elements[0]; window.MarkStale("模型已变化");
            var editor = window.FieldGrid.Items[0]; var text = (string)editor.GetType().GetProperty("State").GetValue(editor);
            Assert(text.Contains("复检") || text.Contains("过期"), "Stale values look editable with no local explanation");
            window.Accept(fake.Snapshot);
        });
        check("classification_refreshes_new_fields_without_mutating_actual_scan", () => {
            var original = fake.Snapshot.Elements[0]; window.ElementGrid.SelectedItem = original;
            window.SlotSelector.SelectedItem = "绿地"; window.PreviewClassification();
            var proposed = new ModelElementCheck { Id = original.Id, UniqueId = original.UniqueId, Name = original.Name, Entity = original.Entity, Slot = "绿地", Classified = true, Fields = new[] {
                new FieldCheck { PropertySet = "Pset_绿地信息属性集", Property = "投影面积", Source = "绿地投影面积", RawType = "Real", State = "缺参数", OwnerId = original.Id, OwnerUniqueId = original.UniqueId, Binding = "实例", SuggestedValue = "4689.888048", SourceSignature = "green-source" } } };
            window.ShowPreview(new EditPreview { ScanId = fake.Snapshot.ScanId, Changes = new EditChange[0], ProposedElements = new[] { proposed } });
            var editor = window.FieldGrid.Items[0];
            Assert(((FieldCheck)editor.GetType().GetProperty("Field").GetValue(editor)).Source == "绿地投影面积" && (string)editor.GetType().GetProperty("Value").GetValue(editor) == "4689.888048", "Old class properties still displayed");
            editor.GetType().GetProperty("Value").SetValue(editor, "4700");
            Assert(window.CanApply && window.DraftRequests.Any(r => r.Source == "绿地投影面积" && r.Slot == "绿地"), "New class edit was not safely staged");
            Assert(original.Slot == "规划净用地" && original.Fields[0].Value == "场地", "Proposal mutated actual scan");
            Program.Screenshot(window, Path.Combine(output,"manager-class-change.png"));
        });
        check("role_remains_red_after_draft_and_clears_only_after_fresh_readback", () => {
            var road = new ModelElementCheck { Id = 4, UniqueId = "road", Name = "道路", Entity = "IfcSlab", Slot = "道路边界线", Classified = true,
                Fields = new[] { new FieldCheck { PropertySet = "Pset_道路边界线信息属性集", Property = "道路宽度", Source = "道路边界线道路宽度", RawType = "Real", State = "未填写", Editable = true, OwnerId = 4, OwnerUniqueId = "road", Binding = "实例" },
                    new FieldCheck { PropertySet = "Pset_道路边界线信息属性集", Property = "区内道路类型", Source = "区内道路类型", State = "有效", Value = "消防道路" } } };
            fake.Snapshot.Elements = new[] { road }; window.Accept(fake.Snapshot); window.ElementGrid.SelectedItem = road;
            var list = (ListBox)typeof(ManagerWindow).GetField("roleList",Flags).GetValue(window);
            Func<Color> color = () => ((SolidColorBrush)list.Items.Cast<object>().Single(i => ((RoleCoverage)i.GetType().GetProperty("Role").GetValue(i)).Slot == "道路边界线").GetType().GetProperty("Foreground").GetValue(list.Items.Cast<object>().Single(i => ((RoleCoverage)i.GetType().GetProperty("Role").GetValue(i)).Slot == "道路边界线"))).Color;
            Assert(color() == (Color)ColorConverter.ConvertFromString("#B83138"), "Missing road width not red");
            Assert(list.Items.Cast<object>().Where(i => ((RoleCoverage)i.GetType().GetProperty("Role").GetValue(i)).Slot != "道路边界线")
                .All(i => ((SolidColorBrush)i.GetType().GetProperty("Foreground").GetValue(i)).Color != (Color)ColorConverter.ConvertFromString("#B83138")), "Non-road classes received mandatory manual-entry red");
            window.StageValue(road.Fields[0],"4"); Assert(color() == (Color)ColorConverter.ConvertFromString("#B83138"), "Draft hid missing model value");
            Program.Screenshot(window,Path.Combine(output,"manager-required-road-width.png"));
            window.RecordCommitted(new EditPreview { Changes = new[] { new EditChange { Action = "赋值", OwnerUniqueId = "road", Source = "道路边界线道路宽度", ExpectedRaw = "4" } } }, window.DraftRequests.Where(r=>r.Source=="道路边界线道路宽度").ToArray());
            road.Fields[0].Value = "4"; road.Fields[0].State = "有效"; window.Accept(fake.Snapshot);
            Assert(color() != (Color)ColorConverter.ConvertFromString("#B83138"), "Readback did not clear red");
        });
        window.Close();
    }
}
