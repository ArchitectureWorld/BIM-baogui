using System;
using System.Linq;
using Hifc.Reference;

internal static class Program
{
    private static int failures;
    private static int tests;
    private static void Check(string name, Action test)
    {
        tests++;
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Equal(object expected, object actual)
    {
        if (!object.Equals(expected, actual)) throw new Exception("expected=" + expected + "; actual=" + actual);
    }
    private static void Throws(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("expected InvalidOperationException");
    }
    private static ParameterRead Read(string origin, bool set, object value)
    { return new ParameterRead { Name = "投影面积", Origin = origin, HasValue = set, Value = value }; }
    private static ReferenceProperty Prop(string pset)
    { return new ReferenceProperty { PropertySet = pset, Binding = "实例", Property = "投影面积", RawType = "Real" }; }
    public static int Main()
    {
        var catalog = ReferenceCatalog.FromEmbeddedBaseline();
        Check("complete_280_reference_rows", () => Equal(280, catalog.Properties.Length));
        Check("preserve_205_source_names", () => Equal(205, catalog.Properties.Select(p => p.SourceParameter).Distinct().Count()));
        Check("legacy_slot_selects_only_net_land", () => Equal(8, catalog.ForSlot("规划净用地").Length));
        Check("canonical_slot_selects_same_definition", () => Equal(8, catalog.ForSlot("Pset_规划净用地信息属性集").Length));
        Check("empty_slot_blocks", () => Throws(() => catalog.ForSlot("")));
        Check("unknown_slot_blocks", () => Throws(() => catalog.ForSlot("不存在的要素")));
        Check("bad_contract_blocks", () => Throws(() => ReferenceCatalog.Parse("{}")));
        Check("uuid5_source_matches_python", () => Equal("01e9d21a-bd61-5fd5-bc9d-d7a50097806d", ReferencePolicy.SourceGuid("类目").ToString("D")));
        Check("uuid5_bridge_matches_python", () => Equal("157ec3bb-800b-5c8a-8b53-7eaaef0b0256", ReferencePolicy.BridgeGuid(Prop("Pset_阳台信息属性集")).ToString("D")));
        Check("same_property_different_psets_have_separate_bridges", () =>
        {
            if (ReferencePolicy.BridgeName(Prop("Pset_阳台信息属性集")) == ReferencePolicy.BridgeName(Prop("Pset_规划净用地信息属性集")))
                throw new Exception("property-set routing collapsed");
        });
        Check("instance_value_precedes_type", () => Equal(12.5, ReferencePolicy.Resolve(new[] { Read("instance", true, 12.5), Read("type", true, 99.0) }, "投影面积")?.Value));
        Check("unset_instance_uses_type", () => Equal(99.0, ReferencePolicy.Resolve(new[] { Read("instance", false, null), Read("type", true, 99.0) }, "投影面积")?.Value));
        Check("zero_is_not_missing", () => Equal(0, ReferencePolicy.Resolve(new[] { Read("instance", true, 0), Read("type", true, 99) }, "投影面积")?.Value));
        Check("false_is_not_missing", () => Equal(false, ReferencePolicy.Resolve(new[] { Read("instance", true, false) }, "投影面积")?.Value));
        Check("duplicate_source_names_block", () => Throws(() => ReferencePolicy.Resolve(new[] { Read("instance", true, 1), Read("instance", true, 2) }, "投影面积")));
        Check("boolean_bridge_uses_numeric_encoding", () => Equal("0", ReferencePolicy.ToBridgeValue(Read("instance", true, false))));
        Check("number_bridge_uses_invariant_precision", () => Equal("12.5", ReferencePolicy.ToBridgeValue(Read("instance", true, 12.5))));
        Check("generated_tsv_uses_property_scoped_bridge", () =>
        {
            var text = catalog.BuildExportConfiguration();
            if (!text.Contains("\t" + ReferencePolicy.BridgeName(Prop("Pset_阳台信息属性集")))) throw new Exception("bridge mapping absent");
            if (text.Contains("IFC参数(实例)") || text.Contains("IFC参数(类型)")) throw new Exception("native carrier recreated");
            if (!text.Contains("\tReall\t")) throw new Exception("raw reference type silently corrected");
        });
        ModelChecksTests.Run(Check);
        CaseWorkflowTests.Run(Check);
        RecognitionTests.Run(Check);
        AutoFillTests.Run(Check);
        ControlLineTests.Run(Check);
        ExportNamingTests.Run(Check);
        PlatformSyncHandoffTests.Run(Check);
        ManagerImprovementsTests.Run(Check);
        UndergroundTests.Run(Check);
        SiteClassificationTests.Run(Check);
        ProjectExportTests.Run(Check);
        BuildingCaseTests.Run(Check);
        PlanningInputTests.Run(Check);
        FloorAreaTests.Run(Check);
        InputReviewTests.Run(Check);
        PlatformWorkflowTests.Run(Check);
        SummaryCacheTests.Run(Check);
        PlatformRuleTests.Run(Check);
        PlatformExportTests.Run(Check);
        Console.WriteLine("RESULT failures=" + failures + "; tests=" + tests);
        return failures == 0 ? 0 : 1;
    }
}
