using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

// Regression fixtures live only under the manifest's isolated models directory.
// Direct setters seed the old bug; actual assignments use the production service.
public sealed class ControlLineAssignmentAcceptanceHost : IExternalApplication, IExternalEventHandler
{
    static ControlLineAssignmentAcceptanceHost scheduled;
    UIControlledApplication app; ExternalEvent api; string run;
    public static string Schedule(string directory)
    {
        if(scheduled!=null)throw new InvalidOperationException("This acceptance driver has already been scheduled.");
        scheduled=new ControlLineAssignmentAcceptanceHost {run=directory};
        scheduled.api=ExternalEvent.Create(scheduled);
        return scheduled.api.Raise().ToString();
    }
    public Result OnStartup(UIControlledApplication application) { run=Environment.GetEnvironmentVariable("HIFC_V1_CONTROL_DEFAULT_TEST_RUN");if(!string.IsNullOrEmpty(run)){app=application;app.Idling+=Start;}return Result.Succeeded; }
    public Result OnShutdown(UIControlledApplication application) { if(app!=null)app.Idling-=Start;api?.Dispose();return Result.Succeeded; }
    void Start(object sender,IdlingEventArgs args){app.Idling-=Start;api=ExternalEvent.Create(this);api.Raise();}
    public string GetName()=>"H-IFC 净用地控制红线自动赋值回归";
    void MissingReferences(object sender,DialogBoxShowingEventArgs args)
    {
        if((args as TaskDialogShowingEventArgs)?.DialogId=="TaskDialog_Unresolved_References") {
            File.AppendAllText(Path.Combine(run,"host.log"),"Previously authorized: ignore unavailable references in the test copy.\n");args.OverrideResult(1002);
        }
    }
    public void Execute(UIApplication ui)
    {
        ui.DialogBoxShowing+=MissingReferences;
        try{CustomerProjectAcceptanceHost.RunControlLineAssignment(ui,run);}
        catch(Exception error){while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;File.WriteAllText(Path.Combine(run,"acceptance-failed.json"),new JavaScriptSerializer().Serialize(new{error=error.ToString()}));}
        finally{ui.DialogBoxShowing-=MissingReferences;}
    }
}

