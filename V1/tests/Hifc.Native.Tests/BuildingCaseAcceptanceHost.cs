using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

// Runs only against verified copies listed by Run-BuildingCaseAcceptance.ps1.
public sealed class BuildingCaseAcceptanceHost : IExternalApplication, IExternalEventHandler
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    UIControlledApplication application; ExternalEvent api; Assembly plugin; string run;
    readonly List<object> outcomes = new List<object>();
    readonly Dictionary<string,bool> checks = new Dictionary<string,bool>();
    void Check(string name,bool ok) { checks[name]=ok;Log(name+" "+ok);if(!ok)throw new Exception(name); }
    static object Get(object value, string name) => value.GetType().GetProperty(name, F).GetValue(value);
    static object[] Items(object value) => ((IEnumerable)value).Cast<object>().ToArray();
    object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, F).Invoke(value, args);
    object Static(string type, string method, params object[] args) => plugin.GetType(type, true).GetMethod(method, F).Invoke(null, args);
    object New(string type, params object[] args) => Activator.CreateInstance(plugin.GetType(type, true), F, null, args, null);
    void Write(string name, object value) => File.WriteAllText(Path.Combine(run, name), new JavaScriptSerializer { MaxJsonLength = 128 * 1024 * 1024 }.Serialize(value));
    static string Hash(string path) { using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    void Log(string text) => File.AppendAllText(Path.Combine(run, "host.log"), DateTime.Now.ToString("O") + " " + text + "\n");
    public Result OnStartup(UIControlledApplication app)
    {
        run = Environment.GetEnvironmentVariable("HIFC_V1_BUILDING_TEST_RUN");
        if (!string.IsNullOrEmpty(run)) { application = app; app.Idling += Start; }
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication app) { if (application != null) { app.Idling -= Start; app.Idling -= Exit; } api?.Dispose(); return Result.Succeeded; }
    public string GetName() => "HIFC case 02 model-copy acceptance";
    void Start(object sender, IdlingEventArgs args) { application.Idling -= Start; api = ExternalEvent.Create(this); api.Raise(); }
    void Exit(object sender, IdlingEventArgs args)
    {
        var ui = (UIApplication)sender; var id = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
        if (ui.CanPostCommand(id)) { application.Idling -= Exit; ui.PostCommand(id); }
    }
    object Scan(Document doc)
    {
        var job = New("Hifc.Revit.Management.CaseScanJob", doc, "报规", "单体建筑");
        while (!(bool)Get(job, "Complete")) Call(job, "ReadNext", doc, 500);
        return Call(job, "Snapshot", doc);
    }
    static string Raw(Element owner,string source)
    {
        var p=owner.GetParameters(source).SingleOrDefault();
        return p==null||!p.HasValue?null:p.StorageType==StorageType.String?p.AsString():
            p.StorageType==StorageType.Integer?p.AsInteger().ToString():p.AsDouble().ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    }
    void ClassifyWindows(Document doc,Element[] windows)
    {
        var read=Static("Hifc.Revit.Management.SelectionService","Read",doc,windows.Select(e=>e.Id.IntegerValue).ToArray(),"报规","单体建筑");
        Check("window_manual_entry_"+windows.Length,Items(Get(read,"Slots")).Contains("窗"));
        var snapshot=Get(read,"Snapshot");
        var preview=Static("Hifc.Revit.Management.SelectionService","Classify",doc,snapshot,"窗");
        Check("window_classification_no_conflicts_"+windows.Length,Items(Get(preview,"Conflicts")).Length==0);
        var draft=New("Hifc.Revit.Management.SelectionDraft",snapshot);
        Call(draft,"SetClassification","窗",Get(preview,"ProposedElements"));
        var typeFields=Items(Get(draft,"Fields"));
        Check("window_type_fields_visible_and_readonly_"+windows.Length,typeFields.Length==2&&typeFields.All(f=>!(bool)Get(f,"Editable")));
        Static("Hifc.Revit.Management.SelectionService","Write",doc,snapshot,Call(draft,"BuildRequests"));
        Check("window_instance_class_written_"+windows.Length,windows.All(w=>Raw(w,"类目")=="窗"));
    }
    void Exercise(Document doc,string name,object initial)
    {
        var all=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(run,"expectations.json")));
        var expected=(Dictionary<string,object>)all[name];
        var windows=Items(expected["windows"]).Cast<Dictionary<string,object>>().Select(x=>{
            var e=doc.GetElement(new ElementId(Convert.ToInt32(x["id"])));
            if(e==null||e.UniqueId!=(string)x["unique_id"]||e.Name!=(string)x["name"])throw new Exception("Reference window identity mismatch");
            return e;
        }).ToArray();
        Check(name+"_case_has_explicit_window_evidence",windows.Length==(name.StartsWith("1")?13:27));
        var typeValues=windows.Select(w=>doc.GetElement(w.GetTypeId())).GroupBy(t=>t.UniqueId).Select(g=>g.First())
            .ToDictionary(t=>t.UniqueId,t=>new[]{Raw(t,"宽度"),Raw(t,"高度")});
        var preview=Static("Hifc.Revit.Management.CaseEdits","Preview",doc,initial,null);
        Write(name+"-preview.json",preview);
        Check(name+"_full_preview_has_no_conflicts",Items(Get(preview,"Conflicts")).Length==0);
        if(Items(Get(preview,"Changes")).Length>0)Static("Hifc.Revit.Management.CaseEdits","Apply",doc,preview);
        // A single window classification must not alter dimensions of shared types.
        var first=windows.First(w=>windows.Count(x=>x.GetTypeId()==w.GetTypeId())>1);
        ClassifyWindows(doc,new[]{first});
        bool blocked=false;
        try { Static("Hifc.Revit.NativeExportService","ExportProject",doc,Path.Combine(run,name+"-partial-must-not-export.ifc")); }
        catch(Exception ex) { blocked=Unwrap(ex).Message.Contains("同类型"); }
        Check(name+"_partial_shared_window_type_export_is_explicitly_blocked",blocked);
        ClassifyWindows(doc,windows);
        Check(name+"_window_type_dimensions_unchanged",typeValues.All(k=>{
            var type=doc.GetElement(k.Key);return k.Value.SequenceEqual(new[]{Raw(type,"宽度"),Raw(type,"高度")});
        }));
        var scan=Scan(doc); Write(name+"-after-write-scan.json",scan);
        Check(name+"_full_recheck_finds_all_confirmed_windows",Items(Get(scan,"Elements")).Count(r=>(string)Get(r,"Slot")=="窗")==windows.Length);
    }
    public void Execute(UIApplication ui)
    {
        Document doc = null;
        try
        {
            var launch = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(run, "launch.json")));
            plugin = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Hifc.V1.Revit");
            Log("loaded " + plugin.GetName().Version);
            Write("loaded-assemblies.json", AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "Hifc.V1.Revit" || a.GetName().Name == "Hifc.Reference.Core")
                .Select(a => new { name = a.GetName().Name, version = a.GetName().Version.ToString(),
                    path = a.Location, sha256 = Hash(a.Location) }).ToArray());
            foreach (var input in Items(launch["inputs"]).Cast<Dictionary<string, object>>())
            {
                var name = (string)input["name"];
                checks.Clear();
                try
                {
                    Log("opening " + name);
                    doc = ui.Application.OpenDocumentFile((string)input["model"]);
                    var scan = Scan(doc); Write(name + "-scan.json", scan); Log(name + " scanned");
                    var carriers = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                        .Where(e => e is Floor || e is Level || e is Area || e is ProjectInfo || e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Windows).ToArray();
                    Write(name + "-sources.json", Static("Hifc.Revit.RevitMappingService", "Capture", doc, carriers));
                    if((string)launch["mode"]=="patched")
                    {
                        Check(name+"_new_plugin_loaded",plugin.GetName().Version==typeof(BuildingCaseAcceptanceHost).Assembly.GetName().Version);
                        Exercise(doc,name,scan);
                        doc.Save();doc.Close(false);doc=ui.Application.OpenDocumentFile((string)input["model"]);
                        var reopened=Scan(doc);Write(name+"-reopened-scan.json",reopened);
                        var classified=Items(Get(reopened,"Elements")).Count(r=>(string)Get(r,"Slot")=="窗");
                        Check(name+"_window_assignment_persists_after_save_reopen",classified==(name.StartsWith("1")?13:27));
                    }
                    string exportError = null;
                    try { Static("Hifc.Revit.NativeExportService", "ExportProject", doc, Path.Combine(run, name + ".ifc")); }
                    catch (Exception ex) { exportError = Unwrap(ex).ToString(); Log(name + " export error: " + exportError); }
                    outcomes.Add(new { name, version = plugin.GetName().Version.ToString(), export_error = exportError,
                        source_unchanged = Hash((string)input["source"]) == (string)input["sha256"],
                        all_passed = exportError == null && checks.Values.All(v => v)
                            && Hash((string)input["source"]) == (string)input["sha256"], checks=checks.ToArray() });
                    Log(name + " complete");
                }
                catch (Exception ex) { outcomes.Add(new { name, error = Unwrap(ex).ToString() }); Log(name + " failed " + Unwrap(ex)); }
                finally { if (doc?.IsValidObject == true) doc.Close(false); doc = null; Write("host-result.json", outcomes); }
            }
        }
        catch (Exception ex) { Write("fatal.json", new { error = Unwrap(ex).ToString() }); }
        finally { if (doc?.IsValidObject == true) doc.Close(false); application.Idling += Exit; }
    }
    static Exception Unwrap(Exception e) { while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException; return e; }
}
