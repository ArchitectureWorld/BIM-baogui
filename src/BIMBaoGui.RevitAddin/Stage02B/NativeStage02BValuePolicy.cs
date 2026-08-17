using System;
using System.Globalization;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal sealed class NativeStage02BValueDecision
  {
    internal bool Accepted { get; set; }
    internal string CanonicalValue { get; set; } = string.Empty;
    internal string Code { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal static class NativeStage02BValuePolicy
  {
    internal static NativeStage02BValueDecision Validate(
      NativeStage02BMetricDefinition metric,
      string rawValue)
    {
      if (metric == null || metric.Property == null)
        return Reject("METRIC_DEFINITION_REQUIRED", "02B 指标定义不能为空。");
      string raw = (rawValue ?? string.Empty).Trim();
      if (raw.Length == 0)
        return Reject("VALUE_REQUIRED", "指标值不能为空。");

      string declaredType = (metric.Property.DeclaredIfcType ?? string.Empty).Trim();
      if (string.Equals(declaredType, "IfcInteger", StringComparison.Ordinal))
      {
        long integerValue;
        if (!long.TryParse(raw, NumberStyles.Integer,
          CultureInfo.InvariantCulture, out integerValue) || integerValue < 0)
          return Reject("INTEGER_NONNEGATIVE_REQUIRED", "指标必须为非负整数。");
        return Accept(integerValue.ToString(CultureInfo.InvariantCulture));
      }

      double realValue;
      if (!double.TryParse(raw, NumberStyles.Float,
        CultureInfo.InvariantCulture, out realValue)
        || double.IsNaN(realValue) || double.IsInfinity(realValue))
        return Reject("FINITE_REAL_REQUIRED", "指标必须为有限实数。");
      bool totalBuildingArea = string.Equals(metric.PropertyId,
        "ca21e324-046b-5bfd-84c8-0d3470082303", StringComparison.Ordinal);
      if (totalBuildingArea ? realValue <= 0d : realValue < 0d)
        return Reject(totalBuildingArea ? "TOTAL_BUILDING_AREA_POSITIVE_REQUIRED"
          : "REAL_NONNEGATIVE_REQUIRED", totalBuildingArea
            ? "总建筑面积必须大于零。" : "指标必须为非负实数。");
      return Accept(realValue.ToString("G17", CultureInfo.InvariantCulture));
    }

    private static NativeStage02BValueDecision Accept(string value)
    {
      return new NativeStage02BValueDecision { Accepted = true, CanonicalValue = value };
    }

    private static NativeStage02BValueDecision Reject(string code, string message)
    {
      return new NativeStage02BValueDecision { Code = code, Message = message };
    }
  }
}
