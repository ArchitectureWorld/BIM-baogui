using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal enum NativeStage02SemanticSuggestionStatus
  {
    Suggested,
    PendingInput
  }

  internal sealed class NativeStage02SemanticSuggestionDecision
  {
    internal NativeStage02SemanticSuggestionStatus Status { get; set; }
    internal string CanonicalValue { get; set; } = string.Empty;
    internal string Source { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal static class NativeStage02SemanticValueSuggestionPolicy
  {
    internal static NativeStage02SemanticSuggestionDecision Evaluate(
      string suggestionKind,
      IEnumerable<string> approvedValues,
      string typeName,
      double? approvedAreaSquareMetres)
    {
      string kind = Clean(suggestionKind);
      string[] values = (approvedValues ?? Array.Empty<string>())
        .Select(Clean)
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

      if (string.Equals(kind, "SYSTEM_FIXED", StringComparison.Ordinal))
      {
        if (values.Length != 1)
          return Pending("系统固定值合同必须且只能声明一个批准值。" );
        return Suggested(values[0], "SystemFixed");
      }

      if (string.Equals(kind, "EXACT_ENUM_FROM_TYPE", StringComparison.Ordinal))
      {
        string candidate = Clean(typeName);
        string exact = values.FirstOrDefault(value => string.Equals(
          value,
          candidate,
          StringComparison.Ordinal));
        return exact == null
          ? Pending("Revit 类型名未精确命中批准枚举，不自动猜测。" )
          : Suggested(exact, "ExactTypeName");
      }

      if (string.Equals(kind, "APPROVED_REVIT_AREA", StringComparison.Ordinal))
      {
        if (!approvedAreaSquareMetres.HasValue
          || double.IsNaN(approvedAreaSquareMetres.Value)
          || double.IsInfinity(approvedAreaSquareMetres.Value)
          || approvedAreaSquareMetres.Value <= 0.0)
          return Pending("没有可靠的 Revit 投影面积来源，保持待填写。" );
        return Suggested(
          approvedAreaSquareMetres.Value.ToString(
            "R",
            CultureInfo.InvariantCulture),
          "ApprovedRevitAreaM2");
      }

      if (string.Equals(kind, "PENDING_INPUT", StringComparison.Ordinal))
        return Pending("该业务值必须由用户确认，不生成默认值。" );

      return Pending("当前建议值策略未批准自动写入。" );
    }

    private static NativeStage02SemanticSuggestionDecision Suggested(
      string value,
      string source)
    {
      return new NativeStage02SemanticSuggestionDecision
      {
        Status = NativeStage02SemanticSuggestionStatus.Suggested,
        CanonicalValue = value ?? string.Empty,
        Source = source ?? string.Empty
      };
    }

    private static NativeStage02SemanticSuggestionDecision Pending(
      string message)
    {
      return new NativeStage02SemanticSuggestionDecision
      {
        Status = NativeStage02SemanticSuggestionStatus.PendingInput,
        Message = message ?? string.Empty
      };
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }
}
