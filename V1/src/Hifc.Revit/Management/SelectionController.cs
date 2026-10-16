using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;

namespace Hifc.Revit.Management
{
    internal sealed class SelectionController:IExternalEventHandler,ISelectionSession
    {
        static readonly Dictionary<Document,SelectionController> Windows=new Dictionary<Document,SelectionController>();
        readonly UIApplication owner;
        readonly Document document;
        readonly ExternalEvent external;
        readonly ManagementStateStore store;
        Action<UIApplication> pending;
        ModelSnapshot snapshot;
        ManualWrite[] history=new ManualWrite[0];
        long epoch,readEpoch;
        bool closed,disposed;
        internal SelectionWindow Window {get;}
        SelectionController(UIApplication ui)
        {
            owner=ui;document=ui.ActiveUIDocument.Document;external=ExternalEvent.Create(this);
            store=new ManagementStateStore(Path.Combine(RuntimePaths.OutputDirectory(),"management-profiles"),ModelReader.Key(document));
            Window=new SelectionWindow(this);new System.Windows.Interop.WindowInteropHelper(Window).Owner=ui.MainWindowHandle;
            try{var state=store.Load();history=state.ManualWrites;var kind=state.Kind=="总图"||state.Kind=="单体建筑"?state.Kind:state.Snapshot?.ModelKind;Window.InitializeContext(state.Stage,kind);}
            catch(Exception ex){Window.SetStatus("暂存未能读取，请确认阶段和模型类型："+ex.Message,true);}
            ui.Application.DocumentChanged+=Changed;
        }
        internal static void Open(UIApplication ui)
        {
            var doc=ui.ActiveUIDocument.Document;
            if(Windows.TryGetValue(doc,out var existing)&&!existing.closed){existing.Window.Activate();return;}
            var c=new SelectionController(ui);Windows[doc]=c;c.Window.Show();
            if(c.Window.Stage!=null&&c.Window.Kind!=null)c.Read();
        }
        void Changed(object sender,DocumentChangedEventArgs e){if(!closed&&document.Equals(e.GetDocument())){epoch++;Window.MarkStale("模型已变化，待写入修改已保留；请重新读取当前选择。");}}
        void Guard(UIApplication ui)
        {
            if(!document.IsValidObject||!document.Equals(ui.ActiveUIDocument?.Document))throw new InvalidOperationException("请切回此窗口对应的项目。");
        }
        void Queue(Action<UIApplication> action)
        {
            if(closed)return;if(pending!=null){Window.SetStatus("已有操作执行中，请稍候。",true);return;}
            pending=action;Window.SetBusy(true);var result=external.Raise();
            if(result!=ExternalEventRequest.Accepted&&result!=ExternalEventRequest.Pending){pending=null;Window.SetBusy(false);Window.SetStatus("Revit 暂无法接受操作，请稍后重试。",true);}
        }
        public string GetName()=>"H-IFC 选中构件赋属性";
        public void Execute(UIApplication ui)
        {
            if(closed){Dispose(ui);return;}var action=pending;pending=null;
            try{Guard(ui);action?.Invoke(ui);}catch(Exception ex){Window.SetBusy(false);Window.MarkStale(ex.Message);}
        }
        public void Read()=>Queue(ui=>ReadCore(ui,ui.ActiveUIDocument.Selection.GetElementIds().Select(id=>id.IntegerValue).ToArray()));
        void ReadCore(UIApplication ui,int[] ids)
        {
            var result=SelectionService.Read(document,ids,Window.Stage,Window.Kind);snapshot=result.Snapshot;readEpoch=epoch;
            Window.Accept(result);
            try{var state=store.Load();state.Stage=Window.Stage;state.Kind=Window.Kind;store.Save(state);history=state.ManualWrites;}
            catch(Exception ex){Window.SetStatus("读取完成，但暂存保存失败："+ex.Message,true);}
            var slots=snapshot.Elements.Select(r=>r.Slot).Distinct().ToArray();
            if(snapshot.Elements.Length>0&&slots.Length==1&&result.Slots.Contains(slots[0]))ClassifyCore(slots[0]);
        }
        void RequireCurrent()
        {
            if(snapshot==null||readEpoch!=epoch||snapshot.Stage!=Window.Stage||snapshot.ModelKind!=Window.Kind)
                throw new InvalidOperationException("模型、阶段或类型已变化，请重新读取当前选择。");
        }
        public void Classify(string slot)=>Queue(ui=>ClassifyCore(slot));
        void ClassifyCore(string slot)
        {
            RequireCurrent();var preview=SelectionService.Classify(document,snapshot,slot);
            Window.ShowClassification(slot,preview,history);
        }
        public void Write(EditRequest[] requests)=>Queue(ui=>{
            RequireCurrent();var selected=snapshot.Elements.Select(e=>e.Id).ToArray();var stage=snapshot.Stage;var kind=snapshot.ModelKind;
            var applied=SelectionService.Write(document,snapshot,requests);
            var warning="";ManagementState records;
            try{records=store.Load();}catch(Exception ex){records=new ManagementState {DocumentKey=ModelReader.Key(document),ManualWrites=history};warning="；暂存读取失败："+ex.Message;}
            var writes=records.ManualWrites.ToList();
            foreach(var request in requests.Where(r=>r.Kind=="赋值"))foreach(var baseline in request.Baselines)
            {
                var change=applied.Changes.LastOrDefault(c=>c.OwnerUniqueId==baseline.OwnerUniqueId&&c.Source==request.Source&&c.Action!="新增参数");if(change==null)continue;
                var key=baseline.OwnerUniqueId+"|"+request.PropertySet+"|"+request.Binding+"|"+request.Source;var prior=writes.FirstOrDefault(w=>w.Key==key);writes.RemoveAll(w=>w.Key==key);
                writes.Add(new ManualWrite {Key=key,Value=change.ExpectedRaw,PreviousValue=baseline.Value,WrittenUtc=DateTime.UtcNow.ToString("O"),Count=(prior?.Count??0)+1});
            }
            records.ManualWrites=writes.ToArray();records.Stage=stage;records.Kind=kind;history=records.ManualWrites;
            try{store.Save(records);}catch(Exception ex){warning+="；暂存保存失败："+ex.Message;}
            ReadCore(ui,selected);Window.SetStatus((applied.Changes.Length>0?"所选构件已写入并回读验证，正在进行全项目检查。":"所选构件没有新变更，正在进行全项目检查。")+warning,warning.Length>0);
            ManagerController.CheckAfterSelection(ui,stage,kind,history);
        });
        public void Close(){if(closed)return;closed=true;pending=null;Windows.Remove(document);external.Raise();}
        void Dispose(UIApplication ui){if(disposed)return;disposed=true;ui.Application.DocumentChanged-=Changed;external.Dispose();}
        internal static void Shutdown(){foreach(var c in Windows.Values.ToArray()){c.closed=true;c.Window.Close();c.Dispose(c.owner);}Windows.Clear();}
    }
}
