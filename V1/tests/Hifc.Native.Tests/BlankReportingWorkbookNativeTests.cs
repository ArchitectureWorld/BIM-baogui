using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

// Run on an already scanned model copy. This acceptance method creates Excel
// files and write previews without opening a transaction or changing the model.
public static class BlankReportingWorkbookNativeTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    public static object Run(Document document, Assembly plugin, object snapshot, string outputDirectory, string auxiliaryLevelUniqueId)
    {
        var rows = (ModelElementCheck[])Get(snapshot, "Elements");
        var auxiliary = rows.Single(r => r.UniqueId == auxiliaryLevelUniqueId);
        Assert(auxiliary.Entity == "IfcBuildingStorey" && !auxiliary.Fields.Any(f => f.Property == "楼层面积"), "Choose a measured auxiliary level omitted by the normal scan");
        var level = (Level)document.GetElement(auxiliaryLevelUniqueId);
        var oldParameters = level.Parameters.Cast<Parameter>().Select(p => p.Id.IntegerValue + "|" + p.AsValueString() + "|" + (p.StorageType == StorageType.String ? p.AsString() : "")).OrderBy(s => s).ToArray();
        var wasModified = document.IsModified;
        var reader = plugin.GetType("Hifc.Revit.Management.CaseModelReader", true);
        var prepare = reader.GetMethod("ReadWorkbookLevelFields", Flags);
        Assert(prepare != null, "Workbook-specific auxiliary level reader is missing");
        var added = (int)prepare.Invoke(null, new[] { (object)document, snapshot });
        var field = auxiliary.Fields.Single(f => f.PropertySet == FloorAreaPolicy.FloorSet && f.Property == "楼层面积");
        Assert(added > 0 && field.OwnerUniqueId == auxiliaryLevelUniqueId && !string.IsNullOrEmpty(field.SourceSignature), "Auxiliary field lost its current source owner or baseline");

        var structure = Call(plugin, "ReportingWorkbookModelReader", "Read", document);
        var levels = (string[])Get(structure, "LevelNames");
        Assert(levels.Contains(auxiliary.Name), "Workbook structure collapsed the auxiliary level name");
        Directory.CreateDirectory(outputDirectory);
        var blankPath = Path.Combine(outputDirectory, "native-blank-" + Guid.NewGuid().ToString("N") + ".xlsx");
        ReportingWorkbookTemplate.WriteForModel(blankPath, "单体建筑", (string)Get(structure, "BuildingName"), levels);
        var blank = ReportingWorkbookReader.Read(blankPath).Tables.Single();
        Assert(blank.Items.All(i => i.Value == ""), "A native model business value entered the blank workbook");

        var filledPath = Path.Combine(outputDirectory, "native-filled-" + Guid.NewGuid().ToString("N") + ".xlsx");
        ReportingWorkbookTemplate.WriteForModel(filledPath, "单体建筑", blank.BuildingName, levels);
        var value = field.MetricValue.HasValue && Math.Abs(field.MetricValue.Value - 123.45) < .000001 ? "123.46" : "123.45";
        FillFloor(filledPath, auxiliary.Name, value);
        var plan = ReportingWorkbookPlanner.Build(ReportingWorkbookReader.Read(filledPath).Tables.Single(), rows, "单体建筑", document.ProjectInformation.UniqueId);
        var change = plan.Changes.Single(c => c.ElementUniqueId == auxiliaryLevelUniqueId && c.Property == "楼层面积");
        Assert(change.CanSelect && change.Value == value, "Filled auxiliary level did not resolve to an importable native field");
        change.Selected = true;
        var decision = ReportingWorkbookPlanner.Decide(plan, false);
        Assert(decision.Changes.Length == 1, "Blank fields proposed additional imported values");

        var requestType = plugin.GetType("Hifc.Revit.Management.EditRequest", true);
        var request = Activator.CreateInstance(requestType, true);
        Set(request, "Kind", "赋值"); Set(request, "Ids", new[] { change.ElementId });
        Set(request, "PropertySet", change.PropertySet); Set(request, "Source", change.Source); Set(request, "Binding", change.Binding);
        Set(request, "Value", change.Value); Set(request, "ValueUnit", change.ValueUnit);
        var baselineType = plugin.GetType("Hifc.Revit.Management.EditBaseline", true);
        var baseline = Activator.CreateInstance(baselineType, true);
        Set(baseline, "ElementId", change.ElementId); Set(baseline, "ElementUniqueId", change.ElementUniqueId); Set(baseline, "OwnerUniqueId", change.OwnerUniqueId);
        Set(baseline, "Value", change.CurrentValue); Set(baseline, "State", change.CurrentState); Set(baseline, "SourceSignature", change.SourceSignature);
        var baselines = Array.CreateInstance(baselineType, 1); baselines.SetValue(baseline, 0); Set(request, "Baselines", baselines);
        var requests = Array.CreateInstance(requestType, 1); requests.SetValue(request, 0);
        var preview = Call(plugin, "CaseEdits", "PreviewBatch", document, snapshot, requests);
        Assert(((Array)Get(preview, "Changes")).Cast<object>().Any(c => (string)Get(c, "OwnerUniqueId") == auxiliaryLevelUniqueId && (string)Get(c, "Source") == change.Source && (string)Get(c, "Action") == "赋值"), "Existing write pipeline dropped the auxiliary floor assignment");

        var fresh = (ModelElementCheck)reader.GetMethod("Inventory", Flags).Invoke(null, new object[] { document, level });
        var settings = (PlanningSettings)Get(snapshot, "Planning");
        var rules = CaseWorkflow.ForModel("报规", "单体建筑", settings.RuleProfileId);
        reader.GetMethod("ReadFieldsWithPlanning", Flags).Invoke(null, new object[] { document, level, fresh, rules, "单体建筑", null, settings });
        Assert(!fresh.Fields.Any(f => f.Property == "楼层面积"), "Workbook import changed ordinary scan/export scope");
        var actualParameters = level.Parameters.Cast<Parameter>().Select(p => p.Id.IntegerValue + "|" + p.AsValueString() + "|" + (p.StorageType == StorageType.String ? p.AsString() : "")).OrderBy(s => s).ToArray();
        Assert(document.IsModified == wasModified && oldParameters.SequenceEqual(actualParameters), "Preview changed native level values");
        return new { blankPath, filledPath, added, auxiliary.Name, auxiliary.UniqueId, field.Source, field.OwnerUniqueId, change.Value, plan, preview, modelUnchanged = true, ordinaryScopePreserved = true };
    }

    static object Call(Assembly plugin, string type, string method, params object[] args)
        => plugin.GetType("Hifc.Revit.Management." + type, true).GetMethod(method, Flags).Invoke(null, args);
    static object Get(object value, string name) => value.GetType().GetProperty(name, Flags).GetValue(value, null);
    static void Set(object value, string name, object item) => value.GetType().GetProperty(name, Flags).SetValue(value, item, null);
    static void Assert(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
    static void FillFloor(string path, string level, string value)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml");
            XDocument sheet;
            using (var stream = entry.Open()) sheet = XDocument.Load(stream);
            var row = sheet.Descendants(ns + "row").Single(r => r.Elements(ns + "c").Any(c => ((string)c.Attribute("r")).StartsWith("A", StringComparison.Ordinal) && c.Value == level));
            row.Elements(ns + "c").Single(c => ((string)c.Attribute("r")).StartsWith("C", StringComparison.Ordinal)).Descendants(ns + "t").Single().Value = value;
            entry.Delete();
            using (var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open(), new UTF8Encoding(false))) sheet.Save(writer);
        }
    }
}
