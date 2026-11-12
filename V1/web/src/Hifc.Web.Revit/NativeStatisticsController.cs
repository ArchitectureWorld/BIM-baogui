using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Hifc.Reference;
using Hifc.Revit.Management;
using Hifc.Web.Contracts;
using Microsoft.Win32;

namespace Hifc.Web.Revit
{
    internal sealed class NativeStatisticsController : IExternalEventHandler
    {
        private sealed class Job { internal Action<UIApplication> Execute; internal Action Cancel; }
        private readonly UIApplication owner;
        private readonly WebModelSession session;
        private readonly string kind;
        private readonly ExternalEvent external;
        private readonly ConcurrentQueue<Job> jobs = new ConcurrentQueue<Job>();
        private readonly Dictionary<Document, string> documents = new Dictionary<Document, string>();
        private readonly BuildingAreaStatisticsWindow building;
        private readonly SiteStatisticsWindow site;
        private readonly Window window;
        private readonly Document host;
        private NativeStatisticsCapture capture;
        private StatisticsResponse report;
        private long epoch, reportEpoch;
        private bool busy, closed, disposed;
        private string documentList;

        internal static Window Open(UIApplication ui, WebModelSession session, string kind)
        {
            if (kind != "building" && kind != "site") throw new ArgumentException("Unknown statistics kind.");
            if (ui.ActiveUIDocument?.Document == null) throw new InvalidOperationException("请打开项目文档。");
            var controller = new NativeStatisticsController(ui, session, kind);
            controller.window.Show(); return controller.window;
        }

        private NativeStatisticsController(UIApplication ui, WebModelSession session, string kind)
        {
            owner = ui; this.session = session; this.kind = kind; host = ui.ActiveUIDocument.Document;
            external = ExternalEvent.Create(this);
            if (kind == "building")
            {
                building = new BuildingAreaStatisticsWindow(); window = building;
                building.RefreshRequested += async key => await RefreshAsync(key, null);
                building.LocateRequested += async (key, ids) => await LocateAsync(new[] { new SiteStatisticsSelection { SourceKey = key, ElementIds = ids } });
                building.ExportRequested += async key => await ExportAsync(key);
            }
            else
            {
                site = new SiteStatisticsWindow(); window = site;
                site.RefreshRequested += async selected => await RefreshAsync(Key(host), selected);
                site.LocateRequested += async selected => await LocateAsync(selected);
                site.ExportRequested += async () => await ExportAsync(Key(host));
            }
            window.Title = "HIFC-V1-Web · " + (kind == "building" ? "单体面积统计" : "总图统计");
            new System.Windows.Interop.WindowInteropHelper(window).Owner = ui.MainWindowHandle;
            owner.Application.DocumentChanged += Changed;
            owner.Idling += Idle;
            window.Closed += (s, e) => Close();
            SyncDocuments();
            if (site != null) RefreshSourceChoices();
            window.Loaded += async (s, e) => await RefreshAsync(building?.SelectedDocumentKey ?? Key(host), site?.SelectedSourceKeys);
        }

