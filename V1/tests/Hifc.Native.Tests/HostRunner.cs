using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using ComboBox = System.Windows.Controls.ComboBox;

// Temporary acceptance application. Registered only for the explicitly launched
// test process. Refuses all documents except the run's model copies; never saves.
public sealed class HifcAcceptanceHost : IExternalApplication, IExternalEventHandler
{
    static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    UIControlledApplication controlled;
    ExternalEvent workflowEvent;
    Action<UIApplication> workflowAction;
    string run, phase = "startup";
    string[] paths;
    int index, startupTicks;
    object controller, manager;
    Type controllerType;
    Window window;
    Document doc;
    Dictionary<string, object> report;
    Dictionary<string, bool> checks;
    DateTime deadline;
    bool uiFinished;
    string uiError;
    string firstScan;
    object priorPreview;
    Dictionary<int,string> reviewedCandidateRoles;
    readonly List<object> results = new List<object>();
    static object Get(object o, string n) => o.GetType().GetProperty(n, Instance).GetValue(o, null);
    static object Field(object o, string n) => o.GetType().GetField(n, Instance).GetValue(o);
    static object Call(object o, string n, params object[] args) => o.GetType().GetMethod(n, Instance).Invoke(o, args);
    static object[] Items(object o) => ((IEnumerable)o).Cast<object>().ToArray();
    static string Error(Exception e) => (e is TargetInvocationException && e.InnerException != null ? e.InnerException : e).ToString();
    void Write(string name, object value) { var j = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }; File.WriteAllText(Path.Combine(run, name), j.Serialize(value)); }
    void Log(string value) => File.AppendAllText(Path.Combine(run, "host-progress.log"), DateTime.Now.ToString("O") + " " + value + "\n");
    public Result OnStartup(UIControlledApplication app)
    {
        run = Environment.GetEnvironmentVariable("HIFC_V1_NATIVE_TEST_RUN");
        if (string.IsNullOrWhiteSpace(run)) return Result.Succeeded;
        if (!Path.IsPathRooted(run) || !Directory.Exists(Path.Combine(run, "models"))) return Result.Failed;
        controlled = app; paths = Directory.GetFiles(Path.Combine(run, "models"), "*.rvt").Where(p => !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), @"\.\d{4}\.rvt$")).OrderBy(p => Path.GetFileName(p).StartsWith("总图") ? 0 : 1).ThenBy(p => p).ToArray();
        deadline = DateTime.UtcNow.AddMinutes(8); app.Idling += Tick; Log("registered: " + paths.Length + " copies"); return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication app) { if (controlled != null) app.Idling -= Tick; workflowEvent?.Dispose(); return Result.Succeeded; }
    public string GetName() => "HIFC acceptance document lifecycle";
    public void Execute(UIApplication ui)
    {
        var action = workflowAction; workflowAction = null;
        try { action?.Invoke(ui); } catch (Exception e) { Fail(e); }
    }
    void Schedule(Action<UIApplication> action)
    {
        workflowAction = action;
        var request = workflowEvent.Raise();
        if (request != ExternalEventRequest.Accepted && request != ExternalEventRequest.Pending) throw new InvalidOperationException("Acceptance event rejected: " + request);
    }
    void Fail(Exception e)
    {
        Log("ERROR " + Error(e)); Write("host-error.json", new { phase, document = doc?.PathName, error = Error(e), partial = report, status = manager == null ? null : Get(manager, "StatusText") }); controlled.Idling -= Tick;
    }
    void OnUi(Action action)
    {
        uiFinished = false; uiError = null;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
            try { action(); } catch (Exception e) { uiError = Error(e); }
            finally { uiFinished = true; }
        }));
    }
    void Tick(object sender, IdlingEventArgs args)
    {
        var ui = sender as UIApplication; if (ui == null) return;
        try
        {
            if (phase == "startup" && startupTicks == 0) { deadline = DateTime.UtcNow.AddMinutes(8); workflowEvent = ExternalEvent.Create(this); }
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Host acceptance timed out in " + phase);
            if (phase == "startup")
            {
                if (++startupTicks < 3) { args.SetRaiseWithoutDelay(); return; }
                if (ui.Application.Documents.Cast<Document>().Any()) throw new InvalidOperationException("Acceptance requires a fresh host with no documents.");
                var a = AppDomain.CurrentDomain.GetAssemblies().Single(x => x.GetName().Name == "Hifc.V1.Revit"); controllerType = a.GetType("Hifc.Revit.Management.ManagerController", true);
                using (var sha = SHA256.Create()) Write("loaded-assembly.json", new { path = a.Location, identity = a.FullName, version = a.GetName().Version.ToString(), sha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(a.Location))).Replace("-", "").ToLowerInvariant(), hifc_assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(x => x.GetName().Name.StartsWith("Hifc.")).Select(x => new { identity = x.FullName, path = x.Location }).ToArray() });
                phase = "open";
            }
            if (phase == "open")
            {
                // Revit 2020 forbids switching active documents from Idling.
                // ExternalEvent is the supported modeless API entry point.
                if (index == paths.Length) { phase = "finishing"; Schedule(Finish); return; }
                phase = "opening"; Schedule(OpenNext);
            }
            else if (phase == "scan")
            {
                if (!uiFinished) { args.SetRaiseWithoutDelay(); return; } if (uiError != null) throw new Exception(uiError);
                if (!(bool)Get(manager, "Current")) { args.SetRaiseWithoutDelay(); return; }
                checks["external_event_scan_without_preselection"] = ui.ActiveUIDocument.Selection.GetElementIds().Count == 0 && (bool)Get(Get(manager, "Snapshot"), "Complete");
                firstScan = (string)Get(Get(manager, "Snapshot"), "ScanId"); report["first_summary"] = Get(manager, "Summary"); Capture("before");
                reviewedCandidateRoles = Items(Get(Get(manager, "Snapshot"), "Elements")).Where(r => Items(Get(r, "Candidates")).Length == 1 && (bool)Get(Items(Get(r, "Candidates"))[0], "CanPrepare"))
                    .ToDictionary(r => (int)Get(r, "Id"), r => (string)Get(Items(Get(r, "Candidates"))[0], "Slot"));
                var preview = Field(manager, "preview"); report["preview_changes"] = Items(Get(preview, "Changes")).Length;
                checks["automatic_batch_preview"] = preview != null && (string)Get(preview, "ScanId") == firstScan;
                if (Items(Get(preview, "Changes")).Length == 0)
                {
                    // An empty completion preview is valid for a clean inventory.
                    // Exercise the real panel's manual classification path to
                    // produce a reviewed edit, without renaming any model.
                    priorPreview = preview; phase = "manual_preview";
                    var row = Items(Get(Get(manager, "Snapshot"), "Elements")).First(r => doc.GetElement(new ElementId((int)Get(r, "Id"))) is Floor);
                    var kind = Path.GetFileName(doc.PathName).StartsWith("总图") ? "规划净用地" : "建筑区域";
                    OnUi(() => { ((DataGrid)Field(manager, "ElementGrid")).SelectedItem = row; ((ComboBox)Field(manager, "SlotSelector")).SelectedItem = kind; Call(manager, "PreviewClassification"); });
                }
                else { phase = "apply"; OnUi(() => Call(controller, "Apply", preview)); }
            }
            else if (phase == "manual_preview")
            {
                if (!uiFinished) { args.SetRaiseWithoutDelay(); return; } if (uiError != null) throw new Exception(uiError);
                var preview = Field(manager, "preview");
                if (ReferenceEquals(preview, priorPreview)) { if (Field(controller, "pending") == null) throw new InvalidOperationException("Classification preview failed: " + Get(manager, "StatusText")); args.SetRaiseWithoutDelay(); return; }
                checks["manual_classification_from_panel"] = Items(Get(preview, "Changes")).Length > 0;
                phase = "apply"; OnUi(() => Call(controller, "Apply", preview));
            }
            else if (phase == "apply")
            {
                if (!uiFinished) { args.SetRaiseWithoutDelay(); return; } if (uiError != null) throw new Exception(uiError);
                if (!(bool)Get(manager, "Current") || (string)Get(Get(manager, "Snapshot"), "ScanId") == firstScan)
                {
                    if (Field(controller, "pending") == null && Field(controller, "scan") == null) throw new InvalidOperationException("Apply/recheck failed: " + Get(manager, "StatusText"));
                    args.SetRaiseWithoutDelay(); return;
                }
                checks["apply_triggers_full_new_scan"] = (bool)Get(Get(manager, "Snapshot"), "Complete"); report["after_summary"] = Get(manager, "Summary"); Capture("after");
                var afterRows = Items(Get(Get(manager, "Snapshot"), "Elements"));
                checks["reviewed_candidates_written_and_found_on_full_rescan"] = reviewedCandidateRoles.Count > 0 && reviewedCandidateRoles.All(pair => afterRows.Any(r => (int)Get(r, "Id") == pair.Key && (string)Get(r, "Slot") == pair.Value && Items(Get(r, "SuggestedSlots")).Length == 0));
                // A real external document edit must invalidate the actual window.
                using (var tx = new Transaction(doc, "Acceptance manual change")) { tx.Start(); doc.ProjectInformation.BuildingName = "test-only changed building name"; tx.Commit(); }
                checks["document_change_invalidates_window"] = !(bool)Get(manager, "Current") && !(bool)Get(manager, "CanApply");
                phase = "close"; OnUi(() => window.Close());
            }
            else if (phase == "close")
            {
                if (!uiFinished) { args.SetRaiseWithoutDelay(); return; }
                checks["modeless_close_outside_api_safe"] = uiError == null; if (uiError != null) report["close_error"] = uiError;
                if (uiError != null) { try { Call(controller, "Close"); } catch (Exception e) { report["cleanup_error"] = Error(e); } }
                phase = "close_cleanup";
            }
            else if (phase == "close_cleanup")
            {
                if (!(bool)Field(controller, "disposed")) { args.SetRaiseWithoutDelay(); return; }
                checks["closed_controller_disposed_in_api"] = true;
                var map = (IDictionary)controllerType.GetField("Windows", Static).GetValue(null);
                checks["closed_controller_removed"] = !map.Contains(doc);
                controllerType.GetMethod("Open", Static).Invoke(null, new object[] { ui });
                controller = map[doc]; manager = Get(controller, "Window"); window = (Window)manager;
                checks["reopen_after_close"] = window.IsVisible; phase = "reclose";
                OnUi(() => { ((ComboBox)Field(manager, "StageSelector")).SelectedItem = "报规"; Call(manager, "StartFullCheck"); window.Close(); });
            }
            else if (phase == "reclose")
            {
                if (!uiFinished) { args.SetRaiseWithoutDelay(); return; }
                checks["modeless_reclose_safe"] = uiError == null; if (uiError != null) { report["reclose_error"] = uiError; try { Call(controller, "Close"); } catch { } }
                if (!(bool)Field(controller, "disposed")) { args.SetRaiseWithoutDelay(); return; }
                checks["pending_scan_close_disposes_controller"] = true;
                report["allPassed"] = checks.Values.All(v => v); report["scope"] = "Live production WPF/ExternalEvent/Idling on a model copy; no RVT save; snapshots captured from actual window.";
                Write(Path.GetFileNameWithoutExtension(doc.PathName) + "-modeless.json", report); results.Add(report); Log("finished " + doc.Title); index++; deadline = DateTime.UtcNow.AddMinutes(8); phase = "open";
            }
            else if (phase == "exit" && ui.ActiveUIDocument == null && !ui.Application.Documents.Cast<Document>().Any())
            {
                controlled.Idling -= Tick; ui.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit)); return;
            }
            args.SetRaiseWithoutDelay();
        }
        catch (Exception e) { Fail(e); }
    }
    void OpenNext(UIApplication ui)
    {
                var path = paths[index]; Log("open " + path);
                var old = doc; doc = ui.OpenAndActivateDocument(path).Document; if (old != null && old.IsValidObject) old.Close(false);
                checks = new Dictionary<string, bool>(); report = new Dictionary<string, object> { ["document"] = path, ["checks"] = checks };
                Log("native start " + doc.Title); report["native"] = NativeAcceptance.Run(doc, run); checks["native_service_regressions"] = (bool)Get(report["native"], "allPassed"); Log("native end " + doc.Title);
                var a = controllerType.Assembly; var scan = a.GetType("Hifc.Revit.Management.CaseScanJob");
                var job = Activator.CreateInstance(scan, Instance, null, new object[] { doc, "报规", "自动识别" }, null);
                while (!(bool)Get(job, "Complete")) Call(job, "ReadNext", doc, 75);
                var snapshot = Call(job, "Snapshot", doc);
                var rows = Items(Get(snapshot, "Elements"));
                checks["materials_and_assets_excluded"] = !rows.Any(r => { var e = doc.GetElement(new ElementId((int)Get(r, "Id"))); return e is Material || e is PropertySetElement || e is AppearanceAssetElement; });
                checks["sun_settings_and_legend_previews_excluded"] = !rows.Any(r => { var e = doc.GetElement(new ElementId((int)Get(r, "Id"))); return e is SunAndShadowSettings || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PreviewLegendComponents; });
                checks["all_floor_instances_retained"] = new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>().All(f => rows.Any(r => (int)Get(r, "Id") == f.Id.IntegerValue));
                report["inventory_categories"] = rows.GroupBy(r => (string)Get(r, "Category")).Select(g => new { category = g.Key, count = g.Count(), classes = g.Select(r => { var e = doc.GetElement(new ElementId((int)Get(r, "Id"))); return e.GetType().Name + "/" + ((BuiltInCategory)e.Category.Id.IntegerValue); }).Distinct().ToArray() }).ToArray();
                ui.ActiveUIDocument.Selection.SetElementIds(new List<ElementId>());
                controllerType.GetMethod("Open", Static).Invoke(null, new object[] { ui });
                var map = (IDictionary)controllerType.GetField("Windows", Static).GetValue(null); controller = map[doc]; manager = Get(controller, "Window"); window = (Window)manager;
                checks["window_requires_stage"] = Get(manager, "Snapshot") == null && Get(manager, "SelectedStage") == null;
                Call(controller, "Guard", ui); checks["document_wrapper_guard"] = true;
                phase = "scan"; OnUi(() => { ((ComboBox)Field(manager, "StageSelector")).SelectedItem = "报规"; ((ComboBox)Field(manager, "KindSelector")).SelectedItem = Path.GetFileName(path).StartsWith("总图") ? "总图" : "单体建筑"; Call(manager, "StartFullCheck"); });
    }
    void Capture(string suffix)
    {
        window.UpdateLayout(); var root = (FrameworkElement)window.Content;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight)); bmp.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp)); using (var stream = File.Create(Path.Combine(run, Path.GetFileNameWithoutExtension(doc.PathName) + "-" + suffix + ".png"))) encoder.Save(stream);
    }
    void Finish(UIApplication ui)
    {
        // Reopen an unchanged earlier test copy so the last modified copy can
        // close without saving. The active clean copy then closes normally.
        var previous = doc; doc = ui.OpenAndActivateDocument(paths[0]).Document; previous.Close(false);
        if (doc.IsModified) throw new InvalidOperationException("The reopened copy is modified; automatic close refused.");
        phase = "exit";
        ui.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Close));
        Write("host-results.json", new { allPassed = results.Count == paths.Length && results.Cast<Dictionary<string, object>>().All(r => (bool)r["allPassed"]), models = results, note = "All changed test copies closed without saving. No original model has been opened." }); Log("complete");
    }
}
