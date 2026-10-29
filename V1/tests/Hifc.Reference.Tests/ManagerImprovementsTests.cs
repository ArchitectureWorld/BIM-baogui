using System;
using System.Linq;
using Hifc.Reference;

internal static class ManagerImprovementsTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> check)
    {
        check("identified_road_stays_incomplete_until_required_width_is_read_back", () => {
            var row = new ModelElementCheck { Id = 1, UniqueId = "road", Entity = "IfcSlab", Slot = "道路边界线", Fields = new[] {
                new FieldCheck { PropertySet = "Pset_道路边界线信息属性集", Property = "道路宽度", Binding = "实例", OwnerUniqueId = "road", State = "未填写" },
                new FieldCheck { PropertySet = "Pset_道路边界线信息属性集", Property = "区内道路类型", Binding = "实例", OwnerUniqueId = "road", State = "有效", Value = "消防道路", AllowedEmpty = true } } };
            Func<RoleCoverage> role = () => CaseWorkflow.Summarize("报规", "总图", new[] { row }).Roles.Single(r => r.Slot == "道路边界线");
            var prop = typeof(RoleCoverage).GetProperty("MissingFields");
            Assert(prop != null, "Role completeness is absent");
            Assert((int)prop.GetValue(role()) == 1 && role().State.Contains("道路宽度"), "Presence hid missing road width");
            row.Fields[0].SuggestedValue = "4";
            Assert((int)prop.GetValue(role()) == 1, "Suggestion incorrectly counted as saved");
            row.Fields[0].Value = "4"; row.Fields[0].State = "有效";
            Assert((int)prop.GetValue(role()) == 0, "Readback did not resolve incomplete status");
        });
        check("only_road_width_is_a_required_manual_entry_indicator", () => {
            var rows = new[] {
                new ModelElementCheck { UniqueId = "green", Entity = "IfcSlab", Slot = "绿地", Fields = new[] { new FieldCheck { PropertySet = "Pset_绿地信息属性集", Property = "投影面积", State = "未填写" } } },
                new ModelElementCheck { UniqueId = "road", Entity = "IfcSlab", Slot = "道路边界线", Fields = new[] {
                    new FieldCheck { PropertySet = "Pset_道路边界线信息属性集", Property = "道路宽度", State = "有效", Value = "4" },
                    new FieldCheck { PropertySet = "Pset_道路边界线信息属性集", Property = "区内道路类型", State = "有效", Value = "消防道路" } } } };
            var summary = CaseWorkflow.Summarize("报规", "总图", rows);
            Assert(summary.Roles.All(r => !r.NeedsAttention), "Other blanks or absent reference models became mandatory manual entry");
            rows[1].Fields[0].State = "未填写"; rows[1].Fields[0].Value = null;
            summary = CaseWorkflow.Summarize("报规", "总图", rows);
            Assert(summary.Roles.Where(r => r.NeedsAttention).Select(r => r.Slot).SequenceEqual(new[] { "道路边界线" }), "Red indicator exceeded road width scope");
            rows[1].Slot=null; rows[1].SuggestedSlots=new[]{ "道路边界线" };
            summary=CaseWorkflow.Summarize("报规","总图",rows);
            Assert(summary.Roles.Single(r=>r.Slot=="道路边界线").NeedsAttention, "Read road width disappeared while classification is pending");
        });
        check("empty_level_footprint_defaults_respect_floor_alias_boundaries", () => {
            foreach (var name in new[] { "一层", "首层", "1层", "1F", "F1", "一层建筑平面", "1F（首层）" }) {
                var f = new FieldCheck { Property = "建筑基底", RawType = "Boolean", State = "缺参数" };
                AutoFillPolicy.Populate(f, "建筑楼层", name, null);
                Assert(f.SuggestedValue == "1", "First floor missed: " + name);
            }
            foreach (var name in new[] { "二层", "十一层", "B1", "-1F", "地下1层", "11F", "F11" }) {
                var f = new FieldCheck { Property = "建筑基底", RawType = "Boolean", State = "未填写" };
                AutoFillPolicy.Populate(f, "建筑楼层", name, null);
                Assert(f.SuggestedValue == "0", "Wrong first floor match: " + name);
            }
        });
        check("explicit_footprint_false_is_never_replaced", () => {
            foreach (var value in new[] { "0", "false", "否" }) {
                var f = new FieldCheck { Property = "建筑基底", State = "有效", Value = value };
                AutoFillPolicy.Populate(f, "建筑楼层", "首层", null);
                Assert(f.SuggestedValue == null && f.Value == value, "Explicit false overwritten");
            }
        });
        check("parking_area_scheme_produces_explainable_supported_candidate", () => {
            var row = new ModelElementCheck { Entity = "IfcSpace", Category = "面积", Name = "面积 01" };
            var scheme = typeof(ModelElementCheck).GetProperty("AreaScheme");
            Assert(scheme != null, "Native Area scheme is not scanned");
            scheme.SetValue(row, "停车位面积方案");
            var candidate = CaseWorkflow.GetCandidates(row, "总图").SingleOrDefault(c => c.Slot == "停车位");
            Assert(candidate != null && candidate.CanPrepare && candidate.Evidence.Contains("面积方案"), "Area scheme is ignored");
            scheme.SetValue(row, "总建筑面积");
            Assert(!CaseWorkflow.GetCandidates(row, "总图").Any(c => c.Slot == "停车位"), "All Areas became parking");
        });
    }
}
