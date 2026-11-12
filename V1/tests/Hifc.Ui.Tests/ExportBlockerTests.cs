using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class ExportBlockerTests
{
    static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
    static string Status(ExportReviewWindow w) => ((TextBlock)typeof(ExportReviewWindow)
        .GetField("status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(w)).Text;
    static DataGrid Blockers(ExportReviewWindow w) => w.FindName("ExportBlockingGrid") as DataGrid;
    static TabControl Tabs(ExportReviewWindow w) => ((DockPanel)w.Content).Children.OfType<TabControl>().Single();
    static bool CanExport(ExportReviewWindow w) => ((Button)w.FindName("ContinueExportButton")).IsEnabled;
    static ExportReviewWindow Window(ExportReviewData data) => new ExportReviewWindow(data);
    public static void Run(Action<string,Action> check,string output)
    {
        check("export_scope_blocker_is_counted_displayed_and_locatable",()=>{
            const string reason="首层：楼层面积同步未完成，建筑区域尚未确认齐全。";
            var input=new InputReviewItem{ElementId=311,Name="首层",Property="楼层面积",Code="FLOOR_AREA_REVIEW",Detail=reason};
            var data=new ExportReviewData{Title="范围阻断",ProfileId=PlatformRules.ProfileId,Inputs=new[]{input},BlockingReasons=new[]{reason}};
            var w=Window(data);try{
                Assert(Status(w).Contains("阻止导出 1 项"),"Scope blocker is reported as zero");
                var grid=Blockers(w);Assert(grid!=null&&grid.Items.Count==1,"Blocking details are not visible");
                Assert(((TabItem)Tabs(w).SelectedItem).Content==grid&&!CanExport(w),"Blocking details are not selected or export is enabled");
                Assert(((InputReviewItem)grid.Items[0]).Detail==reason,"Exact reason was lost");
                int[] located=null;w.LocateRequested+=ids=>located=ids;grid.SelectedIndex=0;w.LocateSelection();
                Assert(located!=null&&located.SequenceEqual(new[]{311}),"Blocking row cannot locate its object");
                Assert(!input.BlocksExport,"Rendering mutated the source review");
            }finally{w.Close();}
        });
        check("required_input_and_its_summary_are_not_double_counted",()=>{
            var item=new InputReviewItem{ElementId=66,Name="绿地",Property="绿地类型",Detail="必填项未写入。",BlocksExport=true};
            var summary="66 / 绿地类型：必填项未写入。";
            var w=Window(new ExportReviewData{Inputs=new[]{item},BlockingReasons=new[]{summary,summary}});try{
                Assert(Status(w).Contains("阻止导出 1 项"),"Required issue was counted twice");
                Assert(Blockers(w)!=null&&Blockers(w).Items.Count==1&&!CanExport(w),"Required blocker is missing");
            }finally{w.Close();}
        });
        check("same_scope_reason_keeps_each_affected_object",()=>{
            const string reason="本层存在面积来源冲突。";
            var w=Window(new ExportReviewData{Inputs=new[]{
                new InputReviewItem{ElementId=77,Name="室内",Property="投影面积",Detail=reason},
                new InputReviewItem{ElementId=78,Name="架空",Property="投影面积",Detail=reason}},BlockingReasons=new[]{reason}});try{
                Assert(Status(w).Contains("阻止导出 2 项"),"Affected objects were collapsed into one count");
                var grid=Blockers(w);Assert(grid!=null&&grid.Items.Count==2,"Affected object was lost");
                int[] located=null;w.LocateRequested+=ids=>located=ids;grid.SelectAll();w.LocateSelection();
                Assert(located!=null&&located.SequenceEqual(new[]{77,78}),"Multiple blockers could not be located");
            }finally{w.Close();}
        });
        check("area_only_blocker_retains_its_element_identity",()=>{
            var w=Window(new ExportReviewData{Areas=new[]{new ExportAreaReviewItem{ElementId=77,Name="区域77",Property="投影面积",Reason="同名参数值不一致"}},BlockingReasons=new[]{"同名参数值不一致"}});try{
                var grid=Blockers(w);Assert(grid!=null&&grid.Items.Count==1,"Area-only blocker is hidden");
                Assert(((InputReviewItem)grid.Items[0]).ElementId==77&&!CanExport(w),"Area blocker lost its identity or guard");
            }finally{w.Close();}
        });
        check("unmatched_blocking_reason_is_visible_without_fake_element",()=>{
            const string reason="项目范围校验未完成。";
            var w=Window(new ExportReviewData{BlockingReasons=new[]{reason}});try{
                var grid=Blockers(w);Assert(grid!=null&&grid.Items.Count==1&&Status(w).Contains("阻止导出 1 项"),"Unmatched blocker was hidden");
                Assert(((InputReviewItem)grid.Items[0]).Detail==reason&&!CanExport(w),"Project blocker was lost");
                bool located=false;w.LocateRequested+=ids=>located=true;grid.SelectedIndex=0;w.LocateSelection();
                Assert(!located&&Status(w).Contains("项目级"),"Project blocker sent an invalid element ID");
            }finally{w.Close();}
        });
        check("optional_warnings_remain_nonblocking",()=>{
            var w=Window(new ExportReviewData{Inputs=new[]{new InputReviewItem{ElementId=49504,Property="人数",Detail="可选项未填写"}}});try{
                Assert(CanExport(w)&&Status(w).Contains("阻止导出 0 项"),"Optional warning became required");
                Assert(Tabs(w).Items.Count==2&&w.InputGrid.Items.Count==1,"Existing warning and area views changed");
            }finally{w.Close();}
        });
        check("empty_blocker_diagnostic_still_fails_closed",()=>{
            var w=Window(new ExportReviewData{BlockingReasons=new[]{""}});try{
                Assert(!CanExport(w)&&Status(w).Contains("阻止导出 1 项"),"Empty diagnostic bypassed the existing guard");
                Assert(Blockers(w)!=null&&!string.IsNullOrWhiteSpace(((InputReviewItem)Blockers(w).Items[0]).Detail),"Missing diagnostic has no explanation");
            }finally{w.Close();}
        });
        check("blocking_review_renders_without_opening_a_window",()=>{
            const string reason="首层：楼层面积同步未完成，建筑区域尚未确认齐全或存在投影重叠。请核对本层实际计量区域。";
            var w=Window(new ExportReviewData{Title="1号楼 · 导出核对",ProfileId=PlatformRules.ProfileId,Inputs=new[]{
                new InputReviewItem{ElementId=311,Name="首层主区域",Property="楼层面积",Detail=reason},
                new InputReviewItem{ElementId=49504,Name="项目信息",Property="人数",Detail="选填项未填写；此提示不新增必填要求。"}},BlockingReasons=new[]{reason}});try{
                Assert(Blockers(w)!=null,"Blocking page is missing");
                var content=(FrameworkElement)w.Content;content.Measure(new Size(1140,660));content.Arrange(new Rect(0,0,1140,660));content.UpdateLayout();
                Assert(Blockers(w).ActualWidth>600&&Blockers(w).ActualHeight>100,"Blocking page has no usable layout");
                var bitmap=new RenderTargetBitmap(1140,660,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory(output);
                using(var file=File.Create(Path.Combine(output,"export-blockers.png")))encoder.Save(file);
            }finally{w.Close();}
        });
    }
}
