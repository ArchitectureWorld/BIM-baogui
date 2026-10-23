using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Hifc.Reference;

namespace Hifc.Revit.PlatformSync
{
    internal static class PlatformSyncRegistry
    {
        static readonly Dictionary<Document, PlatformSyncExportSession> Sessions = new Dictionary<Document, PlatformSyncExportSession>();
        internal static PlatformSyncExportSession Session(Document document)
        {
            foreach (var closed in Sessions.Keys.Where(d => !d.IsValidObject).ToArray()) Sessions.Remove(closed);
            if (!Sessions.TryGetValue(document, out var session)) Sessions[document] = session = new PlatformSyncExportSession();
            return session;
        }
        internal static void BeginExport(Document document)
        {
            // Sync is optional. A preview storage failure must never prevent normal export.
            try { Session(document).BeginExport(); } catch (Exception) { }
        }
        internal static void RegisterCompleted(Document document, string ifc, string receipt, string exportId)
        {
            try { Session(document).Register(ifc, receipt, exportId, document.PathName, document.ProjectInformation.UniqueId); }
            catch (Exception) { /* The native export has already succeeded; no sync task is created. */ }
        }
        internal static void Clear() { Sessions.Clear(); }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class PlatformSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try { PlatformSyncController.Open(data.Application); return Result.Succeeded; }
            catch (Exception error) { return CommandScope.Failure(error, ref message); }
        }
    }

    /// <summary>All document identity reads from the modeless window use a Revit ExternalEvent.</summary>
    internal sealed class PlatformSyncController : IPlatformSyncHost, IExternalEventHandler
    {
        static readonly Dictionary<Document, PlatformSyncController> Windows = new Dictionary<Document, PlatformSyncController>();
        readonly Document document;
        readonly ExternalEvent external;
        TaskCompletionSource<PlatformExportContext> pending;
        bool closed, disposed;
        internal PlatformSyncWindow Window { get; }
        PlatformSyncController(UIApplication ui)
        {
            document = ui.ActiveUIDocument.Document;
            external = ExternalEvent.Create(this);
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HIFC-V1", "platform-sync-preview");
            Window = new PlatformSyncWindow(this, new PlatformSyncClient(), Path.Combine(root, "ui-settings.json"), Path.Combine(root, "handoffs"));
            new System.Windows.Interop.WindowInteropHelper(Window).Owner = ui.MainWindowHandle;
        }
        internal static void Open(UIApplication ui)
        {
            var doc = ui.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument) throw new InvalidOperationException("请先打开需要同步的项目模型。");
            if (Windows.TryGetValue(doc, out var current) && !current.closed) { current.Window.Activate(); current.Window.ReadExportStatus(); return; }
            var controller = new PlatformSyncController(ui); Windows[doc] = controller; controller.Window.Show(); controller.Window.ReadExportStatus();
        }
        public Task<PlatformExportContext> ReadCurrentAsync()
        {
            if (closed) return Failed("平台同步窗口已关闭。");
            if (pending != null) return Failed("已有文档核对正在等待 Revit，请稍候。");
            pending = new TaskCompletionSource<PlatformExportContext>(TaskCreationOptions.RunContinuationsAsynchronously); var task = pending.Task;
            var result = external.Raise();
            if (result != ExternalEventRequest.Accepted && result != ExternalEventRequest.Pending)
            { pending.SetException(new InvalidOperationException("Revit 暂不能核对当前模型，请稍后重试。")); pending = null; }
            return task;
        }
        static Task<PlatformExportContext> Failed(string message)
        { var task = new TaskCompletionSource<PlatformExportContext>(); task.SetException(new InvalidOperationException(message)); return task.Task; }
        public void Execute(UIApplication ui)
        {
            if (closed) { Dispose(); return; }
            var request = pending; pending = null; if (request == null) return;
            try
            {
                if (!document.IsValidObject || !document.Equals(ui.ActiveUIDocument?.Document)) throw new InvalidOperationException("请切回此窗口对应的 Revit 项目；其它模型不能使用本次导出。");
                var session = PlatformSyncRegistry.Session(document);
                request.SetResult(new PlatformExportContext { DocumentPath = document.PathName, ProjectUniqueId = document.ProjectInformation.UniqueId, Completed = session.Completed, ExportStatus = session.Status });
            }
            catch (Exception error) { request.SetException(error); }
        }
        public string GetName() => "H-IFC 平台同步预开发 · 文档核对";
        public void Close()
        {
            if (closed) return; closed = true; Windows.Remove(document); pending?.TrySetException(new InvalidOperationException("平台同步窗口已关闭。")); pending = null; external.Raise();
        }
        void Dispose() { if (disposed) return; disposed = true; external.Dispose(); }
        internal static void Shutdown()
        {
            foreach (var window in Windows.Values.ToArray()) { window.Window.Close(); window.Dispose(); }
            Windows.Clear(); PlatformSyncRegistry.Clear();
        }
    }
}
