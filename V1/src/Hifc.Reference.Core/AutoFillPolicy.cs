using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Hifc.Reference
{
    public static class AutoFillPolicy
    {
        public static void Populate(FieldCheck field,string slot,string nativeName,double? projectedAreaM2)
            => PopulateWithSource(field,slot,nativeName,projectedAreaM2,null);
        public static void PopulateWithSource(FieldCheck field, string slot, string nativeName, double? projectedAreaM2, string areaReason)
        {
            // Existing values, including zero and false, always take precedence.
            // Ambiguous or invalid values require review rather than replacement.
            if ((field.State != "缺参数" && field.State != "未填写") || !string.IsNullOrWhiteSpace(field.Value))
            {
                if(field.Property=="投影面积" && projectedAreaM2.HasValue &&
                    !double.IsNaN(projectedAreaM2.Value)&&!double.IsInfinity(projectedAreaM2.Value)) {
                    var current=field.MetricValue;
                    if(!current.HasValue&&double.TryParse(field.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var value))current=value;
                    if(current.HasValue&&Math.Abs(current.Value-projectedAreaM2.Value)>.001)
                        field.SuggestionReason="已有面积保留；与本轮模型投影 "+projectedAreaM2.Value.ToString("0.######",CultureInfo.InvariantCulture)+" m² 不同，请核对模型变化及统计口径。";
                }
                return;
            }
            if (field.Property == "投影面积")
            {
                if (projectedAreaM2.HasValue && projectedAreaM2.Value > 0 && !double.IsInfinity(projectedAreaM2.Value) && !double.IsNaN(projectedAreaM2.Value))
                {
                    field.SuggestedValue = projectedAreaM2.Value.ToString("0.######", CultureInfo.InvariantCulture);
                    field.SuggestionUnit = "m²";
                    field.SuggestionReason = (areaReason ?? "当前模型几何的水平投影面积（m²），保留孔洞")
                        + "；几何测量建议，申报统计口径请人工核对";
                }
                else field.Detail = "无法从当前构件取得可靠的水平投影面积，请补充模型或人工填写（m²）";
            }
            else if (field.Property == "名称" && !string.IsNullOrWhiteSpace(nativeName))
            {
                field.SuggestedValue = nativeName; field.SuggestionReason = "当前 Revit 构件名称";
            }
            else if (slot == "规划净用地" && field.Property == "版本")
            {
                field.SuggestedValue = "1";
                field.SuggestionReason = "成功案例总图-属性写入-15.ifc / #983 / 规划净用地版本默认值，可人工调整";
            }
            else if (slot == "规划净用地" && field.Property == "类型")
            {
                field.SuggestedValue = slot; field.SuggestionReason = "本轮确认或识别的业务分类";
            }
            else if (slot == "建筑楼层" && field.Property == "建筑基底")
            {
                var name = (nativeName ?? "").Trim().ToUpperInvariant();
                var first = Regex.IsMatch(name, @"^(一层|首层|1层|1F(?![0-9])|F1(?![0-9]))");
                field.SuggestedValue = first ? "1" : "0";
                field.SuggestionReason = "按 Revit 标高名称“" + nativeName + "”预置建筑基底：" + (first ? "首层为是" : "其余标高为否") + "，可人工调整";
            }
            else if (field.RawType == "Boolean")
            {
                field.SuggestedValue = "0";
                field.SuggestionReason = "未填写的是否属性按否处理，可人工勾选修改";
            }
        }
    }
}
