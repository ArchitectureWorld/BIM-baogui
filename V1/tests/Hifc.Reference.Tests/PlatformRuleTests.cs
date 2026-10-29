using System;
using System.Linq;
using Hifc.Reference;

internal static class PlatformRuleTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Same(string[] expected, string[] actual, string message)
    {
        Assert(expected.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(actual.OrderBy(x => x, StringComparer.Ordinal)), message);
    }

    public static void Run(Action<string, Action> check)
    {
        check("planning_control_line_accepts_confirmed_net_land_label_without_relaxing_other_enums", () => {
            const string value = "净用地控制红线";
            Assert(PlatformRules.Choices("规划控制线", "红线类型").Contains(value), "confirmed net-land redline option is missing");
            Assert(PlatformRules.ValueError("规划控制线", "红线类型", value) == null, "confirmed control line is rejected");
            Assert(PlatformRules.ValueError("规划控制线", "名称", value) == null, "control-line name is rejected");
            Assert(PlatformRules.ValueError("规划控制线", "红线类型", "净用地红线") != null, "unconfirmed similar value bypassed validation");
            Assert(PlatformRules.ValueError("规划控制线", "红线类型", "净用地规划控制红线") != null, "superseded redline spelling remains an accepted preset");
            Assert(PlatformRules.ValueError("绿地", "绿地类型", value) != null, "control-line compatibility leaked into another enum");
            Assert(!PlatformRules.Fields("规划控制线").Single(f => f.Name == "红线类型").Items.Contains(value), "frozen platform snapshot was overwritten");
            Assert(PlatformRules.Choices("规划控制线", "红线类型").Contains("红线"), "existing redline option was removed");
        });
        check("platform_profile_requires_the_explicit_scoped_selection", () => {
            Assert(PlatformRules.IsActive("hubei-420626-planning-20261020"), "confirmed profile is inactive");
            foreach (var other in new[] { null, "", "420626", "hubei", "hubei-420626-construction-20261020", "hubei-420626-planning-20261021" })
                Assert(!PlatformRules.IsActive(other), "unconfirmed region, stage or snapshot activated: " + other);
        });
        check("platform_pset_normalization_only_aliases_the_exact_canopy_name", () => {
            foreach (var name in new[] { "雨蓬", "雨蓬信息属性集", "Pset_雨蓬信息属性集", "雨篷", "Pset_雨篷信息属性集" })
                Assert(PlatformRules.CanonicalPropertySet(name) == "Pset_雨篷信息属性集", "canopy alias was not adapted: " + name);
            Assert(PlatformRules.CanonicalPropertySet("建筑技术信息属性集") == "Pset_建筑技术信息属性集", "canonical suffix duplicated");
            Assert(PlatformRules.CanonicalPropertySet("Pset_附属雨蓬信息属性集") == "Pset_附属雨蓬信息属性集", "unrelated text was fuzzily renamed");
            Assert(PlatformRules.Fields("室外停车场").Length == 0, "parking semantics were inferred from a similar name");
        });
        check("platform_snapshot_preserves_every_branch_and_hidden_field_record", () => {
            var rules = PlatformRules.Rules();
            Assert(rules.Length == 39 && rules.Select(x => x.PropertySet).Distinct().Count() == 38, "matching branches collapsed");
            var fields = rules.SelectMany(x => x.Fields).ToArray();
            Assert(fields.Length == 279, "source field records dropped");
            Assert(fields.Count(x => x.DisplayRequired) == 153, "display-required source flags changed");
            Assert(fields.Count(x => x.Name != x.InternalName) == 27, "display/internal mappings lost");
            Assert(fields.Any(x => !x.ShowProperty && !x.AllowNull), "hidden nonnullable metadata was discarded");
        });
        check("platform_required_fields_follow_visible_effective_names_and_applicable_pset", () => {
            Same(new[] { "投影面积", "主功能类别", "子功能类别", "计算系数", "计容系数" }, PlatformRules.RequiredFields("建筑区域"), "region requirements used internal names or hidden fields");
            Same(new[] { "投影面积", "绿地类型", "折算系数" }, PlatformRules.RequiredFields("绿地"), "green-space requirements incorrect");
            Assert(PlatformRules.RequiredFields("不存在的类目").Length == 0, "unknown category inherited all profile requirements");
            Assert(!PlatformRules.RequiredFields("建筑技术").Contains("建筑面积") && !PlatformRules.RequiredFields("建筑楼层").Contains("楼层面积"), "local calculated auxiliaries became platform requirements");
        });
        check("platform_canopy_alias_keeps_seven_required_fields", () => {
            Same(new[] { "投影面积", "公建大类", "公建小类", "计算系数", "计容系数", "间距审查", "退让审查" }, PlatformRules.RequiredFields("雨蓬"), "canopy runtime adapter dropped required inputs");
        });
        check("platform_display_name_lookup_does_not_export_the_internal_field_name", () => {
            var field = PlatformRules.Fields("绿地").Single(x => x.Name == "绿地类型");
            Assert(field.InternalName == "类型", "original internal name not retained");
            Assert(PlatformRules.Choices("绿地", "绿地类型").Length == 8, "display name cannot resolve choices");
            Same(PlatformRules.Choices("绿地", "绿地类型"), PlatformRules.Choices("绿地", "类型"), "exact internal-name fallback lost");
            Assert(PlatformRules.ValueError("绿地", "类型", "错误类别") != null, "internal-name fallback bypassed enum validation");
            Assert(PlatformRules.Fields("建筑区域").Any(x => x.Name == "构件类型" && !x.ShowProperty), "missing display name did not fall back to internal name");
        });
        check("platform_code_less_visible_fields_remain_required_and_hidden_fields_remain_optional", () => {
            var region = PlatformRules.Fields("建筑区域");
            Assert(region.Single(x => x.Name == "投影面积").Code == "" && PlatformRules.RequiredFields("建筑区域").Contains("投影面积"), "code-less area was discarded");
            Assert(PlatformRules.RequiredFields("雨篷").Contains("间距审查"), "code-less review switch was discarded");
            Assert(PlatformRules.ValueError("建筑区域", "构件类型", null) == null, "hidden nonnullable field was promoted to user-required");
        });
        check("platform_default_values_never_satisfy_missing_business_inputs", () => {
            Assert(PlatformRules.Fields("绿地").Single(x => x.Name == "绿地类型").DefaultValue == "集中绿地", "source default lost from metadata");
            Assert(PlatformRules.ValueError("绿地", "绿地类型", " ") != null, "default became a business fact");
            Assert(PlatformRules.ValueError("建筑技术", "地上建筑层数", null) != null, "template six storeys filled a missing model value");
            Assert(PlatformRules.ValueError("建筑区域", "计算系数", "0") == null, "existing zero became missing");
        });
        check("platform_optional_road_width_flag_is_not_rewritten_as_display_required", () => {
            Assert(!PlatformRules.RequiredFields("道路边界线").Contains("道路宽度"), "product manual-entry policy changed the source nullable flag");
            Assert(PlatformRules.ValueError("道路边界线", "道路宽度", null) == null, "optional source value received a display-required error");
        });
        check("platform_function_choices_come_from_the_parent_dictionary", () => {
            Assert(PlatformRules.Choices("建筑区域", "主功能类别").Contains("学校"), "main functions not linked to CodeComType");
            Same(PlatformRules.Choices("建筑区域", "主功能类别"), PlatformRules.Choices("室内停车场", "公建大类"), "public-building major class uses different dictionary");
            var school = PlatformRules.Choices("建筑区域", "子功能类别", "学校");
            Assert(school.Length == 23 && school.Contains("教室") && school.Contains("办公室") && school.Contains("食堂"), "school subfunctions truncated or invented");
            Assert(PlatformRules.Choices("建筑区域", "子功能类别").Length == 0, "missing parent silently became school");
            Assert(PlatformRules.Choices("室内停车场", "公建小类", "不存在的功能").Length == 0, "unknown parent returned unrelated child choices");
        });
        check("platform_child_function_checks_parent_membership_without_inventing_parent", () => {
            Assert(PlatformRules.ValueError("建筑区域", "子功能类别", "教室", "学校") == null, "valid school child rejected");
            Assert(PlatformRules.ValueError("建筑区域", "子功能类别", "教室", "办公") != null, "child from another parent accepted");
            Assert(PlatformRules.ValueError("建筑区域", "子功能类别", "教室", "不存在的功能") != null, "unknown parent accepted a child");
            Assert(PlatformRules.ValueError("建筑区域", "子功能类别", "教室") == null, "child validator invented a missing parent");
            Assert(PlatformRules.ValueError("建筑区域", "主功能类别", "教学") != null, "arbitrary nonempty main function accepted");
        });
        check("platform_building_classification_and_usage_keep_distinct_enumerations", () => {
            Same(new[] { "住宅", "公建", "中小学等", "幼儿园等" }, PlatformRules.Choices("建筑技术", "建筑类型名称"), "building nature mixed with major function");
            Same(new[] { "裙房", "塔楼", "中学" }, PlatformRules.Choices("建筑技术", "建筑用途名称"), "verified school usage missing from effective options");
            Same(new[] { "裙房", "塔楼" }, PlatformRules.Fields("建筑技术").Single(f => f.Name == "建筑用途名称").Items, "original platform snapshot was rewritten");
            Assert(PlatformRules.ValueError("建筑技术", "建筑类型名称", "厂房") != null, "out-of-snapshot building nature accepted");
            Assert(PlatformRules.ValueError("建筑技术", "建筑用途名称", "学校") != null, "function category became building usage");
        });
        check("platform_verified_middle_school_usage_is_scoped_to_building_usage", () => {
            Assert(PlatformRules.ValueError("Pset_建筑技术信息属性集", "建筑用途名称", "中学") == null, "verified usage is blocked by the old enum");
            Assert(PlatformRules.ValueError("建筑技术", "建筑类别", "中学") != null, "usage evidence weakened the separate category enum");
            Assert(PlatformRules.ValueError("建筑技术", "建筑用途名称", "学校") != null, "unverified usage was accepted");
            Assert(PlatformRules.Choices("不存在的类目", "建筑用途名称").Length == 0, "compatibility option leaked into another Pset");
        });
        check("platform_duplicate_enum_ids_do_not_remove_different_labels", () => {
            var choices = PlatformRules.Choices("建筑技术", "建筑类别");
            Assert(choices.Length == 32 && choices.Contains("门卫房") && choices.Contains("配电房"), "duplicate ID 33 removed a label");
            Assert(PlatformRules.ValueError("建筑技术", "建筑类别", "门卫房") == null && PlatformRules.ValueError("建筑技术", "建筑类别", "配电房") == null, "valid same-ID labels rejected");
        });
        check("platform_enum_text_containing_comma_remains_one_choice", () => {
            var choices = PlatformRules.Choices("建筑技术", "结构类型");
            Assert(choices.Length == 5 && choices.Contains("框架,剪力墙"), "comma-bearing enum was split");
            Assert(PlatformRules.ValueError("建筑技术", "结构类型", "框架,剪力墙") == null, "original combined structure rejected");
            Assert(PlatformRules.ValueError("建筑技术", "结构类型", "框架，剪力墙") != null, "unlisted punctuation was fuzzily accepted");
        });
        check("platform_boolean_bridge_preserves_false_and_rejects_other_numbers", () => {
            foreach (var pset in new[] { "建筑楼层", "建筑技术" }) {
                var property = pset == "建筑楼层" ? "建筑基底" : "民用建筑";
                foreach (var value in new[] { "0", "1", "false", "true", "False", "True", "是", "否" })
                    Assert(PlatformRules.ValueError(pset, property, value) == null, "valid boolean bridge rejected: " + value);
                Assert(PlatformRules.ValueError(pset, property, "2") != null, "enum ID mistaken for boolean value");
                Assert(PlatformRules.ValueError(pset, property, "") != null, "empty required boolean accepted");
            }
        });
        check("platform_total_plan_rejects_stale_numeric_component_type", () => {
            Same(new[] { "智能快件(信包)箱", "风雨连廊", "非机动车棚" }, PlatformRules.Choices("总平构件", "构件类型"), "total-plan component choices wrong");
            Assert(PlatformRules.ValueError("总平构件", "构件类型", "250") != null, "legacy numeric type accepted as a business category");
        });
        check("platform_parking_keeps_both_routes_without_a_merged_default", () => {
            var rules = PlatformRules.Rules("停车位");
            Assert(rules.Length == 2, "indoor/outdoor parking branch collapsed");
            var indoor = rules.Single(x => x.EntityCode == "104");
            var outdoor = rules.Single(x => x.EntityCode == "203");
            Assert(indoor.SetType == "2" && indoor.ClassificationFieldOrValue == "是否为室外停车场" && indoor.MatchValue == "否", "indoor parking routing corrupted");
            Assert(outdoor.ClassificationFieldOrValue == "是否为室外停车场" && outdoor.MatchValue == "是", "outdoor parking routing corrupted");
            Assert(outdoor.Fields.Any(x => x.Name == "地下层数" && !x.DisplayRequired), "outdoor-only hidden field lost");
            Assert(PlatformRules.Fields("停车位").Count(x => x.Name == "是否为室外停车场") == 1, "duplicate merged display field");
            Assert(PlatformRules.Fields("停车位").Single(x => x.Name == "是否为室外停车场").DefaultValue == null, "one branch's default became common truth");
        });
        check("platform_indoor_parking_and_site_parking_stay_separate", () => {
            Assert(PlatformRules.Rules("室内停车场").Single().EntityCode == "103", "indoor parking was redirected to site parking");
            Assert(PlatformRules.Rules("停车场").Single().EntityCode == "205", "site parking was renamed to indoor parking");
            Assert(PlatformRules.RequiredFields("室内停车场").Contains("公建大类") && PlatformRules.RequiredFields("室内停车场").Contains("公建小类"), "indoor parking functional inputs absent");
        });
        check("platform_compute_gates_survive_rule_loading", () => {
            var wall = PlatformRules.Rules("墙").Single();
            var stair = PlatformRules.Rules("楼梯段").Single();
            Assert(wall.ComputeGate == "1" && wall.ComputeField == "是否为女儿墙", "parapet calculation gate lost");
            Assert(stair.ComputeGate == "1" && stair.ComputeField == "是否为室外楼梯", "outdoor staircase calculation gate lost");
        });
        check("platform_unknown_auxiliary_fields_do_not_acquire_rules", () => {
            Assert(PlatformRules.ValueError("建筑技术", "建筑面积", null) == null, "local helper acquired platform requirement");
            Assert(PlatformRules.Choices("不存在的类目", "构件类型").Length == 0 && PlatformRules.ValueError("不存在的类目", "构件类型", "250") == null, "unrelated pset inherited this profile's enum");
        });
        check("platform_callers_cannot_mutate_later_rule_reads", () => {
            var fields = PlatformRules.Fields("绿地");
            fields.Single(x => x.Name == "绿地类型").Items[0] = "任意值";
            var branches = PlatformRules.Rules("停车位");
            branches[0].Fields[0] = null;
            Assert(PlatformRules.Choices("绿地", "绿地类型").Contains("集中绿地"), "caller mutation poisoned global enumeration");
            Assert(PlatformRules.Rules("停车位")[0].Fields[0] != null, "caller mutation poisoned branch metadata");
        });
    }
}
