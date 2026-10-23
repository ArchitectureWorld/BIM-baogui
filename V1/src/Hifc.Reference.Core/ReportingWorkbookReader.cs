using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace Hifc.Reference
{
    public static class ReportingWorkbookReader
    {
        static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const long MaxPartBytes = 16 * 1024 * 1024;
        sealed class Cell
        {
            internal string Address, Value, Raw, Type, Format, Formula, Issue;
            internal int Row, Column;
            internal bool Cached;
        }
        sealed class Sheet
        {
            internal string Name;
            internal Dictionary<string, Cell> Cells = new Dictionary<string, Cell>(StringComparer.Ordinal);
            internal List<Tuple<int, int, int, int>> Merges = new List<Tuple<int, int, int, int>>();
            internal int LastRow;
            internal Cell At(int row, int column, bool merged = false)
            {
                var address = Address(row, column);
                Cell cell;
                if (merged)
                {
                    var range = Merges.FirstOrDefault(m => row >= m.Item1 && row <= m.Item3 && column >= m.Item2 && column <= m.Item4);
                    if (range != null && Cells.TryGetValue(Address(range.Item1, range.Item2), out cell)) return cell;
                }
                if (Cells.TryGetValue(address, out cell)) return cell;
                return new Cell { Address = address, Row = row, Column = column, Value = "", Raw = "", Type = "n", Format = "", Formula = "", Issue = "" };
            }
        }
        public static ReportingWorkbookDocument Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !string.Equals(System.IO.Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择 .xlsx 报规表；不运行宏或公式。");
            var full = System.IO.Path.GetFullPath(path);
            byte[] bytes;
            using (var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (file.Length > 32 * 1024 * 1024) throw new InvalidOperationException("报规表超过 32 MiB，请拆分后导入。");
                using (var buffer = new MemoryStream()) { file.CopyTo(buffer); bytes = buffer.ToArray(); }
            }
            string sha;
            using (var hash = SHA256.Create()) sha = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            try
            {
                using (var buffer = new MemoryStream(bytes)) using (var zip = new ZipArchive(buffer, ZipArchiveMode.Read))
                {
                    if (zip.Entries.Count > 4096 || zip.Entries.Sum(e => e.Length) > 128 * 1024 * 1024 || zip.Entries.Any(e => e.Length > MaxPartBytes))
                        throw new InvalidOperationException("报规表解压内容过大，请拆分后导入。");
                    if (zip.Entries.GroupBy(e => e.FullName, StringComparer.Ordinal).Any(g => g.Count() > 1))
                        throw new InvalidOperationException("报规表包含重复 ZIP 部件，无法可靠读取。");
                    var strings = Optional(zip, "xl/sharedStrings.xml")?.Root?.Elements(Main + "si")
                        .Select(e => string.Concat(e.Descendants(Main + "t").Select(t => t.Value))).ToArray() ?? new string[0];
                    var formats = ReadFormats(Optional(zip, "xl/styles.xml"));
                    var workbook = Load(zip, "xl/workbook.xml");
                    var rels = Load(zip, "xl/_rels/workbook.xml.rels").Root.Elements().ToArray();
                    var tables = new List<ReportingWorkbookTable>();
                    foreach (var info in workbook.Descendants(Main + "sheet"))
                    {
                        var name = (string)info.Attribute("name") ?? "未命名";
                        var id = (string)info.Attribute(Relations + "id");
                        var matches = rels.Where(r => (string)r.Attribute("Id") == id).ToArray();
                        if (matches.Length != 1 || (string)matches[0].Attribute("TargetMode") == "External" ||
                            !((string)matches[0].Attribute("Type") ?? "").EndsWith("/worksheet", StringComparison.Ordinal))
                            throw new InvalidOperationException("工作表引用无效或为外部引用：" + name);
                        var target = Resolve((string)matches[0].Attribute("Target"));
                        var sheet = ParseSheet(name, Load(zip, target), strings, formats);
                        tables.AddRange(ReadTables(sheet, full, sha));
                    }
                    if (tables.Count == 0) throw new InvalidOperationException("未找到属性表头。请使用“楼层 / 属性名称 / 值 / 单位 / 备注”模板，或原单体与总图双列表。");
                    return new ReportingWorkbookDocument { Path = full, Sha256 = sha, Tables = tables.ToArray() };
                }
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex) when (ex is XmlException || ex is InvalidDataException || ex is FormatException || ex is OverflowException)
            { throw new InvalidOperationException("报规表结构无效，未导入任何属性：" + ex.Message, ex); }
        }
        static XDocument Optional(ZipArchive zip, string path) => zip.GetEntry(path) == null ? null : Load(zip, path);
        static XDocument Load(ZipArchive zip, string path)
        {
            var entry = zip.GetEntry(path);
            if (entry == null) throw new InvalidOperationException("报规表缺少部件：" + path);
            using (var stream = entry.Open()) using (var reader = XmlReader.Create(stream, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxPartBytes }))
                return XDocument.Load(reader, LoadOptions.None);
        }
        static string Resolve(string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target.Contains(":") || target.Contains("\\")) throw new InvalidOperationException("工作表路径无效。");
            var parts = (target.StartsWith("/", StringComparison.Ordinal) ? target.TrimStart('/') : "xl/" + target).Split('/');
            var safe = new List<string>();
            foreach (var part in parts)
            {
                if (part == "" || part == ".") continue;
                if (part == "..") { if (safe.Count == 0) throw new InvalidOperationException("工作表路径越界。"); safe.RemoveAt(safe.Count - 1); }
                else safe.Add(part);
            }
            if (safe.Count < 2 || safe[0] != "xl") throw new InvalidOperationException("工作表路径越界。");
            return string.Join("/", safe);
        }
        static string[] ReadFormats(XDocument styles)
        {
            if (styles == null) return new string[0];
            var custom = styles.Descendants(Main + "numFmt").ToDictionary(n => (string)n.Attribute("numFmtId"), n => (string)n.Attribute("formatCode") ?? "");
            return styles.Descendants(Main + "cellXfs").Elements(Main + "xf").Select(n => {
                var id = (string)n.Attribute("numFmtId") ?? "0";
                if (custom.TryGetValue(id, out var format)) return format;
                return id == "9" ? "0%" : id == "10" ? "0.00%" : int.TryParse(id, out var number) && number >= 14 && number <= 22 ? "date/time" : "";
            }).ToArray();
        }
        static Sheet ParseSheet(string name, XDocument xml, string[] strings, string[] formats)
        {
            var result = new Sheet { Name = name };
            foreach (var node in xml.Descendants(Main + "sheetData").Descendants(Main + "c"))
            {
                var address = (string)node.Attribute("r"); var position = Position(address);
                var type = (string)node.Attribute("t") ?? "n"; var raw = node.Element(Main + "v")?.Value ?? "";
                var formula = node.Element(Main + "f");
                var value = type == "inlineStr" ? string.Concat(node.Element(Main + "is")?.Descendants(Main + "t").Select(t => t.Value) ?? new string[0]) : raw;
                if (type == "s")
                {
                    if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= strings.Length)
                        throw new InvalidOperationException("共享文本引用无效：" + name + "!" + address);
                    value = strings[index];
                }
                var issue = type == "e" ? "Excel 错误值，保留资料；请修正源表。" : type == "d" ? "日期值不作为业务数值导入。" : "";
                if (formula != null && (string.IsNullOrWhiteSpace(raw) || type != "n" && type != "b" || !decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                    issue = "公式没有有效数值缓存；请在 Excel 重算并保存，不推算公式。";
                if (result.Cells.ContainsKey(address)) throw new InvalidOperationException("重复单元格：" + name + "!" + address);
                result.Cells.Add(address, new Cell { Address = address, Row = position.Item1, Column = position.Item2, Value = value,
                    Raw = type == "inlineStr" || type == "s" ? value : raw, Type = type,
                    Format = int.TryParse((string)node.Attribute("s"), out var style) && style >= 0 && style < formats.Length ? formats[style] : "",
                    Formula = formula?.Value ?? "", Cached = node.Element(Main + "v") != null && !string.IsNullOrWhiteSpace(raw), Issue = issue });
                result.LastRow = Math.Max(result.LastRow, position.Item1);
                if (result.Cells.Count > 100000) throw new InvalidOperationException("单表单元格过多，请按楼栋拆分。");
            }
            foreach (var merge in xml.Descendants(Main + "mergeCell"))
            {
                var range = ((string)merge.Attribute("ref") ?? "").Split(':');
                if (range.Length != 2) throw new InvalidOperationException("合并单元格范围无效。");
                var a = Position(range[0]); var b = Position(range[1]);
                if (a.Item1 > b.Item1 || a.Item2 > b.Item2) throw new InvalidOperationException("合并单元格范围无效。");
                result.Merges.Add(Tuple.Create(a.Item1, a.Item2, b.Item1, b.Item2));
            }
            return result;
        }
        static Tuple<int, int> Position(string address)
        {
            if (string.IsNullOrEmpty(address)) throw new InvalidOperationException("单元格地址缺失。");
            int column = 0, index = 0;
            while (index < address.Length && address[index] >= 'A' && address[index] <= 'Z') { column = checked(column * 26 + address[index] - 'A' + 1); index++; }
            if (column < 1 || column > 16384 || !int.TryParse(address.Substring(index), NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row < 1 || row > 1048576)
                throw new InvalidOperationException("单元格地址无效：" + address);
            return Tuple.Create(row, column);
        }
        static string Address(int row, int column)
        {
            string letters = "";
            for (int c = column; c > 0; c = (c - 1) / 26) letters = (char)('A' + (c - 1) % 26) + letters;
            return letters + row.ToString(CultureInfo.InvariantCulture);
        }
        static IEnumerable<ReportingWorkbookTable> ReadTables(Sheet sheet, string path, string sha)
        {
            if (sheet.At(3, 1, true).Value.Trim() == "单体建筑" && sheet.At(2, 4).Value.Trim() == "值")
            {
                yield return Legacy(sheet, path, sha, "单体建筑", 2, 4, 3);
                if (sheet.At(3, 6, true).Value.Trim() == "总图" && sheet.At(2, 9).Value.Trim() == "值") yield return Legacy(sheet, path, sha, "总图", 7, 9, 8);
                yield break;
            }
            var header = sheet.Cells.Values.Where(c => c.Row <= 20 && (c.Value.Trim() == "属性名称" || c.Value.Trim() == "属性"))
                .OrderBy(c => c.Row).FirstOrDefault(c => sheet.Cells.Values.Any(v => v.Row == c.Row && v.Value.Trim() == "值" && v.Column > c.Column));
            if (header == null) yield break;
            var headers = sheet.Cells.Values.Where(c => c.Row == header.Row).ToArray();
            var valueColumn = headers.First(c => c.Value.Trim() == "值" && c.Column > header.Column).Column;
            var level = headers.FirstOrDefault(c => c.Value.Trim() == "楼层" || c.Value.Trim() == "楼层名称")?.Column ?? 0;
            var unit = headers.FirstOrDefault(c => c.Value.Trim() == "单位")?.Column ?? 0;
            var note = headers.FirstOrDefault(c => c.Value.Trim() == "备注")?.Column ?? 0;
            string kind = Metadata(sheet, header.Row, "模型类型");
            if (kind != "单体建筑" && kind != "总图") kind = sheet.Name.Contains("总图") ? "总图" : "单体建筑";
            var table = new ReportingWorkbookTable { Id = sha + ":" + sheet.Name + ":" + kind, SheetName = sheet.Name, Kind = kind, BuildingName = Metadata(sheet, header.Row, "建筑名称") };
            var items = new List<ReportingWorkbookItem>();
            foreach (var row in sheet.Cells.Values.Where(c => c.Row > header.Row && c.Column == header.Column && !string.IsNullOrWhiteSpace(c.Value)).OrderBy(c => c.Row))
                items.Add(Item(sheet, row, sheet.At(row.Row, valueColumn), level > 0 ? sheet.At(row.Row, level) : null,
                    unit > 0 ? sheet.At(row.Row, unit) : null, note > 0 ? sheet.At(row.Row, note) : null, path, sha));
            table.Items = items.ToArray();
            if (string.IsNullOrWhiteSpace(table.BuildingName)) table.BuildingName = items.FirstOrDefault(i => i.Field == "建筑名称")?.Value ?? "";
            Finish(table); yield return table;
        }
        static string Metadata(Sheet sheet, int before, string field)
        {
            var found = sheet.Cells.Values.Where(c => c.Row < before && c.Value.Trim() == field).OrderBy(c => c.Row).FirstOrDefault();
            return found == null ? "" : sheet.At(found.Row, found.Column + 1).Value.Trim();
        }
        static ReportingWorkbookTable Legacy(Sheet sheet, string path, string sha, string kind, int labelColumn, int valueColumn, int levelColumn)
        {
            var table = new ReportingWorkbookTable { Id = sha + ":" + sheet.Name + ":" + kind, SheetName = sheet.Name, Kind = kind };
            var items = new List<ReportingWorkbookItem>();
            for (int row = 3; row <= sheet.LastRow; row++)
            {
                var label = sheet.At(row, labelColumn, true);
                if (string.IsNullOrWhiteSpace(label.Value)) continue;
                var floor = label.Value.Trim() == "各楼层面积" ? sheet.At(row, levelColumn) : null;
                if (floor != null && string.IsNullOrWhiteSpace(floor.Value)) continue;
                var item = Item(sheet, label, sheet.At(row, valueColumn), floor, null, null, path, sha);
                if (floor != null) { item.Field = "楼层面积"; item.Unit = "m²"; }
                items.Add(item);
            }
            table.Items = items.ToArray(); table.BuildingName = items.FirstOrDefault(i => i.Field == "建筑名称")?.Value ?? "";
            Finish(table); return table;
        }
        static ReportingWorkbookItem Item(Sheet sheet, Cell label, Cell value, Cell level, Cell unit, Cell note, string path, string sha)
            => new ReportingWorkbookItem { Field = label.Value.Trim(), Value = value.Value.Trim(), LevelName = level?.Value.Trim() ?? "", Unit = unit?.Value.Trim() ?? "", Note = note?.Value ?? "", Issue = value.Issue,
                Provenance = new ReportingWorkbookProvenance { Path = path, Sha256 = sha, SheetName = sheet.Name, LabelCell = label.Address, ValueCell = value.Address,
                    LevelCell = level?.Address, UnitCell = unit?.Address, NoteCell = note?.Address, RawValue = value.Raw, CellType = value.Type,
                    NumberFormat = value.Format, Formula = value.Formula, HasCachedValue = value.Cached } };
        static void Finish(ReportingWorkbookTable table)
        {
            var issues = new List<string>();
            foreach (var item in table.Items.Where(i => i.Value.Contains("/") || i.Value.Contains("／")))
            {
                item.Issue = Join(item.Issue, "组合值含斜杠，不能自动拆分；请按一栋一表确认源值。");
                if (new[] { "建筑名称", "建筑类型名称", "建筑编号", "建筑面积" }.Contains(item.Field)) table.AmbiguousBuilding = true;
            }
            var declaredNames = table.Items.Where(i => i.Field == "建筑名称").Select(i => i.Value).Concat(new[] { table.BuildingName })
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToArray();
            if (table.Kind == "单体建筑" && declaredNames.Length > 1) table.AmbiguousBuilding = true;
            if (table.AmbiguousBuilding) issues.Add("该表含多栋组合信息或楼栋名称不一致；其他值也需逐项确认适用楼栋，默认不勾选。");
            foreach (var group in table.Items.GroupBy(i => i.Field + "|" + i.LevelName).Where(g => g.Count() > 1))
                issues.Add("重复属性：" + group.First().Field + (group.First().LevelName == "" ? "" : " / " + group.First().LevelName) + "；请选择一个有依据的来源。");
            if (table.Kind == "总图") AuditMetrics(table, issues);
            table.Issues = issues.ToArray();
        }
        static decimal? Metric(ReportingWorkbookTable table, string field, bool ratio = false)
        {
            var items = table.Items.Where(i => i.Field == field).ToArray();
            if (items.Length != 1 || !string.IsNullOrWhiteSpace(items[0].Issue) || !decimal.TryParse(items[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return null;
            if (ratio && ((items[0].Unit ?? "") == "%" || (items[0].Unit ?? "") == "％") && !(items[0].Provenance?.NumberFormat ?? "").Contains("%")) number /= 100;
            if (ratio && (number < 0 || number > 1)) return null;
            return number;
        }
        static void AuditMetrics(ReportingWorkbookTable table, List<string> issues)
        {
            var land = Metric(table, "规划净用地面积");
            if (!land.HasValue || land.Value <= 0) return;
            AuditRatio(table, issues, "建筑密度", Metric(table, "建筑物占地面积"), land.Value, true, "建筑物占地面积");
            AuditRatio(table, issues, "绿地率", Metric(table, "绿地总面积"), land.Value, true, "绿地总面积");
            var above = Metric(table, "地上计容面积"); var below = Metric(table, "地下计容面积");
            if (above.HasValue || below.HasValue)
                AuditRatio(table, issues, "容积率", (above ?? 0) + (below ?? 0), land.Value, false, "已填写计容面积（未填地下或其他面积口径仍需核对）");
        }
        static void AuditRatio(ReportingWorkbookTable table, List<string> issues, string field, decimal? area, decimal land, bool percent, string basis)
        {
            var current = Metric(table, field, percent);
            if (!area.HasValue || !current.HasValue) return;
            var calculated = area.Value / land;
            if (Math.Abs(calculated - current.Value) > .001m)
                issues.Add(field + "待核对：表内值 " + current.Value.ToString("0.####", CultureInfo.InvariantCulture) + " 与“" + basis + " / 规划净用地面积” "
                    + calculated.ToString("0.####", CultureInfo.InvariantCulture) + " 不一致；只提示，不替换源值。");
        }
        internal static string Join(string a, string b) => string.IsNullOrWhiteSpace(a) ? b ?? "" : string.IsNullOrWhiteSpace(b) ? a : a + "；" + b;
    }
}
