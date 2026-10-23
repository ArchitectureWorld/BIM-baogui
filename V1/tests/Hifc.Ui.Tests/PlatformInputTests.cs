using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class PlatformInputTests
{
    const string Profile = "hubei-420626-planning-20261020";
    const string RegionSet = "Pset_建筑区域信息属性集";
    const string BuildingSet = "Pset_建筑技术信息属性集";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void Assert(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    static object Get(object target, string name)
    {
        var property = target.GetType().GetProperty(name, Flags);
        Assert(property != null, target.GetType().Name + "." + name + " is missing");
        return property.GetValue(target);
    }
    static void Set(object target, string name, object value)
    {
        var property = target.GetType().GetProperty(name, Flags);
        Assert(property != null, target.GetType().Name + "." + name + " is missing");
        property.SetValue(target, value);
    }
    static void Pump(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
    }
    static T[] Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        var found = root is T item ? new[] { item } : new T[0];
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            found = found.Concat(Visuals<T>(VisualTreeHelper.GetChild(root, i))).ToArray();
        return found;
    }
    static Window Planning(PlanningSettings settings = null, string kind = "单体建筑")
    {
        var type = typeof(ManagerWindow).Assembly.GetType("Hifc.Revit.Management.PlanningWindow");
        return (Window)Activator.CreateInstance(type, Flags, null,
            new object[] { new ModelSnapshot { Title = "平台输入回归", ModelKind = kind }, settings ?? new PlanningSettings() }, null);
    }
    static ComboBox Selector(Window window)
    {
        var field = window.GetType().GetField("ProfileSelector", Flags);
        Assert(field != null, "Calculation settings have no explicit rule profile selector");
        return (ComboBox)field.GetValue(window);
    }
    static object[] Rows(Window window) => ((IEnumerable)Get(window, "ValueRows")).Cast<object>().ToArray();
    static object Row(Window window, string property, string slot = null)
    {
        var matches=Rows(window).Where(r=>(string)Get(r,"Property")==property&&(slot==null||(string)Get(r,"Slot")==slot)).ToArray();
        Assert(matches.Length==1,"Expected one common field for "+slot+" / "+property+"; got "+matches.Length);
        return matches[0];
    }
    static ComboBox CommonEditor(Window window, string property, string slot = null)
    {
        var tabs = Visuals<TabControl>(window).Single();
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => (string)t.Header == "项目共用值");
        Pump(window);
        var row = Row(window, property,slot);
        var grid = Visuals<DataGrid>(window).Single(g => g.ItemsSource != null && g.Items.Cast<object>().Contains(row));
        grid.ScrollIntoView(row); Pump(window);
        return Visuals<ComboBox>(grid).Single(c => ReferenceEquals(c.DataContext, row));
    }
    static void Type(ComboBox combo, string value)
    {
        combo.Text = value;
        combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
    }
    static PlanningSettings Saved(Window window) => (PlanningSettings)window.GetType().GetMethod("BuildSettings", Flags).Invoke(window, null);
    static Window LevelPlanning(PlanningSettings settings)
    {
        var rows=new[]{
            new ModelElementCheck{UniqueId="B1",Name="地下车库层",Entity="IfcBuildingStorey",LevelElevationM=-3},
            new ModelElementCheck{UniqueId="1F",Name="首层",Entity="IfcBuildingStorey",LevelElevationM=0},
            new ModelElementCheck{UniqueId="park",Entity="IfcSpace",Slot="室内停车场",LevelUniqueId="B1"},
            new ModelElementCheck{UniqueId="region",Entity="IfcSlab",Slot="建筑区域",LevelUniqueId="1F"}};
        var type=typeof(ManagerWindow).Assembly.GetType("Hifc.Revit.Management.PlanningWindow");
        return (Window)Activator.CreateInstance(type,Flags,null,new object[]{
            new ModelSnapshot{Title="楼层范围切换",ModelKind="单体建筑",Elements=rows},settings},null);
    }
    static CheckBox LevelCheck(Window window,string id)
    {
        var tabs=Visuals<TabControl>(window).Single();
        tabs.SelectedItem=tabs.Items.Cast<TabItem>().Single(t=>(string)t.Header=="实际楼层");Pump(window);
        var grid=(DataGrid)window.GetType().GetField("levelGrid",Flags).GetValue(window);
        var row=((IEnumerable)Get(window,"LevelRows")).Cast<object>().Single(r=>(string)Get(r,"UniqueId")==id);
        grid.ScrollIntoView(row);Pump(window);
        return Visuals<CheckBox>(grid.Columns[0].GetCellContent(row)).Single();
    }
    static FieldCheck Field(int id, string pset, string property, string value = "", string rawType = "Label")
    {
        var result = new FieldCheck { PropertySet = pset, Property = property, Source = property, RawType = rawType,
            State = string.IsNullOrEmpty(value) ? "未填写" : "有效", Value = value, DisplayValue = value,
            Editable = true, OwnerId = id, OwnerUniqueId = "platform-" + id, Binding = "实例", SourceSignature = property + "-source" };
        Set(result, "RuleProfileId", Profile);
        return result;
    }
    static ModelSnapshot Sample()
    {
        var settings = new PlanningSettings(); Set(settings, "RuleProfileId", Profile);
        return new ModelSnapshot { DocumentKey = "platform-ui-" + Guid.NewGuid().ToString("N"), Title = "平台业务输入样本", Stage = "报规",
            RequestedKind = "单体建筑", ModelKind = "单体建筑", Complete = true, ScanId = Guid.NewGuid().ToString("N"), Planning = settings,
            Elements = new[] {
                new ModelElementCheck { Id = 51, UniqueId = "platform-51", Name = "建筑区域", Entity = "IfcSlab", Slot = "建筑区域", Classified = true,
                    Fields = new[] { Field(51, RegionSet, "主功能类别", "学校"), Field(51, RegionSet, "子功能类别", "办公室"), Field(51, RegionSet, "计算系数", "", "Real") } },
                new ModelElementCheck { Id = 52, UniqueId = "platform-52", Name = "建筑技术", Entity = "IfcBuilding", Slot = "建筑技术", Classified = true,
                    Fields = new[] { Field(52, BuildingSet, "建筑类型名称", "厂房"), Field(52, BuildingSet, "建筑类别"), Field(52, BuildingSet, "建筑用途名称"), Field(52, BuildingSet, "结构类型") } },
                new ModelElementCheck { Id = 53, UniqueId = "platform-53", Name = "首层", Entity = "IfcBuildingStorey", Slot = "建筑楼层", Classified = true,
                    Fields = new[] { Field(53, "Pset_建筑楼层信息属性集", "建筑基底", "0", "Boolean") } }
            } };
    }
    static ComboBox Edit(ManagerWindow window, ModelElementCheck element, string property)
    {
        window.FieldGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        window.FieldGrid.CommitEdit(DataGridEditingUnit.Row, true);
        window.ElementGrid.SelectedItem = element; Pump(window);
        var row = window.FieldGrid.Items.Cast<ManagerWindow.PropertyEditor>().Single(r => r.Property == property);
        window.FieldGrid.ScrollIntoView(row, window.FieldGrid.Columns[1]); Pump(window);
        window.FieldGrid.CurrentCell = new DataGridCellInfo(row, window.FieldGrid.Columns[1]);
        window.FieldGrid.SelectedItem = row;
        Assert(window.FieldGrid.BeginEdit(), "Existing platform value cannot be edited: " + property);
        Pump(window);
        var combo = Visuals<ComboBox>(window.FieldGrid.Columns[1].GetCellContent(row)).SingleOrDefault();
        Assert(combo != null, "Platform enumeration is still a free text field: " + property);
        return combo;
    }
    static void Commit(ManagerWindow window)
    {
        Assert(window.FieldGrid.CommitEdit(DataGridEditingUnit.Cell, true), "Cell did not commit");
        Assert(window.FieldGrid.CommitEdit(DataGridEditingUnit.Row, true), "Row did not commit");
        Pump(window);
    }
    public static void Run(Action<string, Action> check, string output)
    {
        check("platform_control_line_editor_can_stage_confirmed_net_land_redline_type", () => {
            const string pset = "Pset_规划控制线信息属性集", value = "净用地控制红线";
            var fake = new FakeSession { Snapshot = Sample() };
            var line = new ModelElementCheck { Id = 54, UniqueId = "platform-54", Name = "用地红线", Entity = "IfcSlab", Slot = "规划控制线", Classified = true,
                Fields = new[] { Field(54, pset, "名称", "用地红线"), Field(54, pset, "红线类型"), Field(54, pset, "编号", "3") } };
            line.Fields[0].Source = "规划控制线名称";
            fake.Snapshot.ModelKind = "总图"; fake.Snapshot.Elements = new[] { line };
            var window = new ManagerWindow(fake, Path.Combine(output, "platform-control-line-" + Guid.NewGuid().ToString("N"))); fake.Window = window;
            try {
                window.Show(); window.StageSelector.SelectedItem = "报规"; window.KindSelector.SelectedItem = "总图"; window.StartFullCheck();
                var editor = Edit(window, line, "红线类型");
                Assert(editor.Items.Cast<string>().Contains(value), "Confirmed net-land redline is absent from the editor");
                Assert(!editor.Items.Cast<string>().Contains("净用地规划控制红线"), "Superseded redline spelling remains in the dropdown");
                Type(editor, value); Commit(window);
                window.StageValue(line.Fields[0], value); Pump(window);
                Assert(window.DraftRequests.Length == 2 && window.DraftRequests.All(r => r.Value == value), "Exact two-field correction was not staged");
                Assert(window.DraftRequests.Select(r => r.Source).OrderBy(s => s).SequenceEqual(new[] { "红线类型", "规划控制线名称" }.OrderBy(s => s)), "Correction staged unrelated properties");
                var row = window.FieldGrid.Items.Cast<ManagerWindow.PropertyEditor>().Single(r => r.Property == "红线类型");
                Assert(string.IsNullOrEmpty((string)Get(row, "ValueHint")) && line.Fields[2].Value == "3", "Redline stays invalid or number changed");
                Assert(Edit(window, line, "红线类型").Text == value, "Staged redline type cannot be edited again");
                Commit(window);
            } finally { window.Close(); }
        });
        check("platform_manual_level_confirmation_keeps_indoor_parking_storey",()=>{
            var rows=new[]{new ModelElementCheck{UniqueId="B1",Name="地下车库层",Entity="IfcBuildingStorey",LevelElevationM=-3},
                new ModelElementCheck{UniqueId="park",Entity="IfcSpace",Slot="室内停车场",LevelUniqueId="B1"}};
            var type=typeof(ManagerWindow).Assembly.GetType("Hifc.Revit.Management.PlanningWindow");
            var window=(Window)Activator.CreateInstance(type,Flags,null,new object[]{
                new ModelSnapshot{Title="室内停车场楼层",ModelKind="单体建筑",Elements=rows},new PlanningSettings{RuleProfileId=Profile}},null);
            try {
                var levels=((IEnumerable)Get(window,"LevelRows")).Cast<object>().ToArray();
                Assert((bool)Get(levels.Single(),"Included"),"indoor-only level was silently dropped when opening calculation settings");
                ((CheckBox)type.GetField("manualLevels",Flags).GetValue(window)).IsChecked=true;
                var settings=(PlanningSettings)type.GetMethod("BuildSettings",Flags).Invoke(window,null);
                Assert(settings.LevelsConfirmed&&settings.BuildingLevelUniqueIds.SequenceEqual(new[]{"B1"}),"manual save lost automatic indoor level");
            } finally { window.Close(); }
        });
        check("platform_profile_switch_refreshes_automatic_building_levels_before_manual_save",()=>{
            var window=LevelPlanning(new PlanningSettings());
            try {
                window.Show();Pump(window);
                Assert(LevelCheck(window,"1F").IsChecked==true&&LevelCheck(window,"B1").IsChecked==false,"legacy default scope is not isolated from indoor parking");
                Selector(window).SelectedValue=Profile;Pump(window);
                Assert(LevelCheck(window,"B1").IsChecked==true,"profile switch left the visible indoor-parking level unchecked");
                Selector(window).SelectedValue="";Pump(window);
                Assert(LevelCheck(window,"B1").IsChecked==false,"switching back retained the platform-only automatic level");
                Selector(window).SelectedValue=Profile;Pump(window);
                ((CheckBox)window.GetType().GetField("manualLevels",Flags).GetValue(window)).IsChecked=true;
                var saved=Saved(window);
                Assert(saved.LevelsConfirmed&&saved.BuildingLevelUniqueIds.OrderBy(s=>s).SequenceEqual(new[]{"1F","B1"}),"same-window profile switch and manual save lost an automatic level");
            } finally { window.Close(); }
        });
        check("platform_profile_switch_preserves_manual_level_edits",()=>{
            var window=LevelPlanning(new PlanningSettings{RuleProfileId=Profile,LevelsConfirmed=true,BuildingLevelUniqueIds=new[]{"1F"}});
            try {
                window.Show();Pump(window);
                var first=LevelCheck(window,"1F");first.IsChecked=false;first.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();
                var basement=LevelCheck(window,"B1");basement.IsChecked=true;basement.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();
                Selector(window).SelectedValue="";Selector(window).SelectedValue=Profile;Pump(window);
                Assert(LevelCheck(window,"1F").IsChecked==false&&LevelCheck(window,"B1").IsChecked==true,"profile switch overwrote explicit manual checkbox edits");
                var saved=Saved(window);
                Assert(saved.LevelsConfirmed&&saved.BuildingLevelUniqueIds.SequenceEqual(new[]{"B1"}),"manual level edits were changed before save");
            } finally { window.Close(); }
        });
        check("platform_profile_is_explicit_and_switching_preserves_unsaved_common_values", () => {
            var previous = new PlanningSettings(); previous.CommonValues["建筑技术|建筑类型名称"] = "旧模型用途";
            previous.CommonValues["未显示类目|保留值"] = "已有业务值";
            var window = Planning(previous);
            try {
                window.Show(); Pump(window); var selector = Selector(window);
                Assert((string)selector.SelectedValue == "", "Platform rules silently became the default for every model");
                Type(CommonEditor(window, "建筑编号"), "已确认楼栋 A");
                selector.SelectedValue = Profile; Pump(window);
                Assert((string)Get(Row(window, "建筑编号"), "Value") == "已确认楼栋 A", "Switching profile discarded the draft");
                Assert((string)Get(Row(window, "建筑类型名称"), "Value") == "旧模型用途", "Invalid legacy value was silently replaced");
                Assert(!string.IsNullOrEmpty((string)Get(Row(window, "建筑类型名称"), "ValueHint")), "Invalid legacy value has no local hint");
                selector.SelectedValue = ""; selector.SelectedValue = Profile; Pump(window);
                var saved = Saved(window);
                Assert((string)Get(saved, "RuleProfileId") == Profile && saved.CommonValues["建筑技术|建筑编号"] == "已确认楼栋 A" && saved.CommonValues["未显示类目|保留值"] == "已有业务值", "Profile or values were not retained");
                Assert(string.IsNullOrEmpty((string)Get(previous, "RuleProfileId")) && !previous.CommonValues.ContainsKey("建筑技术|建筑编号"), "Dialog mutated the existing model settings before save");
            } finally { window.Close(); }
        });
        check("platform_common_enums_link_parent_function_without_defaulting_child", () => {
            var window = Planning();
            try {
                window.Show(); Selector(window).SelectedValue = Profile; Pump(window);
                Assert(Rows(window).All(r => string.IsNullOrEmpty((string)Get(r, "Value"))), "Template defaults were inserted as business facts");
                Assert(Saved(window).CommonValues.Count==0,"Opening a platform profile created common business values");
                var parent = CommonEditor(window, "主功能类别"); Type(parent, "学校"); Pump(window);
                var child = CommonEditor(window, "子功能类别"); var choices = child.Items.Cast<string>().ToArray();
                Assert(choices.Contains("教室") && choices.Contains("办公室") && choices.Contains("图书馆") && choices.Contains("学生宿舍"), "School child functions were reduced to classrooms");
                Assert(string.IsNullOrEmpty(child.Text), "School selection silently selected a child function");
                Type(child, "办公室"); Type(CommonEditor(window, "主功能类别"), "住宅"); Pump(window);
                child = CommonEditor(window, "子功能类别");
                Assert(child.Text == "办公室" && !child.Items.Cast<string>().Contains("办公室"), "Changing parent erased the child or kept stale enum options");
                Assert(!string.IsNullOrEmpty((string)Get(Row(window, "子功能类别"), "ValueHint")), "Invalid parent-child pair is not shown locally");
                Type(CommonEditor(window, "主功能类别"), "学校"); Pump(window);
                Assert(CommonEditor(window, "子功能类别").Text == "办公室", "Valid old child cannot be edited again");
                Program.Screenshot(window, Path.Combine(output, "platform-common-values.png"));
            } finally { window.Close(); }
        });
        check("platform_building_and_green_choices_preserve_complete_rule_labels", () => {
            var window = Planning();
            try {
                window.Show(); Selector(window).SelectedValue = Profile; Pump(window);
                var types = CommonEditor(window, "建筑类型名称").Items.Cast<string>().Where(v => v != "").ToArray();
                Assert(types.SequenceEqual(new[] { "住宅", "公建", "中小学等", "幼儿园等" }), "Building type is mixed with use or legacy presets");
                var categories = CommonEditor(window, "建筑类别").Items.Cast<string>().ToArray();
                Assert(categories.Contains("门卫房") && categories.Contains("配电房"), "Duplicate enum IDs discarded a valid category");
                Assert(CommonEditor(window, "建筑用途名称").Items.Cast<string>().Where(v => v != "").SequenceEqual(new[] { "裙房", "塔楼", "中学" }), "Verified building use is absent");
                Assert(CommonEditor(window, "结构类型").Items.Cast<string>().Contains("框架,剪力墙"), "A comma inside an enum value was split");
            } finally { window.Close(); }
            window = Planning(kind: "总图");
            try {
                window.Show(); Selector(window).SelectedValue = Profile; Pump(window);
                var green = CommonEditor(window, "绿地类型");
                Assert(green.Items.Cast<string>().Count(v => v != "") == 8 && green.Text == "", "Green type is missing choices or silently defaults");
                Assert(string.IsNullOrEmpty((string)Get(Row(window, "折算系数","绿地"), "Value")), "Green factor silently defaulted to one");
            } finally { window.Close(); }
        });
        check("platform_school_usage_common_value_saves_and_reopens_without_civil_default", () => {
            var window = Planning();
            PlanningSettings saved;
            try {
                window.Show(); Selector(window).SelectedValue = Profile; Pump(window);
                var editor = CommonEditor(window, "建筑用途名称");
                Assert(editor.Items.Cast<string>().Contains("中学"), "School usage cannot be selected");
                Type(editor, "中学"); Pump(window);
                Assert(string.IsNullOrEmpty((string)Get(Row(window, "建筑用途名称"), "ValueHint")), "Verified usage still shows an invalid-value hint");
                saved = Saved(window);
                Assert(saved.CommonValues["建筑技术|建筑用途名称"] == "中学" && !saved.CommonValues.ContainsKey("建筑技术|民用建筑"), "Saving usage defaulted civil participation");
            } finally { window.Close(); }
            window = Planning(saved);
            try {
                window.Show(); Pump(window);
                Assert(CommonEditor(window, "建筑用途名称").Text == "中学", "Saved usage was lost on reopening");
            } finally { window.Close(); }
        });
        check("platform_missing_usage_is_red_in_overview_and_property_until_readback", () => {
            var fake = new FakeSession { Snapshot = Sample() };
            var building = fake.Snapshot.Elements[1];
            var field = building.Fields.Single(f => f.Property == "建筑用途名称");
            var window = new ManagerWindow(fake, Path.Combine(output, "platform-usage-red-" + Guid.NewGuid().ToString("N"))); fake.Window = window;
            try {
                window.Show(); window.StageSelector.SelectedItem = "报规"; window.KindSelector.SelectedItem = "单体建筑"; window.StartFullCheck();
                window.ElementGrid.SelectedItem = building; Pump(window);
                var list = (ListBox)typeof(ManagerWindow).GetField("roleList", Flags).GetValue(window);
                Func<object> role = () => list.Items.Cast<object>().Single(i => ((RoleCoverage)Get(i, "Role")).Slot == "建筑技术");
                Func<ManagerWindow.PropertyEditor> editor = () => window.FieldGrid.Items.Cast<ManagerWindow.PropertyEditor>().Single(r => r.Property == "建筑用途名称");
                Func<bool> roleRed = () => ((SolidColorBrush)Get(role(), "Foreground")).Color == (Color)ColorConverter.ConvertFromString("#B83138");
                Func<bool> fieldRed = () => ((SolidColorBrush)editor().Background).Color == (Color)ColorConverter.ConvertFromString("#FFFFDADA");
                Assert(roleRed() && fieldRed(), "Missing usage is not red in both overview and property");
                Assert(editor().SourceDetail.Contains("建筑用途名称"), "Missing-value explanation does not name the field");
                list.ScrollIntoView(role()); Pump(window);
                Program.Screenshot(window, Path.Combine(output, "platform-building-usage-required.png"));
                window.StageValue(field, "中学"); Pump(window);
                Assert(roleRed() && fieldRed(), "An unsaved draft removed the red indicator");
                window.RecordCommitted(new EditPreview { Changes = new[] { new EditChange { Action = "赋值", OwnerUniqueId = field.OwnerUniqueId, Source = field.Source, ExpectedRaw = "中学" } } }, window.DraftRequests);
                field.Value = "中学"; field.State = "有效"; window.Accept(fake.Snapshot); window.ElementGrid.SelectedItem = building; Pump(window);
                Assert(!roleRed() && !fieldRed(), "Written usage remained red after a fresh readback");
            } finally { window.Close(); }
        });
        check("platform_school_usage_property_editor_stages_only_usage", () => {
            var fake = new FakeSession { Snapshot = Sample() };
            var building = fake.Snapshot.Elements[1];
            building.Fields = building.Fields.Concat(new[] { Field(52, BuildingSet, "民用建筑", "0", "Boolean") }).ToArray();
            var window = new ManagerWindow(fake, Path.Combine(output, "platform-school-" + Guid.NewGuid().ToString("N"))); fake.Window = window;
            try {
                window.Show(); window.StageSelector.SelectedItem = "报规"; window.KindSelector.SelectedItem = "单体建筑"; window.StartFullCheck();
                var editor = Edit(window, building, "建筑用途名称");
                Assert(editor.Items.Cast<string>().Contains("中学"), "Property editor has no verified school option");
                Type(editor, "中学"); Commit(window);
                Assert(window.DraftRequests.Length == 1 && window.DraftRequests[0].Source == "建筑用途名称" && window.DraftRequests[0].Value == "中学", "Usage edit staged unrelated values");
                var row = window.FieldGrid.Items.Cast<ManagerWindow.PropertyEditor>().Single(r => r.Property == "建筑用途名称");
                Assert(string.IsNullOrEmpty((string)Get(row, "ValueHint")) && building.Fields.Last().Value == "0", "Usage edit remained invalid or changed civil participation");
                Assert(Edit(window, building, "建筑用途名称").Text == "中学", "Pending usage is not editable again");
                Commit(window); Program.Screenshot(window, Path.Combine(output, "platform-school-usage.png"));
            } finally { window.Close(); }
        });
        check("platform_property_editor_retains_invalid_existing_value_and_stages_only_user_edits", () => {
            var fake = new FakeSession { Snapshot = Sample() };
            var window = new ManagerWindow(fake, Path.Combine(output, "platform-edit-" + Guid.NewGuid().ToString("N"))); fake.Window = window;
            try {
                window.Show(); window.StageSelector.SelectedItem = "报规"; window.KindSelector.SelectedItem = "单体建筑"; window.StartFullCheck();
                var building = fake.Snapshot.Elements[1]; var combo = Edit(window, building, "建筑类型名称");
                Assert(combo.IsEditable && combo.Text == "厂房" && !combo.Items.Cast<string>().Contains("厂房"), "Invalid original value was erased or presented as a valid platform enum");
                var row = window.FieldGrid.Items.Cast<ManagerWindow.PropertyEditor>().Single(r => r.Property == "建筑类型名称");
                Assert(!string.IsNullOrEmpty((string)Get(row, "ValueHint")), "Existing invalid value has no local explanation");
                Assert(row.State!="有效","An enum warning still labels the value as valid");
                Commit(window); Assert(window.DraftRequests.Length == 0, "Entering an existing value created an unsolicited write");
                var display=window.FieldGrid.Columns[1].GetCellContent(row);
                var hint=Visuals<TextBlock>(display).Single(t=>t.Text==(string)Get(row,"ValueHint"));
                Assert(hint.IsVisible&&hint.ActualHeight>0&&display.ActualHeight>32,"Local enum hint is clipped by the old fixed row height");
                Program.Screenshot(window,Path.Combine(output,"platform-existing-value-warning.png"));
                combo = Edit(window, building, "建筑类型名称"); Type(combo, "中小学等"); Commit(window);
                Assert(window.DraftRequests.Single().Value == "中小学等" && building.Fields[0].Value == "厂房", "Edit did not stay in the existing draft workflow");
                Assert(Edit(window, building, "建筑类型名称").Text == "中小学等", "Staged value could not be edited again");
                Commit(window); Program.Screenshot(window, Path.Combine(output, "platform-property-editor.png"));
            } finally { window.Close(); }
        });
        check("platform_property_child_choices_follow_parent_drafts_and_boolean_preserves_zero", () => {
            var fake = new FakeSession { Snapshot = Sample() };
            var window = new ManagerWindow(fake, Path.Combine(output, "platform-linked-" + Guid.NewGuid().ToString("N"))); fake.Window = window;
            try {
                window.Show(); window.StageSelector.SelectedItem = "报规"; window.KindSelector.SelectedItem = "单体建筑"; window.StartFullCheck();
                var region = fake.Snapshot.Elements[0]; var child = Edit(window, region, "子功能类别");
                Assert(child.Text == "办公室" && child.Items.Cast<string>().Contains("图书馆"), "Existing school function could not be edited");
                Commit(window); var parent = Edit(window, region, "主功能类别"); Type(parent, "住宅"); Commit(window);
                child = Edit(window, region, "子功能类别");
                Assert(child.Text == "办公室" && child.Items.Cast<string>().Contains("住宅") && !child.Items.Cast<string>().Contains("办公室"), "Child enum did not follow the pending parent value");
                Assert(!string.IsNullOrEmpty((string)Get(window.FieldGrid.Items.Cast<ManagerWindow.PropertyEditor>().Single(r => r.Property == "子功能类别"), "ValueHint")), "Pending incompatible child has no local hint");
                Commit(window); Assert(window.DraftRequests.All(r => r.Source != "子功能类别"), "Parent change silently wrote the child value");
                var boolean = Edit(window, fake.Snapshot.Elements[2], "建筑基底");
                Assert((string)boolean.SelectedItem == "否" && boolean.Items.Cast<string>().SequenceEqual(new[] { "未填写", "是", "否" }), "Boolean zero was lost by platform enum routing");
                boolean.SelectedItem = "是"; Commit(window);
                Assert(window.DraftRequests.Any(r => r.Source == "建筑基底" && r.Value == "1"), "Boolean selection no longer uses raw 0/1");
            } finally { window.Close(); }
        });
        check("platform_profile_settings_preserve_model_drafts_and_unselected_scope_is_explicit", () => {
            var fake = new FakeSession { Snapshot = ManagerImprovementsTests.Sample() };
            var directory = Path.Combine(output, "platform-state-" + Guid.NewGuid().ToString("N"));
            var window = new ManagerWindow(fake, directory); fake.Window = window;
            try {
                window.Show(); window.StageSelector.SelectedItem = "报规"; window.KindSelector.SelectedItem = "总图"; window.StartFullCheck();
                Assert(window.DiagnosticText.Contains("旧案例参考") && window.DiagnosticText.Contains("未选择地区平台规则"), "Old reference rules are presented as selected platform compliance");
                var element = fake.Snapshot.Elements[0]; window.ElementGrid.SelectedItem = element;
                window.StageValue(element.Fields[0], "人工确认的场地名称");
                var settings = new PlanningSettings(); Set(settings, "RuleProfileId", Profile); window.SetPlanningSettings(settings);
                var state = new ManagementStateStore(directory, fake.Snapshot.DocumentKey).Load();
                Assert((string)Get(state.Planning, "RuleProfileId") == Profile && state.Drafts.Single().Value == "人工确认的场地名称", "Selecting a rule profile discarded a per-element draft");
                Assert(element.Fields[0].Value == "场地", "Saving settings wrote the model value before step 4");
            } finally { window.Close(); }
        });
    }
}
