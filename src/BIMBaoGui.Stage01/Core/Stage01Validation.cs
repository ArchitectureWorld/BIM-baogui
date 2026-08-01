using System;
using System.Collections.Generic;
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
    public bool IsValid => Messages.All(message => message.Severity != ValidationSeverity.Error);
    public int ErrorCount => Messages.Count(message => message.Severity == ValidationSeverity.Error);
    public int WarningCount => Messages.Count(message => message.Severity == ValidationSeverity.Warning);
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
      IReadOnlyList<FieldDefinition> uniqueDefinitions = (definitions ?? Array.Empty<FieldDefinition>())
        .Where(definition => definition != null)
        .GroupBy(definition => definition.Key ?? string.Empty, StringComparer.Ordinal)
        .Select(group => group.First())
        .ToList();

      foreach (string key in RequiredKeys)
      {
        FieldDefinition definition = uniqueDefinitions.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal))
          ?? CreateFallbackDefinition(key);
        string error = FieldInputRules.Validate(definition, model.GetValue(key), true);
        if (!string.IsNullOrWhiteSpace(error))
          messages.Add(new ValidationMessage(ValidationSeverity.Error, key, error));
      }

      foreach (FieldDefinition definition in uniqueDefinitions)
      {
        if (RequiredKeys.Contains(definition.Key, StringComparer.Ordinal)) continue;
        if (definition.ReadOnly || definition.Deferred) continue;
        if (PlanningTargetCatalog.IsManagedMvdField(definition.Key)) continue;
        string value = definition.Entity == "IfcOrganization"
          ? model.GetOrganizationValue(definition.Key)
          : model.GetValue(definition.Key);
        string error = FieldInputRules.Validate(definition, value, false);
        if (!string.IsNullOrWhiteSpace(error))
          messages.Add(new ValidationMessage(ValidationSeverity.Error, definition.Key, error));
      }

      ValidatePlanningTargets(model, messages);

      if (!model.ConfirmBlankProject)
      {
        messages.Add(new ValidationMessage(
          ValidationSeverity.Error,
          "HBR|Precheck|BlankProject",
          "必须确认当前文件尚未开始正式建模；Revit 模板默认内容允许保留。"));
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
      return FieldInputRules.TryDouble(value, out result);
    }

    private static void ValidatePlanningTargets(Stage01Model model, ICollection<ValidationMessage> messages)
    {
      string modelFileType = model.GetValue(Stage01Keys.ModelFileType);
      foreach (PlanningTargetDefinition definition in PlanningTargetCatalog.All)
      {
        PlanningTargetRequirement requirement = PlanningTargetRequirementPolicy.GetRequirement(modelFileType, definition.MetricCode);
        PlanningTargetValue target = model.GetPlanningTarget(definition.MetricCode);
        if (requirement == PlanningTargetRequirement.Required && target == null)
        {
          messages.Add(new ValidationMessage(
            ValidationSeverity.Error,
            definition.MvdFieldKey,
            definition.Label + "为总平模型必填的规划控制目标，请设置运算符和数值。"));
        }
        else if (target != null && target.Unit != definition.Unit)
        {
          messages.Add(new ValidationMessage(
            ValidationSeverity.Error,
            definition.MvdFieldKey,
            definition.Label + "的单位与指标定义不一致。"));
        }
      }
    }

    private static FieldDefinition CreateFallbackDefinition(string key)
    {
      FieldKind kind = key == Stage01Keys.BaseX
        || key == Stage01Keys.BaseY
        || key == Stage01Keys.BaseElevation
        || key == Stage01Keys.TrueNorthAngle
        ? FieldKind.Number
        : FieldKind.Text;
      return new FieldDefinition
      {
        Key = key,
        Label = key == Stage01Keys.TrueNorthAngle ? "真北角度" : key,
        Kind = kind,
        Essential = true
      };
    }
  }
}
