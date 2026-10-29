using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class WindowTests
{
    public static void Run()
    {
        Program.Check("template_exports_one_site_and_one_building_per_sheet_without_overwriting",()=>{
            var path=Path.Combine(Program.Output,"new-template-"+Guid.NewGuid().ToString("N")+".xlsx");
            ReportingWorkbookTemplate.Write(path,new[]{"综合楼","大门"});var book=ReportingWorkbookReader.Read(path);
            Program.Assert(book.Tables.Length==3&&book.Tables.Count(t=>t.Kind=="总图")==1&&book.Tables.Count(t=>t.Kind=="单体建筑")==2,"template combined buildings or lost project sheet");
            Program.Assert(book.Tables.Single(t=>t.BuildingName=="综合楼").Items.Any(i=>i.Field=="建筑面积"&&i.Value==""),"template invented area defaults");
            var before=File.ReadAllBytes(path);bool rejected=false;try{ReportingWorkbookTemplate.Write(path,new[]{"不应覆盖"});}catch(IOException){rejected=true;}
            Program.Assert(rejected&&before.SequenceEqual(File.ReadAllBytes(path)),"template overwrote an existing workbook");
        });
        Program.Check("workbook_window_exposes_real_preview_and_explicit_conflict_checkboxes",()=>{
            var rows=PlannerTests.Model();var window=new ReportingWorkbookWindow(rows,"单体建筑");
            try {
                window.LoadFiles(new[]{Program.Package("window",Program.Fixture("normalized.xml"))});window.Show();window.UpdateLayout();Pump();
                Program.Assert(window.Plan.Changes.Length>=5&&window.ChangeGrid.Items.Count==window.Plan.Changes.Length,"window did not bind actual import preview");
                var area=window.Plan.Changes.Single(c=>c.Property=="建筑面积");
                Program.Assert(!area.Selected&&area.IsConflict&&window.BuildDecision().Changes.All(c=>c.Property!="建筑面积"),"UI preaccepted conflicting area");
                window.ChangeGrid.ScrollIntoView(area);window.ChangeGrid.UpdateLayout();Pump();
                var check=FindChecks(window.ChangeGrid).SingleOrDefault(c=>ReferenceEquals(c.DataContext,area));
                Program.Assert(check!=null&&check.IsEnabled,"conflict has no explicit usable checkbox");
                Screenshot(window,Path.Combine(Program.Output,"workbook-preview.png"));
                check.IsChecked=true;Pump();Program.Assert(window.BuildDecision().Changes.Any(c=>c.Property=="建筑面积"),"checkbox selection did not reach returned decision");
                Program.Assert(rows[0].Fields.Single(f=>f.Property=="建筑面积").Value=="70","UI changed actual model snapshot");
                Screenshot(window,Path.Combine(Program.Output,"workbook-explicit-choice.png"));
            } finally {window.Close();}
        });
        Program.Check("workbook_window_requires_table_choice_when_multiple_building_sheets_exist",()=>{
            var window=new ReportingWorkbookWindow(PlannerTests.Model(),"单体建筑");
            try {
                window.LoadFiles(new[]{Program.Package("window-many",Program.Fixture("normalized.xml"),Program.Fixture("normalized.xml"))});
                Program.Assert(window.TableSelector.Items.Count==2&&window.TableSelector.SelectedIndex==-1&&window.Plan.Changes.Length==0,"multiple tables silently selected first sheet");
                window.TableSelector.SelectedIndex=1;Program.Assert(window.Plan.Changes.Length>0,"explicit table choice did not rebuild preview");
            } finally {window.Close();}
        });
        Program.Check("clearing_target_building_removes_proposed_writes",()=>{
            var window=new ReportingWorkbookWindow(PlannerTests.Model(),"单体建筑");
            try {
                window.LoadFiles(new[]{Program.Package("window-clear-target",Program.Fixture("normalized.xml"))});
                Program.Assert(window.Plan.Changes.Length>0,"valid exact target did not initialize");
                window.BuildingSelector.SelectedValue="";
                Program.Assert(window.Plan.Changes.Length==0&&string.IsNullOrEmpty(window.Plan.TargetBuildingUniqueId),"cleared UI target still silently falls back to matched building");
            } finally {window.Close();}
        });
    }
    static System.Collections.Generic.IEnumerable<CheckBox> FindChecks(DependencyObject root)
    {
        if(root is CheckBox box)yield return box;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var check in FindChecks(VisualTreeHelper.GetChild(root,i)))yield return check;
    }
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(()=>{}));
    static void Screenshot(Window window,string path)
    {
        window.UpdateLayout();Pump();var root=(FrameworkElement)window.Content;
        var image=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(root),null,new Rect(0,0,root.ActualWidth,root.ActualHeight));image.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(path))encoder.Save(file);
    }
}
