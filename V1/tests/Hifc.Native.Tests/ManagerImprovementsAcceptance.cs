using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;

// Runs only in the fresh acceptance host, on hash-verified model copies.
// Fixture tests roll back; PreparePersistence intentionally saves ONLY those copies.
public static class ManagerImprovementsAcceptance
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static Assembly Plugin => AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Hifc.V1.Revit");
    static Type Edits => Plugin.GetType("Hifc.Revit.Management.CaseEdits");
    static object Get(object value, string name) => value.GetType().GetProperty(name, Flags).GetValue(value);
    static void Set(object value, string name, object data) => value.GetType().GetProperty(name, Flags).SetValue(value, data);
    static object[] Items(object value) => ((IEnumerable)value).Cast<object>().ToArray();
    static void Guard(Document doc, string run)
    {
        if (!Path.GetFullPath(doc.PathName).StartsWith(Path.Combine(Path.GetFullPath(run), "models") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Acceptance refused a document outside the run's model copies");
    }
    static object Scan(Document doc, string kind)
    {
        var type = Plugin.GetType("Hifc.Revit.Management.CaseScanJob");
        var job = Activator.CreateInstance(type, Flags, null, new object[] { doc, "报规", kind }, null);
        while (!(bool)Get(job, "Complete")) type.GetMethod("ReadNext", Flags).Invoke(job, new object[] { doc, 75 });
        return type.GetMethod("Snapshot", Flags).Invoke(job, new object[] { doc });
    }
    static object Request(object row, object field, string value, string slot = null)
    {
        var request = Activator.CreateInstance(Plugin.GetType("Hifc.Revit.Management.EditRequest"));
        Set(request, "Kind", field == null ? "分类" : "赋值"); Set(request,"Ids",new[] { (int)Get(row,"Id") }); Set(request,"Slot",slot);
        var baselineType = Plugin.GetType("Hifc.Revit.Management.EditBaseline"); var baseline = Activator.CreateInstance(baselineType);
        Set(baseline,"ElementId",Get(row,"Id")); Set(baseline,"ElementUniqueId",Get(row,"UniqueId"));
        Set(baseline,"OwnerUniqueId",field == null ? Get(row,"UniqueId") : Get(field,"OwnerUniqueId"));
        Set(baseline,"Value",field == null ? Get(row,"Slot") : Get(field,"Value"));
        if (field != null)
        {
            foreach (var name in new[] { "Source", "PropertySet", "Binding" }) Set(request,name,Get(field,name));
            Set(request,"Value",value); Set(baseline,"State",Get(field,"State")); Set(baseline,"SourceSignature",Get(field,"SourceSignature"));
        }
        var baselines = Array.CreateInstance(baselineType,1); baselines.SetValue(baseline,0); Set(request,"Baselines",baselines); return request;
    }
    static object Batch(Document doc, object scan, params object[] requests)
    {
        var list = Array.CreateInstance(Plugin.GetType("Hifc.Revit.Management.EditRequest"),requests.Length);
        for (int i=0; i<requests.Length; i++) list.SetValue(requests[i],i);
        return Edits.GetMethod("PreviewBatch",Flags).Invoke(null,new object[] { doc, scan, list });
    }
    static void Apply(Document doc, object preview) => Edits.GetMethod("Apply",Flags).Invoke(null,new object[] { doc,preview });
    static string Raw(Document doc, string uid, string source)
    {
        var parameters = doc.GetElement(uid).GetParameters(source); if (parameters.Count != 1) return "<missing-or-ambiguous>";
        var p=parameters[0]; if (!p.HasValue) return null;
        return p.StorageType == StorageType.String ? string.IsNullOrEmpty(p.AsString()) ? null : p.AsString() : p.StorageType == StorageType.Integer ? p.AsInteger().ToString() : p.AsDouble().ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    }
    static void AddExpected(Dictionary<string,string> expected, object preview)
    {
        foreach (var change in Items(Get(preview,"Changes")).Where(c => (string)Get(c,"Action") != "新增参数"))
            expected[(string)Get(change,"OwnerUniqueId") + "|" + (string)Get(change,"Source")] = (string)Get(change,"ExpectedRaw");
    }
    public static object Fixtures(Document doc, string run)
    {
        Guard(doc,run); var checks=new Dictionary<string,bool>();
        using (var group = new TransactionGroup(doc,"HIFC six-item fixtures - rollback"))
        {
            group.Start();
            try
            {
                var level=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().First();
                var scheme=new FilteredElementCollector(doc).OfClass(typeof(AreaScheme)).Cast<AreaScheme>().First();
                Area parking,invalid; ViewPlan plan;
                using (var tx=new Transaction(doc,"Create native parking Area fixtures"))
                {
                    tx.Start(); tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new NativeTestWarnings()));
                    plan=ViewPlan.CreateAreaPlan(doc,scheme.Id,level.Id);
                    var sketch=SketchPlane.Create(doc,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,new XYZ(0,0,level.Elevation)));
                    var corners=new[]{new XYZ(3500,3500,level.Elevation),new XYZ(3530,3500,level.Elevation),new XYZ(3530,3520,level.Elevation),new XYZ(3500,3520,level.Elevation)};
                    for (int i=0;i<4;i++) doc.Create.NewAreaBoundaryLine(sketch,Line.CreateBound(corners[i],corners[(i+1)%4]),plan);
                    doc.Regenerate(); parking=doc.Create.NewArea(plan,new UV(3510,3510)); parking.Name="停车位";
                    invalid=doc.Create.NewArea(plan,new UV(3600,3600)); invalid.Name="停车位未封闭";
                    tx.Commit();
                }
                var snapshot=Scan(doc,"总图"); var rows=Items(Get(snapshot,"Elements"));
                var validRow=rows.Single(r=>(int)Get(r,"Id")==parking.Id.IntegerValue);
                var invalidRow=rows.Single(r=>(int)Get(r,"Id")==invalid.Id.IntegerValue);
                checks["native_areas_survive_view_specific_inventory_filter"]=(string)Get(validRow,"AreaScheme")==scheme.Name;
                checks["parking_area_has_ifcspace_and_preparable_pset"]=(string)Get(validRow,"Entity")=="IfcSpace" && Items(Get(validRow,"Candidates")).Any(c=>(string)Get(c,"Slot")=="停车位" && (bool)Get(c,"CanPrepare")) && Items(Get(validRow,"Fields")).Any(f=>(string)Get(f,"PropertySet")=="Pset_停车位信息属性集");
                checks["unclosed_zero_area_is_reported_and_not_auto_prepared"]=!string.IsNullOrEmpty((string)Get(invalidRow,"CoverageNote")) && !Items(Get(invalidRow,"Candidates")).Any(c=>(bool)Get(c,"CanPrepare"));
                var preview=Batch(doc,snapshot); Apply(doc,preview);
                checks["native_parking_area_classification_written"]=Raw(doc,parking.UniqueId,"类目")=="停车位";
            }
            finally { group.RollBack(); }
        }
        return new { allPassed=checks.Values.All(v=>v), checks };
    }
    public static Dictionary<string,string> PreparePersistence(Document doc, string run, Dictionary<string,bool> checks)
    {
        Guard(doc,run); var site=Path.GetFileName(doc.PathName).StartsWith("总图"); var kind=site?"总图":"单体建筑";
        var expected=new Dictionary<string,string>(); var scan=Scan(doc,kind); var rows=Items(Get(scan,"Elements"));
        if (site)
        {
            var row=rows.Single(r=>(int)Get(r,"Id")==451959); var classification=Request(row,null,null,"绿地");
            var proposal=Batch(doc,scan,classification); var proposed=Items(Get(proposal,"ProposedElements")).Single();
            var field=Items(Get(proposed,"Fields")).Single(f=>(string)Get(f,"Source")=="绿地投影面积");
            checks["new_class_exposes_current_model_autofill_before_write"]=!string.IsNullOrWhiteSpace((string)Get(field,"SuggestedValue")) && (string)Get(row,"Slot")=="规划净用地";
            var value=Request(row,field,"321.25","绿地"); var combined=Batch(doc,scan,classification,value);
            checks["cleanup_preview_contains_old_exclusive_sources"]=Items(Get(combined,"Changes")).Any(c=>(string)Get(c,"Source")=="规划净用地投影面积" && (string)Get(c,"Action")=="清空");
            var type=doc.GetElement(doc.GetElement(new ElementId(451959)).GetTypeId()); var commonBefore=Raw(doc,type.UniqueId,"构件类型");
            // A late read-only failure exercises the production transaction rollback
            // after the classification and cleanup have already run.
            using (var outer = new TransactionGroup(doc,"Injected failure isolation"))
            {
                outer.Start();
                try
                {
                    var changes=Items(Get(combined,"Changes")); var ct=changes[0].GetType(); var fault=Activator.CreateInstance(ct);
                    var owner=doc.GetElement(new ElementId(451959)); var readonlyParameter=owner.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    Set(fault,"Id",owner.Id.IntegerValue); Set(fault,"OwnerUniqueId",owner.UniqueId); Set(fault,"Source",readonlyParameter.Definition.Name); Set(fault,"Action","赋值"); Set(fault,"ExpectedRaw","1");
                    var array=Array.CreateInstance(ct,changes.Length+1); for(int i=0;i<changes.Length;i++) array.SetValue(changes[i],i); array.SetValue(fault,changes.Length); Set(combined,"Changes",array);
                    Set(combined,"SnapshotHash",Edits.GetMethod("Fingerprint",Flags).Invoke(null,new object[]{doc,array}));
                    bool rejected=false; try { Apply(doc,combined); } catch { rejected=true; }
                    checks["late_failure_rolls_back_classification_cleanup_and_new_value"]=rejected && Raw(doc,owner.UniqueId,"类目")=="规划净用地" && !string.IsNullOrEmpty(Raw(doc,owner.UniqueId,"规划净用地投影面积"));
                }
                finally { outer.RollBack(); }
            }
            combined=Batch(doc,scan,classification,value); Apply(doc,combined); AddExpected(expected,combined);
            checks["old_exclusive_values_cleared_new_manual_value_written"]=Raw(doc,(string)Get(row,"UniqueId"),"规划净用地投影面积")==null && Raw(doc,(string)Get(row,"UniqueId"),"规划净用地名称")==null && Raw(doc,(string)Get(row,"UniqueId"),"绿地投影面积")=="321.25";
            checks["shared_type_value_preserved"]=Raw(doc,type.UniqueId,"构件类型")==commonBefore;
            expected[type.UniqueId+"|构件类型"]=commonBefore;
        }
        else
        {
            var info=rows.Single(r=>(string)Get(r,"Entity")=="IfcBuilding");
            var fields=Items(Get(info,"Fields")); var category=fields.Single(f=>(string)Get(f,"Property")=="建筑类别"); var type=fields.Single(f=>(string)Get(f,"Property")=="建筑类型名称");
            var factory=Path.GetFileName(doc.PathName).StartsWith("厂房");
            var level=rows.First(r=>(string)Get(r,"Entity")=="IfcBuildingStorey" && Items(Get(r,"Fields")).Any(f=>(string)Get(f,"Property")=="建筑基底"));
            var footprint=Items(Get(level,"Fields")).Single(f=>(string)Get(f,"Property")=="建筑基底");
            var preview=Batch(doc,scan,Request(info,type,factory?"厂房":"生活服务设施"),Request(info,category,factory?"工业厂房":"后勤配套"),Request(level,footprint,"0"));
            Apply(doc,preview); AddExpected(expected,preview);
            checks["mandatory_project_type_and_custom_category_written"]=Raw(doc,(string)Get(info,"UniqueId"),(string)Get(type,"Source"))==(factory?"厂房":"生活服务设施") && Raw(doc,(string)Get(info,"UniqueId"),(string)Get(category,"Source"))==(factory?"工业厂房":"后勤配套");
            checks["explicit_false_footprint_written"]=Raw(doc,(string)Get(level,"UniqueId"),(string)Get(footprint,"Source"))=="0";
        }
        return expected;
    }
    public static bool VerifyReopened(Document doc, string run, Dictionary<string,string> expected)
    {
        Guard(doc,run);
        return expected.Count>0 && expected.All(pair=> { var split=pair.Key.IndexOf('|'); return Raw(doc,pair.Key.Substring(0,split),pair.Key.Substring(split+1))==pair.Value; });
    }
}
