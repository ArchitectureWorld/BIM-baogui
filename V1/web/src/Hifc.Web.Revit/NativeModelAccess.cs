using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Hifc.Web.Contracts;
using ElementRecord = Hifc.Web.Contracts.ElementRecord;
using ParameterValue = Hifc.Web.Contracts.ParameterValue;

namespace Hifc.Web.Revit
{
    public sealed class NativeModelAccess : IExternalEventHandler, IDisposable
    {
        private readonly Document document;
        private readonly string path;
        private readonly ExternalEvent external;
        private readonly ConcurrentQueue<Action<UIApplication>> jobs = new ConcurrentQueue<Action<UIApplication>>();
        private bool disposed;
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public string DocumentKey { get; }
        public string Title { get; }
        public NativeModelAccess(UIApplication application)
        {
            document = application.ActiveUIDocument?.Document ?? throw new InvalidOperationException("请先打开项目模型副本。");
            if (document.IsFamilyDocument || document.IsLinked || document.IsReadOnly) throw new InvalidOperationException("请打开可编辑的项目模型副本。");
            path = document.PathName; Title = document.Title;
            DocumentKey = Canonical.Hash((path ?? "").ToLowerInvariant() + "|" + document.ProjectInformation.UniqueId);
            external = ExternalEvent.Create(this);
        }
        private Task<T> Run<T>(Func<UIApplication, Document, T> action)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (disposed) { completion.SetException(new ObjectDisposedException("Web 模型窗口")); return completion.Task; }
            jobs.Enqueue(ui => {
                if (completion.Task.IsCompleted) return;
                try {
                    if (disposed) throw new ObjectDisposedException("Web 模型窗口");
                    if (!document.IsValidObject || !document.Equals(ui.ActiveUIDocument?.Document) || document.PathName != path)
                        throw new InvalidOperationException("当前文档已切换、关闭或另存，请重新打开 Web 窗口。已有草稿已保留。");
                    completion.SetResult(action(ui, document));
                } catch (Exception ex) { completion.TrySetException(ex); }
            });
            try { if (external.Raise() == ExternalEventRequest.Denied) completion.TrySetException(new InvalidOperationException("Revit 暂时无法执行，请结束当前命令后重试。")); }
            catch (Exception ex) { completion.TrySetException(ex); }
            return completion.Task;
        }
        public Task<ElementRecord[]> InventoryAsync(IDictionary<string, string> assigned) => Run((ui, doc) => NativeInventory.Read(doc, assigned));
        public Task<ParameterValue[]> ValuesAsync(CaptureTarget[] targets) => Run((ui, doc) => NativeInventory.ReadValues(doc, targets));
        public Task<EditDraft[]> NormalizeDraftsAsync(EditDraft[] drafts, FieldResult[] fields) => Run((ui, doc) => drafts.Select(draft => {
            var field = fields.Single(f => f.Key == draft.FieldKey); var value = draft.Value;
            var parameters = doc.GetElement(field.OwnerUniqueId)?.GetParameters(field.Source);
            if (parameters?.Count == 1 && parameters[0].StorageType == StorageType.Double)
            {
                if (string.IsNullOrWhiteSpace(value) || !UnitFormatUtils.TryParse(doc.GetUnits(), parameters[0].Definition.UnitType, value, out var raw))
                    throw new InvalidOperationException(field.Property + "：请按当前项目单位输入数值，可附带单位；数值参数不能直接清空。");
                value = raw.ToString("R", CultureInfo.InvariantCulture);
            }
            return new EditDraft { FieldKey = draft.FieldKey, OriginalSignature = draft.OriginalSignature, OriginalValue = draft.OriginalValue, Value = value };
        }).ToArray());
        public Task<WriteReceipt> ApplyAsync(NativeWriteInput input, PublicSigningKey key, Func<DateTime> now) => Run((ui, doc) => NativeWriter.Apply(doc, input, key, now()));
        public Task LocateAsync(int[] ids) => Run((ui, doc) => { var elements = ids.Select(i => new ElementId(i)).Where(id => doc.GetElement(id) != null).ToArray(); ui.ActiveUIDocument.Selection.SetElementIds(elements); if (elements.Length > 0) ui.ActiveUIDocument.ShowElements(elements); return true; });
        public Task SaveAsync() => Run((ui, doc) => { if (string.IsNullOrWhiteSpace(doc.PathName)) throw new InvalidOperationException("请先在 Revit 中将模型另存为副本。"); doc.Save(); return true; });
        public void Execute(UIApplication application) { while (jobs.TryDequeue(out var job)) job(application); }
        public string GetName() => "HIFC Web · 模型采集与写入";
        public void Dispose() { disposed = true; while (jobs.TryDequeue(out var job)) job(null); external.Dispose(); }
    }
}
