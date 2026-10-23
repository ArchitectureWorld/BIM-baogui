using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class ExportNamingWindow : Window
    {
        readonly string stage,kind;readonly DateTime date;
        readonly TextBox project=new TextBox(),building=new TextBox(),directory=new TextBox();
        readonly TextBlock preview=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=16},status=new TextBlock {TextWrapping=TextWrapping.Wrap};
        readonly Button confirm=new Button {Content="确认，继续导出",IsDefault=true,Padding=new Thickness(18,8,18,8)};
        internal string ConfirmedPath { get; private set; }
        internal Func<string,string> BrowseDirectory { get; set; }
        internal ExportNamingWindow(string modelPath,string title,string stage,string kind,DateTime date,string initialDirectory)
        {
            this.stage=stage;this.kind=kind;this.date=date;
            Title="导出 IFC · 名称和位置";Width=800;Height=470;MinWidth=720;MinHeight=450;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;LiquidGlassTheme.Apply(this);NameScope.SetNameScope(this,new NameScope());
            var names=ExportFileNaming.SuggestNames(modelPath,title,kind);
            project.Text=names.ProjectName;building.Text=names.BuildingName;building.IsReadOnly=kind=="总图";directory.Text=initialDirectory;
            foreach(var input in new[]{project,building,directory}){input.MinHeight=30;input.Padding=new Thickness(6,4,6,4);input.VerticalContentAlignment=VerticalAlignment.Center;}
            Register("ProjectNameInput",project);Register("BuildingNameInput",building);Register("DirectoryInput",directory);
            Register("FilenamePreview",preview);Register("ConfirmNamesButton",confirm);
            var root=new DockPanel {Margin=new Thickness(24)};Content=root;
            var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            status.Margin=new Thickness(0,10,0,12);footer.Children.Add(status);
            var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(buttons);
            buttons.Children.Add(new Button {Content="取消",IsCancel=true,Margin=new Thickness(0,0,12,0),Padding=new Thickness(18,8,18,8)});buttons.Children.Add(confirm);
            var body=new StackPanel();root.Children.Add(body);
            body.Children.Add(new TextBlock {Text="确认本次导出的名称和保存位置",FontSize=22,Margin=new Thickness(0,0,0,8)});
            body.Children.Add(new TextBlock {Text="阶段："+stage+"    模型类型："+kind+"\n这里的名称仅用于 IFC 文件，不修改原 RVT 名称或模型属性。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
            Row(body,"项目名称",project);
            Row(body,kind=="总图"?"总图标识":"具体单体名称",building);
            var folder=new DockPanel();var browse=new Button {Content="浏览…",Padding=new Thickness(14,5,14,5),Margin=new Thickness(8,0,0,0)};
            DockPanel.SetDock(browse,Dock.Right);folder.Children.Add(browse);folder.Children.Add(directory);Row(body,"保存文件夹",folder);
            body.Children.Add(new TextBlock {Text="本次 IFC 文件名",Margin=new Thickness(0,8,0,8)});body.Children.Add(preview);
            body.Children.Add(new TextBlock {Text="同名文件自动递增 V1、V2…；导出目录只保存 IFC。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)});
            project.TextChanged+=(s,e)=>Refresh();building.TextChanged+=(s,e)=>Refresh();directory.TextChanged+=(s,e)=>Refresh();
            browse.Click+=(s,e)=>{var chosen=BrowseDirectory?.Invoke(directory.Text);if(!string.IsNullOrWhiteSpace(chosen))directory.Text=chosen;};
            confirm.Click+=(s,e)=>{var path=RefreshPath();if(path!=null){ConfirmedPath=path;DialogResult=true;}};
            Refresh();
        }
        void Register(string name,FrameworkElement value){value.Name=name;RegisterName(name,value);}
        static void Row(Panel parent,string label,UIElement input)
        {
            var row=new Grid {Margin=new Thickness(0,0,0,12)};row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(110)});row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock {Text=label,VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(input,1);row.Children.Add(input);parent.Children.Add(row);
        }
        string NextPath()
        {
            var stem=ExportFileNaming.StemForNames(project.Text,building.Text,stage,kind,date);
            if(!Path.IsPathRooted(directory.Text)||!Directory.Exists(directory.Text))throw new InvalidOperationException("请选择一个已存在的保存文件夹。");
            return ExportFileNaming.NextPath(Path.GetFullPath(directory.Text),stem);
        }
        void Refresh()=>RefreshPath();
        string RefreshPath()
        {
            try {var path=NextPath();preview.Text=Path.GetFileName(path);status.Text="确认后将进入导出前核对。";confirm.IsEnabled=true;return path;}
            catch(Exception e) when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException||e is NotSupportedException)
            {preview.Text="请补全名称和保存位置";status.Text=e.Message;confirm.IsEnabled=false;return null;}
        }
    }
}
