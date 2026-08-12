using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal sealed class NativeStage03Stage01ValidationClassification
  {
    internal NativeStage03Stage01ValidationClassification(
      IEnumerable<string> technicalFatalCodes,
      IEnumerable<string> businessBlockers,
      IEnumerable<string> messages,
      bool hasProjectConditionError)
    {
      TechnicalFatalCodes = Freeze(technicalFatalCodes);
      BusinessBlockers = Freeze(businessBlockers);
      Messages = Freeze(messages);
      HasProjectConditionError = hasProjectConditionError;
    }

    internal IReadOnlyList<string> TechnicalFatalCodes { get; }
    internal IReadOnlyList<string> BusinessBlockers { get; }
    internal IReadOnlyList<string> Messages { get; }
    internal bool HasProjectConditionError { get; }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values)
    {
      return new ReadOnlyCollection<string>((values
        ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray());
    }
  }

  internal static class NativeStage03Stage01ValidationPolicy
  {
    internal static NativeStage03Stage01ValidationClassification Classify(
      NativeStage01ValidationResult validation,
      NativeRuleCatalog catalog)
    {
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));
      var technical = new SortedSet<string>(StringComparer.Ordinal);
      var business = new SortedSet<string>(StringComparer.Ordinal);
      var messages = new List<string>();
      bool hasProjectConditionError = false;

      if (validation == null)
      {
        technical.Add(NativeStage03Codes.Stage01Invalid
          + ":VALIDATION_UNAVAILABLE:-");
        messages.Add("STAGE01_VALIDATION_UNAVAILABLE｜Stage01 未返回校验结果。" );
        return new NativeStage03Stage01ValidationClassification(
          technical,
          business,
          messages,
          false);
      }

      foreach (NativeStage01ValidationMessage item in validation.Messages
        ?? Array.Empty<NativeStage01ValidationMessage>())
      {
        string code = string.IsNullOrWhiteSpace(item?.Code)
          ? "UNKNOWN"
          : item.Code.Trim();
        string fieldKey = (item?.FieldKey ?? string.Empty).Trim();
        if (string.Equals(
            code,
            NativeStage01ValidationCodes.ProjectConditionDeclarationMissing,
            StringComparison.Ordinal)
          || string.Equals(
            code,
            NativeStage01ValidationCodes.ProjectConditionDeclarationConflict,
            StringComparison.Ordinal))
        {
          hasProjectConditionError = true;
        }
        else if (IsTechnical(code, fieldKey))
        {
          technical.Add(Code(
            NativeStage03Codes.Stage01Invalid,
            code,
            fieldKey));
        }
        else
        {
          business.Add(Code(
            NativeStage03Codes.Stage01BusinessInvalid,
            code,
            fieldKey));
        }
        messages.Add(Describe(item, catalog));
      }

      return new NativeStage03Stage01ValidationClassification(
        technical,
        business,
        messages,
        hasProjectConditionError);
    }

    private static bool IsTechnical(string code, string fieldKey)
    {
      if (string.Equals(
        code,
        NativeStage01ValidationCodes.ConditionMissing,
        StringComparison.Ordinal))
        return true;
      if (string.Equals(
        code,
        NativeStage01ValidationCodes.UnknownModelProfile,
        StringComparison.Ordinal))
        return true;
      return string.Equals(
          code,
          NativeStage01ValidationCodes.InvalidGuid,
          StringComparison.Ordinal)
        && string.Equals(
          fieldKey,
          NativeStage01Keys.FileGuid,
          StringComparison.Ordinal);
    }

    private static string Describe(
      NativeStage01ValidationMessage item,
      NativeRuleCatalog catalog)
    {
      string code = string.IsNullOrWhiteSpace(item?.Code)
        ? "UNKNOWN"
        : item.Code.Trim();
      string fieldKey = (item?.FieldKey ?? string.Empty).Trim();
      string label = ResolveLabel(fieldKey, catalog);
      return code + "｜" + label + "｜" + fieldKey + "｜"
        + (item?.Message ?? string.Empty);
    }

    private static string ResolveLabel(string fieldKey, NativeRuleCatalog catalog)
    {
      if (catalog.Stage01FieldsByKey.TryGetValue(
        fieldKey ?? string.Empty,
        out NativeStage01FieldDefinition field))
      {
        return string.IsNullOrWhiteSpace(field.Label)
          ? fieldKey
          : field.Label;
      }
      NativeConditionDefinition condition = catalog.Conditions.FirstOrDefault(
        value => string.Equals(
          value.ConditionId,
          fieldKey,
          StringComparison.Ordinal));
      if (condition != null)
      {
        return string.IsNullOrWhiteSpace(condition.DisplayName)
          ? fieldKey
          : condition.DisplayName;
      }
      if (string.Equals(
        fieldKey,
        NativeProjectConditionDeclarationPolicy.NoneConditionId,
        StringComparison.Ordinal))
      {
        return NativeProjectConditionDeclarationPolicy.NoneDisplayName;
      }
      return fieldKey.Length == 0 ? "未指定字段" : fieldKey;
    }

    private static string Code(string prefix, string code, string fieldKey)
    {
      return (prefix ?? string.Empty) + ":" + (code ?? string.Empty)
        + ":" + (string.IsNullOrWhiteSpace(fieldKey) ? "-" : fieldKey);
    }
  }
}