public sealed partial class CustomerProjectAcceptanceHost
{
    const string ControlLabel="净用地控制红线";
    static string ControlHash(string path){using(var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    static string ControlRaw(Element e,string name){var p=e.GetParameters(name);if(p.Count!=1)throw new InvalidOperationException("Fixture parameter is not unique: "+name);return p[0].AsString();}
    static Dictionary<string,string> ControlSnapshot(Document doc,int target)
    {
        var serializer=new JavaScriptSerializer {MaxJsonLength=256*1024*1024};
        return new FilteredElementCollector(doc).WhereElementIsNotElementType().Cast<Element>()
            .Concat(new FilteredElementCollector(doc).WhereElementIsElementType())
            .Where(e=>!(e is Material)&&(e.Category?.CategoryType==CategoryType.Model||e is ProjectInfo||e is Level))
            .SelectMany(e=>e.Parameters.Cast<Parameter>().Where(p=>p.Definition!=null&&!(e.Id.IntegerValue==target&&new[]{"类目","规划控制线名称","红线类型"}.Contains(p.Definition.Name)))
                .Select(p=>new {Key=e.UniqueId+"|"+p.Id.IntegerValue,Name=p.Definition.Name,Storage=p.StorageType.ToString(),Value=p.StorageType==StorageType.String?p.AsString():p.StorageType==StorageType.Integer?p.AsInteger().ToString():p.StorageType==StorageType.Double?p.AsDouble().ToString("R",CultureInfo.InvariantCulture):p.StorageType==StorageType.ElementId?p.AsElementId().IntegerValue.ToString():null}))
            // Some Revit parameter collections repeat a built-in parameter.
            // Preserve every occurrence and value instead of assuming unique IDs.
            .GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>serializer.Serialize(g.Select(p=>new{p.Name,p.Storage,p.Value}).OrderBy(p=>p.Name,StringComparer.Ordinal).ThenBy(p=>p.Storage,StringComparer.Ordinal).ThenBy(p=>p.Value,StringComparer.Ordinal).ToArray()));
    }
    static void SeedControlFixture(Document doc,Element target,string name,string type,string slot)
    {
        using(var tx=new Transaction(doc,"隔离回归：构造旧默认值")){tx.Start();
            foreach(var pair in new[]{new[]{"类目",slot},new[]{"规划控制线名称",name},new[]{"红线类型",type}}){var ps=target.GetParameters(pair[0]);if(ps.Count!=1||ps[0].StorageType!=StorageType.String||ps[0].IsReadOnly)throw new InvalidOperationException("Unsafe fixture seed: "+pair[0]);ps[0].Set(pair[1]);}
            tx.Commit();}
    }
    public static void RunControlLineAssignment(UIApplication ui,string directory)=>new CustomerProjectAcceptanceHost {run=directory}.ControlAssignmentCore(ui);
    void ControlAssignmentCore(UIApplication ui)
    {
        var json=new JavaScriptSerializer {MaxJsonLength=256*1024*1024};
        var input=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(run,"input.json")));
        var source=(string)input["source"];var copy=(string)input["copy"];var sourceHash=(string)input["source_sha256"];
        var checks=new Dictionary<string,bool>();
        Action<string,bool> check=(name,ok)=>{checks[name]=ok;Write("checks.json",checks);if(!ok)throw new InvalidOperationException("Acceptance failed: "+name);};
        check("isolated_copy_path",Path.GetFullPath(copy).StartsWith(Path.GetFullPath(Path.Combine(run,"models"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&source!=copy);
        var open=ui.Application.Documents.Cast<Document>().ToArray();
        check("only_pristine_test_copy_open",open.All(d=>string.Equals(Path.GetFullPath(d.PathName),Path.GetFullPath(copy),StringComparison.OrdinalIgnoreCase)&&!d.IsModified));
        check("source_and_copy_match_manifest",ControlHash(source)==sourceHash&&ControlHash(copy)==sourceHash);
        plugin=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Hifc.V1.Revit");core=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(plugin.Location),"Hifc.Reference.Core.dll"));
        var package=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine((string)input["package"],"build-manifest.json")));
        var hashes=Items(package["files"]).Cast<Dictionary<string,object>>().ToDictionary(x=>(string)x["name"],x=>(string)x["sha256"]);
        check("installed_0415_assemblies_match_package",new[]{plugin,core}.All(a=>a.GetName().Version.ToString()=="0.4.15.0"&&ControlHash(a.Location)==hashes[a.GetName().Name+".dll"]));
        Write("loaded-assemblies.json",new[]{plugin,core}.Select(a=>new{name=a.GetName().Name,version=a.GetName().Version.ToString(),path=a.Location,sha256=ControlHash(a.Location)}).ToArray());
        var doc=open.SingleOrDefault()??ui.OpenAndActivateDocument(copy).Document;var target=doc.GetElement((string)input["target_unique_id"]);
        check("expected_model_and_carrier",doc.ProjectInformation.UniqueId==(string)input["project_unique_id"]&&target is Floor&&target.Name=="用地红线");
        planningSettings=Static("Hifc.Revit.Management.PlatformContext","Settings",doc);Set(planningSettings,"RuleProfileId","hubei-420626-planning-20261020");
        Static("Hifc.Revit.Management.PlatformContext","Set",doc,planningSettings);Static("Hifc.Revit.Management.PlatformContext","SetStage",doc,"报规");
        var geometry=Json(Geometry(doc,Carriers(doc)));var before=ControlSnapshot(doc,target.Id.IntegerValue);
        Write("unrelated-parameters-before.json",before);
        foreach(var mode in new[]{"empty","legacy"})
        {
            SeedControlFixture(doc,target,mode=="empty"?"":"用地红线",mode=="empty"?"":"净用地规划控制红线",mode=="empty"?"":"规划控制线");
            var read=Static("Hifc.Revit.Management.SelectionService","Read",doc,new[]{target.Id.IntegerValue},"报规","总图");var snapshot=Get(read,"Snapshot");
            var preview=Static("Hifc.Revit.Management.SelectionService","Classify",doc,snapshot,"规划控制线");Write(mode+"-classification-preview.json",preview);
            check(mode+"_classification_has_no_conflicts",Items(Get(preview,"Conflicts")).Length==0);
            var proposed=Items(Get(preview,"ProposedElements")).Single();var fields=Items(Get(proposed,"Fields")).Where(f=>new[]{"名称","红线类型"}.Contains((string)Get(f,"Property"))).ToArray();
            check(mode+"_both_values_are_automatic",fields.Length==2&&fields.All(f=>(string)Get(f,"SuggestedValue")==ControlLabel));
            var draft=New("Hifc.Revit.Management.SelectionDraft",snapshot);Call(draft,"SetClassification","规划控制线",Get(preview,"ProposedElements"));
            check(mode+"_selection_draft_displays_canonical_values",Items(Get(draft,"Fields")).Where(f=>new[]{"名称","红线类型"}.Contains((string)Get(f,"Property"))).All(f=>(string)Get(f,"Value")==ControlLabel));
            var written=Static("Hifc.Revit.Management.SelectionService","Write",doc,snapshot,Call(draft,"BuildRequests"));Write(mode+"-write.json",written);
            check(mode+"_production_write_has_both_values",ControlRaw(target,"类目")=="规划控制线"&&ControlRaw(target,"规划控制线名称")==ControlLabel&&ControlRaw(target,"红线类型")==ControlLabel);
            check(mode+"_geometry_unchanged",Json(Geometry(doc,Carriers(doc)))==geometry);
            var after=ControlSnapshot(doc,target.Id.IntegerValue);check(mode+"_unselected_and_type_values_preserved",before.All(p=>after.ContainsKey(p.Key)&&after[p.Key]==p.Value));
            var fresh=Get(Static("Hifc.Revit.Management.SelectionService","Read",doc,new[]{target.Id.IntegerValue},"报规","总图"),"Snapshot");
            var again=Static("Hifc.Revit.Management.SelectionService","Classify",doc,fresh,"规划控制线");
            check(mode+"_repeat_classification_is_idempotent",Items(Get(again,"Changes")).Length==0);
        }
        var latest=Get(Static("Hifc.Revit.Management.SelectionService","Read",doc,new[]{target.Id.IntegerValue},"报规","总图"),"Snapshot");
        var good=Static("Hifc.Revit.Management.SelectionService","Classify",doc,latest,"规划控制线");
        var invalid=New("Hifc.Revit.Management.SelectionDraft",latest);Call(invalid,"SetClassification","规划控制线",Get(good,"ProposedElements"));
        Set(Items(Get(invalid,"Fields")).Single(f=>(string)Get(f,"Property")=="红线类型"),"Value","");
        bool rejected=false;try{Static("Hifc.Revit.Management.SelectionService","Write",doc,latest,Call(invalid,"BuildRequests"));}catch(Exception e){var error=Unwrap(e);Write("rejected-clear.json",new{error=error.Message});rejected=error is InvalidOperationException&&error.Message.Contains(ControlLabel);}
        check("manual_clear_rejected_without_mutation",rejected&&ControlRaw(target,"红线类型")==ControlLabel);
        Write("full-scan-after-write.json",Scan(doc,"总图"));doc.Save();
        var savedHash=ControlHash(copy);var reopenedPath=Path.Combine(run,"models","保存重开核验.rvt");File.Copy(copy,reopenedPath);
        var reopened=ui.Application.OpenDocumentFile(reopenedPath);
        try{var e=reopened.GetElement(target.UniqueId);check("both_values_survive_save_reopen",ControlRaw(e,"规划控制线名称")==ControlLabel&&ControlRaw(e,"红线类型")==ControlLabel);}
        finally{reopened.Close(false);}
        var ifc=Path.Combine(run,"总图-规则回归.ifc");var captured=Static("Hifc.Revit.RevitMappingService","Capture",doc,Carriers(doc));
        Write("source-parameters.json",captured);Write("geometry-expectations.json",Geometry(doc,Carriers(doc)));
        Static("Hifc.Revit.NativeExportService","ExportProject",doc,ifc);
        check("export_preserves_saved_model",savedHash==ControlHash(copy)&&Json(captured)==Json(Static("Hifc.Revit.RevitMappingService","Capture",doc,Carriers(doc)))&&geometry==Json(Geometry(doc,Carriers(doc))));
        check("user_original_unchanged",sourceHash==ControlHash(source));
        Write("acceptance-result.json",new{success=true,checks,source,source_sha256=sourceHash,copy,copy_sha256=savedHash,ifc,ifc_sha256=ControlHash(ifc),target_id=target.Id.IntegerValue,target_unique_id=target.UniqueId,save_reopen_verified=true,source_unchanged=true});
    }
}
