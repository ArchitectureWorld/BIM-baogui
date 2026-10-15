using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class SimplifiedWorkflowTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static object Call(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name, Flags);
        Assert(method != null, "Missing behavior: " + name);
        try { return method.Invoke(target, args); } catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static System.Collections.Generic.IEnumerable<DependencyObject> Tree(DependencyObject item)
    {
        yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())
            foreach (var nested in Tree(child)) yield return nested;
    }
    static ModelSnapshot Sample(string key)
    {
        var fields = new[] { "面积", "名称" }.Select((name, i) => new FieldCheck { Property = name, Source = "净用地" + name,
            PropertySet = "Pset_规划净用地信息属性集", RawType = i == 0 ? "Real" : "Label", State = "有效",
            Value = i == 0 ? "26.25" : "原名称", DisplayValue = i == 0 ? "26.25" : "原名称", Editable = true,
            OwnerId = 1001, OwnerUniqueId = key + "-element", Binding = "实例" }).ToArray();
        return new ModelSnapshot { DocumentKey = key, Title = "暂存验证模型", Stage = "报规", RequestedKind = "总图", ModelKind = "总图", Complete = true,
            ScanId = Guid.NewGuid().ToString("N"), Elements = new[] { new ModelElementCheck { Id = 1001, UniqueId = key + "-element", Name = "净用地",
                Entity = "IfcSlab", Slot = "规划净用地", Classified = true, Fields = fields } } };
    }
    static T Visual<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T found) return found;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) { var child = Visual<T>(VisualTreeHelper.GetChild(item, i)); if (child != null) return child; }
        return null;
    }
    public static void Run(Action<string, Action> check, string output)
    {
        var directory = Path.Combine(output, "simplified-" + Guid.NewGuid().ToString("N"));
        var fake = new FakeSession { Snapshot = Sample("model-A") };
        var window = new ManagerWindow(fake, directory); fake.Window = window;
        window.Show();
        check("four_step_toolbar_exposes_check_and_write", () => {
            var captions = Tree((DependencyObject)window.Content).OfType<Button>().Select(b => b.Content as string).ToArray();
            Assert(captions.Contains("3  全面检查") && captions.Any(c => c != null && c.StartsWith("4  写入")), "Check/write actions are not explicit top-level steps");
        });
        check("glass_theme_renders_three_frosted_work_surfaces", () => {
            var panels = Tree((DependencyObject)window.Content).OfType<Border>()
                .Where(b => b.CornerRadius.TopLeft >= 16 && b.BorderThickness.Left > 0).ToArray();
            Assert(panels.Length >= 3, "The left, inventory, and inspector panels must each have a glass surface");
            Assert(window.ElementGrid.EnableRowVirtualization && window.ElementGrid.AlternationCount == 2,
                "Glass UI must retain a virtualized, legible object table");
        });
        check("glass_work_columns_have_no_panel_overlap", () => {
            var work = Tree((DependencyObject)window.Content).OfType<Grid>()
                .Single(g => g.ColumnDefinitions.Count == 3);
            var cards = work.Children.OfType<Border>().ToArray();
            Assert(cards.Length == 3 && cards.Select(Grid.GetColumn).SequenceEqual(new[] { 0, 1, 2 }),
                "All three work cards must occupy distinct columns; a child column assignment is not enough");
            Assert(((Grid)window.Content).Background != null, "Window content canvas cannot be transparent in screenshots");
        });
        check("category_navigation_uses_business_label_template", () => {
            var list = (ListBox)typeof(ManagerWindow).GetField("roleList", Flags).GetValue(window);
            Assert(list.ItemTemplate != null && string.IsNullOrEmpty(list.DisplayMemberPath),
                "Custom glass item template must bind Role.Slot and Subtitle, not show the CLR type name");
        });
        check("all_action_buttons_use_one_exact_36_dip_height", () => {
            window.UpdateLayout();
            var captions = new[] { "3  全面检查", "取消检查", "全选列表",
                "定位所选", "设置所选分类", "保存检查与缺失报告" };
            var buttons = Tree((DependencyObject)window.Content).OfType<Button>().ToArray();
            foreach (var caption in captions)
            {
                var button = buttons.Single(b => (b.Content as string) == caption);
                Assert(Math.Abs(button.Height - LiquidGlassTheme.ControlHeight) < 0.01,
                    caption + " has a different declared height");
                Assert(Math.Abs(button.ActualHeight - LiquidGlassTheme.ControlHeight) < 0.5,
                    caption + " has a different rendered height");
                Assert(Math.Abs(button.FontSize - LiquidGlassTheme.ControlFontSize) < 0.01,
                    caption + " has a different font size");
            }
            var write = buttons.Single(b => (b.Content as string)?.StartsWith("4  写入") == true);
            Assert(Math.Abs(write.Height - LiquidGlassTheme.ControlHeight) < 0.01,
                "Write button must not change height when the pending count changes");
        });
        check("dropdowns_and_status_tag_match_button_height", () => {
            var comboBoxes = Tree((DependencyObject)window.Content).OfType<ComboBox>().ToArray();
            var filter = comboBoxes.Single(c => c.Items.Count == 6);
            foreach (var combo in new[] { window.StageSelector, window.KindSelector, window.SlotSelector, filter })
            {
                Assert(Math.Abs(combo.Height - LiquidGlassTheme.ControlHeight) < 0.01,
                    "Dropdown declared height must be 36 DIP");
                combo.ApplyTemplate();
                var surface = combo.Template.FindName("comboSurface", combo) as Border;
                Assert(surface != null && Math.Abs(surface.CornerRadius.TopLeft - LiquidGlassTheme.ControlRadius) < 0.01,
                    "Dropdown must use the same glass corner radius as the buttons");
                Assert(combo.Template.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup,
                    "Dropdown must provide a working WPF Popup");
            }
            var range = (TextBlock)typeof(ManagerWindow).GetField("range", Flags).GetValue(window);
            Assert(range.Parent is Border tag && Math.Abs(tag.Height - LiquidGlassTheme.ControlHeight) < 0.01,
                "Object count must remain a non-interactive, aligned 36 DIP status tag");
        });
        check("native_dropdown_selection_still_operates", () => {
            var combo = window.KindSelector;
            var prior = combo.SelectedItem;
            combo.IsDropDownOpen = true;
            window.UpdateLayout();
            var popup = combo.Template.FindName("PART_Popup", combo) as System.Windows.Controls.Primitives.Popup;
            Assert(popup != null && popup.IsOpen, "Native dropdown did not open");
            combo.IsDropDownOpen = false;
            Assert(!popup.IsOpen && Equals(combo.SelectedItem, prior),
                "Closing a dropdown must preserve its selection");
        });
        check("glass_skin_preserves_real_selection_and_edit_controls", () => {
            Assert(window.StageSelector.Items.Count == 2 && window.KindSelector.Items.Count == 3, "Workflow selectors changed");
            Assert(window.FieldGrid.Columns.Cast<DataGridColumn>().Count(c => !c.IsReadOnly) == 1,
                "Direct property editing must remain available on exactly the editable value column");
            var buttons = Tree((DependencyObject)window.Content).OfType<Button>().ToArray();
            Assert(buttons.Any(b => (b.Content as string) == "3  全面检查" && b.Background is LinearGradientBrush),
                "The scan action is not visually distinguished");
        });
        check("reference_metrics_are_inside_bottom_diagnostics", () => {
            var panel = Tree((DependencyObject)window.Content).OfType<Expander>().SingleOrDefault();
            Assert(panel != null && !panel.IsExpanded && (panel.Header as string).Contains("开发"), "Development detail must start collapsed");
            Assert(Tree((DependencyObject)panel.Content).OfType<Button>().Any(b => (b.Content as string)?.Contains("参考项覆盖") == true), "Existing coverage cards not moved into diagnostics");
        });
        check("attribute_values_are_editable_without_separate_preview_button", () => {
            Assert(!window.FieldGrid.IsReadOnly && window.FieldGrid.Columns.Any(c => !c.IsReadOnly), "Property table still read only");
            Assert(!Tree((DependencyObject)window.Content).OfType<Button>().Any(b => (b.Content as string) == "预览属性修改"), "Old extra preview step still exposed");
        });
        check("remembered_model_state_can_be_initialized", () => Call(window, "RestoreState", "model-A"));
        window.StageSelector.SelectedItem = "报规"; window.KindSelector.SelectedItem = "总图"; window.StartFullCheck();
        window.ElementGrid.SelectedItem = fake.Snapshot.Elements[0];
        check("inline_changes_accumulate_and_are_pending_red", () => {
            Call(window, "StageValue", fake.Snapshot.Elements[0].Fields[0], "40.5");
            Call(window, "StageValue", fake.Snapshot.Elements[0].Fields[1], "新名称");
            var drafts = (EditRequest[])typeof(ManagerWindow).GetProperty("DraftRequests", Flags)?.GetValue(window);
            Assert(drafts?.Length == 2, "Second property replaced the first draft");
            Assert((string)Call(window, "FieldMark", fake.Snapshot.Elements[0].Fields[0]) == "待写入", "Unsaved edit is not marked pending");
        });
        check("real_property_cell_edit_stages_value_without_changing_snapshot", () => {
            window.ElementGrid.SelectedItem = null; window.ElementGrid.SelectedItem = fake.Snapshot.Elements[0]; window.UpdateLayout();
            var item = window.FieldGrid.Items[0]; window.FieldGrid.CurrentCell = new DataGridCellInfo(item, window.FieldGrid.Columns[1]);
            window.FieldGrid.SelectedItem = item; Assert(window.FieldGrid.BeginEdit(), "Actual property cell cannot enter edit mode"); window.UpdateLayout();
            var editor = Visual<TextBox>(window.FieldGrid.Columns[1].GetCellContent(item)); Assert(editor != null, "Editing TextBox missing");
            editor.Text = "41.5"; editor.GetBindingExpression(TextBox.TextProperty).UpdateSource(); window.FieldGrid.CommitEdit(DataGridEditingUnit.Cell, true); window.FieldGrid.CommitEdit(DataGridEditingUnit.Row, true);
            Assert((string)item.GetType().GetProperty("Value").GetValue(item) == "41.5" && fake.Snapshot.Elements[0].Fields[0].Value == "26.25", "Inline edit modified the model snapshot or lost the draft");
            var brush = (SolidColorBrush)item.GetType().GetProperty("Background").GetValue(item);
            Assert(brush.Color == (Color)ColorConverter.ConvertFromString("#FFFFDADA"), "Pending cell does not use the requested red background");
            Program.Screenshot(window, Path.Combine(output, "manager-simple-drafts.png"));
        });
        check("top_write_submits_all_pending_properties_once", () => {
            Call(window, "WriteChanges");
            Assert(fake.Written?.Length == 2 && fake.Written.Any(r => r.Value == "41.5") && fake.Written.Any(r => r.Value == "新名称"), "Write did not include all drafts");
        });
        window.Close();
        var resumedFake = new FakeSession { Snapshot = fake.Snapshot };
        var resumed = new ManagerWindow(resumedFake, directory); resumedFake.Window = resumed;
        resumed.Show();
        check("reopen_restores_phase_type_and_view_but_requires_fresh_check", () => {
            Call(resumed, "RestoreState", "model-A");
            Assert(resumed.SelectedStage == "报规" && resumed.RequestedKind == "总图" && resumed.Snapshot != null && !resumed.Current,
                "Reopen forgot settings or trusted cached checks as fresh");
            resumed.Accept(resumedFake.Snapshot); resumed.ElementGrid.SelectedItem = resumedFake.Snapshot.Elements[0];
            Assert(((EditRequest[])typeof(ManagerWindow).GetProperty("DraftRequests", Flags).GetValue(resumed)).Length == 2, "Drafts lost on close");
        });
        check("successful_write_is_orange_and_second_edit_is_pending_again", () => {
            var requests = (EditRequest[])typeof(ManagerWindow).GetProperty("DraftRequests", Flags)?.GetValue(resumed);
            Assert(requests != null, "Draft behavior missing");
            var fields = resumedFake.Snapshot.Elements[0].Fields;
            Call(resumed, "RecordCommitted", new EditPreview { Changes = fields.Select((f, i) => new EditChange { Id = f.OwnerId,
                OwnerUniqueId = f.OwnerUniqueId, Source = f.Source, Binding = f.Binding, Action = "赋值", ExpectedRaw = i == 0 ? "41.5" : "新名称" }).ToArray() }, requests);
            fields[0].Value = fields[0].DisplayValue = "41.5"; fields[1].Value = fields[1].DisplayValue = "新名称";
            resumed.Accept(resumedFake.Snapshot); resumed.ElementGrid.SelectedItem = resumedFake.Snapshot.Elements[0];
            Assert((string)Call(resumed, "FieldMark", fields[0]) == "已手工写入", "Committed manual edit not marked");
            var savedRow = resumed.FieldGrid.Items[0]; var brush = (SolidColorBrush)savedRow.GetType().GetProperty("Background").GetValue(savedRow);
            Assert(brush.Color == (Color)ColorConverter.ConvertFromString("#FFFFD9AD"), "Committed cell is not orange");
            Program.Screenshot(resumed, Path.Combine(output, "manager-simple-saved.png"));
            Call(resumed, "StageValue", fields[0], "55.25");
            Assert((string)Call(resumed, "FieldMark", fields[0]) == "待写入", "Second edit lost dirty state");
        });
        check("model_changes_after_cached_edit_block_unsafe_write", () => {
            var altered = Sample("model-A"); altered.Elements[0].Fields[0].Value = altered.Elements[0].Fields[0].DisplayValue = "99";
            resumed.Accept(altered); resumed.ElementGrid.SelectedItem = altered.Elements[0];
            Assert(!resumed.CanApply && ((string)Call(resumed, "FieldMark", altered.Elements[0].Fields[0])).Contains("冲突"), "External edit silently rebased draft");
        });
        resumed.Close();
        var historyFake = new FakeSession { Snapshot = fake.Snapshot };
        var historyWindow = new ManagerWindow(historyFake, directory); historyFake.Window = historyWindow;
        check("orange_history_and_pending_second_edit_survive_window_restart", () => {
            Call(historyWindow, "RestoreState", "model-A"); historyWindow.Accept(historyFake.Snapshot); historyWindow.ElementGrid.SelectedItem = historyFake.Snapshot.Elements[0];
            var field = historyFake.Snapshot.Elements[0].Fields[0];
            Assert((string)Call(historyWindow, "FieldMark", field) == "待写入", "Second edit was lost on restart");
            Call(historyWindow, "StageValue", field, field.DisplayValue);
            Assert((string)Call(historyWindow, "FieldMark", field) == "已手工写入", "Persisted orange history was lost");
        });
        historyWindow.Close();
        var otherFake = new FakeSession { Snapshot = Sample("model-B") };
        var other = new ManagerWindow(otherFake, directory); otherFake.Window = other;
        check("different_models_do_not_share_phase_drafts_or_colors", () => {
            Call(other, "RestoreState", "model-B");
            Assert(other.SelectedStage == null && other.Snapshot == null, "Model A state leaked into B");
        });
        other.Close();
        var autoSnapshot = Sample("model-autofill");
        var autoField = autoSnapshot.Elements[0].Fields[0]; autoField.State = "未填写"; autoField.Value = autoField.DisplayValue = null;
        autoField.SuggestedValue = "154.321"; autoField.SuggestionReason = "当前模型水平投影面积（m²）";
        var second = Sample("model-second").Elements[0]; second.Id = 1002; second.Fields[0].OwnerId = 1002;
        autoSnapshot.Elements = new[] { autoSnapshot.Elements[0], second };
        var autoFake = new FakeSession { Snapshot = autoSnapshot }; var autoWindow = new ManagerWindow(autoFake, Path.Combine(output, "auto-profiles")); autoFake.Window = autoWindow;
        autoWindow.StageSelector.SelectedItem = "报规"; autoWindow.KindSelector.SelectedItem = "总图"; autoWindow.Accept(autoSnapshot); autoWindow.Show();
        autoWindow.ElementGrid.SelectedItem = autoSnapshot.Elements[0];
        check("automatic_value_is_visible_and_can_be_overridden_inline", () => {
            var row = autoWindow.FieldGrid.Items[0];
            Assert((string)row.GetType().GetProperty("Value").GetValue(row) == "154.321" && (string)row.GetType().GetProperty("State").GetValue(row) == "自动待写入", "Model-derived value is still blank in property table");
            row.GetType().GetProperty("Value").SetValue(row, "154.321"); Assert(autoWindow.DraftRequests.Length == 0, "Unchanged automatic value became a manual edit");
            row.GetType().GetProperty("Value").SetValue(row, "160");
            Assert(autoWindow.DraftRequests.Single().Value == "160" && (string)row.GetType().GetProperty("State").GetValue(row) == "待写入", "Manual correction did not override the automatic value");
            Program.Screenshot(autoWindow, Path.Combine(output, "manager-auto-corrected.png"));
        });
        check("late_cell_commit_keeps_original_model_target", () => {
            var originalEditor = autoWindow.FieldGrid.Items[0]; autoWindow.ElementGrid.SelectedItem = second;
            originalEditor.GetType().GetProperty("Value").SetValue(originalEditor, "170");
            Assert(autoWindow.DraftRequests.Length == 1 && autoWindow.DraftRequests[0].Ids.Single() == 1001 && autoWindow.DraftRequests[0].Value == "170", "Cell commit modified the newly selected object");
        });
        autoWindow.Close();
        var landSnapshot = Sample("model-four-values"); landSnapshot.Title = "自动填值界面验证（模拟数据）";
        landSnapshot.Elements[0].Name = "场地";
        landSnapshot.Elements[0].Fields = CaseWorkflow.ForModel("报规", "总图").Single(r => r.Slot == "规划净用地").Properties.Select(p => {
            var f = ModelChecks.Evaluate(p, new ParameterRead[0]); f.OwnerId = 1001; f.OwnerUniqueId = "model-four-values-element"; f.Binding = "实例";
            AutoFillPolicy.Populate(f, "规划净用地", "场地", 154.321); return f;
        }).ToArray();
        var landFake = new FakeSession { Snapshot = landSnapshot }; var landWindow = new ManagerWindow(landFake, Path.Combine(output,"four-value-profiles")); landFake.Window = landWindow;
        landWindow.StageSelector.SelectedItem = "报规"; landWindow.KindSelector.SelectedItem = "总图"; landWindow.Accept(landSnapshot); landWindow.Show(); landWindow.ElementGrid.SelectedItem = landSnapshot.Elements[0];
        landWindow.ShowPreview(new EditPreview { ScanId = landSnapshot.ScanId, Changes = landSnapshot.Elements[0].Fields.Select(f => new EditChange { Id = f.OwnerId, Source = f.Source, Action = "赋值", NewValue = f.SuggestedValue }).ToArray() });
        check("all_four_land_fields_render_automatic_values_together", () => {
            Assert(landWindow.FieldGrid.Items.Count == 4 && landWindow.FieldGrid.Items.Cast<object>().All(row => !string.IsNullOrWhiteSpace((string)row.GetType().GetProperty("Value").GetValue(row))), "The net-land property table still contains blank fields");
            Assert(landWindow.CanApply && landWindow.DraftRequests.Length == 0, "Automatic values incorrectly require a manual edit before writing");
            Program.Screenshot(landWindow, Path.Combine(output,"manager-auto-values.png"));
        });
        landWindow.Close();
    }
}
