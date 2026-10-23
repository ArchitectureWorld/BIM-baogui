using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class PlanningRoleRow
    {
        public ModelElementCheck Model { get; set; }
        public bool Included { get; set; }
        public string Slot { get; set; }
        public int Id => Model.Id;
        public string Name => Model.Name;
        public string Level => Model.Level;
        public string Area => Model.ProjectedAreaM2?.ToString("0.######")??"未取得";
        public string Note { get; set; }
    }
    internal sealed class PlanningValueRow : INotifyPropertyChanged
    {
        public ReferenceProperty Definition { get; set; }
        public string RuleProfileId { get; set; }
        internal Func<string> ParentFunction { get; set; }
        internal Action<PlanningValueRow> Changed { get; set; }
        string value;
        public string Slot => ReferenceCatalog.LegacySlot(Definition.PropertySet);
        public string Property => Definition.Property;
        public string Key => Slot+"|"+Property;
        public string Value
        {
            get => value;
            set { if(this.value==value)return; this.value=value; Notify("Value","EditorValue","ValueHint"); Changed?.Invoke(this); }
        }
        public string EditorValue
        {
            get => Definition.ExportType=="Boolean" ? Value=="1"?"是":Value=="0"?"否":Value : Value;
            set => Value=Definition.ExportType=="Boolean" ? value=="是"?"1":value=="否"?"0":value : value;
        }
        public string[] Choices => Definition.ExportType=="Boolean" ? new[]{"","是","否"} : RuleInputOptions.Choices(RuleProfileId,Definition.PropertySet,Property,ParentFunction?.Invoke());
        public string ValueHint => RuleInputOptions.ValueHint(RuleProfileId,Definition.PropertySet,Property,Value,ParentFunction?.Invoke());
        public event PropertyChangedEventHandler PropertyChanged;
        internal void Notify(params string[] names) { foreach(var name in names)PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name)); }
    }
    internal sealed class PlanningLevelRow
    {
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public double? Elevation { get; set; }
        public bool Included { get; set; }
    }
    internal sealed class PlanningWindow:Window
    {
        readonly ModelSnapshot snapshot;
        readonly PlanningSettings previous;
        readonly Dictionary<string,string> commonDraft;
        string ruleProfileId;
        readonly DataGrid roles,values,levelGrid;
        internal readonly ComboBox ProfileSelector=new ComboBox {DisplayMemberPath="Title",SelectedValuePath="Id",MinWidth=300};
        readonly TextBlock ruleScope=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,10)};
        readonly CheckBox manualLevels=new CheckBox {Content="手工指定实际建筑楼层（未勾选时，按已确认建筑区域及适用室内停车场确定）",Margin=new Thickness(0,0,0,12)};
        readonly ComboBox upper=new ComboBox {DisplayMemberPath="Name",SelectedValuePath="UniqueId",MinWidth=190};
        readonly TextBlock error=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,8)};
        internal PlanningRoleRow[] RoleRows { get; }
        internal PlanningValueRow[] ValueRows { get; private set; }
        internal PlanningLevelRow[] LevelRows { get; }
        internal PlanningSettings Result { get; private set; }
        internal event Action<int[]> LocateRequested;
        internal PlanningWindow(ModelSnapshot snapshot,PlanningSettings settings)
        {
            this.snapshot=snapshot;previous=settings??new PlanningSettings();
            commonDraft=new Dictionary<string,string>(previous.CommonValues??new Dictionary<string,string>());
            ruleProfileId=previous.RuleProfileId??"";
            Title="报规计算设置 · "+snapshot.Title;Width=1020;Height=700;MinWidth=780;MinHeight=530;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;LiquidGlassTheme.Apply(this);
            RoleRows=snapshot.Elements.Where(r=>string.IsNullOrWhiteSpace(r.Slot))
                .Select(r=>new {Row=r,Candidates=CaseWorkflow.GetCandidates(r,snapshot.ModelKind)
                    .Where(c=>c.Slot=="建筑区域"||c.Slot=="规划净用地").ToArray()})
                .Where(x=>x.Candidates.Length==1).Select(x=>new PlanningRoleRow {
                    Model=x.Row,Slot=x.Candidates[0].Slot,
                    Included=(previous.Roles??new PlanningRoleChoice[0]).Any(c=>c.UniqueId==x.Row.UniqueId&&c.Identity==PlanningInputs.Identity(x.Row)&&c.Slot==x.Candidates[0].Slot),
                    Note=x.Candidates[0].Evidence+(snapshot.Elements.Count(r=>!string.IsNullOrEmpty(r.ProjectionKey)&&r.ProjectionKey==x.Row.ProjectionKey)>1?"；存在同投影对象，请核对重复计量":"")
                }).ToArray();
            ValueRows=CreateValueRows();
            LevelRows=snapshot.Elements.Where(r=>r.Entity=="IfcBuildingStorey").OrderBy(r=>r.LevelElevationM)
                .Select(r=>new PlanningLevelRow{UniqueId=r.UniqueId,Name=r.Name,Elevation=r.LevelElevationM,
                    Included=previous.LevelsConfirmed?(previous.BuildingLevelUniqueIds??new string[0]).Contains(r.UniqueId):
                        PlanningInputs.DefaultBuildingLevels(snapshot.Elements,ruleProfileId).Contains(r.UniqueId)}).ToArray();
            var root=new Grid {Margin=new Thickness(20),Background=Background};Content=root;
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            var introduction=new StackPanel();
            var profileLine=new StackPanel {Orientation=Orientation.Horizontal};
            profileLine.Children.Add(new TextBlock {Text="规则方案：",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)});
            var profiles=new List<RuleProfileOption> {new RuleProfileOption {Id="",Title="旧案例参考"},new RuleProfileOption {Id=PlatformRules.ProfileId,Title=RuleInputOptions.PlatformTitle}};
            if(!string.IsNullOrEmpty(ruleProfileId)&&!PlatformRules.IsActive(ruleProfileId))profiles.Add(new RuleProfileOption {Id=ruleProfileId,Title="原有规则方案（当前未支持）"});
            ProfileSelector.ItemsSource=profiles;ProfileSelector.SelectedValue=ruleProfileId;profileLine.Children.Add(ProfileSelector);
            introduction.Children.Add(profileLine);ruleScope.Text=RuleInputOptions.Scope(ruleProfileId);introduction.Children.Add(ruleScope);
            introduction.Children.Add(new TextBlock {Text="先确认哪些对象用于报规计算，再设置有来源的共用业务值。保存后重新全面检查，点击“4 写入 Revit”才写入模型。\n统一设置只补空值；已有人工值保留。建筑用途名称、消防道路宽度缺失时标红，写入并复检后解除。用途按实际填写，消防道路宽度需手填。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
            root.Children.Add(introduction);
            var tabs=new TabControl{Background=Background,BorderThickness=new Thickness(0),Padding=new Thickness(6,12,6,6)};
            var tabStyle=new Style(typeof(TabItem));tabStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,8,12,8)));
            tabs.Resources[typeof(TabItem)]=tabStyle;Grid.SetRow(tabs,1);root.Children.Add(tabs);
            roles=Table(RoleRows);
            roles.Columns.Add(new DataGridCheckBoxColumn{Header="确认用途",Binding=new Binding("Included"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=80});
            Column(roles,"ID","Id",85);Column(roles,"名称","Name",160);Column(roles,"拟定类目","Slot",115);Column(roles,"所属楼层","Level",100);Column(roles,"投影面积 m²","Area",120);Column(roles,"依据 / 提示","Note",350);
            var rolePanel=new DockPanel();
            var locate=new Button {Content="定位选中对象",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,10)};
            locate.Click+=(s,e)=>{var ids=roles.SelectedItems.Cast<PlanningRoleRow>().Select(r=>r.Id).ToArray();if(ids.Length>0)LocateRequested?.Invoke(ids);};
            DockPanel.SetDock(locate,Dock.Top);rolePanel.Children.Add(locate);rolePanel.Children.Add(roles);
            tabs.Items.Add(new TabItem{Header="计算范围",Content=rolePanel});
            values=Table(ValueRows);values.RowHeight=double.NaN;values.MinRowHeight=32;Column(values,"应用类目","Slot",160);Column(values,"共用属性","Property",180);
            var editor=new FrameworkElementFactory(typeof(ComboBox));
            editor.SetValue(ComboBox.IsEditableProperty,true);editor.SetValue(ComboBox.IsTextSearchEnabledProperty,false);
            editor.SetBinding(ComboBox.ItemsSourceProperty,new Binding("Choices"));
            editor.SetBinding(ComboBox.TextProperty,new Binding("EditorValue"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
            var valuePanel=new FrameworkElementFactory(typeof(StackPanel));valuePanel.AppendChild(editor);
            var hint=new FrameworkElementFactory(typeof(TextBlock));hint.SetBinding(TextBlock.TextProperty,new Binding("ValueHint"));
            hint.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);hint.SetValue(TextBlock.ForegroundProperty,LiquidGlassTheme.Color("#8A602B"));
            var hintStyle=new Style(typeof(TextBlock));var emptyHint=new DataTrigger {Binding=new Binding("ValueHint"),Value=""};
            emptyHint.Setters.Add(new Setter(VisibilityProperty,Visibility.Collapsed));hintStyle.Triggers.Add(emptyHint);hint.SetValue(FrameworkElement.StyleProperty,hintStyle);
            valuePanel.AppendChild(hint);
            values.Columns.Add(new DataGridTemplateColumn{Header="统一补齐值（仅填空值，可直接输入）",CellTemplate=new DataTemplate{VisualTree=valuePanel},Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
            tabs.Items.Add(new TabItem{Header="项目共用值",Content=values});
            levelGrid=Table(LevelRows);levelGrid.IsEnabled=previous.LevelsConfirmed;
            levelGrid.Columns.Add(new DataGridCheckBoxColumn{Header="实际楼层",Binding=new Binding("Included"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=95});
            Column(levelGrid,"楼层 / 标高","Name",330);Column(levelGrid,"项目标高 m","Elevation",170);
            var levelPanel=new DockPanel();manualLevels.IsChecked=previous.LevelsConfirmed;
            manualLevels.Checked+=(s,e)=>levelGrid.IsEnabled=true;manualLevels.Unchecked+=(s,e)=>levelGrid.IsEnabled=false;
            DockPanel.SetDock(manualLevels,Dock.Top);levelPanel.Children.Add(manualLevels);
            var topLine=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};
            topLine.Children.Add(new TextBlock{Text="最高层上边界：",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)});
            upper.ItemsSource=new[]{new PlanningLevelRow{UniqueId="",Name="未确认（不代填顶层高度）"}}.Concat(LevelRows).ToArray();upper.SelectedValue=previous.TopBoundaryLevelUniqueId??"";topLine.Children.Add(upper);
            DockPanel.SetDock(topLine,Dock.Top);levelPanel.Children.Add(topLine);levelPanel.Children.Add(levelGrid);
            if(snapshot.ModelKind=="单体建筑")tabs.Items.Add(new TabItem{Header="实际楼层",Content=levelPanel});
            tabs.Items.Add(new TabItem{Header="本轮计算核对",Content=new ScrollViewer{Content=new TextBlock{
                Text=string.Join("\n\n",snapshot.CalculationIssues??new string[0]),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12)}}});
            Grid.SetRow(error,2);root.Children.Add(error);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(buttons,3);root.Children.Add(buttons);
            var cancel=new Button{Content="取消",MinWidth=90,IsCancel=true,Margin=new Thickness(0,0,10,0)};cancel.Click+=(s,e)=>Close();buttons.Children.Add(cancel);
            var save=new Button{Content="保存设置并全面检查",MinWidth=180};LiquidGlassTheme.Primary(save);buttons.Children.Add(save);
            save.Click+=(s,e)=>{try {Result=BuildSettings();DialogResult=true;}catch(InvalidOperationException ex){error.Text=ex.Message;}};
            ProfileSelector.SelectionChanged+=(s,e)=>{
                values.CommitEdit(DataGridEditingUnit.Cell,true);values.CommitEdit(DataGridEditingUnit.Row,true);CaptureValues();
                ruleProfileId=ProfileSelector.SelectedValue as string??"";ValueRows=CreateValueRows();values.ItemsSource=ValueRows;
                if(manualLevels.IsChecked!=true) {
                    var included=PlanningInputs.DefaultBuildingLevels(snapshot.Elements,ruleProfileId);
                    foreach(var level in LevelRows)level.Included=included.Contains(level.UniqueId);
                    levelGrid.Items.Refresh();
                }
                ruleScope.Text=RuleInputOptions.Scope(ruleProfileId);error.Text="";
            };
        }
        PlanningValueRow[] CreateValueRows()
        {
            var rows=PlanningInputs.CommonFields(snapshot.ModelKind,ruleProfileId).Select(p=>new PlanningValueRow {
                Definition=p,RuleProfileId=ruleProfileId,Value=commonDraft.TryGetValue(ReferenceCatalog.LegacySlot(p.PropertySet)+"|"+p.Property,out var value)?value:""
            }).ToArray();
            foreach(var row in rows) {
                row.ParentFunction=()=>rows.FirstOrDefault(r=>PlatformRules.CanonicalPropertySet(r.Definition.PropertySet)==PlatformRules.CanonicalPropertySet(row.Definition.PropertySet)&&RuleInputOptions.IsParentFunction(r.Property))?.Value;
                row.Changed=changed=>{if(RuleInputOptions.IsParentFunction(changed.Property))foreach(var child in rows.Where(r=>RuleInputOptions.IsChildFunction(r.Property)&&r.Definition.PropertySet==changed.Definition.PropertySet))child.Notify("Choices","ValueHint");};
            }
            return rows;
        }
        void CaptureValues() { foreach(var row in ValueRows) { if(string.IsNullOrWhiteSpace(row.Value))commonDraft.Remove(row.Key);else commonDraft[row.Key]=row.Value; } }
        static DataGrid Table(object rows)
        {
            var grid=new DataGrid{ItemsSource=(System.Collections.IEnumerable)rows,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,SelectionMode=DataGridSelectionMode.Extended};
            LiquidGlassTheme.Table(grid);return grid;
        }
        static void Column(DataGrid grid,string name,string path,double width)=>grid.Columns.Add(new DataGridTextColumn{Header=name,Binding=new Binding(path),IsReadOnly=true,Width=width});
        internal PlanningSettings BuildSettings()
        {
            foreach(var grid in new[]{roles,values,levelGrid}){grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);}
            var selected=RoleRows.Where(r=>r.Included).ToArray();
            var land=selected.Where(r=>r.Slot=="规划净用地").Select(r=>new ModelElementCheck{Id=r.Id,Name=r.Name,Slot=r.Slot,ProjectionKey=r.Model.ProjectionKey})
                .Concat(snapshot.Elements.Where(r=>r.Slot=="规划净用地")).ToArray();
            var duplicates=PlanningInputs.DuplicateLandIssues(land);
            if(duplicates.Length>0)throw new InvalidOperationException(string.Join("\n",duplicates));
            CaptureValues();
            var common=new Dictionary<string,string>(commonDraft);
            foreach(var row in ValueRows) {
                if(string.IsNullOrWhiteSpace(row.Value)){common.Remove(row.Key);continue;}
                var value=row.Value.Trim();
                if(!ModelChecks.ValueIsValid(row.Definition.ExportType,value) ||
                    row.Definition.ExportType=="Real"&&double.Parse(value,System.Globalization.CultureInfo.InvariantCulture)<0)
                    throw new InvalidOperationException(row.Property+" 的格式不正确；系数需为非负数。");
                common[row.Key]=value;
            }
            if(manualLevels.IsChecked==true && !LevelRows.Any(r=>r.Included))throw new InvalidOperationException("请至少选择一个实际建筑楼层，或取消手工指定。");
            return new PlanningSettings{
                RuleProfileId=ruleProfileId,
                Roles=selected.Select(r=>new PlanningRoleChoice{UniqueId=r.Model.UniqueId,Slot=r.Slot,Identity=PlanningInputs.Identity(r.Model)}).ToArray(),
                CommonValues=common,LevelsConfirmed=manualLevels.IsChecked==true,
                BuildingLevelUniqueIds=LevelRows.Where(r=>r.Included).Select(r=>r.UniqueId).ToArray(),TopBoundaryLevelUniqueId=upper.SelectedValue as string};
        }
    }
}
