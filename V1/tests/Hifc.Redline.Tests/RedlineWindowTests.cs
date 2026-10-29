using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class RedlineWindowTests
{
    sealed class Session:IRedlineSession
    {
        internal RedlineData Saved;
        public RedlineReport Compare(RedlineData data,double toleranceM,double minimumGapM)=>new RedlineReport{Pair=new RedlineCheck{Passed=true,Status="通过",Detail="样本两实体已分离"},Manual=RedlineComparison.Manual(data,Program.Square(),toleranceM)};
        public void Save(RedlineData data){Saved=RedlineDataPolicy.Deserialize(RedlineDataPolicy.Serialize(data));}
    }
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(()=>{}));
    internal static void Run(Action<string,Action> check,string output)
    {
        check("wpf_import_retains_unknown_metadata_and_does_not_save",()=>{
            var session=new Session();var window=new RedlineWindow(session,"验收样本");
            try {window.Show();Pump();window.ImportManualFile(Program.Workbook("wpf-manual",Program.Rows()));Program.Assert(window.Current.Points.Length==4&&session.Saved==null&&window.UnitSelector.SelectedValue.Equals(RedlineUnit.Unknown)&&window.ManualConfirmed.IsChecked!=true,"import guessed metadata or saved model");}
            finally{window.Close();}
        });
        check("wpf_explicit_save_allows_store_only_unknown_frame",()=>{
            var session=new Session();var window=new RedlineWindow(session,"验收样本");
            try {window.ImportManualFile(Program.Workbook("wpf-store",Program.Rows()));window.ManualConfirmed.IsChecked=true;window.SaveData();Program.Assert(session.Saved!=null&&session.Saved.Accepted&&!RedlineDataPolicy.Validate(session.Saved).CanCompare&&window.StatusMessage.Contains("仅存资料"),"unconfirmed metadata reported pass or not stored");}
            finally{window.Close();}
        });
        check("wpf_confirmation_enables_manual_comparison_and_edit_invalidates_result",()=>{
            var session=new Session();var window=new RedlineWindow(session,"验收样本",Program.ManualData());
            try {window.Show();Pump();window.CompareNow();Program.Assert(window.LastReport.Manual.Passed,"confirmed manual comparison failed");window.FrameSelector.SelectedValue=RedlineFrame.Unknown;Pump();Program.Assert(window.LastReport==null,"changed coordinate frame retained old pass");window.CompareNow();Program.Assert(!window.LastReport.Manual.Passed&&window.LastReport.Pair.Passed,"pair and manual status conflated");}
            finally{window.Close();}
        });
        check("wpf_export_uses_imported_points_and_preserves_original",()=>{
            var session=new Session();var window=new RedlineWindow(session,"验收样本");var source=Program.Workbook("wpf-export-source",Program.Rows());var bytes=File.ReadAllBytes(source);
            try {window.ImportManualFile(source);window.ManualConfirmed.IsChecked=true;var destination=Path.Combine(output,"wpf-export-"+Guid.NewGuid().ToString("N")+".xlsx");window.ExportManualFile(destination);var points=RedlineWorkbook.Read(destination).Points;Program.Assert(points.Length==4&&points[2].X==10&&File.ReadAllBytes(source).SequenceEqual(bytes),"export invented points or touched original");}
            finally{window.Close();}
        });
        check("wpf_actual_window_renders_at_standard_and_minimum_size",()=>{
            var window=new RedlineWindow(new Session(),"人工坐标与模型双载体 · 验收样本",Program.ManualData());
            try {window.Show();Pump();window.CompareNow();Capture(window,Path.Combine(output,"redline-1100.png"));window.Width=960;window.Height=640;Pump();Capture(window,Path.Combine(output,"redline-960.png"));}
            finally{window.Close();}
        });
    }
    static void Capture(Window window,string path)
    {
        window.UpdateLayout();Pump();var root=(FrameworkElement)window.Content;
        Program.Assert(root.ActualWidth>900&&root.ActualHeight>500,"redline content clipped");
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(root),null,new Rect(0,0,root.ActualWidth,root.ActualHeight));bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);
    }
}
