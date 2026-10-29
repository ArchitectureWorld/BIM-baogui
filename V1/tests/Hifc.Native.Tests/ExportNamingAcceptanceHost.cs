using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

public sealed class ExportNamingAcceptanceHost : IExternalApplication,IExternalEventHandler
{
    UIControlledApplication app;ExternalEvent api;string run;
    public Result OnStartup(UIControlledApplication application){run=Environment.GetEnvironmentVariable("HIFC_V1_EXPORT_NAMING_TEST_RUN");if(!string.IsNullOrEmpty(run)){app=application;app.Idling+=Start;}return Result.Succeeded;}
    public Result OnShutdown(UIControlledApplication application){if(app!=null)app.Idling-=Start;api?.Dispose();return Result.Succeeded;}
    void Start(object sender,IdlingEventArgs args){app.Idling-=Start;api=ExternalEvent.Create(this);api.Raise();}
    public string GetName()=>"H-IFC 自动命名和导出目录回归";
    void MissingReferences(object sender,DialogBoxShowingEventArgs args){if((args as TaskDialogShowingEventArgs)?.DialogId=="TaskDialog_Unresolved_References")args.OverrideResult(1002);}
    public void Execute(UIApplication ui){ui.DialogBoxShowing+=MissingReferences;try{CustomerProjectAcceptanceHost.RunExportNaming(ui,run);app.Idling+=Exit;}catch(Exception e){while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException;File.WriteAllText(Path.Combine(run,"acceptance-failed.json"),new JavaScriptSerializer().Serialize(new{error=e.ToString()}));}finally{ui.DialogBoxShowing-=MissingReferences;}}
    void Exit(object sender,IdlingEventArgs args){var ui=(UIApplication)sender;var command=RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);if(ui.CanPostCommand(command)){app.Idling-=Exit;ui.PostCommand(command);}else args.SetRaiseWithoutDelay();}
}

