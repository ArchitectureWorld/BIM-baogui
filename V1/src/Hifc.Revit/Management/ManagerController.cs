using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace Hifc.Revit.Management
{
    internal sealed class ManagerController : IExternalEventHandler, IManagerSession
    {
        private static readonly Dictionary<Document, ManagerController> Windows = new Dictionary<Document, ManagerController>();
        private readonly Document document;
        private readonly UIApplication owner;
        private readonly ExternalEvent external;
        private Action<UIApplication> pending;
        private CaseScanJob scan;
        private ModelSnapshot snapshot;
        private bool closed, cancelled, disposed;
        private long epoch, scanEpoch, previewEpoch;
        internal ManagerWindow Window { get; }

        private ManagerController(UIApplication ui)
        {
            owner = ui; document = ui.ActiveUIDocument.Document;
            external = ExternalEvent.Create(this);
            Window = new ManagerWindow(this, Path.Combine(RuntimePaths.OutputDirectory(), "management-profiles"));
            Window.RestoreState(ModelReader.Key(document));
            new System.Windows.Interop.WindowInteropHelper(Window).Owner = ui.MainWindowHandle;
            ui.Application.DocumentChanged += Changed;
        }
        internal static void Open(UIApplication ui)
        {
            var doc = ui.ActiveUIDocument.Document;
            if (Windows.TryGetValue(doc, out var current) && !current.closed) { current.Window.Activate(); return; }
            var controller = new ManagerController(ui); Windows[doc] = controller; controller.Window.Show();
            if (controller.Window.HasRememberedStage) controller.Scan();
        }
        internal static void CheckAfterSelection(UIApplication ui,string stage,string kind,ManualWrite[] history)
        {
            var doc=ui.ActiveUIDocument.Document;
            if(!Windows.TryGetValue(doc,out var c)||c.closed){c=new ManagerController(ui);Windows[doc]=c;c.Window.Show();}
            c.Window.MergeManualHistory(history);
            c.Window.StageSelector.SelectedItem=stage;c.Window.KindSelector.SelectedItem=kind;
            if(c.scan!=null)c.StopScan(ui);
            c.StartScan(ui);
        }
        private void Changed(object sender, DocumentChangedEventArgs args)
        {
            if (closed || !document.Equals(args.GetDocument())) return;
            epoch++; Window.MarkStale("模型已变化，请重新扫描。旧预览不能执行。");
        }
        private void Guard(UIApplication ui)
        {
            // Revit creates distinct managed wrappers for the same native document across callbacks.
            if (!document.IsValidObject || !document.Equals(ui.ActiveUIDocument?.Document))
                throw new InvalidOperationException("窗口对应项目已关闭或不再是当前项目。请切回该项目或重新打开管理窗口。");
        }
        private void Queue(Action<UIApplication> action)
        {
            if (closed) return;
            if (pending != null || scan != null) { Window.SetStatus("已有任务执行中，请等待或取消扫描。", true); return; }
            pending = action;
            var result = external.Raise();
            if (result != ExternalEventRequest.Accepted && result != ExternalEventRequest.Pending) { pending = null; Window.SetStatus("Revit 当前无法接受操作，请稍后重试。", true); }
        }
        public void Execute(UIApplication ui)
        {
            if (closed) { DisposeInApi(ui); return; }
            var action = pending; pending = null;
            if (action == null) return;
            try { Guard(ui); action(ui); }
            catch (Exception ex) { Window.SetBusy(false, ex.Message); Window.MarkStale(ex.Message); }
        }
        public string GetName() => "H-IFC 模型检查与属性管理";
        public void Scan() => Queue(StartScan);
        private void StartScan(UIApplication ui)
        {
            cancelled = false; snapshot = null; scan = new CaseScanJob(document, Window.SelectedStage, Window.RequestedKind); scanEpoch = epoch;
            Window.SetBusy(true, "开始扫描整个当前项目…", 0, scan.Total);
            ui.Idling += Tick;
        }
        private void Tick(object sender, IdlingEventArgs args)
        {
            var ui = sender as UIApplication ?? owner;
            try
            {
                if (closed || cancelled) { StopScan(ui); if (!closed) { Window.SetBusy(false, "扫描已取消，未生成新的完整结果。"); Window.MarkStale("扫描已取消，请重新扫描后处理。"); } return; }
                Guard(ui);
                if (epoch != scanEpoch) throw new InvalidOperationException("扫描期间模型发生变化，结果已丢弃，请重新扫描。");
                scan.ReadNext(document, 75);
                Window.SetBusy(true, "正在扫描 " + scan.Position + " / " + scan.Total + " 个构件…", scan.Position, scan.Total);
                if (scan.Complete)
                {
                    var result = scan.Snapshot(document); StopScan(ui); snapshot = result; Window.Accept(result);
                    EditPreview batchPreview = null;
                    if (result.Stage == "报规" && Hifc.Reference.CaseWorkflow.ForModel(result.Stage, result.ModelKind).Length > 0)
                    {
                        try { batchPreview = CaseEdits.PreviewBatch(document, snapshot, Window.DraftRequests); }
                        catch (Exception ex) { batchPreview = CaseEdits.Preview(document, snapshot); Window.SetStatus("暂存修改需重新确认：" + ex.Message, true); }
                        previewEpoch = epoch; Window.ShowPreview(batchPreview);
                    }
                    try
                    {
                        var directory = RuntimePaths.NewRunDirectory();
                        Evidence.Write(Path.Combine(directory, "management-scan.json"), new {
                            schema = "hifc.management-scan/2.0", assembly_sha256 = Evidence.FileHash(typeof(App).Assembly.Location),
                            complete = true, preselection_used = false, actual_revit_selection_count = ui.ActiveUIDocument.Selection.GetElementIds().Count,
                            summary = Window.Summary, snapshot = result, preview = batchPreview
                        });
                        ManagementReport.Write(Path.Combine(directory, "检查与缺失报告.html"), result, Window.Summary, batchPreview);
                    }
                    catch (Exception ex) { Window.SetStatus("扫描完成，但本地检测记录保存失败：" + ex.Message, true); }
                }
                else args.SetRaiseWithoutDelay();
            }
            catch (Exception ex) { StopScan(ui); if (!closed) { Window.SetBusy(false, ex.Message); Window.MarkStale(ex.Message); } }
        }
        private void StopScan(UIApplication ui) { ui.Idling -= Tick; scan = null; }
        public void Cancel() { cancelled = true; }
        public void Locate(int[] ids) => Queue(ui => {
            var targets = ids.Select(id => new ElementId(id)).ToArray(); ui.ActiveUIDocument.Selection.SetElementIds(targets);
            var geometry = targets.Where(id => !(document.GetElement(id) is ProjectInfo) && !(document.GetElement(id) is Level)).ToArray();
            if (geometry.Length > 0) ui.ActiveUIDocument.ShowElements(geometry); else Window.SetStatus("已选择信息载体；项目与标高数据可在右侧属性表查看。");
        });
        private void RequireCurrent()
        {
            if (!Window.Current || snapshot == null || epoch != scanEpoch || snapshot.Stage != Window.SelectedStage || snapshot.RequestedKind != Window.RequestedKind)
                throw new InvalidOperationException("阶段、类型或模型已变化，请重新全面检查。");
        }
        public void PrepareAll() => Queue(ui => { RequireCurrent(); var result = CaseEdits.Preview(document, snapshot); previewEpoch = epoch; Window.ShowPreview(result); });
        public void Preview(EditRequest request) => Queue(ui => { RequireCurrent(); var result = CaseEdits.Preview(document, snapshot, request); previewEpoch = epoch; Window.ShowPreview(result); });
        public void Apply(EditPreview preview) => Queue(ui => {
            RequireCurrent();
            if (epoch != previewEpoch || preview.ScanId != snapshot.ScanId) throw new InvalidOperationException("预览后模型已变化，请重新生成预览。");
            var requests = Window.DraftRequests;
            CaseEdits.Apply(document, preview); Window.RecordCommitted(preview, requests); Window.InvalidatePreview("修改已写入并回读验证，开始全面复检。"); StartScan(ui);
        });
        public void Write(EditRequest[] requests) => Queue(ui => {
            RequireCurrent();
            EditPreview combined;
            try { combined = CaseEdits.PreviewBatch(document, snapshot, requests); }
            catch (Exception ex) { Window.SetStatus("无法写入，修改已保留：" + ex.Message, true); return; }
            if (combined.Changes.Length == 0) { Window.SetStatus("当前没有需要写入的变更。"); return; }
            Window.ShowPreview(combined); Window.SetBusy(true, "正在写入 Revit 并回读验证…");
            CaseEdits.Apply(document, combined); Window.RecordCommitted(combined, requests);
            Window.InvalidatePreview("已写入 Revit；正在全面复检。请按需要保存 RVT 文件。"); StartScan(ui);
        });
        public void Export(int[] ids, string path) => Queue(ui => {
            var elements = ids.Select(id => document.GetElement(new ElementId(id)) ?? throw new InvalidOperationException("导出目标已删除，请复检。")).ToArray();
            if (elements.Any(e => { var row = ModelReader.Read(document, e); return !row.Classified || row.Fields.Any(f => !f.Valid); }))
                throw new InvalidOperationException("导出范围存在缺失或非法字段，请重新扫描并处理。");
            NativeExportService.Export(document, elements, path);
            Window.MarkStale("IFC 已生成，临时模型改动已回滚。请执行实际 IFC 核验与原软件同模型 A/B 对照；可重新扫描当前项目。");
        });
        public void Close()
        {
            if (closed) return;
            closed = true; cancelled = true; pending = null;
            Windows.Remove(document);
            // WPF Closed runs outside a Revit API callback. Even removing an
            // Application event is forbidden there (Revit 2020 throws).
            // ExternalEvent.Raise is the supported modeless entry point.
            external.Raise();
        }
        private void DisposeInApi(UIApplication ui)
        {
            if (disposed) return;
            StopScan(ui);
            owner.Application.DocumentChanged -= Changed;
            external.Dispose(); disposed = true;
        }
        internal static void Shutdown()
        {
            foreach (var current in Windows.Values.ToArray())
            {
                current.closed = true; current.pending = null;
                current.Window.Close(); current.DisposeInApi(current.owner);
            }
            Windows.Clear();
        }
    }
}
