using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace BIMBaoGui.Stage01.Core
{
  internal static class FieldInputRules
  {
    private static readonly HashSet<string> RequiredKeys = new HashSet<string>(StringComparer.Ordinal)
    {
      Stage01Keys.ProjectNumber,
      Stage01Keys.ProjectName,
      Stage01Keys.SubitemName,
      Stage01Keys.ModelFileType,
      Stage01Keys.ModelScope,
      Stage01Keys.BaseX,
      Stage01Keys.BaseY,
      Stage01Keys.BaseElevation,
      Stage01Keys.CoordinateSystem,
      Stage01Keys.ElevationSystem,
      Stage01Keys.TrueNorthAngle
    };

    public static bool IsRequired(FieldDefinition definition)
    {
      return definition != null
        && !definition.Deferred
        && RequiredKeys.Contains(definition.Key ?? string.Empty);
    }

    public static string BuildPlaceholder(FieldDefinition definition)
    {
      if (definition == null) return "点击填写";
      if (definition.ReadOnly || definition.Deferred) return "由系统读取／生成";
      if (definition.Kind == FieldKind.Enum)
        return definition.AllowedValues.Count > 0 ? "下拉选择｜请选择一项" : "文本｜按实际填写";

      string label = definition.Label ?? string.Empty;
      string key = definition.Key ?? string.Empty;

      if (key == Stage01Keys.ProjectNumber) return "文本｜示例：HB-2026-001";
      if (key == Stage01Keys.ProjectName) return "文本｜示例：武汉市某建设项目";
      if (key == Stage01Keys.SubitemName) return "文本｜示例：总平面";
      if (key == Stage01Keys.BaseX || key == Stage01Keys.BaseY) return "数值（m）｜示例：38561234.123";
      if (key == Stage01Keys.BaseElevation) return "数值（m）｜示例：23.450";
      if (key == Stage01Keys.TrueNorthAngle) return "角度 -180～180｜示例：0";
      if (Contains(label, "邮政编码")) return "6位数字｜示例：430000";
      if (ContainsAny(label, "手机", "电话", "联系电话")) return "电话｜示例：13800138000";
      if (Contains(label, "邮箱")) return "邮箱｜示例：name@example.com";
      if (ContainsAny(label, "统一信用代码", "社会统一信用代码")) return "18位代码｜示例：91420100MA4K123456";
      if (ContainsAny(label, "日期", "时间")) return "日期｜示例：2026-07-31";
      if (ContainsAny(label, "地址", "所在地")) return "文本｜示例：武汉市洪山区某路1号";
      if (ContainsAny(label, "编号", "代码", "编码")) return "文本｜示例：HB-001";

      switch (definition.Kind)
      {
        case FieldKind.Number: return "数值｜示例：123.45";
        case FieldKind.Integer: return "整数｜示例：1";
        case FieldKind.Boolean: return "布尔值｜是 / 否";
        case FieldKind.DateTime: return "日期｜示例：2026-07-31";
        case FieldKind.Guid: return "GUID｜由系统生成或填写标准GUID";
        default: return "文本｜示例：按实际填写";
      }
    }

    public static string Validate(FieldDefinition definition, string value, bool required)
    {
      if (definition == null) return null;
      if (definition.ReadOnly || definition.Deferred) return null;
      string normalized = (value ?? string.Empty).Trim();
      if (normalized.Length == 0)
        return required ? "该项为必填项。" : null;

      string label = definition.Label ?? string.Empty;
      string key = definition.Key ?? string.Empty;

      if (Contains(label, "邮政编码") && !Regex.IsMatch(normalized, "^\\d{6}$"))
        return "邮政编码应为 6 位数字，例如 430000。";

      if (ContainsAny(label, "手机", "电话", "联系电话")
        && !Regex.IsMatch(normalized, "^\\+?[0-9][0-9\\- ]{6,19}$"))
        return "电话号码格式不正确，例如 13800138000。";

      if (Contains(label, "邮箱"))
      {
        try { var address = new MailAddress(normalized); if (!string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase)) return "邮箱格式不正确，例如 name@example.com。"; }
        catch { return "邮箱格式不正确，例如 name@example.com。"; }
      }

      if (ContainsAny(label, "统一信用代码", "社会统一信用代码")
        && !Regex.IsMatch(normalized.ToUpperInvariant(), "^[0-9A-HJ-NPQRTUWXY]{18}$"))
        return "统一社会信用代码应为 18 位大写字母或数字。";

      if (definition.Kind == FieldKind.Number)
      {
        if (!TryDouble(normalized, out double number))
          return "应填写数值，例如 123.45。";
        if (key == Stage01Keys.TrueNorthAngle && (number < -180.0 || number > 180.0))
          return "真北角度必须位于 -180° 到 180°。";
      }
      else if (definition.Kind == FieldKind.Integer)
      {
        if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
          && !int.TryParse(normalized, NumberStyles.Integer, CultureInfo.CurrentCulture, out _))
          return "应填写整数，例如 1。";
      }
      else if (definition.Kind == FieldKind.Boolean)
      {
        if (!bool.TryParse(normalized, out _)
          && !string.Equals(normalized, "是", StringComparison.Ordinal)
          && !string.Equals(normalized, "否", StringComparison.Ordinal))
          return "应填写“是”或“否”。";
      }
      else if (definition.Kind == FieldKind.DateTime)
      {
        if (!DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.None, out _)
          && !DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
          return "日期格式不正确，例如 2026-07-31。";
      }
      else if (definition.Kind == FieldKind.Guid)
      {
        if (!Guid.TryParse(normalized, out _))
          return "GUID 格式不正确，例如 550e8400-e29b-41d4-a716-446655440000。";
      }
      else if (definition.Kind == FieldKind.Enum && definition.AllowedValues.Count > 0)
      {
        if (!definition.AllowedValues.Contains(normalized))
          return "请选择下拉列表中的有效选项。";
      }

      return null;
    }

    public static bool TryDouble(string value, out double result)
    {
      return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result)
        || double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out result);
    }

    private static bool Contains(string value, string token)
    {
      return (value ?? string.Empty).IndexOf(token ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
      return tokens != null && tokens.Any(token => Contains(value, token));
    }
  }
}
