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
    static Window CreateWithClock(string title,string kind,string directory,Func<DateTime> clock)
    {
        var type=typeof(Program).Assembly.GetType("Hifc.Revit.Management.ExportNamingWindow");
        var constructor=type.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
            new[]{typeof(string),typeof(string),typeof(string),typeof(string),typeof(Func<DateTime>),typeof(string)},null);
        Assert(constructor!=null,"Naming confirmation cannot read the actual date through an injected clock");
        return (Window)constructor.Invoke(new object[]{"",title,"报规",kind,clock,directory});
    }
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
                Assert(Find<TextBlock>(w,"FilenamePreview").Text=="OUT_大门_学校_报规_20261023_V1.ifc","Editing did not update filename");
                w.UpdateLayout();w.Dispatcher.Invoke(DispatcherPriority.Render,new Action(()=>{}));
                var bitmap=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(w);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(System.IO.Path.Combine(output,"export-names.png")))encoder.Save(stream);
            }finally{w.Close();}
        });
        check("export_names_recalculate_custom_folder_and_confirmation_version",()=>{
            var folder=System.IO.Path.Combine(output,"names-custom");Directory.CreateDirectory(folder);
            const string stem="OUT_大门_学校_报规_20261023";
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
        check("export_names_ambiguous_underscores_wait_for_explicit_identity",()=>{
            const string title="3D_综合楼_东区_学校_一期_报规_20261112_V1.rvt";
            var w=Create(title,"单体建筑",output);
            try {
                w.Show();w.UpdateLayout();
                Assert(Find<TextBox>(w,"ProjectNameInput").Text==""&&Find<TextBox>(w,"BuildingNameInput").Text=="","Ambiguous names were filled as facts");
                Assert(!Find<Button>(w,"ConfirmNamesButton").IsEnabled,"Ambiguous identity could be approved without editing");
                var source=Find<TextBlock>(w,"SourceNameText");
                Assert(source!=null&&source.Text.Contains(title),"Source identity is unavailable while correcting names");
                Find<TextBox>(w,"ProjectNameInput").Text="学校_一期";
                Find<TextBox>(w,"BuildingNameInput").Text="综合楼_东区";
                Assert(Find<Button>(w,"ConfirmNamesButton").IsEnabled&&Find<TextBlock>(w,"FilenamePreview").Text=="OUT_综合楼_东区_学校_一期_报规_20261023_V1.ifc","Explicit fields did not resolve the ambiguity");
            }finally{w.Close();}
        });
        check("export_names_confirmation_uses_current_day_after_midnight",()=>{
            var folder=System.IO.Path.Combine(output,"names-midnight");Directory.CreateDirectory(folder);
            var now=new DateTime(2026,11,12,23,59,59);
            File.WriteAllText(System.IO.Path.Combine(folder,"OUT_学校_报规_大门_20261113_V4.ifc"),"next-day-history");
            var w=CreateWithClock("3D_学校_报规_大门_20261109_V2.rvt","单体建筑",folder,()=>now);Exception failure=null;
            OnShown(w,()=>{try {
                Assert(Find<TextBlock>(w,"FilenamePreview").Text=="OUT_大门_学校_报规_20261112_V1.ifc","Preview used the RVT date");
                now=new DateTime(2026,11,13,0,0,1);
                Click(w,"ConfirmNamesButton");
            }catch(Exception e){failure=e;w.Close();}});
            Assert(w.ShowDialog()==true&&failure==null,"Midnight confirmation failed: "+failure);
            Assert(PathResult(w)==System.IO.Path.Combine(folder,"OUT_大门_学校_报规_20261113_V5.ifc"),"Confirmation retained yesterday or ignored old-order versions");
            Assert(File.ReadAllText(System.IO.Path.Combine(folder,"OUT_学校_报规_大门_20261113_V4.ifc"))=="next-day-history","Confirmation overwrote history");
        });
        check("export_names_confirmed_underscore_identity_uses_legacy_versions",()=>{
            var folder=System.IO.Path.Combine(output,"names-confirmed-identity");Directory.CreateDirectory(folder);
            File.WriteAllText(System.IO.Path.Combine(folder,"OUT_学校_一期_报规_综合楼_东区_20261023_V6.ifc"),"keep");
            var w=Create("3D_综合楼_东区_学校_一期_报规_20261112_V1.rvt","单体建筑",folder);Exception failure=null;
            OnShown(w,()=>{try {
                Find<TextBox>(w,"ProjectNameInput").Text="学校_一期";
                Find<TextBox>(w,"BuildingNameInput").Text="综合楼_东区";
                Assert(Find<TextBlock>(w,"FilenamePreview").Text=="OUT_综合楼_东区_学校_一期_报规_20261023_V7.ifc","Preview lost the confirmed field boundary");
                Click(w,"ConfirmNamesButton");
            }catch(Exception e){failure=e;w.Close();}});
            Assert(w.ShowDialog()==true&&failure==null,"Explicit identity confirmation failed: "+failure);
            Assert(PathResult(w)==System.IO.Path.Combine(folder,"OUT_综合楼_东区_学校_一期_报规_20261023_V7.ifc"),"Confirmation lost the confirmed identity");
        });
    }
}
