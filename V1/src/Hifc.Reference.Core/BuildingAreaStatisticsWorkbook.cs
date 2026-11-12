using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Hifc.Reference
{
    public static class BuildingAreaStatisticsWorkbook
    {
        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        public static void Write(string path, BuildingAreaStatisticsReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("统计文件应使用 .xlsx 扩展名。", nameof(path));
            var floorRows = report.Floors.Concat(new[] { report.Total }).Select(f => new object[] {
                f.Name, f.IsTotal ? (object)"—" : f.ElevationM, f.IsTotal ? (object)"—" : f.HeightM,
                f.IsTotal ? "—" : f.FunctionSummary, f.DeclaredProjectionArea, f.GeometricArea,
                f.CalculatedArea, f.FloorRatioArea, f.NonFloorRatioArea, f.DifferenceArea, f.DifferencePercent,
                f.ExceedsTolerance ? "超过 " + report.TolerancePercent + "%" : "", f.IsTotal ? "—" : f.SourceLevels, f.Notes ?? ""
            }).ToArray();
            var componentHeaders = new[] { "构件ID", "唯一ID", "名称", "类目", "属性集", "来源标高", "标高(m)", "公建大类", "使用性质",
                "填报投影面积(㎡)", "实际几何面积(㎡)", "计算系数", "计容系数", "计算面积(㎡)", "计容面积(㎡)", "不计容面积(㎡)",
                "差额(㎡)", "差异率(%)", "超过阈值", "状态", "备注" };
            var comparisons = report.Comparisons.Select(c => new object[] { c.Name, c.AttributeValue,
                c.AttributeOnly ? (object)"—" : c.CalculatedValue, c.AttributeOnly ? (object)"—" : c.Difference,
                c.AttributeOnly ? (object)"—" : c.DifferencePercent, c.ExceedsTolerance ? "超过 " + report.TolerancePercent + "%" : "", c.Notes ?? "" }).ToArray();
            var notes = new List<object[]> {
                new object[] { "模型", report.DocumentTitle }, new object[] { "路径", report.SourcePath },
                new object[] { "读取时间 UTC", report.CapturedUtc }, new object[] { "统计范围", report.ScopeDescription },
                new object[] { "计算公式", report.FormulaDescription }, new object[] { "提示阈值(%)", report.TolerancePercent },
                new object[] { "数据完整性", report.Complete ? "完整" : "包含待补或待核对项" }
            };
            notes.AddRange(report.Notes.Select(n => new object[] { "备注", n }));
            var sheets = new[] {
                Tuple.Create("分层汇总", Sheet(new[] { "楼层", "标高(m)", "层高(m)", "使用性质及计算面积(㎡)", "填报投影面积(㎡)", "实际几何面积(㎡)",
                    "计算面积(㎡)", "计容面积(㎡)", "不计容面积(㎡)", "差额(㎡)", "差异率(%)", "超过阈值", "来源标高", "备注" }, floorRows,
                    report.Floors.Select(f => f.ExceedsTolerance).Concat(new[] { report.Total.ExceedsTolerance }).ToArray())),
                Tuple.Create("构件明细", Sheet(componentHeaders, report.Components.Select(Component).ToArray(), report.Components.Select(c => c.ExceedsTolerance).ToArray())),
                Tuple.Create("待完善构件", Sheet(componentHeaders, report.Pending.Select(Component).ToArray())),
                Tuple.Create("属性对照", Sheet(new[] { "指标", "填写值(㎡)", "统计值(㎡)", "差额(㎡)", "差异率(%)", "超过阈值", "备注" }, comparisons, report.Comparisons.Select(c => c.ExceedsTolerance).ToArray())),
                Tuple.Create("口径与来源", Sheet(new[] { "项目", "内容" }, notes.ToArray()))
            };
            ReportingWorkbookTemplate.WriteSheets(path, sheets, Styles());
        }

        private static object[] Component(BuildingAreaComponent c) => new object[] {
            c.ElementId, c.UniqueId, c.Name, c.Slot, c.PropertySet, c.LevelName, c.ElevationM, c.MainFunction, c.Function,
            c.DeclaredProjectionArea, c.GeometricArea, c.CalculationFactor, c.FloorRatioFactor, c.CalculatedArea,
            c.FloorRatioArea, c.NonFloorRatioArea, c.DifferenceArea, c.DifferencePercent, c.ExceedsTolerance ? "是" : "",
            c.Status, c.Notes
        };

        internal static XDocument Sheet(string[] headers, object[][] data, bool[] highlighted = null)
        {
            var rows = new List<XElement> { Row(1, headers.Cast<object>().ToArray(), true, false) };
            rows.AddRange(data.Select((r, i) => Row(i + 2, r, false, highlighted != null && highlighted[i])));
            return new XDocument(new XElement(Main + "worksheet",
                new XElement(Main + "sheetViews", new XElement(Main + "sheetView", new XAttribute("workbookViewId", 0),
                    new XElement(Main + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                new XElement(Main + "cols", headers.Select((h, i) => new XElement(Main + "col", new XAttribute("min", i + 1), new XAttribute("max", i + 1),
                    new XAttribute("width", h.Contains("性质") || h == "备注" || h == "内容" ? 60 : h == "唯一ID" ? 42 : 22), new XAttribute("customWidth", 1)))),
                new XElement(Main + "sheetData", rows),
                new XElement(Main + "autoFilter", new XAttribute("ref", "A1:" + Column(headers.Length - 1) + (data.Length + 1)))));
        }

        private static XElement Row(int index, object[] values, bool header, bool highlighted)
            => new XElement(Main + "row", new XAttribute("r", index), values.Select((v, i) => Cell(Column(i) + index, v, header, highlighted)));

        private static XElement Cell(string reference, object value, bool header, bool highlighted)
        {
            var numeric = value is decimal || value is int;
            var style = header ? 1 : numeric ? (highlighted ? 4 : 2) : (highlighted ? 3 : 0);
            var cell = new XElement(Main + "c", new XAttribute("r", reference), new XAttribute("s", style));
            if (numeric) cell.Add(new XElement(Main + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
            else cell.Add(new XAttribute("t", "inlineStr"), new XElement(Main + "is", new XElement(Main + "t",
                new XAttribute(XNamespace.Xml + "space", "preserve"), value?.ToString() ?? "待补")));
            return cell;
        }

        private static string Column(int index)
        {
            var result = "";
            do { result = (char)('A' + index % 26) + result; index = index / 26 - 1; } while (index >= 0);
            return result;
        }

        internal static XDocument Styles() => XDocument.Parse("<styleSheet xmlns='" + Main.NamespaceName + "'>" +
            "<fonts count='2'><font><sz val='11'/><name val='Microsoft YaHei'/></font><font><b/><color rgb='FFFFFFFF'/><sz val='11'/><name val='Microsoft YaHei'/></font></fonts>" +
            "<fills count='4'><fill><patternFill patternType='none'/></fill><fill><patternFill patternType='gray125'/></fill><fill><patternFill patternType='solid'><fgColor rgb='FF245A70'/></patternFill></fill><fill><patternFill patternType='solid'><fgColor rgb='FFFFE1D9'/></patternFill></fill></fills>" +
            "<borders count='1'><border/></borders><cellStyleXfs count='1'><xf numFmtId='0' fontId='0' fillId='0' borderId='0'/></cellStyleXfs>" +
            "<cellXfs count='5'>" + Style(0, 0, 0) + Style(0, 1, 2) + Style(2, 0, 0) + Style(0, 0, 3) + Style(2, 0, 3) +
            "</cellXfs><cellStyles count='1'><cellStyle name='Normal' xfId='0' builtinId='0'/></cellStyles></styleSheet>");

        private static string Style(int format, int font, int fill)
            => "<xf numFmtId='" + format + "' fontId='" + font + "' fillId='" + fill + "' borderId='0' xfId='0' applyNumberFormat='1' applyFill='1' applyFont='1'><alignment vertical='center' wrapText='1'/></xf>";
    }
}
