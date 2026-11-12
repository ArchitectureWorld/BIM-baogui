using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Hifc.Reference;

internal static class SiteStatisticsWorkbookTests
{
    internal static void Run()
    {
        Program.Check("site_statistics_workbook_keeps_numeric_rates_scope_and_applicability", () => {
            var report = new SiteStatisticsReport {
                DocumentTitle = "总图", Sources = new[] { new SiteStatisticsSource { Key = "host", Name = "总图", IsHost = true, Selected = true } },
                Rows = new[] { new SiteStatisticsRow { Kind = "绿地", Name = "附属绿地", DeclaredArea = 100.555m, GeometricArea = 100m, ConvertedAreaApplicable = true, DeclaredConvertedArea = 50.2775m } },
                Metrics = new[] { new SiteStatisticsMetric { Name = "绿地率", Unit = "%", DeclaredValue = 12.3456m, GeometricValue = 12m },
                    new SiteStatisticsMetric { Name = "机动车位数", Unit = "个", DeclaredValue = 0, GeometricApplicable = false } }
            };
            var path = Path.Combine(Program.Output,"site-statistics.xlsx");
            SiteStatisticsWorkbook.Write(path,report);
            using (var zip = ZipFile.OpenRead(path)) {
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var metric = XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml").Open());
                Program.Assert(metric.Descendants(ns+"v").Any(v=>v.Value=="12.3456"),"Rate rounded in workbook");
                Program.Assert(metric.Descendants(ns+"c").Single(c=>(string)c.Attribute("r")=="C3").Value=="—","Parking metric fabricated missing geometric count");
                var grouped = XDocument.Load(zip.GetEntry("xl/worksheets/sheet2.xml").Open());
                Program.Assert(grouped.Descendants(ns+"v").Any(v=>v.Value=="50.2775"),"Weighted precision lost");
                Program.Assert(grouped.ToString().Contains("—"),"Unrelated counts rendered as missing values");
                Program.Assert(zip.GetEntry("xl/worksheets/sheet5.xml")!=null,"Source scope sheet absent");
            }
        });
    }
}
