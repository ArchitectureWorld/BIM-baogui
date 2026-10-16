using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Hifc.Revit.Management
{
    internal sealed class SelectionWindow : Window
    {
        readonly ISelectionSession session;
        readonly TextBlock summary = new TextBlock(), status = new TextBlock(), details = new TextBlock();
        readonly Button read = new Button { Content = "读取当前选择" }, write = new Button { Content = "写入所选构件" };
        bool restoring, busy, stale = true;
        internal SelectionDraft Draft { get; private set; }
        internal ComboBox StageSelector { get; } = new ComboBox { Width=110, ItemsSource=new[]{"报规","施工图"} };
        internal ComboBox KindSelector { get; } = new ComboBox { Width=125, ItemsSource=new[]{"总图","单体建筑"} };
        internal ComboBox SlotSelector { get; } = new ComboBox { MinWidth=180 };
        internal DataGrid FieldGrid { get; } = new DataGrid();
        internal DataGrid ObjectGrid { get; } = new DataGrid();
        internal string Stage => StageSelector.SelectedItem as string;
        internal string Kind => KindSelector.SelectedItem as string;
        internal bool CanWrite => !busy && !stale && Draft?.Slot != null && Draft.Snapshot.Elements.Length > 0;
        internal SelectionWindow(ISelectionSession session)
        {
            this.session=session; Title="H-IFC V1 · 选中构件赋属性"; Width=920; Height=740; MinWidth=780; MinHeight=590;
            WindowStartupLocation=WindowStartupLocation.CenterOwner; LiquidGlassTheme.Apply(this);
            var root=new Grid {Margin=new Thickness(18)}; Content=root;
            foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(125),GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto}) root.RowDefinitions.Add(new RowDefinition {Height=height});
            var title=LiquidGlassTheme.Title("选中构件赋属性");title.FontSize=21;title.Margin=new Thickness(0,0,0,12);root.Children.Add(title);
            var context=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};Grid.SetRow(context,1);root.Children.Add(context);
            context.Children.Add(Label("阶段"));context.Children.Add(StageSelector);context.Children.Add(Label("模型类型"));context.Children.Add(KindSelector);
            read.Margin=new Thickness(18,0,0,0);context.Children.Add(read);read.Click+=(s,e)=>{if(ConfirmDiscard())session.Read();};
            StageSelector.SelectionChanged+=(s,e)=>ContextChanged();KindSelector.SelectionChanged+=(s,e)=>ContextChanged();
            foreach(var combo in new[]{StageSelector,KindSelector,SlotSelector}){combo.Height=36;combo.VerticalContentAlignment=VerticalAlignment.Center;}
            Configure(ObjectGrid,true);ObjectGrid.Margin=new Thickness(0,0,0,12);
            ObjectGrid.Columns.Add(Column("构件", "Name", 1));ObjectGrid.Columns.Add(Column("Revit 类别","Category",140,false));ObjectGrid.Columns.Add(Column("ID","Id",85,false));ObjectGrid.Columns.Add(Column("当前类目","Slot",140,false));
            ObjectGrid.Columns[0].MinWidth=235;
            Grid.SetRow(ObjectGrid,2);root.Children.Add(ObjectGrid);
            var classification=new Grid {Margin=new Thickness(0,0,0,10)};classification.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});classification.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(210)});classification.ColumnDefinitions.Add(new ColumnDefinition());
            classification.Children.Add(Label("指定业务类目"));Grid.SetColumn(SlotSelector,1);classification.Children.Add(SlotSelector);
            summary.Margin=new Thickness(14,0,0,0);summary.VerticalAlignment=VerticalAlignment.Center;summary.TextWrapping=TextWrapping.Wrap;Grid.SetColumn(summary,2);classification.Children.Add(summary);Grid.SetRow(classification,3);root.Children.Add(classification);
            SlotSelector.SelectionChanged+=(s,e)=>{
                if(restoring || SlotSelector.SelectedItem==null)return;
                CommitEditors();
                if(!ConfirmDiscard()){restoring=true;SlotSelector.SelectedItem=Draft?.Slot;restoring=false;return;}
                session.Classify((string)SlotSelector.SelectedItem);
            };
            Configure(FieldGrid,false);FieldGrid.SelectionUnit=DataGridSelectionUnit.CellOrRowHeader;
            FieldGrid.Columns.Add(Column("属性","Property",200,false));
            var value=new ValueColumn {Header="值（单击修改）",MinWidth=180,Binding=new Binding("Value") {Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true},Width=new DataGridLength(1,DataGridLengthUnitType.Star)};
            var cellStyle=new Style(typeof(DataGridCell));cellStyle.Setters.Add(new Setter(Control.BackgroundProperty,new Binding("Background")));value.CellStyle=cellStyle;FieldGrid.Columns.Add(value);
            FieldGrid.Columns.Add(Column("状态","State",110,false));FieldGrid.Columns.Add(Column("来源 / 说明","Detail",230,false));
            FieldGrid.BeginningEdit+=(s,e)=>{if(!CanWrite || !(e.Row.Item is SelectionField f) || !f.Editable)e.Cancel=true;};
            FieldGrid.PreviewMouseLeftButtonDown+=(s,e)=>{
                var cell=Ancestor<DataGridCell>(e.OriginalSource as DependencyObject);
                if(cell==null || cell.Column!=value || !(cell.DataContext is SelectionField f) || !f.Editable || !CanWrite || cell.IsEditing)return;
                FieldGrid.CurrentCell=new DataGridCellInfo(f,value);cell.Focus();FieldGrid.BeginEdit(e);
            };
            Grid.SetRow(FieldGrid,4);root.Children.Add(FieldGrid);
            details.TextWrapping=TextWrapping.Wrap;details.Margin=new Thickness(0,10,0,8);details.Foreground=LiquidGlassTheme.Color("#645B48");Grid.SetRow(details,5);root.Children.Add(details);
            var footer=new DockPanel();Grid.SetRow(footer,6);root.Children.Add(footer);write.Margin=new Thickness(12,0,0,0);LiquidGlassTheme.Primary(write);DockPanel.SetDock(write,Dock.Right);footer.Children.Add(write);
            status.Text="先在 Revit 中选择构件，再读取当前选择。";status.TextWrapping=TextWrapping.Wrap;status.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(status);
            write.Click+=(s,e)=>{try{CommitEditors();if(!CanWrite)throw new InvalidOperationException("请重新读取选择并指定业务类目。");session.Write(Draft.BuildRequests());}catch(Exception ex){SetStatus(ex.Message,true);}};
            Closed+=(s,e)=>session.Close();UpdateButtons();
        }
        static TextBlock Label(string text)=>new TextBlock {Text=text,Margin=new Thickness(0,0,9,0),VerticalAlignment=VerticalAlignment.Center};
        static DataGridTextColumn Column(string title,string binding,double width,bool star=true)=>new DataGridTextColumn {Header=title,Binding=new Binding(binding),IsReadOnly=true,Width=new DataGridLength(width,star?DataGridLengthUnitType.Star:DataGridLengthUnitType.Pixel)};
        static void Configure(DataGrid grid,bool readOnly){grid.AutoGenerateColumns=false;grid.IsReadOnly=readOnly;grid.CanUserAddRows=false;grid.CanUserDeleteRows=false;grid.RowHeight=34;grid.Background=Brushes.White;grid.HeadersVisibility=DataGridHeadersVisibility.Column;}
        static T Ancestor<T>(DependencyObject item) where T:DependencyObject {while(item!=null){if(item is T result)return result;item=VisualTreeHelper.GetParent(item);}return null;}
        bool ConfirmDiscard()=>Draft?.Dirty!=true || MessageBox.Show(this,"当前有未写入的分类或属性修改。是否放弃这些修改并重新读取？","选中构件赋属性",MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.OK;
        void ContextChanged(){if(!restoring)MarkStale("阶段或模型类型已变化，请重新读取当前选择。");}
        internal void InitializeContext(string stage,string kind){restoring=true;StageSelector.SelectedItem=stage;KindSelector.SelectedItem=kind;restoring=false;}
        internal void Accept(SelectionReadResult result)
        {
            restoring=true;try{Draft=new SelectionDraft(result.Snapshot);ObjectGrid.ItemsSource=result.Snapshot.Elements;SlotSelector.ItemsSource=result.Slots;SlotSelector.SelectedItem=null;FieldGrid.ItemsSource=null;}finally{restoring=false;}
            Draft.Changed+=UpdateButtons;stale=false;SetBusy(false);summary.Text="读取 "+result.SelectedCount+" 个 · 可处理 "+result.Snapshot.Elements.Length+" 个";
            details.Text=string.Join("\n",result.Excluded)+(result.Slots.Length==0?"\n所选构件没有共同的业务类目，请按载体分批选择。":"\n业务值只写入上方列出的实例；共享类型属性仅查看。");
            SetStatus("选择业务类目后自动读取和补齐属性，已有值也可直接修改。");UpdateButtons();
        }
        internal void ShowClassification(string slot,EditPreview preview,ManualWrite[] history)
        {
            Draft.SetClassification(slot,preview.ProposedElements);Draft.UpdateHistory(history);FieldGrid.ItemsSource=Draft.Fields;
            restoring=true;SlotSelector.SelectedItem=slot;restoring=false;stale=false;SetBusy(false);
            details.Text="将处理 "+Draft.Snapshot.Elements.Length+" 个实例；清理旧类目专属值 "+preview.Changes.Count(c=>c.Action=="清空")+" 项，补充参数 "+preview.Changes.Count(c=>c.Action=="新增参数")+" 项。";
            SetStatus(preview.Conflicts.Length>0?string.Join("；",preview.Conflicts):"自动值按每个构件分别计算；“多个值”仅为显示提示。",preview.Conflicts.Length>0);UpdateButtons();
        }
        internal void SetBusy(bool value){busy=value;StageSelector.IsEnabled=KindSelector.IsEnabled=SlotSelector.IsEnabled=!value;FieldGrid.IsEnabled=!value;UpdateButtons();}
        internal void MarkStale(string text){stale=true;SetStatus(text,true);UpdateButtons();}
        internal void SetStatus(string text,bool error=false){status.Text=text;status.Foreground=LiquidGlassTheme.Color(error?"#B83138":"#26465B");}
        void UpdateButtons(){write.IsEnabled=CanWrite;read.IsEnabled=!busy;}
        internal void CommitEditors(){if(!FieldGrid.CommitEdit(DataGridEditingUnit.Cell,true)||!FieldGrid.CommitEdit(DataGridEditingUnit.Row,true))throw new InvalidOperationException("请修正当前属性值后再写入。");}
        sealed class ValueColumn:DataGridTextColumn
        {
            protected override FrameworkElement GenerateEditingElement(DataGridCell cell,object item)
            {
                if(!((SelectionField)item).RawType.Equals("Boolean",StringComparison.Ordinal))return base.GenerateEditingElement(cell,item);
                var combo=new ComboBox {ItemsSource=((SelectionField)item).Choices,VerticalContentAlignment=VerticalAlignment.Center};
                combo.SetBinding(ComboBox.SelectedItemProperty,new Binding("Value") {Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true});return combo;
            }
            protected override bool CommitCellEdit(FrameworkElement element){if(element is ComboBox combo){combo.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();return !Validation.GetHasError(combo);}return base.CommitCellEdit(element);}
        }
    }
}
