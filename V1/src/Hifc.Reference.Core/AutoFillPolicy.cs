using System;
using System.Globalization;

namespace Hifc.Reference
{
    public static class AutoFillPolicy
    {
        public static void Populate(FieldCheck field, string slot, string nativeName, double? projectedAreaM2)
        {
            // Existing values, including zero and false, always take precedence.
            // Ambiguous or invalid values require review rather than replacement.
            if ((field.State != "缺参数" && field.State != "未填写") || !string.IsNullOrWhiteSpace(field.Value)) return;
            if (field.Property == "投影面积")
            {
                if (projectedAreaM2.HasValue && projectedAreaM2.Value > 0 && !double.IsInfinity(projectedAreaM2.Value) && !double.IsNaN(projectedAreaM2.Value))
                {
                    field.SuggestedValue = projectedAreaM2.Value.ToString("0.######", CultureInfo.InvariantCulture);
                    field.SuggestionUnit = "m²";
                    field.SuggestionReason = "当前模型顶面三角网的水平投影面积（m²），保留孔洞";
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
        }
    }
}
