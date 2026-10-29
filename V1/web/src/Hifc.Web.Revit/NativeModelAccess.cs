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
        public Task<NativeDefinitionSnapshot[]> DefinitionsAsync(string[] sources) => Run((ui, doc) => NativeInventory.ReadDefinitions(doc, sources));
        public Task<ProjectDataRecord[]> ProjectDataAsync() => Run((ui, doc) => NativeProjectData.Read(doc));
        public Task<NativeRedlineEvidence> RedlineEvidenceAsync(string[] uniqueIds) => Run((ui, doc) => NativeRedlineGeometry.Capture(doc, uniqueIds));
        public Task<Tuple<string, string>> DocumentIdentityAsync() => Run((ui, doc) => Tuple.Create(doc.PathName, doc.ProjectInformation.UniqueId));
        public Task<EditDraft[]> NormalizeDraftsAsync(EditDraft[] drafts, FieldResult[] fields) => Run((ui, doc) => drafts.Select(draft => {
            var field = fields.Single(f => f.Key == draft.FieldKey); var value = draft.Value;
            var parameters = doc.GetElement(field.OwnerUniqueId)?.GetParameters(field.Source);
            if (field.RawType == "Boolean")
            {
                if (value == "是" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) value = "1";
                else if (value == "否" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) value = "0";
                else if (value != "" && value != "0" && value != "1") throw new InvalidOperationException(field.Property + "：请选择是、否或未填写。");
            }
            else if (field.RawType == "Real" || field.RawType == "Integer" || parameters?.Count == 1 && parameters[0].StorageType == StorageType.Double)
            {
                if (value == "" && (parameters == null || parameters.Count == 0 || parameters[0].StorageType == StorageType.String)) { }
                else value = NativeUnitInput.Normalize(value, field.InputUnit, (text, unit) => {
                    var type = unit == "m²" ? UnitType.UT_Area : UnitType.UT_Length;
                    if (!UnitFormatUtils.TryParse(doc.GetUnits(), type, text, out var raw)) return null;
                    return UnitUtils.ConvertFromInternalUnits(raw, unit == "m²" ? DisplayUnitType.DUT_SQUARE_METERS : DisplayUnitType.DUT_METERS);
                });
            }
            return new EditDraft { FieldKey = draft.FieldKey, OriginalSignature = draft.OriginalSignature, OriginalValue = draft.OriginalValue, Value = value };
        }).ToArray());
        public Task<WriteReceipt> ApplyAsync(NativeWriteInput input, PublicSigningKey key, Func<DateTime> now) => Run((ui, doc) => NativeWriter.Apply(doc, input, key, now()));
        public Task<ExportReceipt> ExportAsync(NativeExportInput input, PublicSigningKey key, Func<DateTime> now) => Run((ui, doc) => NativeExportExecutor.Execute(doc, input, key, now()));
        public Task LocateAsync(int[] ids) => Run((ui, doc) => { var elements = ids.Select(i => new ElementId(i)).Where(id => doc.GetElement(id) != null).ToArray(); ui.ActiveUIDocument.Selection.SetElementIds(elements); if (elements.Length > 0) ui.ActiveUIDocument.ShowElements(elements); return true; });
        public Task SaveAsync() => Run((ui, doc) => { if (string.IsNullOrWhiteSpace(doc.PathName)) throw new InvalidOperationException("请先在 Revit 中将模型另存为副本。"); doc.Save(); return true; });
        public void Execute(UIApplication application) { while (jobs.TryDequeue(out var job)) job(application); }
        public string GetName() => "HIFC Web · 模型采集与写入";
        public void Dispose() { disposed = true; while (jobs.TryDequeue(out var job)) job(null); external.Dispose(); }
    }
}
