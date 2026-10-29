using System;
using System.Globalization;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public static class NativeUnitInput
    {
        // Bare numbers are already in the unit advertised by the server. Only an
        // explicitly qualified value is delegated to Revit's unit parser.
        public static string Normalize(string text, string inputUnit, Func<string, string, double?> parseExplicit)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("数值参数不能直接清空。");
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return Finite(number);
            if (inputUnit != "m" && inputUnit != "m²") throw new InvalidOperationException("此字段需要不带单位的数值。");
            var parsed = parseExplicit?.Invoke(text, inputUnit);
            if (!parsed.HasValue) throw new InvalidOperationException("请输入有效数值，可附带明确的长度或面积单位。");
            return Finite(parsed.Value);
        }
        private static string Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("数值必须是有限数。");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
        public static string EditorValue(FieldResult field)
        {
            if ((field.InputUnit == "m" || field.InputUnit == "m²") && field.MetricValue.HasValue) return Finite(field.MetricValue.Value);
            if (field.RawType == "Real" || field.RawType == "Integer") return field.Value;
            return field.RawType == "Boolean" ? field.Value : field.DisplayValue ?? field.Value;
        }
        public static string DisplayValue(FieldResult field)
        {
            var value = EditorValue(field);
            return !string.IsNullOrEmpty(field.InputUnit) && !string.IsNullOrWhiteSpace(value) ? value + " " + field.InputUnit : value;
        }
    }
}