        private string Key(Document document)
        {
            if (!documents.TryGetValue(document, out var value)) { value = Guid.NewGuid().ToString("N"); documents.Add(document, value); }
            return value;
        }
        private void SyncDocuments()
        {
            var available = owner.Application.Documents.Cast<Document>().Where(d => d.IsValidObject && !d.IsFamilyDocument && !d.IsLinked).ToArray();
            var signature = string.Join("|", available.Select(d => Key(d) + ":" + d.PathName + ":" + d.Title));
            if (signature == documentList) return;
            documentList = signature;
            building?.SetDocuments(available.Select(d => new BuildingAreaDocumentOption { Key = Key(d), Name = d.Title, Path = d.PathName }).ToArray(),
                building.SelectedDocumentKey ?? (owner.ActiveUIDocument?.Document == null ? null : Key(owner.ActiveUIDocument.Document)));
        }
        private void RefreshSourceChoices()
        {
            if (!host.IsValidObject) throw new InvalidOperationException("总图文档已关闭。");
            var sources = NativeStatisticsCapture.ListSources(host, Key(host), true);
            try { site.SetSources(sources.Select(s => s.Info).ToArray()); }
            finally { foreach (var source in sources) source.Dispose(); }
        }
        private void Idle(object sender, IdlingEventArgs e)
        {
            if (closed) { DisposeInApiContext(); return; }
            if (busy) return;
            SyncDocuments();
        }
        private void Changed(object sender, DocumentChangedEventArgs e)
        {
            if (closed) return;
            var changed = e.GetDocument();
            if (capture != null && !capture.Sources.Any(s => s.Document != null && s.Document.Equals(changed) || s.Host.Equals(changed))) return;
            epoch++; MarkStale("模型已变化，请刷新统计后再导出。");
        }
        private Task<T> Native<T>(Func<UIApplication, T> action)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (closed) { completion.SetCanceled(); return completion.Task; }
            jobs.Enqueue(new Job { Execute = ui => {
                if (completion.Task.IsCompleted) return;
                try { if (closed) completion.TrySetCanceled(); else completion.TrySetResult(action(ui)); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }, Cancel = () => completion.TrySetCanceled() });
            try
            {
                var request = external.Raise();
                if (request == ExternalEventRequest.Denied || request == ExternalEventRequest.TimedOut)
                    completion.TrySetException(new InvalidOperationException("请结束当前 Revit 命令后重试。"));
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            return completion.Task;
        }
        public void Execute(UIApplication app)
        {
            if (closed) { DisposeInApiContext(); return; }
            while (jobs.TryDequeue(out var job)) job.Execute(app);
        }
        public string GetName() => "HIFC Web statistics read-only capture";

        private async Task RefreshAsync(string key, string[] selected)
        {
            if (busy || closed) return;
            busy = true; SetBusy(true, "正在连接 Web 统计服务……"); report = null;
            try
            {
                var rule = await session.StatisticsRuleVersionAsync();
                await Native(ui => {
                    SyncDocuments();
                    var document = documents.SingleOrDefault(pair => pair.Value == key).Key;
                    if (document == null || !document.IsValidObject) throw new InvalidOperationException("源文档已关闭，请重新选择。");
                    capture?.Dispose();
                    var sources = NativeStatisticsCapture.ListSources(document, key, kind == "site");
                    capture = new NativeStatisticsCapture(kind, rule, sources, selected, session.Workspace.Settings.RuleProfileId);
                    return true;
                });
                var capturedEpoch = epoch;
                foreach (var source in capture.Request.Sources.Where(s => s.Source.Selected && s.Source.Available))
                {
                    SetStatus("正在盘点 " + source.Source.Name);
                    await Native(ui => { CheckEpoch(capturedEpoch); capture.ReadInventory(source.Source.Key); return true; });
                }
                var plan = await session.StatisticsCaptureAsync(capture.Request);
                if (plan.RuleVersion != rule) throw new InvalidOperationException("统计规则已更新，请重新刷新。");
                foreach (var source in plan.Sources)
                {
                    SetStatus("正在读取统计所需属性……");
                    await Native(ui => { CheckEpoch(capturedEpoch); capture.ReadValues(source); return true; });
                }
                var geometry = await session.StatisticsGeometryAsync(capture.Request);
                if (geometry.CaptureHash != StatisticsIdentity.Capture(capture.Request)) throw new InvalidOperationException("统计取数响应不匹配。");
                var measured = new List<StatisticsGeometryResult>();
                foreach (var group in geometry.Groups)
                {
                    SetStatus("正在核对几何投影 " + (measured.Count + 1) + " / " + geometry.Groups.Length);
                    measured.Add(await Native(ui => { CheckEpoch(capturedEpoch); return capture.Measure(group); }));
                }
                capture.Request.CaptureHash = geometry.CaptureHash; capture.Request.Geometry = measured.ToArray();
                var result = await session.StatisticsReportAsync(capture.Request);
                await Native(ui => { CheckEpoch(capturedEpoch); capture.Validate(); return true; });
                if (result.CaptureHash != geometry.CaptureHash) throw new InvalidOperationException("统计结果与本轮取数不一致。");
                report = result; reportEpoch = capturedEpoch;
                if (building != null) building.ShowReport(result.Building);
                else { site.SetSources(result.Site.Sources); site.ShowReport(result.Site); }
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { busy = false; SetBusy(false, null); }
        }

        private void CheckEpoch(long expected)
        {
            if (closed || epoch != expected) throw new InvalidOperationException("统计期间模型已变化，请重新刷新。");
        }
        private async Task LocateAsync(SiteStatisticsSelection[] selections)
        {
            if (busy || closed) return;
            busy = true;
            try
            {
                await Native(ui => {
                    CheckEpoch(reportEpoch); capture.Validate();
                    var root = capture.Sources.Single(s => s.Info.IsHost);
                    if (!root.Document.Equals(ui.ActiveUIDocument?.Document))
                    {
                        if (string.IsNullOrEmpty(root.Document.PathName)) throw new InvalidOperationException("请先在 Revit 中切换到该未保存文档。");
                        ui.OpenAndActivateDocument(root.Document.PathName);
                    }
                    var visible = new List<ElementId>(); var corners = new List<XYZ>(); var hasLinks = false;
                    foreach (var selection in selections)
                    {
                        var source = capture.Sources.Single(s => s.Info.Key == selection.SourceKey); source.Validate();
                        foreach (var id in selection.ElementIds.Distinct())
                        {
                            var element = source.Document.GetElement(new ElementId(id));
                            if (element == null) throw new InvalidOperationException("统计构件已删除。");
                            if (source.LinkId == null) visible.Add(element.Id);
                            else { visible.Add(source.LinkId); hasLinks = true; }
                            var box = element.get_BoundingBox(null);
                            if (box != null)
                                foreach (var x in new[] { box.Min.X, box.Max.X })
                                foreach (var y in new[] { box.Min.Y, box.Max.Y })
                                foreach (var z in new[] { box.Min.Z, box.Max.Z })
                                    corners.Add(source.Placement.OfPoint(box.Transform.OfPoint(new XYZ(x, y, z))));
                        }
                    }
                    ui.ActiveUIDocument.Selection.SetElementIds(visible.Distinct().ToList());
                    var view = ui.ActiveUIDocument.GetOpenUIViews().FirstOrDefault(v => v.ViewId == ui.ActiveUIDocument.ActiveView.Id);
                    if (hasLinks && corners.Count > 0 && view != null)
                        view.ZoomAndCenterRectangle(new XYZ(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z)),
                            new XYZ(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z)));
                    else if (visible.Count > 0) ui.ActiveUIDocument.ShowElements(visible.Distinct().ToList());
                    return true;
                });
                SetStatus("已定位所选构件。");
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { busy = false; SetBusy(false, null); }
        }

        private async Task ExportAsync(string key)
        {
            if (busy || closed) return;
            busy = true;
            try
            {
                if (report == null || capture.Request.DocumentKey != key) throw new InvalidOperationException("请先刷新当前统计。");
                await Native(ui => { CheckEpoch(reportEpoch); capture.Validate(); return true; });
                var workbook = await session.StatisticsWorkbookAsync(capture.Request);
                if (workbook.CaptureHash != report.CaptureHash) throw new InvalidOperationException("Excel 与当前统计不一致，请刷新。");
                var dialog = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = workbook.FileName, OverwritePrompt = true,
                    InitialDirectory = Directory.Exists(Path.GetDirectoryName(capture.Request.SourcePath)) ? Path.GetDirectoryName(capture.Request.SourcePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
                if (dialog.ShowDialog(window) != true) return;
                await Native(ui => { CheckEpoch(reportEpoch); capture.Validate(); return true; });
                var bytes = Convert.FromBase64String(workbook.ContentBase64);
                var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temporary, bytes); if (File.Exists(dialog.FileName)) File.Replace(temporary, dialog.FileName, null); else File.Move(temporary, dialog.FileName); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                SetStatus("统计 Excel 已导出。" + dialog.FileName);
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { busy = false; SetBusy(false, null); }
        }
        private void SetStatus(string message) { if (closed) return; building?.SetStatus(message); site?.SetStatus(message); }
        private void SetBusy(bool value, string message) { if (closed) return; building?.SetBusy(value, message); site?.SetBusy(value, message); }
        private void MarkStale(string message) { building?.MarkStale(message); site?.MarkStale(message); }
        private void Close()
        {
            closed = true;
            while (jobs.TryDequeue(out var job)) job.Cancel();
            external.Raise();
        }
        private void DisposeInApiContext()
        {
            if (disposed) return;
            disposed = true;
            owner.Application.DocumentChanged -= Changed; owner.Idling -= Idle;
            capture?.Dispose(); external.Dispose(); session.Dispose();
        }
    }
}
