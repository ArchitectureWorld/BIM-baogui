using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class UndergroundWindowTests
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static void Assert(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static object Get(object o,string name)=>o.GetType().GetProperty(name,Flags).GetValue(o);
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
    static Window Dialog()
    {
        var type=typeof(ManagerWindow).Assembly.GetType("Hifc.Revit.Management.UndergroundWindow");
        Assert(type!=null,"Underground floor/datum confirmation UI is absent");
        return (Window)Activator.CreateInstance(type,Flags,null,new object[]{new[]{
            new UndergroundLevel {UniqueId="a",Name="B1",ElevationM=-3.6},
            new UndergroundLevel {UniqueId="s",Name="结构底板",ElevationM=-3.7},
            new UndergroundLevel {UniqueId="g",Name="首层",ElevationM=0}},
            new UndergroundSettings {Confirmed=true,GroundElevationM=0,LevelUniqueIds=new[]{"a"}}},null);
    }
    public static void Run(Action<string,Action> check,string output)
    {
        check("underground_dialog_restores_only_confirmed_architectural_levels",()=>{
            var w=Dialog();try {w.Show();w.UpdateLayout();var selected=((IEnumerable)Get(w,"Rows")).Cast<object>().Where(r=>(bool)Get(r,"Included")).ToArray();Assert(selected.Length==1&&(string)Get(selected[0],"UniqueId")=="a","Cached confirmation was lost or a structure level was added");
                var root=(FrameworkElement)w.Content;var image=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var ctx=drawing.RenderOpen()){ctx.DrawRectangle(w.Background,null,new Rect(0,0,root.ActualWidth,root.ActualHeight));ctx.DrawRectangle(new VisualBrush(root),null,new Rect(0,0,root.ActualWidth,root.ActualHeight));}image.Render(drawing);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(output,"underground-levels.png")))png.Save(file);
            }finally{w.Close();}
        });
        check("underground_dialog_returns_edited_datum_without_changing_level_choices",()=>{
            var w=Dialog();try {((TextBox)Get(w,"GroundInput")).Text="0.5";var s=(UndergroundSettings)Call(w,"BuildSettings");var r=UndergroundPolicy.Evaluate(s,new[]{new UndergroundLevel {UniqueId="a",ElevationM=-3.6}});Assert(s.Confirmed&&s.LevelUniqueIds.SequenceEqual(new[]{"a"})&&Math.Abs(r.HeightM.Value-4.1)<1e-8,"UI datum not used by calculation");}finally{w.Close();}
        });
        check("underground_floor_names_remain_readable_in_actual_window",()=>{
            var w=Dialog();try {w.Show();w.UpdateLayout();var table=(DataGrid)w.GetType().GetField("grid",Flags).GetValue(w);Assert(table.Columns[1].ActualWidth>=260,"Floor names and header are clipped");}finally{w.Close();}
        });
        check("underground_confirmation_persists_across_manager_reopen",()=>{
            var directory=Path.Combine(output,"underground-profiles");var session=new FakeSession();var w=new ManagerWindow(session,directory);
            w.RestoreState("underground-test-model");var set=new UndergroundSettings {Confirmed=true,GroundElevationM=102,LevelUniqueIds=new[]{"level-a"}};
            var method=w.GetType().GetMethod("SetUndergroundSettings",Flags);Assert(method!=null,"Manager does not remember underground settings");method.Invoke(w,new object[]{set});w.Close();
            var reopened=new ManagerWindow(session,directory);try {reopened.RestoreState("underground-test-model");var cached=(UndergroundSettings)Get(reopened,"UndergroundSettings");Assert(cached.Confirmed&&cached.GroundElevationM==102&&cached.LevelUniqueIds.SequenceEqual(new[]{"level-a"}),"Underground confirmation was not restored");}finally{reopened.Close();}
        });
    }
}
