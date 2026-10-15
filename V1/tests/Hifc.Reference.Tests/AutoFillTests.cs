using System;
using Hifc.Reference;

internal static class AutoFillTests
{
    static void Fill(FieldCheck field, string slot, string name, double? area)
    {
        var type = typeof(CaseWorkflow).Assembly.GetType("Hifc.Reference.AutoFillPolicy");
        if (type == null) throw new Exception("Model-derived automatic values are not implemented");
        type.GetMethod("Populate").Invoke(null, new object[] { field, slot, name, area });
    }
    static void Assert(bool value) { if (!value) throw new Exception("Autofill result differs from its source"); }
    public static void Run(Action<string, Action> check)
    {
        check("empty_projected_area_uses_measured_model_geometry", () => { var f = new FieldCheck { Property = "投影面积", RawType = "Real", State = "未填写" }; Fill(f,"规划净用地","场地",154.321); Assert(f.SuggestedValue=="154.321" && f.SuggestionReason.Contains("m²")); });
        check("empty_name_uses_current_model_name", () => { var f = new FieldCheck { Property = "名称", RawType = "Text", State = "缺参数" }; Fill(f,"规划净用地","新场地",null); Assert(f.SuggestedValue=="新场地"); });
        check("net_land_version_uses_verified_success_case_default", () => { var f = new FieldCheck { Property = "版本", RawType = "Text", State = "未填写" }; Fill(f,"规划净用地","场地",null); Assert(f.SuggestedValue=="1" && f.SuggestionReason.Contains("983")); });
        check("net_land_type_uses_confirmed_business_class", () => { var f = new FieldCheck { Property = "类型", RawType = "Text", State = "未填写" }; Fill(f,"规划净用地","场地",null); Assert(f.SuggestedValue=="规划净用地"); });
        check("autofill_preserves_existing_manual_value_including_zero", () => { var f = new FieldCheck { Property = "投影面积", RawType = "Real", State = "有效", Value = "0" }; Fill(f,"规划净用地","场地",154.321); Assert(f.SuggestedValue==null); });
        check("unavailable_geometry_is_not_fabricated_as_zero_or_case_area", () => { var f = new FieldCheck { Property = "投影面积", RawType = "Real", State = "未填写" }; Fill(f,"规划净用地","场地",null); Assert(f.SuggestedValue==null); });
    }
}
