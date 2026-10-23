using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class ExportAreaReviewItem
    {
        public int ElementId { get; set; }
        public string Name { get; set; }
        public string PropertySet { get; set; }
        public string Property { get; set; }
        public string SourceValue { get; set; }
        public string ExportValue { get; set; }
        public string Reason { get; set; }
    }
    internal sealed class ExportReviewData
    {
        public string Title { get; set; }
        public string ProfileId { get; set; }
        public string OutputPath { get; set; }
        public InputReviewItem[] Inputs { get; set; } = new InputReviewItem[0];
        public ExportAreaReviewItem[] Areas { get; set; } = new ExportAreaReviewItem[0];
        public string[] BlockingReasons { get; set; } = new string[0];
    }
    internal sealed class ExportReviewWindow : Window
    {
        internal DataGrid InputGrid { get; }
        internal DataGrid AreaGrid { get; }
        internal event Action<int[]> LocateRequested;
        readonly TabControl tabs;
        readonly TextBlock status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
        internal ExportReviewWindow(ExportReviewData data)
        {
            NameScope.SetNameScope(this,new NameScope());
            Title="导出前核对 · "+data.Title;Width=1180;Height=720;MinWidth=900;MinHeight=560;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;LiquidGlassTheme.Apply(this);
            var root=new DockPanel {Margin=new Thickness(20),Background=Background};Content=root;
            var intro=new TextBlock {Text="将导出当前项目全部模型。下列缺项可能影响平台分类和计算；默认值仅用于说明，不会自动写入模型。\n面积采用已写入 Revit 的值；楼层面积优先，同层区域按已确认面积规则同步。单位：㎡。",
                TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
            DockPanel.SetDock(intro,Dock.Top);root.Children.Add(intro);
            if(!string.IsNullOrEmpty(data.OutputPath)) {
                var destination=new TextBlock {Name="ExportDestinationText",Text="输出文件："+data.OutputPath,
                    TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
                RegisterName(destination.Name,destination);DockPanel.SetDock(destination,Dock.Top);root.Children.Add(destination);
            }
            var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            status.Text="需核对输入 "+data.Inputs.Length+" 项；面积值 "+data.Areas.Length+" 项。"+
                (PlatformRules.IsActive(data.ProfileId)?"":" 当前为旧案例参考，平台默认值未评估。")+
                (data.BlockingReasons.Length==0?"":"\n存在无法确定的面积或范围，请定位核对并返回修改后再导出。");
            footer.Children.Add(status);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(buttons);
            var locate=new Button{Content="定位选中对象",Margin=new Thickness(0,0,10,0),Padding=new Thickness(14,8,14,8)};
            locate.Click+=(s,e)=>LocateSelection();buttons.Children.Add(locate);
            var back=new Button{Content="返回修改",IsCancel=true,Margin=new Thickness(0,0,10,0),Padding=new Thickness(14,8,14,8)};
            buttons.Children.Add(back);
            var proceed=new Button{Name="ContinueExportButton",Content="确认以上内容，继续导出",Padding=new Thickness(14,8,14,8),IsEnabled=data.BlockingReasons.Length==0};
            RegisterName(proceed.Name,proceed);
            proceed.Click+=(s,e)=>{if(data.BlockingReasons.Length==0)DialogResult=true;};buttons.Children.Add(proceed);
            tabs=new TabControl();root.Children.Add(tabs);
            InputGrid=Table(data.Inputs);Column(InputGrid,"构件 ID","ElementId",85);Column(InputGrid,"对象","Name",150);
            Column(InputGrid,"字段","Property",130);Column(InputGrid,"当前值","CurrentText",95);
            Column(InputGrid,"平台可能默认","DefaultText",155);Column(InputGrid,"说明","Detail",410);
            tabs.Items.Add(new TabItem{Header="输入核对（"+data.Inputs.Length+"）",Content=InputGrid});
            AreaGrid=Table(data.Areas);Column(AreaGrid,"构件 ID","ElementId",85);Column(AreaGrid,"对象","Name",160);
            Column(AreaGrid,"面积字段","Property",110);Column(AreaGrid,"模型当前值 ㎡","SourceValue",135);
            Column(AreaGrid,"本次 IFC 值 ㎡","ExportValue",135);Column(AreaGrid,"取值依据","Reason",390);
            tabs.Items.Add(new TabItem{Header="面积导出值（"+data.Areas.Length+"）",Content=AreaGrid});
            InputGrid.MouseDoubleClick+=(s,e)=>LocateSelection();AreaGrid.MouseDoubleClick+=(s,e)=>LocateSelection();
        }
        internal void LocateSelection()
        {
            var ids=tabs.SelectedIndex==1?AreaGrid.SelectedItems.Cast<ExportAreaReviewItem>().Select(r=>r.ElementId)
                :InputGrid.SelectedItems.Cast<InputReviewItem>().Select(r=>r.ElementId);
            try{LocateRequested?.Invoke(ids.Distinct().ToArray());}
            catch(Exception error){status.Text="定位未完成："+error.Message;}
        }
        static DataGrid Table(object rows)=>new DataGrid{ItemsSource=(System.Collections.IEnumerable)rows,IsReadOnly=true,
            AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,SelectionMode=DataGridSelectionMode.Extended,
            HeadersVisibility=DataGridHeadersVisibility.Column,RowHeight=double.NaN};
        static void Column(DataGrid grid,string title,string path,double width)=>grid.Columns.Add(new DataGridTextColumn{
            Header=title,Binding=new Binding(path),Width=width,ElementStyle=Wrap()});
        static Style Wrap(){var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));return style;}
    }
}
