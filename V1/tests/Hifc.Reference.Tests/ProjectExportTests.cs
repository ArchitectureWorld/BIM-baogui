using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

internal static class ProjectExportTests
{
    static Dictionary<string, string> Options(int[] localIds = null)
    {
        var type = typeof(ProjectExportTests).Assembly.GetType("Hifc.Revit.NativeExportProfile");
        if (type == null) throw new Exception("Whole-project native export profile is missing");
        try { return (Dictionary<string, string>)type.GetMethod("BuildOptions", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { "model.psets.tsv", localIds }); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static void Run(Action<string, Action> check)
    {
        check("project_export_has_no_selected_id_restriction", () => {
            Assert(!Options().ContainsKey("ElementsForExport"), "Project export still restricts element IDs");
        });
        check("project_export_includes_spatial_objects_without_active_view_geometry", () => {
            var options = Options();
            Assert(options["ExportRoomsInView"] == "true" && options["UseActiveViewGeometry"] == "false", "Area or whole-document geometry is restricted by the active view");
        });
        check("project_export_keeps_native_geometry_and_mapped_psets", () => {
            var options = Options();
            Assert(options["ExportUserDefinedPsets"] == "true" && options["ExportUserDefinedPsetsFileName"] == "model.psets.tsv"
                && options["ExportIFCCommonPropertySets"] == "true" && options["ExportInternalRevitPropertySets"] == "false", "Native geometry and isolated HIFC property routing are not configured together");
        });
        check("project_export_stays_in_current_document", () => {
            Assert(Options()["ExportLinkedFiles"] == "false", "Export unexpectedly traverses linked source documents");
        });
        check("internal_local_acceptance_export_retains_explicit_scope", () => {
            Assert(Options(new[] { 42, 11, 42 })["ElementsForExport"] == "11;42", "Local acceptance scope is no longer explicit");
        });
        check("empty_local_scope_cannot_silently_become_whole_project", () => {
            try { Options(new int[0]); }
            catch (InvalidOperationException) { return; }
            throw new Exception("An empty local export silently became project export");
        });
    }
}
