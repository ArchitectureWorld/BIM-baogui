using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Core
{
  internal static class Stage01Feedback
  {
    private const string BlankConfirmationKey = "HBR|Precheck|BlankProject";

    public static IReadOnlyList<string> Build(
      ValidationResult validation,
      IReadOnlyList<FieldDefinition> definitions,
      IEnumerable<string> environmentMessages,
      int maximum)
    {
      return Build(
        validation,
        definitions,
        environmentMessages,
        Array.Empty<string>(),
        maximum);
    }

    public static IReadOnlyList<string> Build(
      ValidationResult validation,
      IReadOnlyList<FieldDefinition> definitions,
      IEnumerable<string> environmentMessages,
      IEnumerable<string> operationFailureMessages,
      int maximum)
    {
      int limit = Math.Max(1, maximum);
      var result = new List<string>();

      foreach (string message in operationFailureMessages ?? Array.Empty<string>())
      {
        if (string.IsNullOrWhiteSpace(message)) continue;
        AddDistinct(result, "最近写入：" + message.Trim());
      }

      foreach (string message in environmentMessages ?? Array.Empty<string>())
      {
        if (string.IsNullOrWhiteSpace(message)) continue;
        string normalized = message.Trim();
        string prefix = IsWriteFailure(normalized)
          ? "最近写入："
          : "文件环境：";
        AddDistinct(result, prefix + normalized);
      }

      foreach (ValidationMessage message in validation?.Messages ?? Array.Empty<ValidationMessage>())
      {
        if (message == null || message.Severity != ValidationSeverity.Error) continue;
        if (string.Equals(message.FieldKey, BlankConfirmationKey, StringComparison.Ordinal))
        {
          AddDistinct(result, "提交与校验：请勾选“确认当前文件尚未开始正式建模（允许 Revit 模板默认内容）”。");
          continue;
        }

        FieldDefinition definition = FindDefinition(definitions, message.FieldKey);
        if (definition == null)
        {
          AddDistinct(result, message.Message);
          continue;
        }

        string group = NormalizeGroup(definition.Group);
        string groupName = DisplayGroup(group);
        AddDistinct(result, groupName + " > " + definition.Label + "：" + message.Message);
      }

      return result.Take(limit).ToArray();
    }

    public static int CountErrorsForGroup(
      ValidationResult validation,
      IReadOnlyList<FieldDefinition> definitions,
      string group)
    {
      string normalizedGroup = NormalizeGroup(group);
      return (validation?.Messages ?? Array.Empty<ValidationMessage>())
        .Where(message => message != null && message.Severity == ValidationSeverity.Error)
        .Count(message => string.Equals(GroupForMessage(message, definitions), normalizedGroup, StringComparison.Ordinal));
    }

    public static string FirstProblemGroup(
      ValidationResult validation,
      IReadOnlyList<FieldDefinition> definitions)
    {
      foreach (ValidationMessage message in validation?.Messages ?? Array.Empty<ValidationMessage>())
      {
        if (message == null || message.Severity != ValidationSeverity.Error) continue;
        string group = GroupForMessage(message, definitions);
        if (!string.IsNullOrWhiteSpace(group)) return group;
      }
      return string.Empty;
    }

    public static string ErrorForField(
      ValidationResult validation,
      string fieldKey)
    {
      ValidationMessage match = (validation?.Messages ?? Array.Empty<ValidationMessage>())
        .FirstOrDefault(message => message != null
          && message.Severity == ValidationSeverity.Error
          && string.Equals(message.FieldKey, fieldKey, StringComparison.Ordinal));
      return match?.Message ?? string.Empty;
    }

    public static string NormalizeGroup(string group)
    {
      if (string.Equals(group, "01_文件与阶段", StringComparison.Ordinal))
        return "01_文件与项目身份";
      return group ?? string.Empty;
    }

    public static string DisplayGroup(string group)
    {
      string normalized = NormalizeGroup(group);
      int separator = normalized.IndexOf('_');
      return separator >= 0 && separator + 1 < normalized.Length
        ? normalized.Substring(separator + 1)
        : normalized;
    }

    private static bool IsWriteFailure(string message)
    {
      if (string.IsNullOrWhiteSpace(message)) return false;
      return message.StartsWith("初始化失败", StringComparison.Ordinal)
        || message.StartsWith("写入失败", StringComparison.Ordinal)
        || message.IndexOf("事务已回滚", StringComparison.Ordinal) >= 0
        || message.IndexOf("写入或回读失败", StringComparison.Ordinal) >= 0;
    }

    private static string GroupForMessage(
      ValidationMessage message,
      IReadOnlyList<FieldDefinition> definitions)
    {
      if (message == null) return string.Empty;
      if (string.Equals(message.FieldKey, BlankConfirmationKey, StringComparison.Ordinal))
        return "11_提交与校验";
      FieldDefinition definition = FindDefinition(definitions, message.FieldKey);
      return NormalizeGroup(definition?.Group);
    }

    private static FieldDefinition FindDefinition(
      IReadOnlyList<FieldDefinition> definitions,
      string fieldKey)
    {
      return (definitions ?? Array.Empty<FieldDefinition>())
        .FirstOrDefault(definition => definition != null
          && string.Equals(definition.Key, fieldKey, StringComparison.Ordinal));
    }

    private static void AddDistinct(ICollection<string> result, string value)
    {
      if (string.IsNullOrWhiteSpace(value)) return;
      if (!result.Contains(value)) result.Add(value);
    }
  }
}
