using System;
using System.Linq;
using System.Reflection;
using Hifc.Reference;

internal static class BooleanPolicyTests
{
    static string Normalize(string value)
    {
        var type = typeof(AutoFillPolicy).Assembly.GetType("Hifc.Reference.BooleanPolicy");
        if (type == null) throw new Exception("Boolean normalization policy missing");
        try { return (string)type.GetMethod("Normalize").Invoke(null, new object[] { value }); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    static void Equal(string expected, string actual) { if (expected != actual) throw new Exception("expected=" + expected + "; actual=" + actual); }
    public static void Run(Action<string, Action> check)
    {
        check("boolean_legacy_values_normalize_without_losing_false", () => {
            foreach (var v in new[] { "0", "否", "false", "FALSE", " 0 " }) Equal("0", Normalize(v));
            foreach (var v in new[] { "1", "是", "true", "TRUE", " 1 " }) Equal("1", Normalize(v));
        });
        check("boolean_blank_is_false", () => { Equal("0", Normalize(null)); Equal("0", Normalize(" ")); });
        check("invalid_boolean_is_not_silently_false", () => {
            foreach (var value in new[] { "2", "1#", "未核定" }) {
                try { Normalize(value); } catch (InvalidOperationException) { continue; }
                throw new Exception("invalid boolean accepted: " + value);
            }
        });
        check("boolean_model_check_accepts_legacy_chinese_and_retains_numeric_contract", () => {
            var p = new ReferenceProperty { Property="建筑基底", SourceParameter="建筑基底", RawType="Boolean" };
            var result = ModelChecks.Evaluate(p,new[] { new ParameterRead { Name=p.SourceParameter, Origin="instance", HasValue=true, StorageType="String", Value="否" } });
            Equal("有效",result.State); Equal("0",result.Value);
        });
        check("active_platform_boolean_sources_have_no_mixed_physical_type", () => {
            var properties = CaseExportPolicy.SourceProperties(PlatformRules.ProfileId);
            var mixed = properties.GroupBy(p => p.SourceParameter).Where(g => g.Any(p => p.ExportType == "Boolean") && g.Any(p => p.ExportType != "Boolean"))
                .Select(g => g.Key + ": " + string.Join(" | ", g.Select(p => p.PropertySet + "/" + p.Entity + "/" + p.Property + "/" + p.ExportType).Distinct())).ToArray();
            if (mixed.Length > 0) throw new Exception("mixed physical Boolean sources: " + string.Join("; ", mixed));
            var sources = properties.Where(p => p.ExportType == "Boolean").Select(p => p.SourceParameter).Distinct().ToArray();
            if (sources.Length == 0) throw new Exception("no active Boolean sources tested");
            foreach (var source in sources)
                if (!BooleanPolicy.IsSource(source, PlatformRules.ProfileId) || properties.Any(p => p.SourceParameter == source && p.ExportType != "Boolean"))
                    throw new Exception("mixed physical Boolean source: " + source);
        });
        check("active_general_site_density_uses_dedicated_boolean_source_and_original_ifc_name", () => {
            var properties = CaseExportPolicy.PropertiesForSlot("总平构件", PlatformRules.ProfileId).Where(p => p.Property == "建筑密度").ToArray();
            if (properties.Length != 2 || !properties.Any(p => p.Entity == "IfcSlab") || !properties.Any(p => p.Entity == "IfcBuildingElementProxy"))
                throw new Exception("general-site density carriers missing");
            foreach (var property in properties) {
                Equal("Pset_总平构件信息属性集", property.PropertySet); Equal("建筑密度", property.Property);
                Equal("Boolean", property.ExportType); Equal("总平构件建筑密度", property.SourceParameter);
                Equal("1", CaseExportPolicy.ValueForExport(property, "是", PlatformRules.ProfileId));
            }
            if (!BooleanPolicy.IsSource("总平构件建筑密度", PlatformRules.ProfileId) || BooleanPolicy.IsSource("建筑密度", PlatformRules.ProfileId))
                throw new Exception("dedicated Boolean and original Real sources not distinguished");
        });
        check("active_site_density_keeps_real_source_and_existing_guid", () => {
            var property = CaseExportPolicy.SourceProperties(PlatformRules.ProfileId).Single(p => p.PropertySet == "Pset_场地信息属性集" && p.Property == "建筑密度" && p.Entity == "IfcBuilding");
            Equal("Real", property.ExportType); Equal("建筑密度", property.SourceParameter);
            Equal("742548c0-1017-5eda-916a-9d7401e23a91", ReferencePolicy.SourceGuid(property.SourceParameter).ToString());
            Equal("0.25", CaseExportPolicy.ValueForExport(property, "0.25", PlatformRules.ProfileId));
            if (BooleanPolicy.SourceGuid("总平构件建筑密度") == ReferencePolicy.SourceGuid(property.SourceParameter))
                throw new Exception("Boolean physical GUID collided with existing Real source");
        });
        check("non_platform_density_still_uses_frozen_real_contract", () => {
            foreach (var profile in new[] { null, "", "other-profile" }) {
                var properties = CaseExportPolicy.SourceProperties(profile).Where(p => p.Property == "建筑密度").ToArray();
                if (properties.Length != 3) throw new Exception("legacy density contract count changed");
                foreach (var property in properties) { Equal("Real", property.ExportType); Equal("建筑密度", property.SourceParameter); }
                if (BooleanPolicy.IsSource("总平构件建筑密度", profile)) throw new Exception("platform source leaked into legacy contract");
            }
            var frozen = ReferenceCatalog.FromEmbeddedBaseline().Properties.Where(p => p.Property == "建筑密度").ToArray();
            if (frozen.Length != 3 || frozen.Any(p => p.RawType != "Real" || p.SourceParameter != "建筑密度"))
                throw new Exception("runtime adaptation mutated frozen source evidence");
        });
        check("active_stair_audit_booleans_have_dedicated_sources_and_unchanged_ifc_names", () => {
            foreach (var propertyName in new[] { "间距审查", "退让审查" }) {
                var property = CaseExportPolicy.PropertiesForSlot("楼梯段", PlatformRules.ProfileId).Single(p => p.Property == propertyName);
                Equal("Pset_楼梯段信息属性集", property.PropertySet); Equal("IfcStairFlight", property.Entity);
                Equal(propertyName, property.Property); Equal("Boolean", property.ExportType); Equal("楼梯段" + propertyName, property.SourceParameter);
                if (!BooleanPolicy.IsSource(property.SourceParameter, PlatformRules.ProfileId) || BooleanPolicy.IsSource(propertyName, PlatformRules.ProfileId))
                    throw new Exception("stair Boolean still shares a Label source: " + propertyName);
                Equal("0", CaseExportPolicy.ValueForExport(property, "否", PlatformRules.ProfileId));
            }
        });
        check("active_insulation_labels_and_non_platform_stair_sources_are_preserved", () => {
            foreach (var propertyName in new[] { "间距审查", "退让审查" }) {
                var label = CaseExportPolicy.PropertiesForSlot("保温层", PlatformRules.ProfileId).Single(p => p.Property == propertyName);
                Equal("Label", label.ExportType); Equal(propertyName, label.SourceParameter);
                Equal("否", CaseExportPolicy.ValueForExport(label, "否", PlatformRules.ProfileId));
                foreach (var profile in new[] { null, "", "other-profile" }) {
                    var legacy = CaseExportPolicy.PropertiesForSlot("楼梯段", profile).Single(p => p.Property == propertyName);
                    Equal("Boolean", legacy.ExportType); Equal(propertyName, legacy.SourceParameter);
                }
            }
        });
        check("numeric_building_identifiers_are_never_inferred_as_booleans", () => {
            var property = CaseExportPolicy.SourceProperties(PlatformRules.ProfileId).First(p => p.Property == "建筑编号");
            if (BooleanPolicy.IsSource(property.SourceParameter, PlatformRules.ProfileId)) throw new Exception("building identifier inferred as Boolean");
            foreach (var value in new[] { "0", "1" }) {
                var result = ModelChecks.Evaluate(property, new[] { new ParameterRead { Name=property.SourceParameter, Origin="instance", HasValue=true, StorageType="String", Value=value } });
                Equal("Label", result.RawType); Equal(value, result.Value); Equal("有效", result.State);
            }
        });
        check("boolean_legacy_source_lookup_only_accepts_exact_dedicated_physical_sources", () => {
            var method = typeof(BooleanPolicy).GetMethod("LegacySource", new[] { typeof(string) });
            if (method == null) throw new Exception("controlled legacy source lookup missing");
            Equal("建筑密度", (string)method.Invoke(null, new object[] { "总平构件建筑密度" }));
            Equal("间距审查", (string)method.Invoke(null, new object[] { "楼梯段间距审查" }));
            Equal("退让审查", (string)method.Invoke(null, new object[] { "楼梯段退让审查" }));
            foreach (var value in new[] { null, "", "建筑密度", "建筑基底", "间距审查", "退让审查", " 总平构件建筑密度", "总平构件建筑密度 ", "总平构件建筑密度2", "YesNo/v1/总平构件建筑密度", "楼梯段间距审查 ", "楼梯段退让审查2" })
                Equal(null, (string)method.Invoke(null, new object[] { value }));
        });
    }
}
