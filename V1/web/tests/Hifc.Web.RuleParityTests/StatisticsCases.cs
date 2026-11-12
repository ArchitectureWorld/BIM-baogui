using Hifc.Reference;
using Hifc.Web.Contracts;

internal static partial class Program
{
    static void FillRequired(Fixture fixture)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            var fields = fixture.Check().Fields.Where(f => (!f.AllowedEmpty || f.ManualInputRequired) && string.IsNullOrWhiteSpace(f.Value)).ToArray();
            if (fields.Length == 0) return;
            foreach (var field in fields)
            {
                var value = field.Options.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                    ?? (field.RawType == "Boolean" ? "0" : field.RawType == "Real" || field.RawType == "Integer" ? "1" : "测试");
                if (field.RawType == "Boolean") value = BooleanPolicy.Normalize(value);
                var parameter = fixture.Set(field.OwnerUniqueId, field.Source, value,
                    field.RawType == "Boolean" ? "YesNo" : "Text", field.RawType == "Boolean" ? "Integer" : "String");
                if (field.RawType == "Boolean") parameter.SharedGuid = BooleanPolicy.SourceGuid(field.Source).ToString("D");
            }
        }
    }
    static byte[] LegacyMultiWorkbook()
    {
        var path = Path.Combine(Path.GetTempPath(), "hifc-web-multi-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try { ReportingWorkbookTemplate.Write(path, new[] { "A", "B" }, false); return File.ReadAllBytes(path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    static void SetStatisticsField(Fixture fixture, string element, string property, string value)
    {
        var field = fixture.Check().Fields.First(f => f.ElementUniqueId == element && f.Property == property);
        fixture.Set(field.OwnerUniqueId, field.Source, value);
    }

    static StatisticsRequest Statistics(Fixture fixture, string kind)
    {
        return new StatisticsRequest { RuleVersion = fixture.Engine.RuleVersion, Kind = kind, DocumentKey = "document", DocumentTitle = "统计测试",
            SourcePath = @"D:\模型\统计测试.rvt", Sources = new[] { new StatisticsSourceCapture {
                Source = new SiteStatisticsSource { Key = "document", Name = "统计测试", IsHost = true, Selected = true, Path = @"D:\模型\统计测试.rvt" },
                PlacementIdentity = "host-identity", Settings = fixture.Inventory[0].Settings,
                Elements = fixture.Inventory.SelectMany(p => p.Elements).ToArray(), Values = fixture.Values
            } } };
    }

    static void MeasureStatistics(Fixture fixture, StatisticsRequest request)
    {
        var plan = fixture.Engine.PlanStatisticsGeometry(request);
        request.CaptureHash = plan.CaptureHash;
        request.Geometry = plan.Groups.Select(g => new StatisticsGeometryResult { Key = g.Key, Verified = true,
            UnionArea = g.Members.Sum(m => m.GeometricArea ?? 0), ComponentAreaSum = g.Members.Sum(m => m.GeometricArea ?? 0), OverlapArea = 0 }).ToArray();
    }

    static Fixture BuildingStatisticsFixture()
    {
        var level = Element("level", 1, "IfcBuildingStorey"); level.LevelElevationM = 0; level.Name = "1F";
        var floor = Element("area", 2, "IfcSlab", "建筑区域", "level", 20); floor.LevelElevationM = 0; floor.Level = "1F";
        var fixture = new Fixture(new[] { Element("building", 3, "IfcBuilding"), level, floor }, kind: "单体建筑");
        SetStatisticsField(fixture, "area", "投影面积", "20.36");
        SetStatisticsField(fixture, "area", "计算系数", "0.5");
        SetStatisticsField(fixture, "area", "计容系数", "0.5");
        return fixture;
    }

    static void StatisticsCases()
    {
        Test("web_site_metrics_use_the_category_union_and_preserve_overlap", () => {
            var fixture = new Fixture(new[] { Element("green-a", 1, "IfcSlab", "绿地", area: 100),
                Element("green-b", 2, "IfcSlab", "绿地", area: 100), Element("land", 3, "IfcSlab", "规划净用地", area: 1000) });
            foreach (var id in new[] { "green-a", "green-b" }) {
                SetStatisticsField(fixture, id, "投影面积", "100"); SetStatisticsField(fixture, id, "折算系数", "1");
                var field = fixture.Check().Fields.First(f => f.ElementUniqueId == id && f.Property == "绿地类型");
                SetStatisticsField(fixture, id, "绿地类型", field.Options.First());
            }
            SetStatisticsField(fixture, "land", "投影面积", "1000");
            var request = Statistics(fixture, "site"); MeasureStatistics(fixture, request);
            foreach (var geometry in request.Geometry.Where(g => g.Key.StartsWith("绿地|"))) { geometry.UnionArea = 180; geometry.OverlapArea = 20; }
            var report = fixture.Engine.EvaluateStatistics(request).Site;
            Assert(report.Metrics.Single(m => m.Name == "折算绿地面积").GeometricValue == 180, "Category metric fell back to the overlapping component sum.");
            Assert(report.Metrics.Single(m => m.Name == "绿地率").GeometricValue == 18, "Green ratio ignored its verified category union.");
        });
        Test("web_building_pending_retains_unclassified_raw_area_sources", () => {
            var fixture = new Fixture(new[] { Element("pending", 1, "IfcSlab", area: 20) }, kind: "单体建筑");
            fixture.Set("pending", "建筑区域投影面积", "20");
            var request = Statistics(fixture, "building"); MeasureStatistics(fixture, request);
            var report = fixture.Engine.EvaluateStatistics(request).Building;
            Assert(report.Pending.Any(c => c.UniqueId == "pending") && report.Components.Length == 0, "Unclassified saved area disappeared from pending items or was counted.");
        });
        Test("web_site_pending_retains_unclassified_raw_site_sources", () => {
            var fixture = new Fixture(new[] { Element("pending", 1, "IfcSlab", area: 20) });
            fixture.Set("pending", "绿地投影面积", "20");
            var request = Statistics(fixture, "site"); MeasureStatistics(fixture, request);
            var report = fixture.Engine.EvaluateStatistics(request).Site;
            Assert(report.Pending.Any(c => c.UniqueId == "pending") && report.Components.Length == 0, "Unclassified site source disappeared from pending items or was counted.");
        });
        Test("web_building_statistics_uses_saved_values_and_independent_coefficients", () => {
            var fixture = BuildingStatisticsFixture(); var request = Statistics(fixture, "building");
            MeasureStatistics(fixture, request); var report = fixture.Engine.EvaluateStatistics(request).Building;
            Assert(report.Total.CalculatedArea == 10.18m && report.Total.FloorRatioArea == 10.18m, "Independent 0.5 coefficients were multiplied together or saved values were replaced.");
            Assert(report.Components.Single(c => c.Included).GeometricArea == 20m, "Native geometric area lost.");
            Assert(request.Sources[0].Values.Single(v => v.OwnerUniqueId == "area" && v.Source == "建筑区域投影面积").RawValue == "20.36", "Statistics changed source values.");
        });
        Test("web_statistics_rejects_stale_or_incomplete_geometry", () => {
            var fixture = BuildingStatisticsFixture(); var request = Statistics(fixture, "building"); MeasureStatistics(fixture, request);
            var original = request.CaptureHash; request.CaptureHash = new string('a', 64); Reject(() => fixture.Engine.EvaluateStatistics(request));
            request.CaptureHash = original; request.Geometry = []; Reject(() => fixture.Engine.EvaluateStatistics(request));
        });
        Test("web_statistics_captures_only_selected_unique_link_placements", () => {
            var fixture = new Fixture(); var request = Statistics(fixture, "site");
            var duplicate = new StatisticsSourceCapture { Source = new SiteStatisticsSource { Key = "duplicate", Name = "同位置链接", Selected = true },
                PlacementIdentity = request.Sources[0].PlacementIdentity, Settings = request.Sources[0].Settings, Elements = request.Sources[0].Elements, Values = request.Sources[0].Values };
            request.Sources = request.Sources.Append(duplicate).ToArray();
            Assert(fixture.Engine.PlanStatisticsCapture(request).Sources.Length == 1, "Duplicate placement was counted twice.");
        });
        Test("web_statistics_excel_is_built_from_same_verified_input", () => {
            var fixture = BuildingStatisticsFixture(); var request = Statistics(fixture, "building"); MeasureStatistics(fixture, request);
            var workbook = fixture.Engine.ExportStatisticsWorkbook(request);
            using var stream = new MemoryStream(Convert.FromBase64String(workbook.ContentBase64));
            using var archive = new System.IO.Compression.ZipArchive(stream);
            Assert(workbook.CaptureHash == request.CaptureHash && archive.Entries.Any(e => e.FullName.StartsWith("xl/worksheets/")), "Statistics workbook or provenance missing.");
        });
        Test("web_blank_workbook_contains_only_current_kind_and_actual_levels", () => {
            var fixture = BuildingStatisticsFixture();
            var path = Path.Combine(Path.GetTempPath(), "hifc-web-blank-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try {
                File.WriteAllBytes(path, fixture.Engine.CreateWorkbookTemplate(fixture.Check()));
                var table = ReportingWorkbookReader.Read(path).Tables.Single();
                Assert(table.Kind == "单体建筑" && table.Items.All(i => i.Value == ""), "Blank model workbook contains prefilled business values or another model kind.");
                Assert(table.Items.Any(i => i.Field == "楼层面积" && i.LevelName == "1F"), "Actual floor label missing.");
            } finally { if (File.Exists(path)) File.Delete(path); }
        });
        Test("web_export_proof_is_bound_to_plan_and_receipt", () => {
            var fixture = new Fixture(); FillRequired(fixture); var basis = fixture.Check();
            var plan = fixture.Engine.BuildExportPlan(basis, fixture.Values, Export(basis), DateTime.UtcNow);
            var original = Canonical.ExportHash(plan); Assert(plan.InputValidation.complete && plan.InputValidation.blocking_count == 0, "Complete input validation missing.");
            var output = Environment.GetEnvironmentVariable("HIFC_WEB_PROOF_OUTPUT");
            if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { proof = plan.InputValidation, identity = RequiredInputProof.Identity(plan.InputValidation) }));
            plan.InputValidation.blocking_count = 1; Assert(Canonical.ExportHash(plan) != original, "Input proof can be modified without changing the signature payload.");
        });
        Test("web_platform_control_line_preserves_name_and_requires_valid_enum", () => {
            var fixture = new Fixture(new[] { Element("control", 1, "IfcSlab", "规划控制线") });
            var basis = fixture.Check();
            Assert(basis.Fields.Where(f => f.Property == "名称" || f.Property == "红线类型").All(f => f.RequiredValue == null), "Platform labels were force-rewritten by legacy control policy.");
            var type = basis.Fields.Single(f => f.Property == "红线类型");
            Reject(() => fixture.Edit(basis, new[] { Draft(type, "任意类型") }, false));
        });
    }
}
