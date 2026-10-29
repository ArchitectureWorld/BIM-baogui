using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hifc.Reference;
using Microsoft.Win32;

namespace Hifc.Revit.Management
{
    internal interface IRedlineSession
    {
        RedlineReport Compare(RedlineData data,double toleranceM,double minimumGapM);
        void Save(RedlineData data);
    }
    internal sealed class RedlineWindow:Window
    {
        sealed class Choice {public object Value{get;set;}public string Name{get;set;}}
        readonly IRedlineSession session;
        readonly DataGrid pointGrid=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeight=30};
        readonly TextBlock source=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,10)};
        readonly TextBlock status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,8)};
        readonly TextBox report=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(12),BorderThickness=new Thickness(0)};
        readonly TabControl tabs=new TabControl();
        bool loading;
        internal readonly ComboBox UnitSelector=new ComboBox();
        internal readonly ComboBox FrameSelector=new ComboBox();
        internal readonly ComboBox AxisSelector=new ComboBox();
        internal readonly CheckBox ManualConfirmed=new CheckBox();
        internal readonly TextBox Tolerance=new TextBox{Text="0.01"};
        internal readonly TextBox MinimumGap=new TextBox{Text="0.001"};
        internal readonly TextBox CoordinateSystem=new TextBox();
        internal RedlineData Current {get;private set;}
        internal RedlineReport LastReport {get;private set;}
        internal string StatusMessage {get;private set;}
        internal RedlineWindow(IRedlineSession session,string title,RedlineData initial=null)
        {
            this.session=session??throw new ArgumentNullException(nameof(session));
            Title="红线人工资料及双载体核对 · "+title;Width=1100;Height=760;MinWidth=960;MinHeight=640;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;LiquidGlassTheme.Apply(this);
            var root=new Grid{Margin=new Thickness(20),Background=Background};Content=root;
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var intro=new TextBlock{Text="导入人工填写的原红线模板，确认坐标口径后只读比对。规划净用地与净用地控制红线须外轮廓/水平位置一致、实际实体竖向分离。保存仅写本模型的人工项目资料。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};root.Children.Add(intro);
            var toolbar=new WrapPanel{Margin=new Thickness(0,0,0,8)};Grid.SetRow(toolbar,1);root.Children.Add(toolbar);
            AddButton(toolbar,"导入人工坐标 XLSX",()=>{
                var dialog=new OpenFileDialog{Filter="人工红线坐标表 (*.xlsx)|*.xlsx",Title="选择人工填写的规划红线对比坐标点.xlsx"};
                if(dialog.ShowDialog(this)==true)ImportManualFile(dialog.FileName);
            });
            AddButton(toolbar,"保存人工资料到本模型",SaveData);
            AddButton(toolbar,"另存已确认人工坐标表",()=>{
                var dialog=new SaveFileDialog{Filter="Excel 坐标表 (*.xlsx)|*.xlsx",FileName="规划红线对比坐标点_已确认人工.xlsx",Title="另存人工红线坐标表（保留原模板表头）"};
                if(dialog.ShowDialog(this)==true)ExportManualFile(dialog.FileName);
            });
            var compare=AddButton(toolbar,"只读核对模型",CompareNow);LiquidGlassTheme.Primary(compare);
            var metadata=new StackPanel{Margin=new Thickness(0,0,0,10)};Grid.SetRow(metadata,2);root.Children.Add(metadata);
            Choices(UnitSelector,new Choice{Value=RedlineUnit.Unknown,Name="单位未确认"},new Choice{Value=RedlineUnit.Meters,Name="米（m）"},new Choice{Value=RedlineUnit.Millimeters,Name="毫米（mm）"});
            Choices(FrameSelector,new Choice{Value=RedlineFrame.Unknown,Name="坐标框架未确认"},new Choice{Value=RedlineFrame.Internal,Name="Revit 内部坐标"},new Choice{Value=RedlineFrame.Shared,Name="当前项目位置的共享坐标"});
            Choices(AxisSelector,new Choice{Value=RedlineAxes.Unknown,Name="X/Y 方向未确认"},new Choice{Value=RedlineAxes.XEastYNorth,Name="图纸 X=东 / Y=北"},new Choice{Value=RedlineAxes.XNorthYEast,Name="图纸 X=北 / Y=东"});
            var first=new WrapPanel();Field(first,"单位",UnitSelector,125);Field(first,"框架",FrameSelector,230);Field(first,"X/Y",AxisSelector,190);metadata.Children.Add(first);
            var second=new WrapPanel{Margin=new Thickness(0,8,0,0)};Field(second,"坐标系备注",CoordinateSystem,230);Field(second,"水平容差 m",Tolerance,85);Field(second,"最小实体净距 m",MinimumGap,85);metadata.Children.Add(second);
            ManualConfirmed.Content="确认表中坐标由人工填写/审核，作为本项目红线对照资料";ManualConfirmed.Margin=new Thickness(0,10,0,0);metadata.Children.Add(ManualConfirmed);
            metadata.Children.Add(new TextBlock{Text="单位、框架或 X/Y 方向未确认时可仅存资料，人工坐标比对保持“待核对”。共享坐标按当前项目位置；内部坐标按内部 X/Y，方向须与图纸核实。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)});
            Grid.SetRow(tabs,3);root.Children.Add(tabs);
            Column(pointGrid,"序号","Sequence",65);Column(pointGrid,"图纸的X坐标","X",200);Column(pointGrid,"图纸的Y坐标","Y",200);Column(pointGrid,"X 来源单元格","SourceCellX",120);Column(pointGrid,"Y 来源单元格","SourceCellY",120);
            var points=new DockPanel();DockPanel.SetDock(source,Dock.Top);points.Children.Add(source);points.Children.Add(pointGrid);
            tabs.Items.Add(new TabItem{Header="人工坐标资料",Content=points});tabs.Items.Add(new TabItem{Header="只读核对结果",Content=report});
            Grid.SetRow(status,4);root.Children.Add(status);
            var footer=new DockPanel();Grid.SetRow(footer,5);root.Children.Add(footer);
            var close=new Button{Content="关闭",MinWidth=90,IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right};close.Click+=(s,e)=>Close();DockPanel.SetDock(close,Dock.Right);footer.Children.Add(close);
            footer.Children.Add(new TextBlock{Text="本工具不移动几何；模型轮廓不会被用来生成或覆盖人工对照坐标。",TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,16,0)});
            foreach(var selector in new[]{UnitSelector,FrameSelector,AxisSelector})selector.SelectionChanged+=(s,e)=>Changed();
            foreach(var text in new[]{Tolerance,MinimumGap,CoordinateSystem})text.TextChanged+=(s,e)=>Changed();
            ManualConfirmed.Checked+=(s,e)=>Changed();ManualConfirmed.Unchecked+=(s,e)=>Changed();
            SetData(initial??new RedlineData());
        }
        static void Choices(ComboBox box,params Choice[] choices){box.ItemsSource=choices;box.DisplayMemberPath="Name";box.SelectedValuePath="Value";box.Height=32;}
        static void Field(Panel panel,string label,FrameworkElement control,double width)
        {
            panel.Children.Add(new TextBlock{Text=label+"：",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,5,0)});
            control.Width=width;control.Margin=new Thickness(0,0,16,0);if(control is TextBox)control.Height=30;panel.Children.Add(control);
        }
        static void Column(DataGrid grid,string header,string path,double width)=>grid.Columns.Add(new DataGridTextColumn{Header=header,Binding=new Binding(path),Width=width});
        Button AddButton(Panel panel,string label,Action action)
        {
            var button=new Button{Content=label,Margin=new Thickness(0,0,8,6)};button.Click+=(s,e)=>{
                try{action();}catch(Exception ex)when(ex is InvalidOperationException||ex is IOException||ex is UnauthorizedAccessException){Message("操作未完成："+ex.Message);}
            };panel.Children.Add(button);return button;
        }
        void Message(string message){StatusMessage=message;status.Text=message;}
        void Changed()
        {
            if(loading)return;LastReport=null;report.Clear();
            Message("资料或比对口径已调整，请重新只读核对。保存后请同时保存 RVT，以在重开模型时保留人工资料。");
        }
        void SetData(RedlineData data)
        {
            loading=true;Current=data;pointGrid.ItemsSource=data.Points;UnitSelector.SelectedValue=data.Unit;FrameSelector.SelectedValue=data.Frame;AxisSelector.SelectedValue=data.Axes;
            CoordinateSystem.Text=data.CoordinateSystemName??"";ManualConfirmed.IsChecked=data.Accepted;LastReport=null;report.Clear();
            source.Text=(data.SourcePath??"尚未导入人工表")+"\n来源：MANUAL；Sheet1；"+data.Points.Length+" 个完整点；按序号连接，末点回首点闭合。\n"+(string.IsNullOrEmpty(data.SourceHash)?"无导入哈希":"SHA-256 "+data.SourceHash);
            loading=false;var check=RedlineDataPolicy.Validate(data);Message(check.CanCompare?"人工资料及坐标口径已确认，可进行只读核对。":string.Join("；",check.Issues));
        }
        RedlineData Capture()=>new RedlineData{Source=Current.Source,SourcePath=Current.SourcePath,SourceHash=Current.SourceHash,SheetName=Current.SheetName,
            Points=Current.Points,ImportIssues=Current.ImportIssues,Accepted=ManualConfirmed.IsChecked==true,Unit=UnitSelector.SelectedValue is RedlineUnit unit?unit:RedlineUnit.Unknown,
            Frame=FrameSelector.SelectedValue is RedlineFrame frame?frame:RedlineFrame.Unknown,Axes=AxisSelector.SelectedValue is RedlineAxes axes?axes:RedlineAxes.Unknown,CoordinateSystemName=CoordinateSystem.Text.Trim()};
        internal void ImportManualFile(string path){var data=RedlineWorkbook.Read(path);SetData(data);tabs.SelectedIndex=0;}
        internal void SaveData()
        {
            var data=Capture();RedlineDataPolicy.Serialize(data);session.Save(data);Current=data;
            Message(RedlineDataPolicy.Validate(data).CanCompare?"已写入本模型项目资料并回读。请保存 RVT，以在重开后保留；比对仍需点击“只读核对模型”。":"已仅存资料并回读，坐标口径尚未确认，不能报人工比对通过。请保存 RVT，以在重开后保留。");
        }
        internal void CompareNow()
        {
            if(!double.TryParse(Tolerance.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var tolerance)||tolerance<=0||double.IsInfinity(tolerance)||double.IsNaN(tolerance))throw new InvalidOperationException("水平容差请填写大于 0 的米数值。");
            if(!double.TryParse(MinimumGap.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var gap)||gap<0||double.IsInfinity(gap)||double.IsNaN(gap))throw new InvalidOperationException("最小实体净距请填写不小于 0 的米数值。");
            var data=Capture();var result=session.Compare(data,tolerance,gap);Current=data;LastReport=result;
            report.Text="模型双载体："+(result.Pair?.Status??"待核对")+"\n"+(result.Pair?.Detail??"未取得模型双载体检查结果")+"\n\n人工坐标对照："+(result.Manual?.Status??"待核对")+"\n"+(result.Manual?.Detail??"未取得人工坐标检查结果")+
                ((result.Issues?.Length??0)>0?"\n\n需核对事项：\n"+string.Join("\n",result.Issues):"");
            tabs.SelectedIndex=1;Message("只读核对完成。水平容差 "+tolerance.ToString("0.########",CultureInfo.InvariantCulture)+" m；实体净距要求大于 "+gap.ToString("0.########",CultureInfo.InvariantCulture)+" m。模型双载体与人工资料分别列示。");
        }
        internal void ExportManualFile(string path){var data=Capture();RedlineWorkbook.Write(path,data);Message("已另存人工坐标表："+Path.GetFullPath(path)+"；保留原模板标题及表头。");}
    }
}
