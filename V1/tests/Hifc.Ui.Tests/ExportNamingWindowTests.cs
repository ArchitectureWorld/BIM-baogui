using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class ExportNamingWindowTests
{
    static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
    static Window Create(string title,string kind,string directory)
    {
        var type=typeof(Program).Assembly.GetType("Hifc.Revit.Management.ExportNamingWindow");
        Assert(type!=null,"Export does not provide editable project and building names");
        return (Window)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
            new object[]{"",title,"报规",kind,new DateTime(2026,10,23),directory},null);
    }
    static T Find<T>(Window w,string name) where T:FrameworkElement => (T)w.FindName(name);
    static string PathResult(Window w)=>(string)w.GetType().GetProperty("ConfirmedPath",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(w);
    static void Click(Window w,string name)=>Find<Button>(w,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void OnShown(Window w,Action action)=>w.Loaded+=(s,e)=>w.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,action);
    public static void Run(Action<string,Action> check,string output)
    {
        check("export_names_edit_project_and_building_in_actual_controls",()=>{
            var w=Create("3D_歇马镇中心学校-综合楼","单体建筑",output);
            try {
                w.Show();w.UpdateLayout();
                Assert(Find<TextBox>(w,"ProjectNameInput").Text=="歇马镇中心学校"&&Find<TextBox>(w,"BuildingNameInput").Text=="综合楼","Legacy name suggestions incorrect");
                var project=Find<TextBox>(w,"ProjectNameInput");project.Focus();project.SelectAll();project.SelectedText="学校";
                var building=Find<TextBox>(w,"BuildingNameInput");building.Focus();building.SelectAll();building.SelectedText="大门";
                Assert(Find<TextBlock>(w,"FilenamePreview").Text=="OUT_学校_报规_大门_20261023_V1.ifc","Editing did not update filename");
                w.UpdateLayout();w.Dispatcher.Invoke(DispatcherPriority.Render,new Action(()=>{}));
                var bitmap=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(w);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(System.IO.Path.Combine(output,"export-names.png")))encoder.Save(stream);
            }finally{w.Close();}
        });
        check("export_names_recalculate_custom_folder_and_confirmation_version",()=>{
            var folder=System.IO.Path.Combine(output,"names-custom");Directory.CreateDirectory(folder);
            const string stem="OUT_学校_报规_大门_20261023";
            File.WriteAllText(System.IO.Path.Combine(folder,stem+"_V4.ifc"),"keep");
            var w=Create("学校_大门","单体建筑",output);Exception failure=null;
            OnShown(w,()=>{try {
                Find<TextBox>(w,"DirectoryInput").Text=folder;
                Assert(Find<TextBlock>(w,"FilenamePreview").Text==stem+"_V5.ifc","Changed folder retained wrong version");
                File.WriteAllText(System.IO.Path.Combine(folder,stem+"_V6.ifc"),"appeared after preview");
                Click(w,"ConfirmNamesButton");
            }catch(Exception e){failure=e;w.Close();}});
            Assert(w.ShowDialog()==true&&failure==null,"Name confirmation failed: "+failure);
            Assert(PathResult(w)==System.IO.Path.Combine(folder,stem+"_V7.ifc"),"Confirmation did not recompute collision");
            Assert(File.ReadAllText(System.IO.Path.Combine(folder,stem+"_V4.ifc"))=="keep","Existing IFC changed");
        });
        check("export_names_site_is_fixed_and_blank_names_cannot_approve",()=>{
            var w=Create("学校_总图","总图",output);try {
                w.Show();w.UpdateLayout();
                Assert(Find<TextBox>(w,"BuildingNameInput").IsReadOnly&&Find<TextBox>(w,"BuildingNameInput").Text=="总图","Site name is editable");
                Find<TextBox>(w,"ProjectNameInput").Text="  ";Assert(!Find<Button>(w,"ConfirmNamesButton").IsEnabled,"Blank project allowed");
            }finally{w.Close();}
            w=Create("学校","单体建筑",output);try {
                w.Show();Assert(Find<TextBox>(w,"BuildingNameInput").Text==""&&!Find<Button>(w,"ConfirmNamesButton").IsEnabled,"Missing building became generic label");
            }finally{w.Close();}
        });
        check("export_names_cancel_does_not_create_output",()=>{
            var before=Directory.GetFiles(output).Length;var w=Create("学校_综合楼","单体建筑",output);
            OnShown(w,()=>w.Close());Assert(w.ShowDialog()!=true&&PathResult(w)==null,"Cancellation approved export");
            Assert(Directory.GetFiles(output).Length==before,"Naming window wrote files before approval");
        });
    }
}
