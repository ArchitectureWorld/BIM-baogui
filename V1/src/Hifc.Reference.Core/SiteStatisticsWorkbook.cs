using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hifc.Reference
{
    public static class SiteStatisticsWorkbook
    {
        public static void Write(string path, SiteStatisticsReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (!string.Equals(Path.GetExtension(path),".xlsx",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("统计文件应使用.xlsx扩展名。");
            var headers = new[] { "分类", "分项", "填报面积(㎡)", "实际几何(㎡)", "填报折算(㎡)", "几何折算(㎡)", "机动车(个)", "非机动车(个)", "差额(㎡)", "差异率(%)", "来源", "备注" };
            var detailHeaders = new[] { "来源键", "构件ID", "唯一ID", "类目", "折算系数" }.Concat(headers).Concat(new[] { "状态" }).ToArray();
            var metrics = report.Metrics.Select(m => new object[] { m.Name, m.DeclaredValue,
                m.GeometricApplicable ? (object)m.GeometricValue : "—", m.Unit, m.Complete ? "完整" : "待补 / 核对", m.Formula ?? "", m.Notes ?? "" }).ToArray();
            var sources = new List<object[]> {
                new object[] { "模型", report.DocumentTitle }, new object[] { "来源路径", report.SourcePath },
                new object[] { "读取时间 UTC", report.CapturedUtc }, new object[] { "面积差异提示(%)", report.TolerancePercent },
                new object[] { "范围完整性", report.Complete ? "完整" : "含待补或待核对项" }
            };
            sources.AddRange(report.Sources.Select(s => new object[] { s.Key, s.Name, s.Path ?? "", s.IsHost || s.Selected ? "选中" : "未选", s.Available ? "已加载" : "未加载", s.Notes ?? "" }));
            sources.AddRange(report.Notes.Select(n => new object[] { "说明", n }));
            var sheets = new[] {
                Tuple.Create("总图指标", BuildingAreaStatisticsWorkbook.Sheet(new[] { "指标", "填报口径", "几何口径", "单位", "状态", "公式", "说明" },metrics)),
                Tuple.Create("分类汇总", BuildingAreaStatisticsWorkbook.Sheet(headers,report.Rows.Select(Row).ToArray(),report.Rows.Select(r=>r.ExceedsTolerance).ToArray())),
                Tuple.Create("构件明细", BuildingAreaStatisticsWorkbook.Sheet(detailHeaders,report.Components.Select(Detail).ToArray(),report.Components.Select(r=>r.ExceedsTolerance).ToArray())),
                Tuple.Create("待完善构件", BuildingAreaStatisticsWorkbook.Sheet(detailHeaders,report.Pending.Select(Detail).ToArray())),
                Tuple.Create("来源与口径", BuildingAreaStatisticsWorkbook.Sheet(new[] { "标识", "名称或内容", "路径", "选择", "加载", "说明" },sources.ToArray()))
            };
            ReportingWorkbookTemplate.WriteSheets(path,sheets,BuildingAreaStatisticsWorkbook.Styles());
        }
        private static object[] Row(SiteStatisticsRow row) => new object[] {
            row.Kind, row.Name, Applicable(row.DeclaredAreaApplicable,row.DeclaredArea), row.GeometricArea,
            Applicable(row.ConvertedAreaApplicable,row.DeclaredConvertedArea), Applicable(row.ConvertedAreaApplicable,row.GeometricConvertedArea),
            Applicable(row.CountApplicable,row.MotorCount), Applicable(row.CountApplicable,row.NonMotorCount),
            Applicable(row.DeclaredAreaApplicable,row.Difference), Applicable(row.DeclaredAreaApplicable,row.DifferencePercent), row.SourceNames ?? "", row.Notes ?? ""
        };
        private static object[] Detail(SiteStatisticsComponent row)
            => new object[] { row.SourceKey, row.ElementId, row.UniqueId, row.Slot, Applicable(row.ConvertedAreaApplicable,row.Factor) }
                .Concat(Row(row)).Concat(new object[] { row.Status ?? "" }).ToArray();
        private static object Applicable(bool applicable, decimal? value) => applicable ? (object)value : "—";
    }
}
