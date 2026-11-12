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
    internal sealed class BuildingAreaStatisticsController : IExternalEventHandler
    {
        private sealed class Entry
        {
            internal Document Document;
            internal string Key = Guid.NewGuid().ToString("N");
            internal long Epoch;
        }
        private static BuildingAreaStatisticsController current;
        private readonly UIApplication owner;
        private readonly ExternalEvent external;
        private readonly List<Entry> documents = new List<Entry>();
        private readonly BuildingAreaStatisticsWindow window;
        private Action<UIApplication> pending;
        private BuildingAreaStatisticsReader reader;
        private Entry reading;
        private long readEpoch, reportEpoch;
        private BuildingAreaStatisticsReport report;
        private string documentSignature;
        private bool closed, disposed;

        private BuildingAreaStatisticsController(UIApplication ui)
        {
            owner = ui; external = ExternalEvent.Create(this); window = new BuildingAreaStatisticsWindow();
            new System.Windows.Interop.WindowInteropHelper(window).Owner = ui.MainWindowHandle;
            window.RefreshRequested += key => Queue(app => BeginRead(key));
            window.LocateRequested += (key, ids) => Queue(app => Locate(app, key, ids));
            window.ExportRequested += key => Queue(app => Export(key));
            window.Closed += (s, e) => { closed = true; pending = null; reader = null; external.Raise(); };
            ui.Application.DocumentChanged += Changed;
            ui.Idling += Idle;
            SyncDocuments();
        }

        internal static void Open(UIApplication ui)
        {
            if (current != null && !current.closed) { current.window.Activate(); return; }
            current = new BuildingAreaStatisticsController(ui);
            current.window.Show();
            if (current.window.SelectedDocumentKey != null) current.BeginRead(current.window.SelectedDocumentKey);
        }

        private void Queue(Action<UIApplication> action)
        {
            if (closed) return;
            pending = action;
            var response = external.Raise();
            if (response == ExternalEventRequest.Denied) { pending = null; window.SetBusy(false, "Revit 暂时无法响应，请结束当前命令后重试。"); }
        }

        public void Execute(UIApplication ui)
        {
            if (closed) { Dispose(); return; }
            var action = pending; pending = null;
            try { SyncDocuments(); action?.Invoke(ui); }
            catch (Exception ex) { Fail(ex); }
        }

        public string GetName() => "H-IFC 单体面积统计";

        private void BeginRead(string key)
        {
            try
            {
                reading = Find(key); readEpoch = reading.Epoch; report = null;
                window.SetBusy(true, "正在读取面积载体……");
                reader = new BuildingAreaStatisticsReader(reading.Document, key);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void Idle(object sender, IdlingEventArgs args)
        {
            if (closed) { Dispose(); return; }
            try
            {
                SyncDocuments();
                if (reader == null) return;
                if (!reading.Document.IsValidObject || reading.Epoch != readEpoch)
                    throw new InvalidOperationException("模型在读取期间发生变化，请刷新统计。");
                reader.ReadNext();
                if (!reader.Complete) { window.SetStatus(reader.Progress); args.SetRaiseWithoutDelay(); return; }
                report = reader.Report(); reportEpoch = readEpoch; reader = null;
                window.ShowReport(report);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void Changed(object sender, DocumentChangedEventArgs args)
        {
            var entry = documents.FirstOrDefault(d => d.Document.Equals(args.GetDocument()));
            if (closed || entry == null) return;
            entry.Epoch++;
            if (entry.Key != window.SelectedDocumentKey) return;
            reader = null; pending = null;
            window.SetBusy(false, null);
            window.MarkStale("模型已修改，请刷新统计。");
        }

        private void SyncDocuments()
        {
            var live = owner.Application.Documents.Cast<Document>().Where(d => !d.IsFamilyDocument && !d.IsLinked).ToArray();
            documents.RemoveAll(e => !e.Document.IsValidObject || !live.Any(d => d.Equals(e.Document)));
            foreach (var doc in live)
                if (!documents.Any(e => e.Document.Equals(doc))) documents.Add(new Entry { Document = doc });
            var signature = string.Join("\n", documents.Select(e => e.Key + "|" + e.Document.Title + "|" + e.Document.PathName));
            if (signature == documentSignature) return;
            documentSignature = signature;
            var selected = window.SelectedDocumentKey;
            if (selected == null) selected = documents.FirstOrDefault(e => e.Document.Equals(owner.ActiveUIDocument?.Document))?.Key;
            var options = documents.Select(e => new BuildingAreaDocumentOption { Key = e.Key, Name = e.Document.Title, Path = e.Document.PathName }).ToArray();
            window.SetDocuments(options, selected);
            if (selected != window.SelectedDocumentKey) { reader = null; report = null; window.SetBusy(false, "文档列表已更新，请刷新所选单体。"); }
            else if (report != null && report.SourcePath != Find(report.DocumentKey).Document.PathName)
            {
                reader = null; report = null;
                window.SetBusy(false, null); window.MarkStale("模型另存路径已变化，请刷新来源信息。");
            }
            else if (reader != null)
            {
                reader = null; window.SetBusy(false, "文档状态已变化，请刷新统计。");
            }
        }

        private Entry Find(string key)
            => documents.FirstOrDefault(e => e.Key == key && e.Document.IsValidObject)
                ?? throw new InvalidOperationException("统计文档已关闭，请选择当前已打开的单体。");

        private void Locate(UIApplication ui, string key, int[] ids)
        {
            var entry = Find(key);
            var valid = ids.Select(id => new ElementId(id)).Where(id => entry.Document.GetElement(id) != null).ToArray();
            if (valid.Length == 0) throw new InvalidOperationException("所选构件已不存在，请刷新统计。");
            var uiDocument = ui.ActiveUIDocument;
            if (uiDocument == null || !uiDocument.Document.Equals(entry.Document))
            {
                if (string.IsNullOrWhiteSpace(entry.Document.PathName)) throw new InvalidOperationException("请先切换到此未保存项目，再定位构件。");
                uiDocument = ui.OpenAndActivateDocument(entry.Document.PathName);
            }
            if (!uiDocument.Document.Equals(entry.Document)) throw new InvalidOperationException("活动文档与统计来源不一致，请重新选择单体。");
            uiDocument.Selection.SetElementIds(valid);
            uiDocument.ShowElements(valid);
            window.SetBusy(false, "已定位 " + valid.Length + " 个构件。");
        }

        private void Export(string key)
        {
            var entry = Find(key);
            if (report == null || report.DocumentKey != key || reportEpoch != entry.Epoch)
                throw new InvalidOperationException("统计结果已过期，请刷新后导出 Excel。");
            var safeName = new string(entry.Document.Title.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            var picker = new SaveFileDialog {
                Title = "导出单体面积统计", Filter = "Excel 工作簿 (*.xlsx)|*.xlsx", DefaultExt = ".xlsx",
                InitialDirectory = string.IsNullOrWhiteSpace(entry.Document.PathName) ? RuntimePaths.OutputDirectory() : Path.GetDirectoryName(entry.Document.PathName),
                FileName = safeName + "_面积统计_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx", OverwritePrompt = true
            };
            if (picker.ShowDialog(window) != true) { window.SetBusy(false, "已取消导出。"); return; }
            // Save the complete workbook first, then atomically replace only the destination confirmed in the dialog.
            var temporary = Path.Combine(Path.GetDirectoryName(picker.FileName), ".hifc-area-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                BuildingAreaStatisticsWorkbook.Write(temporary, report);
                if (File.Exists(picker.FileName)) File.Replace(temporary, picker.FileName, null);
                else File.Move(temporary, picker.FileName);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            window.SetBusy(false, "Excel 已保存至 " + picker.FileName);
        }

        private void Fail(Exception error)
        {
            reader = null;
            window.SetBusy(false, error.Message);
        }

        private void Dispose()
        {
            if (disposed) return;
            disposed = true; owner.Application.DocumentChanged -= Changed; owner.Idling -= Idle; external.Dispose();
            if (ReferenceEquals(current, this)) current = null;
        }

        internal static void Shutdown()
        {
            var controller = current;
            if (controller == null) return;
            controller.closed = true; controller.window.Close(); controller.Dispose();
        }
    }
}
