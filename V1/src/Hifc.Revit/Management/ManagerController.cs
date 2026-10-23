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
        private static readonly Dictionary<Document, string> StateKeys = new Dictionary<Document, string>();
        private static readonly Dictionary<Document, Action> PendingStateTransfers = new Dictionary<Document, Action>();
        private readonly Document document;
        private readonly UIApplication owner;
        private readonly ExternalEvent external;
        private Action<UIApplication> pending;
        private CaseScanJob scan;
        private ModelSnapshot snapshot;
        private bool closed, cancelled, disposed;
        private long epoch, scanEpoch, previewEpoch;
        private Action<ModelSnapshot,string,string> selectionCheckCompleted;
        internal ManagerWindow Window { get; }

        private ManagerController(UIApplication ui)
        {
            owner = ui; document = ui.ActiveUIDocument.Document;
            FinishStateTransfer(document);
            external = ExternalEvent.Create(this);
            Window = new ManagerWindow(this, Path.Combine(RuntimePaths.OutputDirectory(), "management-profiles"));
            Window.RestoreState(ModelReader.Key(document));
            StateKeys[document]=ModelReader.Key(document);
            PlatformContext.Set(document, Window.PlanningSettings);
            PlatformContext.SetStage(document, Window.SelectedStage);
            Window.UndergroundRequested+=ConfigureUnderground;
            Window.PlanningRequested+=ConfigurePlanning;
            Window.WorkbookRequested+=ImportWorkbook;
            Window.RedlineRequested+=()=>Queue(RedlineController.Show);
            new System.Windows.Interop.WindowInteropHelper(Window).Owner = ui.MainWindowHandle;
            ui.Application.DocumentChanged += Changed;
        }
        internal static void Open(UIApplication ui)
        {
            var doc = ui.ActiveUIDocument.Document;
            FinishStateTransfer(doc);
            if (Windows.TryGetValue(doc, out var current) && !current.closed) { if(!current.Window.IsVisible)current.Window.Show(); current.Window.Activate(); return; }
            var controller = new ManagerController(ui); Windows[doc] = controller; controller.Window.Show();
            if (controller.Window.HasRememberedStage) controller.Scan();
        }
        internal static void EnsureExportReady(Document doc)
        {
            FinishStateTransfer(doc);
            if (Windows.TryGetValue(doc, out var current) && !current.closed)
            {
                if (current.scan != null) throw new InvalidOperationException("全面检查仍在进行，请检查完成后再导出。");
                current.Window.EnsureExportReady();
                PlatformContext.Set(doc,current.Window.PlanningSettings);
                PlatformContext.SetStage(doc,current.Window.SelectedStage);
            }
            else
                new ManagementStateStore(Path.Combine(RuntimePaths.OutputDirectory(), "management-profiles"), ModelReader.Key(doc)).Load().EnsureExportReady();
        }
        internal static void CheckAfterSelection(UIApplication ui,string stage,string kind,ManualWrite[] history)
            =>CheckAfterSelectionWithResult(ui,stage,kind,history,null);
        internal static void CheckAfterSelectionWithResult(UIApplication ui,string stage,string kind,ManualWrite[] history,Action<ModelSnapshot,string,string> completed)
        {
            var doc=ui.ActiveUIDocument.Document;
            if(!Windows.TryGetValue(doc,out var c)||c.closed){c=new ManagerController(ui);Windows[doc]=c;}
            c.selectionCheckCompleted=completed;
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
        internal static void SavedAs(object sender, DocumentSavedAsEventArgs args)
        {
            if(args.Status!=RevitAPIEventStatus.Succeeded)return;
            var doc=args.Document;
            if(doc.IsFamilyDocument)return;
            var oldKey=StateKeys.TryGetValue(doc,out var known)?known:ModelReader.KeyAtPath(doc,args.OriginalPath);
            var key=ModelReader.Key(doc);StateKeys[doc]=key;
            if(key==oldKey)return;
            if(Windows.TryGetValue(doc,out var current)&&!current.closed) { current.Window.RebindDocumentKey(key);return; }
            PendingStateTransfers.TryGetValue(doc,out var preceding);
            PendingStateTransfers[doc]=()=>{
                preceding?.Invoke();
                var root=Path.Combine(RuntimePaths.OutputDirectory(),"management-profiles");
                var source=new ManagementStateStore(root,oldKey);
                if(!source.Exists)return;
                var state=source.Load();state.RebindDocumentKey(key);
                new ManagementStateStore(root,key).Save(state);
            };
            try { FinishStateTransfer(doc); }
            catch(Exception error) { TaskDialog.Show("H-IFC V1 · 暂存未迁移",error.Message); }
        }
        private static void FinishStateTransfer(Document doc)
        {
            if(!PendingStateTransfers.TryGetValue(doc,out var transfer))return;
            try { transfer();PendingStateTransfers.Remove(doc); }
            catch(Exception error) { throw new InvalidOperationException("模型已另存为，但暂存记录未能转移。旧记录仍保留，当前不能导出；请恢复暂存目录访问后重试。"+error.Message,error); }
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
        private void ConfigureUnderground()=>Queue(ui=>{
            if(Window.SelectedStage!="报规")throw new InvalidOperationException("地下楼层指标当前适用于报规阶段，请先确认阶段。");
            var dialog=new UndergroundWindow(UndergroundContextReader.Levels(document),Window.UndergroundSettings){Owner=Window};
            if(dialog.ShowDialog()!=true)return;
            Window.SetUndergroundSettings(dialog.Result);StartScan(ui);
        });
        private void StartScan(UIApplication ui)
        {
            PlatformContext.Set(document, Window.PlanningSettings);
            PlatformContext.SetStage(document, Window.SelectedStage);
            cancelled = false; snapshot = null; scan = new CaseScanJob(document, Window.SelectedStage, Window.RequestedKind, Window.UndergroundSettings,Window.PlanningSettings); scanEpoch = epoch;
            Window.SetBusy(true, "开始扫描整个当前项目…", 0, scan.Total);
            ui.Idling += Tick;
        }
        private void ConfigurePlanning()=>Queue(ui=>{
            RequireCurrent();
            if(snapshot.Stage!="报规" || snapshot.ModelKind!="总图" && snapshot.ModelKind!="单体建筑")
                throw new InvalidOperationException("请先确认报规阶段及模型类型。");
            var dialog=new PlanningWindow(snapshot,Window.PlanningSettings){Owner=Window};
            dialog.LocateRequested+=ids=>{var elements=ids.Select(id=>new ElementId(id)).ToArray();ui.ActiveUIDocument.Selection.SetElementIds(elements);ui.ActiveUIDocument.ShowElements(elements);};
            if(dialog.ShowDialog()!=true)return;
            Window.SetPlanningSettings(dialog.Result);StartScan(ui);
        });
        private void ImportWorkbook()=>Queue(ui=>{
            RequireCurrent();
            var dialog=new ReportingWorkbookWindow(snapshot.Elements,snapshot.ModelKind,ReportingWorkbookProjectStorage.Load(document).Select(e=>e.Import).ToArray());
            if(Window.IsVisible)dialog.Owner=Window;else new System.Windows.Interop.WindowInteropHelper(dialog).Owner=ui.MainWindowHandle;
            if(dialog.ShowDialog()==true)Window.StageWorkbook(dialog.Decision);
        });
        private void Tick(object sender, IdlingEventArgs args)
        {
            var ui = sender as UIApplication ?? owner;
            try
            {
                if (closed || cancelled) { StopScan(ui);var callback=selectionCheckCompleted;selectionCheckCompleted=null;callback?.Invoke(null,null,"扫描已取消，请重新全面检查。"); if (!closed) { Window.SetBusy(false, "扫描已取消，未生成新的完整结果。"); Window.MarkStale("扫描已取消，请重新扫描后处理。"); } return; }
                Guard(ui);
                if (epoch != scanEpoch) throw new InvalidOperationException("扫描期间模型发生变化，结果已丢弃，请重新扫描。");
                scan.ReadNext(document, 75);
                Window.SetBusy(true, "正在扫描 " + scan.Position + " / " + scan.Total + " 个构件…", scan.Position, scan.Total);
                if (scan.Complete)
                {
                    var result = scan.Snapshot(document); StopScan(ui); snapshot = result; Window.Accept(result);
                    EditPreview batchPreview = null;
                    if (result.Stage == "报规" && Hifc.Reference.CaseWorkflow.ForModel(result.Stage, result.ModelKind,result.Planning?.RuleProfileId).Length > 0)
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
                        var reportPath=Path.Combine(directory,"检查与缺失报告.html");
                        ManagementReport.Write(reportPath, result, Window.Summary, batchPreview);
                        var callback=selectionCheckCompleted;selectionCheckCompleted=null;callback?.Invoke(result,reportPath,null);
                    }
                    catch (Exception ex) { Window.SetStatus("扫描完成，但本地检测记录保存失败：" + ex.Message, true);var callback=selectionCheckCompleted;selectionCheckCompleted=null;callback?.Invoke(result,null,ex.Message); }
                }
                else args.SetRaiseWithoutDelay();
            }
            catch (Exception ex) { StopScan(ui);var callback=selectionCheckCompleted;selectionCheckCompleted=null;callback?.Invoke(null,null,ex.Message); if (!closed) { Window.SetBusy(false, ex.Message); Window.MarkStale(ex.Message); } }
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
            CaseEdits.ApplyWithWorkbook(document, preview,Window.PendingWorkbooks); Window.RecordCommitted(preview, requests);Window.WorkbookCommitted(); Window.InvalidatePreview("修改已写入并回读验证，开始全面复检。"); StartScan(ui);
        });
        public void Write(EditRequest[] requests) => Queue(ui => {
            RequireCurrent();
            EditPreview combined;
            try { combined = CaseEdits.PreviewBatch(document, snapshot, requests); }
            catch (Exception ex) { Window.SetStatus("无法写入，修改已保留：" + ex.Message, true); return; }
            if (combined.Changes.Length == 0 && Window.PendingWorkbooks.Length==0) { Window.SetStatus("当前没有需要写入的变更。"); return; }
            Window.ShowPreview(combined); Window.SetBusy(true, "正在写入 Revit 并回读验证…");
            CaseEdits.ApplyWithWorkbook(document, combined,Window.PendingWorkbooks); Window.RecordCommitted(combined, requests);Window.WorkbookCommitted();
            Window.InvalidatePreview("已写入 Revit；正在全面复检。请按需要保存 RVT 文件。"); StartScan(ui);
        });
        public void ExportProject(string path) => Queue(ui => {
            PlatformContext.Set(document, Window.PlanningSettings);
            PlatformContext.SetStage(document, Window.SelectedStage);
            if (!ExportReviewController.Confirm(ui,path)) { Window.SetStatus("已返回修改，IFC 尚未生成。"); return; }
            var scope = NativeExportService.ExportProject(document, path);
            Window.MarkStale(scope.Summary + "\n可重新扫描当前项目。导出报告不等于平台验收。");
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
            var callback=selectionCheckCompleted;selectionCheckCompleted=null;callback?.Invoke(null,null,"管理检查已关闭，请重新全面检查；已写入属性保留。");
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
