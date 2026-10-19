using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class UndergroundLevelChoice
    {
        public string UniqueId {get;set;}
        public string Name {get;set;}
        public double ElevationM {get;set;}
        public bool Included {get;set;}
    }
    internal sealed class UndergroundWindow:Window
    {
        readonly UndergroundLevel[] levels;
        readonly DataGrid grid=new DataGrid {AutoGenerateColumns=false,CanUserAddRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeight=33};
        readonly TextBlock error=new TextBlock {TextWrapping=TextWrapping.Wrap};
        internal UndergroundLevelChoice[] Rows {get;}
        internal TextBox GroundInput {get;}=new TextBox {Width=130,VerticalContentAlignment=VerticalAlignment.Center};
        internal UndergroundSettings Result {get;private set;}
        internal UndergroundWindow(UndergroundLevel[] levels,UndergroundSettings settings)
        {
            this.levels=levels;settings=settings??new UndergroundSettings();
            Title="地下楼层与地坪基准";Width=700;Height=580;MinWidth=610;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            LiquidGlassTheme.Apply(this);
            Rows=levels.Select(l=>new UndergroundLevelChoice {UniqueId=l.UniqueId,Name=l.Name,ElevationM=l.ElevationM,
                Included=settings.Confirmed?(settings.LevelUniqueIds??new string[0]).Contains(l.UniqueId):!l.Name.Contains("结构")&&Regex.IsMatch(l.Name,@"^(B\d+|地下[一二三四五六七八九十0-9]+层)",RegexOptions.IgnoreCase)}).ToArray();
            var root=new Grid {Margin=new Thickness(18)};Content=root;
            foreach(var h in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})root.RowDefinitions.Add(new RowDefinition {Height=h});
            root.Children.Add(new TextBlock {Text="勾选计入地下层数的建筑楼层，排除重复结构和辅助标高。名称只提供候选，确认后才计算。\n标高统一采用项目标高（m）；高度建议为地坪基准到最低所选楼层的标高差，可在属性栏调整。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
            var datum=new WrapPanel {Margin=new Thickness(0,0,0,14)};Grid.SetRow(datum,1);root.Children.Add(datum);
            datum.Children.Add(new TextBlock {Text="地坪基准（m）",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});datum.Children.Add(GroundInput);
            var ground=levels.FirstOrDefault(l=>Regex.IsMatch(l.Name,@"^(首层|一层|1层|1F$|F1$)",RegexOptions.IgnoreCase));
            GroundInput.Text=(settings.GroundElevationM??ground?.ElevationM??0).ToString("0.######",CultureInfo.InvariantCulture);
            var selector=new ComboBox {ItemsSource=levels,DisplayMemberPath="Name",Width=170,Margin=new Thickness(12,0,0,0),ToolTip="也可从标高复制地坪基准，再手工调整"};
            selector.SelectionChanged+=(s,e)=>{if(selector.SelectedItem is UndergroundLevel l)GroundInput.Text=l.ElevationM.ToString("0.######",CultureInfo.InvariantCulture);};datum.Children.Add(selector);
            grid.ItemsSource=Rows;
            grid.Columns.Add(new DataGridCheckBoxColumn {Header="计入地下",Binding=new Binding("Included"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=90});
            grid.Columns.Add(new DataGridTextColumn {Header="建筑楼层 / 标高名称",Binding=new Binding("Name"),IsReadOnly=true,MinWidth=260,Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
            grid.Columns.Add(new DataGridTextColumn {Header="项目标高（m）",Binding=new Binding("ElevationM"){StringFormat="0.######"},IsReadOnly=true,Width=135});
            Grid.SetRow(grid,2);root.Children.Add(grid);Grid.SetRow(error,3);error.Margin=new Thickness(0,10,0,8);root.Children.Add(error);
            var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(buttons,4);root.Children.Add(buttons);
            var cancel=new Button {Content="取消",MinWidth=90,Margin=new Thickness(0,0,10,0),IsCancel=true};cancel.Click+=(s,e)=>Close();buttons.Children.Add(cancel);
            var confirm=new Button {Content="确认并全面检查",MinWidth=150};LiquidGlassTheme.Primary(confirm);buttons.Children.Add(confirm);
            confirm.Click+=(s,e)=>{try {Result=BuildSettings();DialogResult=true;}catch(InvalidOperationException ex){error.Text=ex.Message;}};
        }
        internal UndergroundSettings BuildSettings()
        {
            grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);
            if(!double.TryParse(GroundInput.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var datum))throw new InvalidOperationException("请输入有效地坪基准，单位 m。");
            var settings=new UndergroundSettings {Confirmed=true,GroundElevationM=datum,LevelUniqueIds=Rows.Where(r=>r.Included).Select(r=>r.UniqueId).ToArray()};
            var result=UndergroundPolicy.Evaluate(settings,levels);
            if(!result.Count.HasValue)throw new InvalidOperationException(result.Note);
            return settings;
        }
    }
}
