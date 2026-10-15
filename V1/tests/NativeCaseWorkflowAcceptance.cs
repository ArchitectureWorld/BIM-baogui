// Compiled into NativeAcceptance and invoked on Revit Idling on a model COPY.
// Every service mutation is wrapped in an outer rollback-only TransactionGroup.
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var statics = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
var assembly = System.Linq.Enumerable.Single(System.AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "Hifc.V1.Revit");
var run = @"__RUN_DIR__";
if (!System.IO.Path.GetFullPath(document.PathName).StartsWith(System.IO.Path.Combine(run, "models") + System.IO.Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase)) throw new System.InvalidOperationException("This test only mutates validation copies.");
var scanType = assembly.GetType("Hifc.Revit.Management.CaseScanJob");
var edits = assembly.GetType("Hifc.Revit.Management.CaseEdits");
var requestType = assembly.GetType("Hifc.Revit.Management.EditRequest");
System.Func<object,string,object> get = (obj,name) => obj.GetType().GetProperty(name, flags).GetValue(obj,null);
System.Func<object,object[]> items = obj => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Cast<object>((System.Collections.IEnumerable)obj));
System.Func<string,string,object> scan = (stage,kind) => {
    System.IO.File.AppendAllText(System.IO.Path.Combine(run,"native-progress.log"),System.DateTime.Now.ToString("O")+" "+document.Title+" scan "+stage+"/"+kind+"\n");
    var job = System.Activator.CreateInstance(scanType, flags, null, new object[] { document, stage, kind }, null);
    while (!(bool)get(job,"Complete")) scanType.GetMethod("ReadNext",flags).Invoke(job,new object[]{document,75});
    return scanType.GetMethod("Snapshot",flags).Invoke(job,new object[]{document});
};
var checks = new System.Collections.Generic.Dictionary<string, bool>();
var result = new System.Collections.Generic.Dictionary<string,object>();
NativeTestWarnings.Observed.Clear();
result["assembly_version"] = assembly.GetName().Version.ToString(); result["document"] = document.PathName;
var initial = scan("报规","自动识别"); var beforeRows = items(get(initial,"Elements"));
var expectedKind = System.IO.Path.GetFileName(document.PathName).StartsWith("总图") ? "总图" : "单体建筑";
checks["automatic_model_kind"] = (string)get(initial,"ModelKind") == expectedKind;
checks["project_information_in_inventory"] = System.Linq.Enumerable.Any(beforeRows,r=>(string)get(r,"Entity")=="IfcBuilding");
checks["levels_in_inventory"] = System.Linq.Enumerable.Any(beforeRows,r=>(string)get(r,"Entity")=="IfcBuildingStorey");
var expectedRoles = expectedKind == "总图"
    ? new System.Collections.Generic.Dictionary<int,string> { {451481,"道路边界线"}, {451959,"规划净用地"}, {452264,"绿地"}, {454070,"消防回车场"}, {456868,"规划控制线"}, {457029,"道路红线"} }
    : (System.IO.Path.GetFileName(document.PathName).StartsWith("厂房") ? new[]{468302,473137,473574,478866,481696} : new[]{466199,466241,466521}).ToDictionary(id=>id,id=>"建筑区域");
