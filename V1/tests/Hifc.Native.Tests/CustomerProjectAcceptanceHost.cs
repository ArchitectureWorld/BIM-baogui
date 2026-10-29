using System;
using System.Collections;
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

// Runs the installed application workflow on fresh model copies. No synthetic
// geometry, copied case values or invented business classifications are added.
public sealed partial class CustomerProjectAcceptanceHost : IExternalApplication, IExternalEventHandler
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    UIControlledApplication application; ExternalEvent api; Assembly plugin, core; string run;
    object planningSettings,undergroundSettings;
    bool planningValidation;
    bool inventoryOnly,resumeOnInput;
    string ruleProfileId = "";
    readonly List<object> results = new List<object>();
    static object Get(object value, string name) => value.GetType().GetProperty(name, F).GetValue(value);
    static object[] Items(object value) => ((IEnumerable)value).Cast<object>().ToArray();
    object Static(string type, string method, params object[] args) => plugin.GetType(type, true).GetMethod(method, F).Invoke(null, args);
    object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, F).Invoke(target, args);
    object New(string type, params object[] args) => Activator.CreateInstance(plugin.GetType(type, true), F, null, args, null);
    static string Json(object value) => new JavaScriptSerializer { MaxJsonLength = 256 * 1024 * 1024 }.Serialize(value);
    void Write(string name, object value) => File.WriteAllText(Path.Combine(run, name), Json(value));
    void Log(string value) => File.AppendAllText(Path.Combine(run, "host.log"), DateTime.Now.ToString("O") + " " + value + "\n");
    static string Hash(string path)
    {
        using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    static Exception Unwrap(Exception ex) { while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException; return ex; }
    public string GetName() => "HIFC current customer project acceptance";
    public Result OnStartup(UIControlledApplication app)
    {
        run = Environment.GetEnvironmentVariable("HIFC_V1_CUSTOMER_TEST_RUN");
        if (!string.IsNullOrEmpty(run)) { application = app; app.Idling += Start; }
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication app)
    {
        if (application != null) { app.Idling -= Start; app.Idling -= Exit; app.Idling -= ResumeWorkbook; }
        api?.Dispose(); return Result.Succeeded;
    }
    void Start(object sender, IdlingEventArgs args) { application.Idling -= Start; api = ExternalEvent.Create(this); api.Raise(); }
    void Exit(object sender, IdlingEventArgs args)
    {
        var ui = (UIApplication)sender; var id = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
        if (ui.CanPostCommand(id)) { application.Idling -= Exit; ui.PostCommand(id); }
    }
    void ResumeWorkbook(object sender,IdlingEventArgs args)
    {
        var continuation=Path.Combine(run,"continue-workbook.json");
        if(!File.Exists(continuation))return;
        // The controller writes a complete file via atomic rename, after the
        // read-only inventory and project-specific inputs have been reviewed.
        var text=File.ReadAllText(continuation);
        var next=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text);
        if(next.ContainsKey("inventory_only")&&Convert.ToBoolean(next["inventory_only"]))return;
        if(!next.ContainsKey("workbook_input_manifest"))return;
        File.WriteAllText(Path.Combine(run,"launch.json"),text);
        File.Move(continuation,Path.Combine(run,"resumed-workbook-launch.json"));
        application.Idling-=ResumeWorkbook;api.Raise();
    }
    object Scan(Document doc, string requestedKind)
    {
        if(planningSettings==null) {
            var stored=Call(Store(doc),"Load");
            planningSettings=Get(stored,"Planning");undergroundSettings=Get(stored,"Underground");
        }
        var job=New("Hifc.Revit.Management.CaseScanJob", doc, "报规", requestedKind,undergroundSettings,planningSettings);
        while (!(bool)Get(job, "Complete")) Call(job, "ReadNext", doc, 500);
        return Call(job, "Snapshot", doc);
    }
    object Store(Document doc) => New("Hifc.Revit.Management.ManagementStateStore",
        Path.Combine((string)Static("Hifc.Revit.RuntimePaths","OutputDirectory"),"management-profiles"),
        Static("Hifc.Revit.Management.ModelReader","Key",doc));
    object ContextEvidence(Document doc)
    {
        var stored=Call(Store(doc),"Load");
        return new {
            document_key=Static("Hifc.Revit.Management.ModelReader","Key",doc),document_path=doc.PathName,
            effective_profile_id=Static("Hifc.Revit.Management.PlatformContext","ProfileId",doc),
            configured_profile_id=Get(Static("Hifc.Revit.Management.PlatformContext","Settings",doc),"RuleProfileId"),
            stage=Static("Hifc.Revit.Management.PlatformContext","Stage",doc),
            persisted_stage=Get(stored,"Stage"),persisted_kind=Get(stored,"Kind"),
            persisted_planning=Get(stored,"Planning"),persisted_underground=Get(stored,"Underground")
        };
    }
    string WriteFingerprintEvidence(Document doc,object changes,string filename)
    {
        var fingerprint=(string)Static("Hifc.Revit.Management.CaseEdits","Fingerprint",doc,changes);
        var entries=Items(changes).Select((change,index)=> {
            var owner=doc.GetElement(new ElementId((int)Get(change,"Id")));
            if(owner==null||owner.UniqueId!=(string)Get(change,"OwnerUniqueId"))throw new InvalidOperationException("Fingerprint evidence owner changed.");
            var source=(string)Get(change,"Source");
            return new {index,owner_id=owner.Id.IntegerValue,action=Get(change,"Action"),binding=Get(change,"Binding"),expected_raw=Get(change,"ExpectedRaw"),
                fingerprint_input=new {owner.UniqueId,owner.Name,type=owner.GetTypeId().IntegerValue,Source=source,
                    reads=Items(Static("Hifc.Revit.RevitMappingService","ReadOwner",owner,source,owner is ElementType?"type":"instance"))}};
        }).ToArray();
        Write(filename,new{fingerprint,context=ContextEvidence(doc),entries});
        return fingerprint;
    }
    static void Set(object target,string name,object value)=>target.GetType().GetProperty(name,F).SetValue(target,value);
    object CoreNew(string type)=>Activator.CreateInstance(core.GetType("Hifc.Reference."+type,true));
    void ConfirmTestScope(Document doc,object scan,string kind)
    {
        // Explicit test inputs exercise the user confirmation workflow. They are
        // recorded separately and do not claim approval of planning use/coefficient.
        planningSettings=CoreNew("PlanningSettings");
        Set(planningSettings,"RuleProfileId",ruleProfileId);
        var method=core.GetType("Hifc.Reference.PlanningInputs").GetMethod("Identity");
        var selected=Items(Get(scan,"Elements")).Where(row=>Items(Get(row,"Candidates")).Any(c=>
            (string)Get(c,"Slot")==(kind=="单体建筑"?"建筑区域":"规划净用地"))).ToArray();
        if(kind=="总图")selected=selected.OrderByDescending(row=>doc.GetElement(new ElementId((int)Get(row,"Id"))).get_BoundingBox(null)?.Max.Z).Take(1).ToArray();
        var choices=Array.CreateInstance(core.GetType("Hifc.Reference.PlanningRoleChoice"),selected.Length);
        for(int i=0;i<selected.Length;i++) {
            var choice=CoreNew("PlanningRoleChoice");Set(choice,"UniqueId",Get(selected[i],"UniqueId"));Set(choice,"Slot",kind=="总图"?"规划净用地":"建筑区域");
            Set(choice,"Identity",method.Invoke(null,new[]{selected[i]}));choices.SetValue(choice,i);
        }
        Set(planningSettings,"Roles",choices);
        // No unknown use classifications or coefficients are populated in customer copies.
        undergroundSettings=CoreNew("UndergroundSettings");
        var levels=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToArray();
        var ground=levels.SingleOrDefault(l=>l.Name=="室外地坪");var first=levels.SingleOrDefault(l=>l.Name=="F1");
        if(kind=="单体建筑"&&ground!=null&&first!=null) {
            Set(undergroundSettings,"Confirmed",true);Set(undergroundSettings,"GroundElevationM",(double?)(ground.ProjectElevation*.3048));
            Set(undergroundSettings,"BaseLevelUniqueId",first.UniqueId);
        }
        // Follow the manager's persisted confirmation flow. A reopened document
        // must restore this context before any scan can reapply host-local settings.
        var store=Store(doc);var state=Call(store,"Load");
        Set(state,"Stage","报规");Set(state,"Kind",kind);Set(state,"Planning",planningSettings);Set(state,"Underground",undergroundSettings);
        Call(store,"Save",state);
        var saved=Call(store,"Load");
        if((string)Get(saved,"Stage")!="报规"||(string)Get(saved,"Kind")!=kind
            ||Json(Get(saved,"Planning"))!=Json(planningSettings)||Json(Get(saved,"Underground"))!=Json(undergroundSettings))
            throw new InvalidOperationException("Confirmed test context did not persist exactly.");
        Static("Hifc.Revit.Management.PlatformContext","Set",doc,planningSettings);
        Static("Hifc.Revit.Management.PlatformContext","SetStage",doc,"报规");
        Write(doc.Title+"-test-input-confirmation.json",new{scope="MODEL_COPY_TEST_ASSUMPTIONS_REQUIRE_BUSINESS_CONFIRMATION",
            note="建筑楼板按候选确认；重合净用地仅取上层载体进行写入验证。未填虚构用途或系数；不代表平台报规范围已确认。",
            planning=planningSettings,underground=undergroundSettings,selected_ids=selected.Select(row=>Get(row,"Id")).ToArray(),persisted_context=ContextEvidence(doc)});
    }
    object Summary(object scan) => core.GetType("Hifc.Reference.CaseWorkflow").GetMethods().Single(m => m.Name == "Summarize" && m.GetParameters().Length == 4)
        .Invoke(null, new[] { Get(scan, "Stage"), Get(scan, "ModelKind"), Get(scan, "Elements"), Get(Get(scan, "Planning"), "RuleProfileId") });
    object FloorFaces(Element element)
    {
        if (!(element is Floor floor)) return null;
        return HostObjectUtils.GetTopFaces(floor).Select(reference =>
        {
            var face = element.GetGeometryObjectFromReference(reference) as Face;
            var planar = face as PlanarFace;
            return new {
                face_type=face?.GetType().Name,
                surface_area_m2=face?.Area*.3048*.3048,
                projected_area_m2=planar==null?(double?)null:planar.Area*Math.Abs(planar.FaceNormal.Z)*.3048*.3048,
                normal_z=planar?.FaceNormal.Z,
                loops=face?.GetEdgesAsCurveLoops().Select(loop=>loop.Select(curve=>new {
                    curve_type=curve.GetType().Name,length_m=curve.Length*.3048,
                    points=curve.Tessellate().Select(p=>new[]{p.X*.3048,p.Y*.3048,p.Z*.3048}).ToArray()
                }).ToArray()).ToArray()
            };
        }).ToArray();
    }
    static Element[] Carriers(Document doc) => new FilteredElementCollector(doc).WhereElementIsNotElementType()
        .Where(e => e is Floor || e is Area || e is Wall || e is ProjectInfo || e is Level ||
            e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Windows).ToArray();
    object Geometry(Document doc, Element[] carriers) => carriers.Where(e => e is Floor || e is Area).Select(e => new {
        id=e.Id.IntegerValue, unique_id=e.UniqueId, name=e.Name,
        revit_class=e.GetType().Name, level=doc.GetElement(e.LevelId)?.Name,
        area_scheme=(e as Area)?.AreaScheme?.Name, has_location=e.Location!=null,
        native_area_m2=e is Area a? (double?)(a.Area*.3048*.3048):
            e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble()*.3048*.3048,
        projected_area_m2=Static("Hifc.Revit.Management.ModelGeometry","ProjectedAreaM2",e),
        top_faces=FloorFaces(e),
        entity=Static("Hifc.Revit.RevitMappingService","Entity",doc,e)
    }).ToArray();
    public void Execute(UIApplication ui)
    {
        Document doc = null;
        try
        {
            results.Clear();
            var launch = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(run,"launch.json")));
            inventoryOnly=launch.ContainsKey("inventory_only")&&Convert.ToBoolean(launch["inventory_only"]);
            resumeOnInput=launch.ContainsKey("resume_on_input")&&Convert.ToBoolean(launch["resume_on_input"]);
            planningValidation=launch.ContainsKey("planning_validation")&&Convert.ToBoolean(launch["planning_validation"]);
            ruleProfileId=launch.ContainsKey("RuleProfileId")?Convert.ToString(launch["RuleProfileId"]):"";
            plugin = AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Hifc.V1.Revit");
            core = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(plugin.Location),"Hifc.Reference.Core.dll"));
            LoadWorkbookInputs(launch);
            Write("loaded-assemblies.json",new[]{plugin,core}.Select(a=>new{name=a.GetName().Name,
                version=a.GetName().Version.ToString(),path=a.Location,sha256=Hash(a.Location)}).ToArray());
            foreach(var input in Items(launch["inputs"]).Cast<Dictionary<string,object>>())
            {
                var name=(string)input["name"]; var copy=(string)input["model"]; var source=(string)input["source"];
                var requestedKind=input.ContainsKey("requested_kind")?(string)input["requested_kind"]:"自动识别";
                var result=new Dictionary<string,object>{{"name",name},{"version",plugin.GetName().Version.ToString()},
                    {"phase","报规"},{"synthetic_objects_added",false},{"source_path",source},
                    {"requested_kind",requestedKind}};
                try
                {
                    planningSettings=null;undergroundSettings=null;
                    if(!Path.GetFullPath(copy).StartsWith(Path.GetFullPath(Path.Combine(run,"models"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
                        ||string.Equals(Path.GetFullPath(copy),Path.GetFullPath(source),StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Only a fresh copy beneath this run's models directory may be opened.");
                    if(Hash(copy)!=(string)input["sha256"])throw new Exception("Model copy does not match the frozen input");
                    Log("opening "+name);
                    doc=ui.Application.OpenDocumentFile(copy);
                    if(!string.Equals(Path.GetFullPath(doc.PathName),Path.GetFullPath(copy),StringComparison.OrdinalIgnoreCase))
                        throw new Exception("Opened document is not the requested model copy");
                    var automatic=Scan(doc,"自动识别");
                    result["automatic_detected_kind"]=Get(automatic,"ModelKind");
                    if(requestedKind!="自动识别")Write(name+"-automatic-scan.json",automatic);
                    var scan=requestedKind=="自动识别"?automatic:Scan(doc,requestedKind);
                    Write(name+"-before-scan.json",scan);
                    Write(name+"-before-summary.json",Summary(scan));
                    var kind=(string)Get(scan,"ModelKind");result["detected_kind"]=kind;
                    var workbookModel=WorkbookModel(input);
                    if(workbookModel!=null) {
                        ConfirmWorkbookScope(doc,scan,kind,workbookModel);scan=Scan(doc,requestedKind);
                        Write(name+"-confirmed-scan.json",scan);
                        result["business_confirmation_scope"]="USER_CONFIRMED_AND_WORKBOOK_SOURCED_INPUTS";
                    }
                    else if(planningValidation&&(kind=="总图"||kind=="单体建筑")) {
                        ConfirmTestScope(doc,scan,kind);scan=Scan(doc,requestedKind);
                        Write(name+"-confirmed-scan.json",scan);
                        result["business_confirmation_scope"]="CONTROLLED_COPY_INPUTS_NOT_PLATFORM_APPROVAL";
                    }
                    result["detection_evidence"]=Get(scan,"DetectionEvidence");
                    result["scan_complete"]=Get(scan,"Complete");
                    var carriers=Carriers(doc);
                    Write(name+"-source-parameters.json",Static("Hifc.Revit.RevitMappingService","Capture",doc,carriers));
                    Write(name+"-geometry-expectations.json",Geometry(doc,carriers));
                    Write(name+"-native-levels.json",carriers.OfType<Level>().Select(l=>new{
                        id=l.Id.IntegerValue,unique_id=l.UniqueId,name=l.Name,
                        elevation_m=l.ProjectElevation*.3048,building_story=l.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY)?.AsInteger()
                    }).ToArray());
                    Log(name+" detected "+kind+"; scan records "+Items(Get(scan,"Elements")).Length);
                    if(inventoryOnly) {
                        result["write_state"]="READ_ONLY_INVENTORY";
                        try {
                            Static("Hifc.Revit.NativeExportService","ExportProject",doc,Path.Combine(run,name+".ifc"));
                            result["export_completed"]=true;
                        } catch(Exception ex) {result["export_completed"]=false;result["export_error"]=Unwrap(ex).ToString();}
                        result["workflow_execution_completed"]=true;
                        Log(name+" read-only inventory complete");
                        continue;
                    }
                    object changes=null;string writtenFingerprint=null;
                    if(kind=="总图"||kind=="单体建筑")
                    {
                        var requests=WorkbookRequests(doc,scan,workbookModel);
                        if(workbookModel!=null)Write(name+"-workbook-requests.json",requests);
                        var preview=Static("Hifc.Revit.Management.CaseEdits","PreviewBatch",doc,scan,requests);
                        Write(name+"-preview.json",preview);
                        var conflicts=Items(Get(preview,"Conflicts"));changes=Get(preview,"Changes");
                        result["automatic_changes"]=Items(changes).Length;result["preview_conflicts"]=conflicts;
                        if(conflicts.Length>0&&!planningValidation)result["write_state"]="BLOCKED_BY_PREVIEW_CONFLICTS";
                        else if(Items(changes).Length==0)result["write_state"]="NO_AUTOMATIC_CHANGES";
                        else
                        {
                            Static("Hifc.Revit.Management.CaseEdits","Apply",doc,preview);
                            writtenFingerprint=WriteFingerprintEvidence(doc,changes,name+"-fingerprint-written.json");
                            result["write_state"]="APPLIED_AND_READ_BACK";
                            if(planningValidation) {
                                var rescanned=Scan(doc,requestedKind);var remaining=Static("Hifc.Revit.Management.CaseEdits","PreviewBatch",doc,rescanned,Array.CreateInstance(plugin.GetType("Hifc.Revit.Management.EditRequest",true),0));
                                result["repeat_automatic_changes"]=Items(Get(remaining,"Changes")).Length;
                                Write(name+"-repeat-preview.json",remaining);
                            }
                        }
                    }
                    else result["write_state"]="MODEL_KIND_REQUIRES_HUMAN_CONFIRMATION";
                    if(workbookModel!=null)WriteWorkbookReferences(doc,workbookModel);
                    Log(name+" write "+result["write_state"]);
                    Write(name+"-after-scan.json",Scan(doc,requestedKind));
                    var beforeContext=ContextEvidence(doc);
                    Write(name+"-context-before-save.json",beforeContext);
                    result["profile_before_save"]=Get(beforeContext,"effective_profile_id");
                    result["stage_before_save"]=Get(beforeContext,"stage");
                    doc.Save();doc.Close(false);planningSettings=null;undergroundSettings=null;
                    doc=ui.Application.OpenDocumentFile(copy);
                    result["save_reopen_completed"]=true;
                    // Read persisted context before Scan or any explicit Set call.
                    var reopenedContext=ContextEvidence(doc);
                    Write(name+"-context-reopened-before-scan.json",reopenedContext);
                    result["profile_after_reopen_before_scan"]=Get(reopenedContext,"effective_profile_id");
                    result["stage_after_reopen_before_scan"]=Get(reopenedContext,"stage");
                    result["profile_context_persisted"]=Json(Get(beforeContext,"effective_profile_id"))==Json(Get(reopenedContext,"effective_profile_id"))
                        &&Json(Get(beforeContext,"stage"))==Json(Get(reopenedContext,"stage"));
                    if(writtenFingerprint!=null)
                        result["written_values_persisted"]=writtenFingerprint==WriteFingerprintEvidence(doc,changes,name+"-fingerprint-reopened.json");
                    var reopened=Scan(doc,requestedKind);Write(name+"-reopened-scan.json",reopened);
                    if(workbookModel!=null) {
                        VerifyWorkbookInputs(doc,reopened,workbookModel);
                        result["workbook_values_and_references_reopened"]=true;
                    }
                    Write(name+"-reopened-summary.json",Summary(reopened));
                    var scope=Static("Hifc.Revit.ProjectExportScope","Read",doc);
                    Write(name+"-export-scope.json",new{model_records=Items(Get(scope,"Elements")).Length,
                        mapped_records=Items(Get(scope,"MappedElements")).Length,issues=Get(scope,"Issues")});
                    try
                    {
                        Static("Hifc.Revit.NativeExportService","ExportProject",doc,Path.Combine(run,name+".ifc"));
                        result["export_completed"]=true;
                    }
                    catch(Exception ex){result["export_completed"]=false;result["export_error"]=Unwrap(ex).ToString();}
                    Write(name+"-source-parameters-after.json",Static("Hifc.Revit.RevitMappingService","Capture",doc,Carriers(doc)));
                    result["workflow_execution_completed"]=true;
                    Log(name+" export "+result["export_completed"]);
                }
                catch(Exception ex){result["workflow_execution_completed"]=false;result["error"]=Unwrap(ex).ToString();Log(name+" error "+Unwrap(ex));}
                finally
                {
                    if(doc?.IsValidObject==true)doc.Close(false);doc=null;
                    result["source_unchanged"]=Hash(source)==(string)input["sha256"];
                    if(workbookInputs!=null)result["workbook_source_unchanged"]=Hash(workbookInputs.WorkbookPath)==workbookInputs.WorkbookSha256;
                    if(inventoryOnly)result["inventory_copy_unchanged"]=Hash(copy)==(string)input["sha256"];
                    results.Add(result);Write(inventoryOnly?"inventory-result.json":"host-result.json",results);
                }
            }
        }
        catch(Exception ex){Write("fatal.json",new{error=Unwrap(ex).ToString()});}
        finally{if(doc?.IsValidObject==true)doc.Close(false);if(inventoryOnly&&resumeOnInput)application.Idling+=ResumeWorkbook;else application.Idling+=Exit;}
    }
}
