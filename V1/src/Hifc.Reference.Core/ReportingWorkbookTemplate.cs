using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Hifc.Reference
{
    public static class ReportingWorkbookTemplate
    {
        static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        public static void Write(string path, IEnumerable<string> buildingNames, bool includeSite = true)
        {
            if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("模板文件应使用 .xlsx 扩展名。");
            var names = (buildingNames ?? new string[0]).Select(n => (n ?? "").Trim()).Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length == 0) names = new[] { "" };
            if (names.Length > 100) throw new InvalidOperationException("模板楼栋超过 100 栋，请分批导出。");
            var sheets = new List<Tuple<string, XDocument>>(); var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (includeSite) { used.Add("总图"); sheets.Add(Tuple.Create("总图", SiteSheet())); }
            foreach (var name in names) sheets.Add(Tuple.Create(SheetName(name, used), BuildingSheet(name)));
            // CreateNew also protects an original source file if a caller bypasses the save dialog.
            using (var file = new FileStream(Path.GetFullPath(path), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var types = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
                WritePart(zip, "[Content_Types].xml", new XDocument(new XElement(types + "Types",
                    new XElement(types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    ContentType(types, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                    ContentType(types, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"),
                    sheets.Select((s, i) => ContentType(types, "/xl/worksheets/sheet" + (i + 1) + ".xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
                WritePart(zip, "_rels/.rels", new XDocument(new XElement(PackageRel + "Relationships", Relationship("rId1", "officeDocument", "xl/workbook.xml"))));
                WritePart(zip, "xl/workbook.xml", new XDocument(new XElement(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", Rel),
                    new XElement(Main + "sheets", sheets.Select((s, i) => new XElement(Main + "sheet", new XAttribute("name", s.Item1), new XAttribute("sheetId", i + 1), new XAttribute(Rel + "id", "rId" + (i + 1))))))));
                WritePart(zip, "xl/_rels/workbook.xml.rels", new XDocument(new XElement(PackageRel + "Relationships",
                    sheets.Select((s, i) => Relationship("rId" + (i + 1), "worksheet", "worksheets/sheet" + (i + 1) + ".xml")), Relationship("styles", "styles", "styles.xml"))));
                WritePart(zip, "xl/styles.xml", Styles());
                for (int i = 0; i < sheets.Count; i++) WritePart(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", sheets[i].Item2);
            }
        }
        static XElement ContentType(XNamespace ns, string name, string type) => new XElement(ns + "Override", new XAttribute("PartName", name), new XAttribute("ContentType", type));
        static XElement Relationship(string id, string type, string target) => new XElement(PackageRel + "Relationship", new XAttribute("Id", id), new XAttribute("Type", Rel.NamespaceName + "/" + type), new XAttribute("Target", target));
        static void WritePart(ZipArchive zip, string name, XDocument xml)
        {
            using (var stream = zip.CreateEntry(name).Open()) using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) xml.Save(writer);
        }
        static string SheetName(string name, HashSet<string> used)
        {
            var text = new string((string.IsNullOrWhiteSpace(name) ? "单体建筑" : name).Where(c => !":\\/?*[]".Contains(c) && !char.IsControl(c)).ToArray()).Trim('\'');
            if (text.Length == 0) text = "单体建筑";
            if (text.Length > 31) text = text.Substring(0, 31);
            var candidate = text; int suffix = 1;
            while (!used.Add(candidate)) { var tail = "_" + (++suffix); candidate = text.Substring(0, Math.Min(text.Length, 31 - tail.Length)) + tail; }
            return candidate;
        }
        static XDocument BuildingSheet(string building)
        {
            var fields = ReferenceCatalog.FromEmbeddedBaseline().Properties.Where(p => !p.NativeGroup && p.Binding == "实例" && p.Entity == "IfcBuilding")
                .GroupBy(p => p.Property).Select(g => g.First()).ToArray();
            var rows = fields.Select(p => new[] { "", p.Property, p.Property == "建筑名称" ? building : "", Unit(p), "" }).ToList();
            rows.Add(new[] { "（填写实际楼层名称）", "楼层面积", "", "m²", "每个实际楼层一行；复制此行，不填写组合值。" });
            return Sheet("单体建筑", building, "一栋一张表；空白保留模型现值，已有值冲突需在导入预览中逐项选择。楼层名称须与模型一致。", rows);
        }
        static string Unit(ReferenceProperty property)
            => PropertyUnits.Canonical(property.Property,property.ExportType)??"";
        static XDocument SiteSheet()
        {
            var fields = new[] { "总建筑面积", "建筑密度", "容积率", "绿地总面积", "绿地率", "规划净用地面积", "规划总用地面积", "地上计容面积", "地上不计容面积", "地下计容面积", "地下不计容面积", "建筑物占地面积", "机动车位数", "非机动车位数", "建筑限高", "海拔限高" };
            return Sheet("总图", "", "项目总图一张表；汇总指标保留为原始资料，按现有模型属性逐项核对。百分比请填数值并保留 % 单位。",
                fields.Select(p => new[] { "", p, "", p == "建筑密度" || p == "绿地率" ? "%" : p.EndsWith("面积", StringComparison.Ordinal) ? "m²" : "", "" }).ToList());
        }
        static XDocument Sheet(string kind, string building, string instruction, IList<string[]> values)
        {
            var rows = new List<XElement> { Row(1, new[] { "HIFC 报规属性导入 v1" }, true), Row(2, new[] { "模型类型", kind }),
                Row(3, new[] { "建筑名称", building }), Row(4, new[] { instruction }), Row(5, new[] { "楼层", "属性名称", "值", "单位", "备注" }, true) };
            for (int i = 0; i < values.Count; i++) rows.Add(Row(i + 6, values[i]));
            return new XDocument(new XElement(Main + "worksheet",
                new XElement(Main + "sheetViews", new XElement(Main + "sheetView", new XAttribute("workbookViewId", 0),
                    new XElement(Main + "pane", new XAttribute("ySplit", 5), new XAttribute("topLeftCell", "A6"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                new XElement(Main + "cols", new[] { 23, 27, 26, 12, 66 }.Select((width, i) => new XElement(Main + "col", new XAttribute("min", i + 1), new XAttribute("max", i + 1), new XAttribute("width", width), new XAttribute("customWidth", 1)))),
                new XElement(Main + "sheetData", rows), new XElement(Main + "autoFilter", new XAttribute("ref", "A5:E" + (values.Count + 5))),
                new XElement(Main + "mergeCells", new XAttribute("count", 2), new XElement(Main + "mergeCell", new XAttribute("ref", "A1:E1")), new XElement(Main + "mergeCell", new XAttribute("ref", "A4:E4")))));
        }
        static XElement Row(int number, string[] values, bool header = false)
            => new XElement(Main + "row", new XAttribute("r", number), new XAttribute("ht", number == 4 ? 36 : 24), new XAttribute("customHeight", 1),
                values.Select((value, i) => new XElement(Main + "c", new XAttribute("r", ((char)('A' + i)).ToString() + number), new XAttribute("t", "inlineStr"), new XAttribute("s", header ? 1 : 0),
                    new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? "")))));
        static XDocument Styles() => XDocument.Parse("<styleSheet xmlns='" + Main.NamespaceName + "'><fonts count='2'><font><sz val='11'/><name val='Microsoft YaHei'/></font><font><b/><color rgb='FFFFFFFF'/><sz val='11'/><name val='Microsoft YaHei'/></font></fonts><fills count='3'><fill><patternFill patternType='none'/></fill><fill><patternFill patternType='gray125'/></fill><fill><patternFill patternType='solid'><fgColor rgb='FF245A70'/><bgColor indexed='64'/></patternFill></fill></fills><borders count='1'><border/></borders><cellStyleXfs count='1'><xf numFmtId='0' fontId='0' fillId='0' borderId='0'/></cellStyleXfs><cellXfs count='2'><xf numFmtId='0' fontId='0' fillId='0' borderId='0' xfId='0'><alignment vertical='center' wrapText='1'/></xf><xf numFmtId='0' fontId='1' fillId='2' borderId='0' xfId='0' applyFill='1' applyFont='1'><alignment vertical='center' wrapText='1'/></xf></cellXfs><cellStyles count='1'><cellStyle name='Normal' xfId='0' builtinId='0'/></cellStyles></styleSheet>");
    }
}
