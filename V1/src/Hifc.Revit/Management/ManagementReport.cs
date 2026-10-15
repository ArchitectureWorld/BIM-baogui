using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Hifc.Reference;
namespace Hifc.Revit.Management
{
    internal static class ManagementReport
    {
        private static string H(object value) => WebUtility.HtmlEncode(value?.ToString() ?? "");
        internal static void Write(string path, ModelSnapshot snapshot, ModelSummary summary, EditPreview preview)
        {
            var html = new StringBuilder("<!doctype html><html lang='zh-CN'><meta charset='utf-8'><title>HIFC 全面检查报告</title><style>body{font:15px/1.7 'Microsoft YaHei',sans-serif;max-width:1200px;margin:36px auto;color:#19384c}table{width:100%;border-collapse:collapse;margin:18px 0}td,th{border:1px solid #d8e0e7;padding:8px;text-align:left;overflow-wrap:anywhere}th{background:#edf3f6}.note{padding:16px;background:#fff3d9}</style>");
            html.Append("<h1>").Append(H(snapshot.Title)).Append(" · 全面检查报告</h1><p>").Append(H(summary.Basis)).Append("</p><p>检查编号 ").Append(H(snapshot.ScanId)).Append(" · UTC ").Append(H(snapshot.CompletedUtc)).Append("</p><p>").Append(H(string.Join("；", snapshot.DetectionEvidence))).Append("</p>");
            html.Append("<p class='note'>").Append(snapshot.Stage == "报规" ? "依据：用户确认通过的报规成功案例 01；案例对照不等于官方合规或平台验收。" : "本阶段检查规则待建立，本报告只记录基础盘点。").Append("全量对象 ").Append(summary.ElementCount).Append("；字段存在 ").Append(summary.PresentFields).Append('/').Append(summary.FieldCount).Append("；有效值 ").Append(summary.ValidFields).Append("；未覆盖项 ").Append(summary.CoverageGaps).Append("。</p>");
            html.Append("<h2>参考清单与候选</h2><p>未找到 ").Append(summary.MissingRequired).Append(" 类；待确认分类 ").Append(summary.PendingClassificationRoles).Append(" 类。候选不计作已识别，也不计作模型缺失。</p><table><tr><th>参考项</th><th>适用范围</th><th>已识别</th><th>候选对象</th><th>结果 / 补充建议</th></tr>");
            foreach (var role in summary.Roles) html.Append("<tr><td>").Append(H(role.Slot)).Append("</td><td>").Append(H(role.Mode)).Append("</td><td>").Append(role.Count).Append("</td><td>").Append(role.Candidates).Append("</td><td>").Append(H(role.State)).Append(role.Candidates > 0 ? "；查看对象识别依据，确认分类及载体关系后预览写入。" : role.Count == 0 && role.Mode == "必要" ? "；本轮没有候选，请核对模型及命名，确认缺失后补充载体。" : "").Append("</td></tr>");
            html.Append("</table><h2>需人工处理</h2><ul>"); foreach (var item in preview?.Conflicts ?? new string[0]) html.Append("<li>").Append(H(item)).Append("</li>");
            html.Append("</ul><h2>全部对象与字段问题</h2><table><tr><th>ID / 名称</th><th>属性组</th><th>本轮结果</th><th>识别依据</th><th>缺口</th></tr>");
            foreach (var row in snapshot.Elements) html.Append("<tr><td>").Append(H(row.Id + " / " + row.Name)).Append("</td><td>").Append(H(row.SlotText)).Append("</td><td>").Append(H(row.StatusText)).Append("</td><td>").Append(H(row.Recognition)).Append("</td><td>").Append(H(string.Join("；", row.Fields.Where(f => !f.Acceptable).Select(f => f.PropertySet + "/" + f.Property + "：" + f.State)))).Append("</td></tr>");
            html.Append("</table></html>"); File.WriteAllText(path, html.ToString(), new UTF8Encoding(false));
        }
    }
}
