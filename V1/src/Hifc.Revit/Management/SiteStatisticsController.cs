using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Hifc.Reference;
using Microsoft.Win32;

namespace Hifc.Revit.Management
{
    internal sealed class SiteStatisticsController : IExternalEventHandler
    {
        private static readonly List<SiteStatisticsController> Windows = new List<SiteStatisticsController>();
        private readonly UIApplication owner;
        private readonly Document document;
        private readonly string key = Guid.NewGuid().ToString("N");
        private readonly ExternalEvent external;
        private readonly SiteStatisticsWindow window;
        private SiteStatisticsDocument[] sources = new SiteStatisticsDocument[0];
        private string signature;
        private Action<UIApplication> pending;
        private SiteStatisticsReader reader;
        private SiteStatisticsReport report;
        private string[] reportKeys;
        private long epoch, readEpoch, reportEpoch;
        private bool closed, disposed;

        private SiteStatisticsController(UIApplication ui, Document doc)
        {
            owner=ui; document=doc; external=ExternalEvent.Create(this); window=new SiteStatisticsWindow();
            new System.Windows.Interop.WindowInteropHelper(window).Owner=ui.MainWindowHandle;
            window.RefreshRequested+=selected=>Queue(app=>BeginRead(selected));
            window.LocateRequested+=selection=>Queue(app=>Locate(app,selection));
            window.ExportRequested+=()=>Queue(app=>Export());
            window.Closed+=(s,e)=> { closed=true; pending=null; StopRead(); external.Raise(); };
            ui.Application.DocumentChanged+=Changed; ui.Idling+=Idle; SyncSources();
        }
        internal static void Open(UIApplication ui)
        {
            var existing=Windows.FirstOrDefault(c=>!c.closed && c.document.Equals(ui.ActiveUIDocument.Document));
            if(existing!=null) { existing.window.Activate(); return; }
            var controller=new SiteStatisticsController(ui,ui.ActiveUIDocument.Document); Windows.Add(controller); controller.window.Show();
            controller.BeginRead(controller.window.SelectedSourceKeys);
        }
        private void Queue(Action<UIApplication> action)
        {
            if(closed)return;
            pending=action;
            if(external.Raise()==ExternalEventRequest.Denied) { pending=null; window.SetBusy(false,"请结束当前Revit命令后重试。"); }
        }
        public void Execute(UIApplication ui)
        {
            if(closed) { Dispose(); return; }
            var action=pending; pending=null;
            try { SyncSources(); action?.Invoke(ui); } catch(Exception ex) { Fail(ex); }
        }
        public string GetName()=>"H-IFC 总图统计";
        private void BeginRead(string[] selected)
        {
            try
            {
                StopRead(); report=null; readEpoch=epoch;
                window.SetBusy(true,"正在读取总图及所选链接……");
                reader=new SiteStatisticsReader(document,key,sources,selected);
            }
            catch(Exception ex) { Fail(ex); }
        }
        private void Idle(object sender,IdlingEventArgs args)
        {
            if(closed) { Dispose(); return; }
            if(!document.IsValidObject) { window.Close(); Dispose(); return; }
            try
            {
                SyncSources();
                if(reader==null)return;
                if(readEpoch!=epoch)throw new InvalidOperationException("模型在统计过程中发生变化，请刷新。");
                reader.ReadNext();
                if(!reader.Complete) { window.SetStatus(reader.Progress); args.SetRaiseWithoutDelay(); return; }
                report=reader.Report(); reportEpoch=readEpoch;
                reportKeys=report.Sources.Where(s=>s.Available && (s.IsHost||s.Selected)).Select(s=>s.Key).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
                StopRead(); window.ShowReport(report);
            }
            catch(Exception ex) { Fail(ex); }
        }
        private void Changed(object sender,DocumentChangedEventArgs args)
        {
            if(closed || !sources.Any(s=>s.Document?.Equals(args.GetDocument())==true))return;
            epoch++; StopRead(); pending=null; window.SetBusy(false,null); window.MarkStale("总图或链接数据已变化，请刷新统计。");
        }
        private void SyncSources()
        {
            if(!document.IsValidObject)throw new InvalidOperationException("总图文档已关闭。");
            var next=SiteStatisticsReader.Sources(document,key);
            var nextSignature=string.Join("\n",next.Select(s=>s.Source.Key+"|"+s.Source.Name+"|"+s.Source.Available+"|"+SiteStatisticsReader.PlacementIdentity(s)));
            if(nextSignature==signature) { foreach(var source in next)source.Placement.Dispose(); return; }
            signature=nextSignature; epoch++; StopRead();
            var selected=new HashSet<string>(window.SelectedSourceKeys);
            foreach(var source in next)source.Source.Selected=source.Source.IsHost||selected.Contains(source.Source.Key);
            foreach(var source in sources)source.Placement.Dispose(); sources=next;
            window.SetSources(sources.Select(s=>s.Source).ToArray());
            window.SetBusy(false,null);
            if(report!=null) { report=null; window.MarkStale("来源或链接位置已变化，请刷新统计。"); }
        }
        private void Locate(UIApplication ui,SiteStatisticsSelection[] selected)
        {
            var uidoc=ui.ActiveUIDocument;
            if(uidoc==null || !uidoc.Document.Equals(document))
            {
                if(string.IsNullOrWhiteSpace(document.PathName))throw new InvalidOperationException("请切回此未保存总图，再定位构件。");
                uidoc=ui.OpenAndActivateDocument(document.PathName);
            }
            if(!uidoc.Document.Equals(document))throw new InvalidOperationException("活动文档与统计总图不一致。");
            var selectedIds=new List<ElementId>(); var corners=new List<XYZ>(); var count=0; var hasLinks=false;
            foreach(var group in selected)
            {
                var source=sources.FirstOrDefault(s=>s.Source.Key==group.SourceKey);
                if(source?.Document?.IsValidObject!=true)throw new InvalidOperationException("所选来源已关闭或卸载，请刷新。");
                foreach(var id in group.ElementIds)
                {
                    var element=source.Document.GetElement(new ElementId(id)); if(element==null)continue;
                    count++;
                    if(source.Source.IsHost) selectedIds.Add(element.Id);
                    else
                    {
                        var link=document.GetElement(source.LinkId) as RevitLinkInstance;
                        if(link==null)throw new InvalidOperationException("链接实例已变化，请刷新。");
                        selectedIds.Add(link.Id); hasLinks=true;
                    }
                    var box=element.get_BoundingBox(null);
                    if(box!=null)foreach(var x in new[]{box.Min.X,box.Max.X})foreach(var y in new[]{box.Min.Y,box.Max.Y})foreach(var z in new[]{box.Min.Z,box.Max.Z})
                        corners.Add(source.Placement.OfPoint(box.Transform.OfPoint(new XYZ(x,y,z))));
                }
            }
            if(count==0)throw new InvalidOperationException("所选构件已不存在，请刷新。");
            uidoc.Selection.SetElementIds(selectedIds.Distinct().ToArray());
            var view=uidoc.GetOpenUIViews().FirstOrDefault(v=>v.ViewId==uidoc.ActiveView.Id);
            if(hasLinks && corners.Count>0 && view!=null)
                view.ZoomAndCenterRectangle(new XYZ(corners.Min(p=>p.X),corners.Min(p=>p.Y),corners.Min(p=>p.Z)),new XYZ(corners.Max(p=>p.X),corners.Max(p=>p.Y),corners.Max(p=>p.Z)));
            else uidoc.ShowElements(selectedIds.Distinct().ToArray());
            window.SetBusy(false,"已定位 "+count+" 个构件范围。"+(hasLinks?"Revit 2020已选择对应链接实例，并缩放至所选构件范围。":""));
        }
        private void Export()
        {
            if(report==null || reportEpoch!=epoch || !window.SelectedSourceKeys.SequenceEqual(reportKeys??new string[0]))
                throw new InvalidOperationException("统计来源或数据已变化，请刷新后导出。");
            var name=new string(document.Title.Where(c=>!Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            var picker=new SaveFileDialog { Title="导出总图统计",Filter="Excel 工作簿 (*.xlsx)|*.xlsx",DefaultExt=".xlsx",OverwritePrompt=true,
                InitialDirectory=string.IsNullOrWhiteSpace(document.PathName)?RuntimePaths.OutputDirectory():Path.GetDirectoryName(document.PathName),
                FileName=name+"_总图统计_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".xlsx" };
            if(picker.ShowDialog(window)!=true) { window.SetBusy(false,"已取消导出。"); return; }
            var temporary=Path.Combine(Path.GetDirectoryName(picker.FileName),".hifc-site-"+Guid.NewGuid().ToString("N")+".xlsx");
            try { SiteStatisticsWorkbook.Write(temporary,report); if(File.Exists(picker.FileName))File.Replace(temporary,picker.FileName,null);else File.Move(temporary,picker.FileName); }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
            window.SetBusy(false,"Excel已保存至 "+picker.FileName);
        }
        private void Fail(Exception ex) { StopRead(); window.SetBusy(false,ex.Message); }
        private void StopRead() { reader?.Dispose(); reader=null; }
        private void Dispose()
        {
            if(disposed)return; disposed=true; StopRead();
            owner.Application.DocumentChanged-=Changed; owner.Idling-=Idle; external.Dispose();
            foreach(var source in sources)source.Placement.Dispose(); sources=new SiteStatisticsDocument[0]; Windows.Remove(this);
        }
        internal static void Shutdown() { foreach(var controller in Windows.ToArray()) { controller.closed=true;controller.window.Close();controller.Dispose(); } }
    }
}