checks["all_known_case_business_carriers_are_reviewable_candidates"] = expectedRoles.All(pair => beforeRows.Any(row => (int)get(row,"Id")==pair.Key && items(get(row,"Candidates")).Any(c => (string)get(c,"Slot")==pair.Value && (bool)get(c,"CanPrepare") && ((string)get(c,"Evidence")).Contains(".ifc"))));
var core = System.AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Hifc.Reference.Core");
var summary = core.GetType("Hifc.Reference.CaseWorkflow").GetMethod("Summarize").Invoke(null,new object[]{"报规",expectedKind,get(initial,"Elements")});
checks["known_roles_are_pending_confirmation_not_missing"] = expectedRoles.Values.Distinct().All(slot => items(get(summary,"Roles")).Any(r=>(string)get(r,"Slot")==slot && (string)get(r,"State")=="待确认分类" && (int)get(r,"Candidates")>0));
var candidatePreview = edits.GetMethod("Preview",statics).Invoke(null,new object[]{document,initial,null}); result["candidate_preview"]=candidatePreview;
checks["candidate_preview_preserves_source_evidence"] = expectedRoles.All(pair=>items(get(candidatePreview,"Changes")).Any(c=>(int)get(c,"Id")==pair.Key && (string)get(c,"Action")=="分类" && (string)get(c,"NewValue")==pair.Value && ((string)get(c,"Detail")).Contains(".ifc")));
if(expectedKind=="总图") {
    checks["parking_mismatch_and_case_anomaly_never_enter_automatic_write"] = !items(get(candidatePreview,"Changes")).Any(c=>new[]{455344,455815}.Contains((int)get(c,"Id"))) && beforeRows.Where(r=>new[]{455344,455815}.Contains((int)get(r,"Id"))).All(r=>items(get(r,"Candidates")).Any() && items(get(r,"Candidates")).All(c=>!(bool)get(c,"CanPrepare")));
}
result["initial"] = initial;
try { scan(null,"自动识别"); checks["stage_required"] = false; } catch { checks["stage_required"] = true; }
var construction = scan("施工图","总图");
checks["construction_no_planning_fields"] = System.Linq.Enumerable.All(items(get(construction,"Elements")),r=>items(get(r,"Fields")).Length==0);
var group = new Autodesk.Revit.DB.TransactionGroup(document,"HIFC 0.3 acceptance - rollback only"); group.Start();
try {
    // Exercise the approved manual type path even when automatic recognition fails.
    var selected = scan("报规",expectedKind);
    var specimen = System.Linq.Enumerable.First(new Autodesk.Revit.DB.FilteredElementCollector(document).OfClass(typeof(Autodesk.Revit.DB.Floor)).Cast<Autodesk.Revit.DB.Floor>());
    var classification = System.Activator.CreateInstance(requestType);
    requestType.GetProperty("Kind").SetValue(classification,"分类",null); requestType.GetProperty("Ids").SetValue(classification,new[]{specimen.Id.IntegerValue},null);
    requestType.GetProperty("Slot").SetValue(classification,expectedKind=="总图"?"规划净用地":"建筑区域",null);
    var classPreview=edits.GetMethod("Preview",statics).Invoke(null,new object[]{document,selected,classification});
    if(items(get(classPreview,"Changes")).Length>0) edits.GetMethod("Apply",statics).Invoke(null,new object[]{document,classPreview});
    checks["manual_classification_written"] = specimen.GetParameters("类目").Count==1 && specimen.GetParameters("类目")[0].AsString()==(expectedKind=="总图"?"规划净用地":"建筑区域");
    selected=scan("报规",expectedKind);
    var preview = edits.GetMethod("Preview",statics).Invoke(null,new object[]{document,selected,null}); result["initial_preview"] = preview;
    var changes = items(get(preview,"Changes"));
    if (changes.Length>0) edits.GetMethod("Apply",statics).Invoke(null,new object[]{document,preview});
    var after = scan("报规",expectedKind); result["after_preparation"] = after;
    var repeat = edits.GetMethod("Preview",statics).Invoke(null,new object[]{document,after,null});
    checks["repeat_preparation_idempotent"] = items(get(repeat,"Changes")).Length == 0;
    var afterRows = items(get(after,"Elements"));
    var fields = System.Linq.Enumerable.SelectMany(afterRows,r=>items(get(r,"Fields")));
    var writable = System.Linq.Enumerable.FirstOrDefault(fields,f=>(bool)get(f,"Editable") && (string)get(f,"Binding")=="实例" && (string)get(f,"RawType")=="Real" && document.GetElement(new Autodesk.Revit.DB.ElementId((int)get(f,"OwnerId"))).GetParameters((string)get(f,"Source"))[0].StorageType==Autodesk.Revit.DB.StorageType.String);
    if (writable==null) throw new System.InvalidOperationException("No writable string-backed Real field for overwrite regression.");
    var target = document.GetElement(new Autodesk.Revit.DB.ElementId((int)get(writable,"OwnerId")));
    var source = (string)get(writable,"Source"); var parameter = target.GetParameters(source)[0];
    var rowForField = System.Linq.Enumerable.First(afterRows,r=>System.Linq.Enumerable.Any(items(get(r,"Fields")),f=>object.ReferenceEquals(f,writable)));
    var request = System.Activator.CreateInstance(requestType);
    requestType.GetProperty("Kind").SetValue(request,"赋值",null); requestType.GetProperty("Ids").SetValue(request,new[]{(int)get(rowForField,"Id")},null);
    requestType.GetProperty("Source").SetValue(request,source,null); requestType.GetProperty("PropertySet").SetValue(request,get(writable,"PropertySet"),null); requestType.GetProperty("Binding").SetValue(request,get(writable,"Binding"),null); requestType.GetProperty("Value").SetValue(request,"123",null);
    var stale = edits.GetMethod("Preview",statics).Invoke(null,new object[]{document,after,request});
    using (var tx = new Autodesk.Revit.DB.Transaction(document,"Simulate manual parameter overwrite")) { tx.Start(); tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new NativeTestWarnings())); parameter.Set("invalid-number"); tx.Commit(); }
    bool rejected = false; try { edits.GetMethod("Apply",statics).Invoke(null,new object[]{document,stale}); } catch { rejected = true; }
    checks["stale_preview_rejected_without_overwriting_manual_value"] = rejected && parameter.AsString()=="invalid-number";
    var overwritten = scan("报规",expectedKind);
    checks["full_recheck_finds_manual_invalid_value"] = System.Linq.Enumerable.Any(items(get(overwritten,"Elements")),r=>System.Linq.Enumerable.Any(items(get(r,"Fields")),f=>(int)get(f,"OwnerId")==target.Id.IntegerValue && (string)get(f,"Source")==source && (string)get(f,"State")=="值非法"));
    using (var tx = new Autodesk.Revit.DB.Transaction(document,"Simulate cleared parameter")) { tx.Start(); tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new NativeTestWarnings())); parameter.Set(""); tx.Commit(); }
    var cleared = scan("报规",expectedKind);
    checks["full_recheck_finds_cleared_value"] = System.Linq.Enumerable.Any(items(get(cleared,"Elements")),r=>System.Linq.Enumerable.Any(items(get(r,"Fields")),f=>(int)get(f,"OwnerId")==target.Id.IntegerValue && (string)get(f,"Source")==source && (string)get(f,"State")=="未填写"));
    var floor = System.Linq.Enumerable.First(new Autodesk.Revit.DB.FilteredElementCollector(document).OfClass(typeof(Autodesk.Revit.DB.Floor)).Cast<Autodesk.Revit.DB.Floor>());
    int previousType = floor.GetTypeId().IntegerValue; int changedType;
    using (var tx = new Autodesk.Revit.DB.Transaction(document,"Simulate type replacement")) { tx.Start(); tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new NativeTestWarnings())); var newType = ((Autodesk.Revit.DB.ElementType)document.GetElement(floor.GetTypeId())).Duplicate("HIFC test " + System.Guid.NewGuid().ToString("N")); changedType=newType.Id.IntegerValue; floor.ChangeTypeId(newType.Id); tx.Commit(); }
    var changed = scan("报规",expectedKind); var changedRow = System.Linq.Enumerable.Single(items(get(changed,"Elements")),r=>(int)get(r,"Id")==floor.Id.IntegerValue);
    checks["full_recheck_reads_replaced_type"] = (int)get(changedRow,"TypeId")==changedType && changedType!=previousType && System.Linq.Enumerable.Any(items(get(changedRow,"Fields")),f=>(string)get(f,"Binding")=="类型" && (int)get(f,"OwnerId")==changedType);
    var oldId = floor.Id.IntegerValue; var oldUnique = floor.UniqueId; int newId;
    using (var tx = new Autodesk.Revit.DB.Transaction(document,"Simulate deletion and reconstruction")) { tx.Start(); tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new NativeTestWarnings())); newId=System.Linq.Enumerable.First(Autodesk.Revit.DB.ElementTransformUtils.CopyElement(document,floor.Id,new Autodesk.Revit.DB.XYZ(200,0,0))).IntegerValue; document.Delete(floor.Id); tx.Commit(); }
    var rebuilt=scan("报规",expectedKind); var rebuiltRows=items(get(rebuilt,"Elements"));
    checks["full_recheck_finds_reconstructed_element"] = !System.Linq.Enumerable.Any(rebuiltRows,r=>(string)get(r,"UniqueId")==oldUnique) && System.Linq.Enumerable.Any(rebuiltRows,r=>(int)get(r,"Id")==newId);
} catch (System.Exception ex) { result["error"]=(ex.InnerException??ex).ToString(); }
finally { group.RollBack(); group.Dispose(); }
result["checks"]=checks; result["allPassed"]=checks.Count>=10 && System.Linq.Enumerable.All(checks.Values,v=>v) && !result.ContainsKey("error");
result["test_transaction_warnings"] = NativeTestWarnings.Observed.ToArray();
result["scope"]="Actual Revit production scan/write services on a copied user model; all test mutations rolled back. Modeless ExternalEvent flow is tested separately.";
var serializer=new System.Web.Script.Serialization.JavaScriptSerializer(); serializer.MaxJsonLength=64*1024*1024;
var reportPath=System.IO.Path.Combine(run,System.IO.Path.GetFileNameWithoutExtension(document.PathName)+"-native.json"); System.IO.File.WriteAllText(reportPath,serializer.Serialize(result),new System.Text.UTF8Encoding(false));
return new {allPassed=result["allPassed"], checks=checks, report=reportPath, error=result.ContainsKey("error")?result["error"]:null};
