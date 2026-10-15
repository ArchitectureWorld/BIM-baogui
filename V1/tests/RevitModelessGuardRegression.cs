// Run through the connected Revit send_code_to_revit tool after opening the manager.
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var assembly = System.Linq.Enumerable.Single(System.AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "Hifc.V1.Revit");
var type = assembly.GetType("Hifc.Revit.Management.ManagerController");
var map = (System.Collections.IDictionary)type.GetField("Windows",System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
var ui = new Autodesk.Revit.UI.UIApplication(document.Application);
var observations = new System.Collections.Generic.List<object>();
bool passed = true;
foreach (System.Collections.DictionaryEntry entry in map) {
    var controller = entry.Value;
    var bound = (Autodesk.Revit.DB.Document)type.GetField("document",flags).GetValue(controller);
    if (!bound.Equals(ui.ActiveUIDocument.Document)) continue;
    bool accepted = false;
    string error = null;
    try { type.GetMethod("Guard",flags).Invoke(controller,new object[]{ui}); accepted = true; }
    catch(System.Exception ex) { error = (ex.InnerException ?? ex).ToString(); }
    observations.Add(new { document = bound.Title, same_native_document = bound.Equals(ui.ActiveUIDocument.Document),
        same_managed_reference = System.Object.ReferenceEquals(bound,ui.ActiveUIDocument.Document),
        guard_accepted = accepted, error = error });
    passed = passed && accepted;
}
return new { test = "same_native_document_across_wrappers_passes_manager_guard", allPassed = passed && observations.Count > 0,
    assembly_version = assembly.GetName().Version.ToString(), observations = observations,
    scope = "Actual Revit API and production Guard; no model writes" };
