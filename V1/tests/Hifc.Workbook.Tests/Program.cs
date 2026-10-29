using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Hifc.Reference;

internal static class Program
{
    static int tests, failures;
    internal static string Output;
    internal static void Check(string name, Action test) { tests++; try { test(); Console.WriteLine("PASS " + name); } catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
    internal static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", name));
    internal static string Package(string name, params string[] sheets)
    {
        var path = Path.Combine(Output, name + ".xlsx");
        using (var file = File.Create(path)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Add(zip, "xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets>" + string.Concat(sheets.Select((s, i) => "<sheet name='楼栋" + (i + 1) + "' sheetId='" + (i + 1) + "' r:id='rId" + (i + 1) + "'/>")) + "</sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>" + string.Concat(sheets.Select((s, i) => "<Relationship Id='rId" + (i + 1) + "' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet" + (i + 1) + ".xml'/>")) + "</Relationships>");
            Add(zip, "xl/sharedStrings.xml", "<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><r><t>综合</t></r><r><t>楼</t></r></si></sst>");
            for (int i = 0; i < sheets.Length; i++) Add(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", sheets[i]);
        }
        return path;
    }
    internal static void Add(ZipArchive zip, string name, string xml) { using (var text = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) text.Write(xml); }
    [STAThread]
    static int Main(string[] args)
    {
        Output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "hifc-workbook-tests")); Directory.CreateDirectory(Output);
        Check("reader_preserves_table_scope_cells_and_formula_cache", () => {
            var path = Package("normalized", Fixture("normalized.xml")); var book = ReportingWorkbookReader.Read(path);
            Assert(book.Tables.Length == 1, "one building sheet must produce one table"); var table = book.Tables.Single();
            Assert(table.Kind == "单体建筑" && table.BuildingName == "综合楼", "scope/shared rich strings lost");
            var area = table.Items.Single(i => i.Field == "建筑面积");
            Assert(area.Value == "60" && area.Unit == "m²" && area.Provenance.ValueCell == "C7" && area.Provenance.LabelCell == "B7", "value/unit/cell provenance lost");
            Assert(area.Provenance.Formula == "30*2" && area.Provenance.HasCachedValue && area.Provenance.Sha256.Length == 64, "formula cache/hash provenance lost");
            Assert(table.Items.Single(i => i.Field == "建筑体积").Issue.Length > 0, "uncached formula must require review");
            Assert(table.Items.Single(i => i.Field == "民用建筑").Value == "0", "valid false was erased");
            Assert(table.Items.Single(i => i.Field == "楼层面积").LevelName == "一层", "floor identity lost");
        });
        PlannerTests.Run();
        BoundaryTests.Run();
        UnitTests.Run();
        WindowTests.Run();
        var sample=Array.IndexOf(args,"--sample");if(sample>=0&&sample+1<args.Length)SampleTests.Run(args[sample+1]);
        Console.WriteLine(tests + " tests, " + failures + " failures"); return failures == 0 ? 0 : 1;
    }
}
