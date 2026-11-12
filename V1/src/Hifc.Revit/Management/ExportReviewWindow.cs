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
        public RequiredInputValidation InputValidation { get; set; }

        // Both the window and the export service use this merged review. Scope
        // and area failures can exist only in BlockingReasons in older callers.
        internal InputReviewItem[] ReviewInputs
        {
            get
            {
                var rows=(Inputs??new InputReviewItem[0]).Select(Copy).ToList();
                foreach(var raw in BlockingReasons??new string[0])
                {
                    var reason=string.IsNullOrWhiteSpace(raw)?"导出校验返回了阻断，但未提供具体原因；请重新核对或查看导出记录。":raw;
                    var matches=rows.Where(i=>i.Detail==reason||BlockingDescription(i)==reason).ToArray();
                    if(matches.Length>0){foreach(var item in matches)item.BlocksExport=true;continue;}
                    var areas=(Areas??new ExportAreaReviewItem[0]).Where(a=>a.Reason==reason).ToArray();
                    if(areas.Length>0)
                        rows.AddRange(areas.Select(a=>new InputReviewItem{ElementId=a.ElementId,Name=a.Name,
                            PropertySet=a.PropertySet,Property=a.Property,Value=a.SourceValue,
                            Code="EXPORT_AREA_REVIEW",Detail=reason,BlocksExport=true}));
                    else rows.Add(new InputReviewItem{Name="项目范围",Property="导出校验",
                        Code="EXPORT_SCOPE_REVIEW",Detail=reason,BlocksExport=true});
                }
                return rows.GroupBy(i=>new{i.ElementId,i.PropertySet,i.Property,i.Detail}).Select(group=>{
                    var item=group.First();item.BlocksExport=group.Any(i=>i.BlocksExport);return item;
                }).ToArray();
            }
        }
        internal InputReviewItem[] BlockingInputs => ReviewInputs.Where(i=>i.BlocksExport).ToArray();
        internal static string BlockingDescription(InputReviewItem item) => item.ElementId>0
            ? item.ElementId+" / "+item.Property+"："+item.Detail : item.Detail;
        static InputReviewItem Copy(InputReviewItem item) => new InputReviewItem{ElementId=item.ElementId,
            Name=item.Name,PropertySet=item.PropertySet,Property=item.Property,Value=item.Value,
            DefaultValue=item.DefaultValue,Code=item.Code,Detail=item.Detail,BlocksExport=item.BlocksExport};
    }
    internal sealed class ExportReviewWindow : Window
    {
        internal DataGrid InputGrid { get; }
        internal DataGrid AreaGrid { get; }
        internal DataGrid BlockingGrid { get; }
        internal event Action<int[]> LocateRequested;
        readonly TabControl tabs;
        readonly TabItem blockingTab;
        readonly TextBlock status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
        internal ExportReviewWindow(ExportReviewData data)
        {
            NameScope.SetNameScope(this,new NameScope());
            Title="导出前核对 · "+data.Title;Width=1180;Height=720;MinWidth=900;MinHeight=560;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;LiquidGlassTheme.Apply(this);
            var root=new DockPanel {Margin=new Thickness(20),Background=Background};Content=root;
            var intro=new TextBlock {Text="将导出当前项目全部模型。必填缺值、枚举不符或来源冲突必须修正后才能导出；平台默认显示不算已填写。\n面积差异、未分类候选及投影重叠仅作提示；保留模型已写面积和系数，不自动分摊或补值。单位：㎡。",
                TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
            DockPanel.SetDock(intro,Dock.Top);root.Children.Add(intro);
            if(!string.IsNullOrEmpty(data.OutputPath)) {
                var destination=new TextBlock {Name="ExportDestinationText",Text="输出文件："+data.OutputPath,
                    TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
                RegisterName(destination.Name,destination);DockPanel.SetDock(destination,Dock.Top);root.Children.Add(destination);
            }
            var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            var inputs=data.ReviewInputs;var blockers=inputs.Where(i=>i.BlocksExport).ToArray();
            var blocked=blockers.Length>0;
            status.Text="需核对输入 "+inputs.Length+" 项，其中阻止导出 "+blockers.Length+" 项；面积值 "+data.Areas.Length+" 项。"+
                (PlatformRules.IsActive(data.ProfileId)?"":" 当前为旧案例参考，平台默认值未评估。")+
                (!blocked?"":"\n全部阻断原因已列在“阻止导出”页，请定位并返回修改后再导出。");
            footer.Children.Add(status);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(buttons);
            var locate=new Button{Content="定位选中对象",Margin=new Thickness(0,0,10,0),Padding=new Thickness(14,8,14,8)};
            locate.Click+=(s,e)=>LocateSelection();buttons.Children.Add(locate);
            var back=new Button{Content="返回修改",IsCancel=true,Margin=new Thickness(0,0,10,0),Padding=new Thickness(14,8,14,8)};
            buttons.Children.Add(back);
            var proceed=new Button{Name="ContinueExportButton",Content="确认以上内容，继续导出",Padding=new Thickness(14,8,14,8),IsEnabled=!blocked};
            RegisterName(proceed.Name,proceed);
            proceed.Click+=(s,e)=>{if(!blocked)DialogResult=true;};buttons.Children.Add(proceed);
            tabs=new TabControl();root.Children.Add(tabs);
            InputGrid=Table(inputs);Column(InputGrid,"构件 ID","ElementId",85);Column(InputGrid,"对象","Name",150);
            Column(InputGrid,"字段","Property",130);Column(InputGrid,"当前值","CurrentText",95);
            Column(InputGrid,"平台可能默认","DefaultText",155);Column(InputGrid,"说明","Detail",410);
            tabs.Items.Add(new TabItem{Header="输入核对（"+inputs.Length+"）",Content=InputGrid});
            AreaGrid=Table(data.Areas);Column(AreaGrid,"构件 ID","ElementId",85);Column(AreaGrid,"对象","Name",160);
            Column(AreaGrid,"面积字段","Property",110);Column(AreaGrid,"模型当前值 ㎡","SourceValue",135);
            Column(AreaGrid,"本次 IFC 值 ㎡","ExportValue",135);Column(AreaGrid,"取值依据","Reason",390);
            tabs.Items.Add(new TabItem{Header="面积导出值（"+data.Areas.Length+"）",Content=AreaGrid});
            if(blocked){
                BlockingGrid=Table(blockers);BlockingGrid.Name="ExportBlockingGrid";RegisterName(BlockingGrid.Name,BlockingGrid);
                Column(BlockingGrid,"构件 ID","ElementId",85);Column(BlockingGrid,"对象","Name",160);
                Column(BlockingGrid,"字段","Property",135);Column(BlockingGrid,"阻止导出的具体原因","Detail",650);
                blockingTab=new TabItem{Header="阻止导出（"+blockers.Length+"）",Content=BlockingGrid};
                tabs.Items.Add(blockingTab);tabs.SelectedItem=blockingTab;
                BlockingGrid.MouseDoubleClick+=(s,e)=>LocateSelection();
            }
            InputGrid.MouseDoubleClick+=(s,e)=>LocateSelection();AreaGrid.MouseDoubleClick+=(s,e)=>LocateSelection();
        }
        internal void LocateSelection()
        {
            var selected=(tabs.SelectedItem==blockingTab&&BlockingGrid!=null
                ?BlockingGrid.SelectedItems.Cast<InputReviewItem>().Select(r=>r.ElementId)
                :tabs.SelectedIndex==1?AreaGrid.SelectedItems.Cast<ExportAreaReviewItem>().Select(r=>r.ElementId)
                :InputGrid.SelectedItems.Cast<InputReviewItem>().Select(r=>r.ElementId)).ToArray();
            if(selected.Length==0){status.Text="请先在当前页选择需要定位的对象。";return;}
            var ids=selected.Where(id=>id>0).Distinct().ToArray();
            if(ids.Length==0){status.Text="此项为项目级校验问题，请按明细原因核对；没有可定位的单个构件。";return;}
            try{LocateRequested?.Invoke(ids);}
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