public sealed partial class CustomerProjectAcceptanceHost
{
    public static void RunExportNaming(UIApplication ui,string directory)=>new CustomerProjectAcceptanceHost{run=directory}.ExportNamingCore(ui);
    void ExportNamingCore(UIApplication ui)
    {
        var json=new JavaScriptSerializer{MaxJsonLength=256*1024*1024};
        var input=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(run,"input.json")));
        var checks=new Dictionary<string,bool>();var records=new List<object>();
        Action<string,bool> check=(name,ok)=>{checks[name]=ok;Write("checks.json",checks);if(!ok)throw new InvalidOperationException("Acceptance failed: "+name);};
        check("no_user_documents_open",!ui.Application.Documents.Cast<Document>().Any());
        plugin=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Hifc.V1.Revit");core=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(plugin.Location),"Hifc.Reference.Core.dll"));
        var package=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine((string)input["package"],"build-manifest.json")));
        var hashes=Items(package["files"]).Cast<Dictionary<string,object>>().ToDictionary(p=>(string)p["name"],p=>(string)p["sha256"]);
        var expectedVersion=((string)package["version"]).Split('-')[0]+".0";
        check("loaded_assemblies_match_package",new[]{plugin,core}.All(a=>a.GetName().Version.ToString()==expectedVersion&&Hash(a.Location)==hashes[a.GetName().Name+".dll"]));
        Write("loaded-assemblies.json",new[]{plugin,core}.Select(a=>new{name=a.GetName().Name,version=a.GetName().Version.ToString(),path=a.Location,sha256=Hash(a.Location)}).ToArray());
        foreach(var model in Items(input["models"]).Cast<Dictionary<string,object>>())
        {
            var key=(string)model["key"];var source=(string)model["source"];var copy=(string)model["copy"];var expected=(string)model["source_sha256"];
            check(key+"_isolated_copy",Path.GetFullPath(copy).StartsWith(Path.GetFullPath(Path.Combine(run,"models"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&copy!=source);
            check(key+"_source_copy_hash",Hash(source)==expected&&Hash(copy)==expected);
            var doc=ui.Application.OpenDocumentFile(copy);
            try
            {
                var settings=Static("Hifc.Revit.Management.PlatformContext","Settings",doc);Set(settings,"RuleProfileId","hubei-420626-planning-20261020");
                Static("Hifc.Revit.Management.PlatformContext","Set",doc,settings);Static("Hifc.Revit.Management.PlatformContext","SetStage",doc,"报规");
                var detection=Static("Hifc.Revit.ExportDestinationPicker","Detect",doc);Write(key+"-detection.json",detection);
                var kind=(string)model["kind"];check(key+"_model_content_detection_available",detection!=null);
                var date=DateTime.Today;var first=(string)Static("Hifc.Revit.ExportDestinationPicker","DefaultPath",doc,kind,date);
                var expectedName="OUT_"+(string)model["project_name"]+"_报规_"+(kind=="总图"?"总图":(string)model["building_name"])+"_"+date.ToString("yyyyMMdd")+"_V1.ifc";
                check(key+"_default_beside_rvt",Path.GetDirectoryName(first)==Path.GetDirectoryName(copy));
                check(key+"_canonical_filename",Path.GetFileName(first)==expectedName);
                var exports=new List<string>();var receipts=new List<string>();var captured=Static("Hifc.Revit.RevitMappingService","Capture",doc,Carriers(doc));
                Action<string> export=output=>{
                    var result=Static("Hifc.Revit.NativeExportService","ExportProject",doc,output);exports.Add(output);
                    var receiptPath=(string)Get(result,"ReceiptPath");receipts.Add(receiptPath);
                    var receipt=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(receiptPath));
                    check(key+"_receipt_"+exports.Count+"_matches_final_ifc",(string)receipt["ifc_path"]==output&&(string)receipt["ifc_sha256"]==Hash(output));
                    check(key+"_receipt_"+exports.Count+"_stored_internally",Path.GetDirectoryName(receiptPath)!=Path.GetDirectoryName(output));
                    var session=Static("Hifc.Revit.PlatformSync.PlatformSyncRegistry","Session",doc);
                    var completed=Get(session,"Completed");
                    check(key+"_receipt_"+exports.Count+"_registered_for_sync",completed!=null&&(string)Get(completed,"ReceiptPath")==receiptPath);
                    check(key+"_only_ifc_delivered_"+exports.Count,Directory.GetFiles(Path.GetDirectoryName(output)).All(p=>p.EndsWith(".ifc",StringComparison.OrdinalIgnoreCase)||p==copy||p.EndsWith("_V4.receipt.json",StringComparison.Ordinal)));
                };
                Write(key+"-source-parameters.json",captured);
                if((bool)model["export"])
                {
                    var geometry=Json(Geometry(doc,Carriers(doc)));
                    export(first);var firstHash=Hash(first);
                    var second=(string)Static("Hifc.Revit.ExportDestinationPicker","DefaultPath",doc,kind,date);
                    check(key+"_repeat_uses_v2",Path.GetFileName(second)==expectedName.Replace("_V1.ifc","_V2.ifc"));
                    export(second);
                    check(key+"_previous_ifc_preserved",Hash(first)==firstHash);
                    var custom=Path.Combine(run,"custom-output");Directory.CreateDirectory(custom);
                    var sentinel=Path.Combine(custom,expectedName.Replace("_V1.ifc","_V4.receipt.json"));File.WriteAllText(sentinel,"existing output evidence");
                    var customPath=(string)core.GetType("Hifc.Reference.ExportFileNaming").GetMethod("ResolveSelection").Invoke(null,new object[]{Path.Combine(custom,Path.GetFileName(second))});
                    check(key+"_custom_folder_uses_own_version",customPath==Path.Combine(custom,expectedName.Replace("_V1.ifc","_V5.ifc")));
                    export(customPath);
                    check(key+"_custom_history_preserved",File.ReadAllText(sentinel)=="existing output evidence"&&Hash(first)==firstHash);
                    check(key+"_geometry_unchanged",geometry==Json(Geometry(doc,Carriers(doc))));
                }
                check(key+"_source_parameters_unchanged",Json(captured)==Json(Static("Hifc.Revit.RevitMappingService","Capture",doc,Carriers(doc))));
                records.Add(new{key,source,copy,kind,first_path=first,exported_paths=exports.ToArray(),receipt_paths=receipts.ToArray(),detected=detection});
            }
            finally{doc.Close(false);}
            check(key+"_rvt_files_unchanged",Hash(source)==expected&&Hash(copy)==expected);
            Write("models.json",records);
        }
        Write("acceptance-result.json",new{success=true,checks,models=records,source_files_unchanged=true});
    }
}
