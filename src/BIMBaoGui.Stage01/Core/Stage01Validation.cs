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
    public static ValidationResult Validate(Stage01Model model, IReadOnlyList<FieldDefinition> definitions)
    {
      var messages = new List<ValidationMessage>();
      IReadOnlyList<FieldDefinition> uniqueDefinitions = (definitions ?? Array.Empty<FieldDefinition>())
        .Where(definition => definition != null)
        .GroupBy(definition => definition.Key ?? string.Empty, StringComparer.Ordinal)
        .Select(group => group.First())
        .ToList();

      foreach (FieldDefinition definition in uniqueDefinitions)
      {
        string value = definition.Entity == "IfcOrganization"
          ? model.GetOrganizationValue(definition.Key)
          : model.GetValue(definition.Key);
        bool required = FieldInputRules.IsRequired(definition);
        string error = FieldInputRules.Validate(definition, value, required);
        if (!string.IsNullOrWhiteSpace(error))
          messages.Add(new ValidationMessage(ValidationSeverity.Error, definition.Key, error));
      }

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
  }
}
