using System;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Hifc.Reference;

internal static class UndergroundTests
{
    static void Assert(bool ok, string why) { if (!ok) throw new Exception(why); }
    static object Calculate(string settings, string levels)
    {
        var assembly = typeof(CaseWorkflow).Assembly;
        var policy = assembly.GetType("Hifc.Reference.UndergroundPolicy");
        Assert(policy != null, "Underground calculation policy is missing");
        var json = new JavaScriptSerializer();
        var selection = json.Deserialize(settings, assembly.GetType("Hifc.Reference.UndergroundSettings"));
        var model = json.Deserialize(levels, assembly.GetType("Hifc.Reference.UndergroundLevel").MakeArrayType());
        try { return policy.GetMethod("Evaluate").Invoke(null, new[] { selection, model }); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static object Get(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
    const string Levels = "[{\"UniqueId\":\"b2\",\"Name\":\"地下二层\",\"ElevationM\":94.8},{\"UniqueId\":\"b1\",\"Name\":\"地下首层\",\"ElevationM\":98.1},{\"UniqueId\":\"structure\",\"Name\":\"结构底板\",\"ElevationM\":94.7},{\"UniqueId\":\"ground\",\"Name\":\"首层\",\"ElevationM\":101.0}]";
    public static void Run(Action<string, Action> check)
    {
        check("underground_area_roles_available_in_both_planning_scopes", () => {
            foreach (var kind in new[] { "总图", "单体建筑" })
                foreach (var slot in new[] { "人防区域", "消防分区", "停车场", "停车位" })
                    Assert(CaseWorkflow.ForModel("报规", kind).Any(r => r.Slot == slot && r.Binding == "实例" && r.Entities.SequenceEqual(new[] { "IfcSpace" })), kind + " lacks " + slot);
        });
        check("underground_supplements_never_require_absent_spaces", () => {
            var summary = CaseWorkflow.Summarize("报规", "单体建筑", new ModelElementCheck[0]);
            foreach (var slot in new[] { "人防区域", "消防分区", "停车场", "停车位" })
                Assert(summary.Roles.Any(r => r.Slot == slot && r.Mode != "必要" && !r.NeedsAttention), "Absent optional space was required: " + slot);
        });
        check("parking_area_scheme_does_not_force_parking_bays_on_car_park", () => {
            var row = new ModelElementCheck { Entity = "IfcSpace", AreaScheme = "停车场", Name = "B1停车场" };
            var candidates = CaseWorkflow.GetCandidates(row, "单体建筑");
            Assert(candidates.Length == 1 && candidates[0].Slot == "停车场" && candidates[0].CanPrepare, "Parking area classification is ambiguous or missing");
        });
        check("space_entity_alone_never_invents_business_class", () => {
            Assert(CaseWorkflow.GetCandidates(new ModelElementCheck { Entity="IfcSpace", AreaScheme="", Name="面积1" }, "单体建筑").Length == 0, "Entity guessed a role");
        });
        check("room_with_civil_defence_parameters_stays_a_carrier_review",()=>{
            var row=new ModelElementCheck {Entity="IfcSpace",Name="普通房间",SuggestedSlots=new[]{"人防区域"}};
            var candidates=CaseWorkflow.GetCandidates(row,"单体建筑");
            Assert(candidates.Length==1&&!candidates[0].CanPrepare&&candidates[0].RequiresReview,"Room silently entered Area-only classification");
        });
        check("civil_defence_area_does_not_misidentify_underground_model_as_site", () => {
            var rows = new[] { new ModelElementCheck {Entity="IfcSpace", Slot="人防区域", AreaScheme="人防"}, new ModelElementCheck {Entity="IfcSlab", Slot="建筑区域"} };
            Assert(CaseWorkflow.Detect(rows).Kind == "单体建筑", "Common underground roles polluted site detection");
        });
        check("underground_floor_count_is_integer_in_generated_export", () => {
            var catalog = ReferenceCatalog.FromEmbeddedBaseline();
            Assert(catalog.Properties.Any(p => p.Property == "地下建筑层数" && p.RawType == "Integerl"), "Original evidence was rewritten");
            Assert(catalog.BuildExportConfiguration().Contains("地下建筑层数\tInteger\t"), "Exporter still receives Integerl");
            var rule = CaseWorkflow.ForModel("报规", "单体建筑").Single(r=>r.Slot=="建筑技术");
            var property = rule.Properties.Single(p=>p.Property=="地下建筑层数");
            var field = ModelChecks.Evaluate(property,new[]{new ParameterRead {Name="地下建筑层数",Origin="instance",HasValue=true,Value="2"}});
            Assert(field.Valid && field.RawType=="Integer", "Count cannot be edited and validated as integer");
        });
        check("selected_export_configuration_does_not_enable_unselected_roles",()=>{
            var catalog=ReferenceCatalog.FromEmbeddedBaseline();var method=typeof(ReferenceCatalog).GetMethod("BuildSelectedExportConfiguration");
            Assert(method!=null,"Scoped native export configuration is missing");
            var config=(string)method.Invoke(catalog,new object[]{catalog.ForSlot("人防区域")});
            Assert(config.Contains("Pset_人防区域信息属性集")&&!config.Contains("Pset_停车场信息属性集")&&!config.Contains("Pset_建筑楼层信息属性集"),"Unselected property sets can fall back to unrelated native values");
        });
        check("underground_count_uses_confirmed_levels_and_explicit_datum", () => {
            var r=Calculate("{\"Confirmed\":true,\"GroundElevationM\":101,\"LevelUniqueIds\":[\"b2\",\"b1\"]}",Levels);
            Assert(Convert.ToInt32(Get(r,"Count"))==2 && Math.Abs(Convert.ToDouble(Get(r,"HeightM"))-6.2)<1e-8, "Wrong confirmed count or datum-based height");
        });
        check("unconfirmed_negative_levels_do_not_generate_underground_totals", () => {
            var r=Calculate("{\"Confirmed\":false,\"GroundElevationM\":0,\"LevelUniqueIds\":[\"b1\"]}","[{\"UniqueId\":\"b1\",\"Name\":\"B1\",\"ElevationM\":-3.6}]");
            Assert(Get(r,"Count")==null && Get(r,"HeightM")==null, "Unconfirmed floor guessed a total");
        });
        check("deleted_confirmed_level_invalidates_cached_totals", () => {
            var r=Calculate("{\"Confirmed\":true,\"GroundElevationM\":101,\"LevelUniqueIds\":[\"deleted\",\"b1\"]}",Levels);
            Assert(Get(r,"Count")==null && Get(r,"HeightM")==null && !string.IsNullOrEmpty((string)Get(r,"Note")), "Deleted floor silently reduced total");
        });
        check("duplicate_physical_elevation_needs_confirmation_instead_of_double_count", () => {
            var r=Calculate("{\"Confirmed\":true,\"GroundElevationM\":0,\"LevelUniqueIds\":[\"a\",\"b\"]}","[{\"UniqueId\":\"a\",\"ElevationM\":-3.6},{\"UniqueId\":\"b\",\"ElevationM\":-3.6}]");
            Assert(Get(r,"Count")==null, "Architectural and structural level were double counted");
        });
    }
}
