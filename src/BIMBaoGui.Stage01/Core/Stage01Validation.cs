using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BIMBaoGui.Stage01.Core
{
  internal enum ValidationSeverity
  {
    Info,
    Warning,
    Error
  }

  internal sealed class ValidationMessage
  {
    public ValidationMessage(ValidationSeverity severity, string fieldKey, string message)
    {
      Severity = severity;
      FieldKey = fieldKey ?? string.Empty;
      Message = message ?? string.Empty;
    }

    public ValidationSeverity Severity { get; }
    public string FieldKey { get; }
    public string Message { get; }
  }

  internal sealed class ValidationResult
  {
    public ValidationResult(IReadOnlyList<ValidationMessage> messages)
    {
      Messages = messages ?? Array.Empty<ValidationMessage>();
    }

    public IReadOnlyList<ValidationMessage> Messages { get; }
    public bool IsValid => Messages.All(x => x.Severity != ValidationSeverity.Error);
    public int ErrorCount => Messages.Count(x => x.Severity == ValidationSeverity.Error);
    public int WarningCount => Messages.Count(x => x.Severity == ValidationSeverity.Warning);
  }

  internal static class Stage01Validator
  {
    private static readonly string[] RequiredKeys =
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

    public static ValidationResult Validate(Stage01Model model, IReadOnlyList<FieldDefinition> definitions)
    {
      var messages = new List<ValidationMessage>();
      foreach (string key in RequiredKeys)
      {
        if (string.IsNullOrWhiteSpace(model.GetValue(key)))
          messages.Add(new ValidationMessage(ValidationSeverity.Error, key, "必填项尚未填写。"));
      }

      if (!model.ConfirmBlankProject)
        messages.Add(new ValidationMessage(
          ValidationSeverity.Error,
          "HBR|Precheck|BlankProject",
          "必须确认当前文件尚未开始正式建模；Revit 模板默认内容允许保留。"));

      foreach (FieldDefinition definition in definitions)
      {
        string value = definition.Entity == "IfcOrganization"
          ? model.GetOrganizationValue(definition.Key)
          : model.GetValue(definition.Key);
        if (string.IsNullOrWhiteSpace(value)) continue;
        ValidateTypedValue(definition, value, messages);
      }

      if (TryDouble(model.GetValue(Stage01Keys.TrueNorthAngle), out double angle))
      {
        if (angle < -180.0 || angle > 180.0)
          messages.Add(new ValidationMessage(ValidationSeverity.Error, Stage01Keys.TrueNorthAngle, "真北角度必须位于 -180° 到 180°。"));
      }

      foreach (Dictionary<string, string> organization in model.Organizations)
      {
        bool hasAny = organization.Values.Any(value => !string.IsNullOrWhiteSpace(value));
        if (!hasAny) continue;
        string nameKey = "IfcOrganization|Pset_组织通用属性集|企业名称";
        if (!organization.TryGetValue(nameKey, out string name) || string.IsNullOrWhiteSpace(name))
          messages.Add(new ValidationMessage(ValidationSeverity.Warning, nameKey, "参建组织已填写部分信息，但企业名称为空。"));
      }

      return new ValidationResult(messages);
    }

    public static bool TryDouble(string value, out double result)
    {
      return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result)
        || double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out result);
    }

    private static void ValidateTypedValue(FieldDefinition definition, string value, ICollection<ValidationMessage> messages)
    {
      switch (definition.Kind)
      {
        case FieldKind.Number:
          if (!TryDouble(value, out _))
            messages.Add(new ValidationMessage(ValidationSeverity.Error, definition.Key, "应填写数值。"));
          break;
        case FieldKind.Integer:
          if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            messages.Add(new ValidationMessage(ValidationSeverity.Error, definition.Key, "应填写整数。"));
          break;
        case FieldKind.Boolean:
          if (!bool.TryParse(value, out _))
            messages.Add(new ValidationMessage(ValidationSeverity.Error, definition.Key, "应填写布尔值。"));
          break;
        case FieldKind.DateTime:
          if (!DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out _)
            && !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            messages.Add(new ValidationMessage(ValidationSeverity.Error, definition.Key, "日期时间格式无效。"));
          break;
        case FieldKind.Guid:
          if (!Guid.TryParse(value, out _))
            messages.Add(new ValidationMessage(ValidationSeverity.Error, definition.Key, "GUID 格式无效。"));
          break;
        case FieldKind.Enum:
          if (definition.AllowedValues.Count > 0 && !definition.AllowedValues.Contains(value))
            messages.Add(new ValidationMessage(ValidationSeverity.Error, definition.Key, "不在允许的选项范围内。"));
          break;
      }
    }
  }
}
