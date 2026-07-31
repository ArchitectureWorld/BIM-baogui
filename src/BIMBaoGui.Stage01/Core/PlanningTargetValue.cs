using System;
using System.Globalization;

namespace BIMBaoGui.Stage01.Core
{
  public enum PlanningTargetOperator
  {
    LessOrEqual,
    GreaterOrEqual,
    Equal,
    Range
  }

  public enum PlanningTargetUnit
  {
    Percent,
    Ratio,
    Count
  }

  public enum PlanningTargetRequirement
  {
    Required,
    Conditional,
    Optional,
    Inherited,
    NotApplicable
  }

  public sealed class PlanningTargetValue
  {
    internal PlanningTargetValue(
      string metricCode,
      PlanningTargetOperator @operator,
      decimal value1,
      decimal? value2,
      PlanningTargetUnit unit,
      string source)
    {
      MetricCode = metricCode ?? string.Empty;
      Operator = @operator;
      Value1 = value1;
      Value2 = value2;
      Unit = unit;
      Source = source ?? string.Empty;
    }

    public string MetricCode { get; }
    public PlanningTargetOperator Operator { get; }
    public decimal Value1 { get; }
    public decimal? Value2 { get; }
    public PlanningTargetUnit Unit { get; }
    public string Source { get; }

    public static bool TryCreate(
      string metricCode,
      PlanningTargetOperator @operator,
      string value1,
      string value2,
      PlanningTargetUnit unit,
      string source,
      out PlanningTargetValue target,
      out string error)
    {
      target = null;
      error = string.Empty;

      if (string.IsNullOrWhiteSpace(metricCode))
      {
        error = "规划指标代码不能为空。";
        return false;
      }

      if (!TryDecimal(value1, out decimal first))
      {
        error = "应填写数值，例如 30。";
        return false;
      }

      decimal? second = null;
      if (@operator == PlanningTargetOperator.Range)
      {
        if (!TryDecimal(value2, out decimal parsedSecond))
        {
          error = "区间上限必须填写数值。";
          return false;
        }
        if (parsedSecond < first)
        {
          error = "区间上限不得小于下限。";
          return false;
        }
        second = parsedSecond;
      }

      if (unit == PlanningTargetUnit.Percent)
      {
        if (!Within(first, 0m, 100m) || (second.HasValue && !Within(second.Value, 0m, 100m)))
        {
          error = "百分比必须位于 0 到 100。";
          return false;
        }
      }
      else if (first < 0m || (second.HasValue && second.Value < 0m))
      {
        error = "数值不得小于 0。";
        return false;
      }

      if (unit == PlanningTargetUnit.Count)
      {
        if (first != decimal.Truncate(first) || (second.HasValue && second.Value != decimal.Truncate(second.Value)))
        {
          error = "数量必须填写整数。";
          return false;
        }
      }

      target = new PlanningTargetValue(metricCode.Trim(), @operator, first, second, unit, source);
      return true;
    }

    public static bool TryParseMvdText(
      string metricCode,
      string text,
      PlanningTargetUnit unit,
      string source,
      out PlanningTargetValue target,
      out string error)
    {
      target = null;
      error = string.Empty;
      string normalized = (text ?? string.Empty).Trim().Replace("％", "%");
      if (unit == PlanningTargetUnit.Percent && normalized.EndsWith("%", StringComparison.Ordinal))
        normalized = normalized.Substring(0, normalized.Length - 1).Trim();
      if (normalized.Length == 0)
      {
        error = "规划指标值为空。";
        return false;
      }

      PlanningTargetOperator @operator;
      string firstText;
      string secondText = null;
      if (normalized.StartsWith("≤", StringComparison.Ordinal) || normalized.StartsWith("<=", StringComparison.Ordinal))
      {
        @operator = PlanningTargetOperator.LessOrEqual;
        firstText = normalized.StartsWith("<=", StringComparison.Ordinal) ? normalized.Substring(2) : normalized.Substring(1);
      }
      else if (normalized.StartsWith("≥", StringComparison.Ordinal) || normalized.StartsWith(">=", StringComparison.Ordinal))
      {
        @operator = PlanningTargetOperator.GreaterOrEqual;
        firstText = normalized.StartsWith(">=", StringComparison.Ordinal) ? normalized.Substring(2) : normalized.Substring(1);
      }
      else if (normalized.StartsWith("=", StringComparison.Ordinal))
      {
        @operator = PlanningTargetOperator.Equal;
        firstText = normalized.Substring(1);
      }
      else
      {
        string[] range = normalized.Split(new[] { '–', '—', '~', '～' }, StringSplitOptions.RemoveEmptyEntries);
        if (range.Length == 2)
        {
          @operator = PlanningTargetOperator.Range;
          firstText = range[0];
          secondText = range[1];
        }
        else
        {
          @operator = PlanningTargetOperator.Equal;
          firstText = normalized;
        }
      }

      return TryCreate(metricCode, @operator, firstText.Trim(), secondText?.Trim(), unit, source, out target, out error);
    }

    public string ToMvdText()
    {
      if (Operator == PlanningTargetOperator.Range)
      {
        string lower = Format(Value1);
        string upper = Format(Value2 ?? Value1);
        string suffix = Unit == PlanningTargetUnit.Percent ? "%" : string.Empty;
        return lower + "–" + upper + suffix;
      }

      string symbol;
      switch (Operator)
      {
        case PlanningTargetOperator.LessOrEqual: symbol = "≤"; break;
        case PlanningTargetOperator.GreaterOrEqual: symbol = "≥"; break;
        default: symbol = "="; break;
      }

      return symbol + Format(Value1) + (Unit == PlanningTargetUnit.Percent ? "%" : string.Empty);
    }

    public string ToCanonicalToken()
    {
      return string.Join("|", new[]
      {
        MetricCode,
        Operator.ToString(),
        Value1.ToString(CultureInfo.InvariantCulture),
        Value2.HasValue ? Value2.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
        Unit.ToString(),
        Source
      });
    }

    public override string ToString()
    {
      return ToMvdText();
    }

    private string Format(decimal value)
    {
      switch (Unit)
      {
        case PlanningTargetUnit.Ratio:
          return value.ToString("0.00", CultureInfo.InvariantCulture);
        case PlanningTargetUnit.Count:
          return value.ToString("0", CultureInfo.InvariantCulture);
        default:
          return value.ToString("0.##", CultureInfo.InvariantCulture);
      }
    }

    private static bool TryDecimal(string value, out decimal result)
    {
      return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result)
        || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result);
    }

    private static bool Within(decimal value, decimal minimum, decimal maximum)
    {
      return value >= minimum && value <= maximum;
    }
  }
}
