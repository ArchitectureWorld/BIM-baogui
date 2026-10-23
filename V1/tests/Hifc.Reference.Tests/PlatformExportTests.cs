using System;
using System.Linq;
using Hifc.Reference;

internal static class PlatformExportTests
{
    const string Profile = "hubei-420626-planning-20261020";
    const string GeneralSite = "Pset_总平构件信息属性集";

    static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    static ReferenceProperty[] Properties(string entity, string slot, bool evidence = false, string profile = Profile)
        => CaseExportPolicy.Properties(entity, slot, evidence, profile);
    static bool Evidence(string entity, string slot, string value, string profile = Profile)
        => CaseExportPolicy.HasGeneralSiteTypeEvidence(entity, slot, value, profile);
    static string Output(ReferenceProperty property, string value, string profile = Profile, string parent = null)
        => CaseExportPolicy.ValueForExport(property, value, profile, parent);
    static void Rejected(Action action, string reason)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception(reason);
    }
    static void OnlySet(ReferenceProperty[] properties, string expected)
    {
        Assert(properties.Length > 0, "business properties are missing");
        Assert(properties.All(p => p.PropertySet == expected), "unrelated property set was added");
    }
    public static void Run(Action<string, Action> check)
    {
        check("source_type_text_250_does_not_classify_an_unassigned_slab", () => {
            Assert(CaseExportPolicy.Properties("IfcSlab", null, true).Length == 0,
                "legacy nonempty type evidence still creates a general-site property set");
            Assert(Properties("IfcSlab", null, true).Length == 0,
                "platform mapping infers a business role from legacy type text");
        });
        check("classified_business_slabs_do_not_inherit_general_site_psets", () => {
            foreach (var slot in new[] { "建筑区域", "规划净用地", "绿地", "道路红线", "雨蓬" })
            {
                var properties = CaseExportPolicy.Properties("IfcSlab", slot, true);
                Assert(properties.Length > 0 && properties.All(p => p.PropertySet != GeneralSite),
                    slot + " inherited unrelated general-site properties");
            }
        });
        check("explicit_general_site_classification_retains_its_own_instance_fields", () => {
            foreach (var entity in new[] { "IfcSlab", "IfcBuildingElementProxy" })
            {
                var properties = Properties(entity, "总平构件", true);
                OnlySet(properties, GeneralSite);
                Assert(properties.Any(p => p.Property == "构件类型" && p.Binding == "实例"),
                    "explicit business classification was moved to a shared type");
                Assert(properties.Any(p => p.Property == "投影面积" && p.SourceParameter == "总平构件投影面积"),
                    "general-site geometry source was lost");
            }
        });
        check("general_site_type_evidence_requires_explicit_business_and_legal_value", () => {
            Assert(!Evidence("IfcSlab", "总平构件", "250"), "250 was accepted as a platform business enum");
            Assert(!Evidence("IfcSlab", "建筑区域", "风雨连廊"), "another business class was overridden by a type value");
            Assert(!Evidence("IfcSlab", null, "风雨连廊"), "a type value invented business classification");
            foreach (var value in new[] { "智能快件(信包)箱", "风雨连廊", "非机动车棚" })
                Assert(Evidence("IfcSlab", "Pset_总平构件信息属性集", value), "valid explicit general-site classification was rejected: " + value);
        });
        check("platform_business_properties_follow_only_the_selected_slot", () => {
            foreach (var slot in new[] { "建筑区域", "规划净用地", "绿地", "道路红线" })
                OnlySet(Properties("IfcSlab", slot, true), "Pset_" + slot + "信息属性集");
        });
        check("platform_windows_route_required_dimensions_on_each_instance", () => {
            var properties = Properties("IfcWindow", "窗");
            OnlySet(properties, "Pset_窗信息属性集");
            foreach (var name in new[] { "标高", "底高度", "高度", "宽度" })
                Assert(properties.Count(p => p.Property == name && p.Binding == "实例" && p.SourceParameter == name) == 1,
                    "window instance field is missing: " + name);
            var config = ReferenceCatalog.FromEmbeddedBaseline().BuildSelectedExportConfiguration(properties);
            Assert(config.Contains("Pset_窗信息属性集\tI\tIfcWindow\n") && !config.Contains("IfcWindowType"),
                "native mapping still routes confirmed windows to their shared types");
            Assert(Properties("IfcWindow", null).Length == 0, "unclassified windows acquired a business set");
        });
        check("platform_rain_canopy_uses_canonical_pset_and_preserves_existing_source_names", () => {
            foreach (var slot in new[] { "雨蓬", "雨篷", "Pset_雨蓬信息属性集", "Pset_雨篷信息属性集" })
            {
                var properties = Properties("IfcSlab", slot);
                OnlySet(properties, "Pset_雨篷信息属性集");
                Assert(properties.Length == 7 && properties.All(p => p.Binding == "实例"), "canopy field coverage changed");
                Assert(properties.Single(p => p.Property == "投影面积").SourceParameter == "雨蓬投影面积",
                    "existing canopy source name and its source GUID were replaced");
            }
        });
        check("indoor_car_park_is_an_independent_area_business_set", () => {
            var indoor = Properties("IfcSpace", "室内停车场");
            OnlySet(indoor, "Pset_室内停车场信息属性集");
            foreach (var name in new[] { "投影面积", "公建大类", "公建小类" })
                Assert(indoor.Any(p => p.Property == name && p.Binding == "实例" && p.SourceParameter == "室内停车场" + name),
                    "indoor car-park source missing or reused from a different class: " + name);
            Assert(Properties("IfcSlab", "室内停车场").Length == 0, "a physical slab was silently treated as an indoor parking Area");
            var outside = Properties("IfcSpace", "停车场");
            OnlySet(outside, "Pset_停车场信息属性集");
            Assert(!outside.Any(p => p.SourceParameter.StartsWith("室内停车场", StringComparison.Ordinal)),
                "class 205 was relabelled as indoor class 103");
        });
        check("platform_mapping_does_not_change_other_region_profiles", () => {
            foreach (var profile in new[] { (string)null, "other-region" })
            {
                var windows = Properties("IfcWindow", "窗", false, profile);
                Assert(windows.Length == 2 && windows.All(p => p.Binding == "类型"), "legacy window compatibility changed outside the selected profile");
                OnlySet(Properties("IfcSlab", "雨蓬", false, profile), "Pset_雨蓬信息属性集");
                Assert(Properties("IfcSlab", null, true, profile).Length == 0, "unclassified legacy slab still acquired a general-site set");
            }
        });
        check("platform_general_site_density_is_boolean_without_retyping_other_regions", () => {
            Assert(Properties("IfcSlab", "总平构件").Single(p => p.Property == "建筑密度").ExportType == "Boolean",
                "platform yes/no density field is exported as a real number");
            Assert(Properties("IfcSlab", "总平构件", false, "other-region").Single(p => p.Property == "建筑密度").ExportType == "Real",
                "project-specific field type was applied to another region");
        });
        check("runtime_source_catalog_covers_new_sources_without_old_rain_spelling", () => {
            var sources = CaseExportPolicy.SourceProperties(Profile);
            Assert(sources.Any(p => p.PropertySet == "Pset_室内停车场信息属性集" && p.SourceParameter == "室内停车场公建小类"),
                "scanning and writing cannot discover the indoor source parameter");
            Assert(sources.Any(p => p.PropertySet == "Pset_雨篷信息属性集" && p.SourceParameter == "雨蓬投影面积")
                && !sources.Any(p => p.PropertySet == "Pset_雨蓬信息属性集"), "runtime rain-canopy catalog has contradictory business identities");
            Assert(!CaseExportPolicy.SourceProperties("other-region").Any(p => p.PropertySet == "Pset_室内停车场信息属性集"),
                "another region unexpectedly acquired the project-specific class");
        });
        check("platform_export_rejects_legacy_250_without_substituting_a_default", () => {
            var type = Properties("IfcSlab", "总平构件").Single(p => p.Property == "构件类型");
            Rejected(() => Output(type, "250"), "legacy 250 was allowed into the general-site bridge");
            Assert(Output(type, null) == null && Output(type, "  ") == null, "missing classification acquired a default value");
            foreach (var value in new[] { "智能快件(信包)箱", "风雨连廊", "非机动车棚" })
                Assert(Output(type, value) == value, "a valid business classification was rewritten");
        });
        check("platform_export_boolean_normalization_does_not_coerce_real_density", () => {
            var density = Properties("IfcSlab", "总平构件").Single(p => p.Property == "建筑密度");
            Assert(Output(density, "是") == "1" && Output(density, "否") == "0", "platform boolean text was not converted to native IFC boolean encoding");
            Rejected(() => Output(density, "0.5"), "a real density became an arbitrary boolean");
            Assert(Output(density, "0.5", "other-region") == "0.5", "another region was subjected to this profile's boolean interpretation");
        });
        check("platform_export_rejects_function_mismatch_and_invalid_numeric_text", () => {
            var area = Properties("IfcSlab", "建筑区域");
            var child = area.Single(p => p.Property == "子功能类别");
            Assert(Output(child, "教室", Profile, "学校") == "教室", "a school classroom was rejected");
            Rejected(() => Output(child, "商铺", Profile, "学校"), "a mismatched child function was exported");
            Rejected(() => Output(area.Single(p => p.Property == "投影面积"), "not-a-number"), "invalid numeric text was exported as an IFC Real");
        });
    }
}
