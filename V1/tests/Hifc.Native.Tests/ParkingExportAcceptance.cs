using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;

// Run through ExternalEvent on a disposable, hash-verified model copy.
// The slabs-only case catches accidental export of an unselected source Area.
public static class ParkingExportAcceptance
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static object Run(Document doc, Assembly plugin, string run, int[] ids, string[] uniqueIds)
    {
        var modelRoot = Path.Combine(Path.GetFullPath(run), "models") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(doc.PathName).StartsWith(modelRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Parking export acceptance requires an isolated model copy.");
        var elements = ids.Select(id => doc.GetElement(new ElementId(id))).ToArray();
        if (elements.Length != uniqueIds.Length || elements.Where((e, i) => e == null || e.UniqueId != uniqueIds[i]).Any())
            throw new InvalidOperationException("Parking export source identity changed.");
        var area = elements.OfType<Area>().Single();
        var initialAreaCount = Areas(doc).Length;
        var initialViewCount = new FilteredElementCollector(doc).OfClass(typeof(View)).GetElementCount();
        var mapping = plugin.GetType("Hifc.Revit.RevitMappingService", true);
        var json = plugin.GetType("Hifc.Revit.Evidence", true).GetMethod("Json", Flags);
        var capture = mapping.GetMethod("Capture", Flags);
        var export = plugin.GetType("Hifc.Revit.NativeExportService", true).GetMethod("Export", Flags);
        var before = (string)json.Invoke(null, new[] { capture.Invoke(null, new object[] { doc, elements }) });
        var cases = new List<object>();
        var sourceExportId = ExportUtils.GetExportId(doc, area.Id).ToString("N");
        foreach (var test in new[] {
            new { Name = "standardized", Elements = elements },
            new { Name = "parking-only", Elements = new Element[] { area } },
            new { Name = "slabs-only", Elements = elements.Where(e => !(e is Area)).ToArray() }
        })
        {
            var output = Path.Combine(run, test.Name + ".ifc");
            object receipt;
            try { receipt = export.Invoke(null, new object[] { doc, test.Elements, output }); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            cases.Add(new { name = test.Name, ifc_path = output, receipt });
        }
        var after = (string)json.Invoke(null, new[] { capture.Invoke(null, new object[] { doc, elements }) });
        var unchanged = before == after && initialAreaCount == Areas(doc).Length
            && initialViewCount == new FilteredElementCollector(doc).OfClass(typeof(View)).GetElementCount();
        if (!unchanged) throw new InvalidOperationException("Acceptance failed to restore source model state.");
        return new { success = true, model_unchanged = unchanged,
            source_area_export_id = sourceExportId, source_area_id = area.Id.IntegerValue,
            source_area_m2 = area.Area * 0.09290304, cases };
    }

    static Area[] Areas(Document doc) => new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Areas)
        .WhereElementIsNotElementType().Cast<Area>().ToArray();
}
