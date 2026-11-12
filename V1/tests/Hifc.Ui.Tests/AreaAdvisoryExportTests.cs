using System;using System.Linq;using System.Windows.Controls;using Hifc.Reference;using Hifc.Revit.Management;
internal static class AreaAdvisoryExportTests {
static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
public static void Run(Action<string,Action> check){
 check("area_advisories_leave_the_normal_export_button_enabled",()=>{var notice=new InputReviewItem{ElementId=311,Name="3F",Property="楼层面积",Code="FLOOR_AREA_DIFFERENCE",Detail="223.86㎡与238.381㎡存在差异；保留原值导出。",BlocksExport=false};var w=new ExportReviewWindow(new ExportReviewData{ProfileId=PlatformRules.ProfileId,Inputs=new[]{notice}});try{Assert(((Button)w.FindName("ContinueExportButton")).IsEnabled,"Advisory disabled export");var text=string.Join("\n",((DockPanel)w.Content).Children.OfType<TextBlock>().Select(t=>t.Text));Assert(text.Contains("不自动分摊"),"Export window still promises area synchronization");}finally{w.Close();}});
 check("non_area_required_inputs_remain_blocking_with_area_advisories",()=>{var w=new ExportReviewWindow(new ExportReviewData{Inputs=new[]{new InputReviewItem{ElementId=311,Property="楼层面积",Code="FLOOR_AREA_DIFFERENCE",Detail="面积差异"},new InputReviewItem{ElementId=49504,Property="建筑用途名称",Code="BUILDING_USAGE_REQUIRED",Detail="必填",BlocksExport=true}}});try{Assert(!((Button)w.FindName("ContinueExportButton")).IsEnabled,"Required input guard was bypassed");Assert(w.BlockingGrid.Items.Count==1,"Area advisory became a blocker");}finally{w.Close();}});
}
}
