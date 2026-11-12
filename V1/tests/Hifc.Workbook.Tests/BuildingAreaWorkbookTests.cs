using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Hifc.Reference;

internal static class BuildingAreaWorkbookTests
{
    internal static void Run()
    {
        Program.Check("statistics_excel_preserves_numeric_precision_missing_zero_and_identity", () => {
            var report = BuildingAreaStatistics.Build(new BuildingAreaStatisticsInput {
                DocumentTitle = "1#", SourcePath = @"D:\模型\1号楼.rvt", CapturedUtc = "2026-11-12T06:00:00Z",
                Components = new[] { new BuildingAreaComponent {
                    ElementId = 123, UniqueId = "owner-123", LevelName = "1F 容积率0", LevelUniqueId = "level1", ElevationM = 0,
                    Included = true, Slot = "架空", Function = "架空停车", GeometricArea = 99.999m,
                    DeclaredProjectionArea = 100.555m, CalculationFactor = 1, FloorRatioFactor = 0
                }, new BuildingAreaComponent { ElementId = 124, UniqueId = "owner-124", Name = "=1+1", Included = false } }
            });
            var path = Path.Combine(Program.Output, "area-statistics.xlsx");
            BuildingAreaStatisticsWorkbook.Write(path, report);
            using (var zip = ZipFile.OpenRead(path)) {
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var book = XDocument.Load(zip.GetEntry("xl/workbook.xml").Open());
                Program.Assert(book.Descendants(ns + "sheet").Count() == 5, "Missing summary/details/pending/comparison/notes sheets");
                var summary = XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml").Open());
                Program.Assert(summary.Descendants(ns + "v").Any(v => v.Value == "100.555"), "Precision converted into display strings");
                Program.Assert(summary.Descendants(ns + "v").Any(v => v.Value == "0"), "Zero missing");
                Program.Assert(summary.Descendants(ns + "c").Single(c => (string)c.Attribute("r") == "C3").Value == "—", "Total height presented as missing data");
                var details = XDocument.Load(zip.GetEntry("xl/worksheets/sheet2.xml").Open());
                Program.Assert(details.ToString().Contains("owner-123") && details.ToString().Contains("1F 容积率0"), "Source ownership lost");
                var pending = XDocument.Load(zip.GetEntry("xl/worksheets/sheet3.xml").Open());
                Program.Assert(!pending.Descendants(ns + "f").Any() && pending.ToString().Contains("=1+1"), "Text interpreted as Excel formula");
                var comparisons = XDocument.Load(zip.GetEntry("xl/worksheets/sheet4.xml").Open());
                Program.Assert(comparisons.ToString().Contains("待补"), "Missing value became zero");
                Program.Assert(comparisons.Descendants(ns + "c").Single(c => (string)c.Attribute("r") == "C3").Value == "—", "Attribute-only footprint shown as missing calculation");
            }
            bool prevented = false;
            try { BuildingAreaStatisticsWorkbook.Write(path, report); } catch (IOException) { prevented = true; }
            Program.Assert(prevented, "Existing file overwritten without a unique destination");
        });
    }
}
