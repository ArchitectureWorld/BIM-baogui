using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BIMBaoGui.Stage01.Stage03
{
  public static class Stage03ExportGatePolicy
  {
    private const string InvalidFieldResultCode = "INVALID_FIELD_RESULT";
    private const string InvalidGateModeCode = "INVALID_GATE_MODE";
    private const string ForceReasonRequiredMessage =
      "Force 模式必须提供非空强制原因。";

    public static Stage03GateDecision Decide(
      Stage03GateMode mode,
      string forceReason,
      IEnumerable<Stage03FieldResult> fields,
      IEnumerable<string> technicalFatalCodes)
    {
      var normalizedTechnicalCodes = new SortedSet<string>(
        NormalizeTechnicalCodes(technicalFatalCodes),
        StringComparer.Ordinal);
      var candidates = new List<Stage03BusinessBlocker>();
      string reason = mode == Stage03GateMode.Force
        ? Normalize(forceReason)
        : string.Empty;
      bool forceReasonMissing = mode == Stage03GateMode.Force
        && reason.Length == 0;
      foreach (Stage03FieldResult field in
        fields ?? Array.Empty<Stage03FieldResult>())
      {
        if (field == null)
        {
          normalizedTechnicalCodes.Add(InvalidFieldResultCode);
          continue;
        }
        if (HasUndefinedStatus(field))
        {
          normalizedTechnicalCodes.Add(
            Stage03TechnicalFatalCodes.InvalidFieldStatus);
          continue;
        }
        if (!IsActiveBusinessBlocker(field)) continue;
        candidates.Add(new Stage03BusinessBlocker(
          Normalize(field.Entity),
          Normalize(field.OwnerUniqueId),
          Normalize(field.Role),
          field.ElementId,
          Normalize(field.PropertyId),
          field.Status,
          Normalize(field.Requirement),
          ResolveBusinessMessage(field)));
      }

      if (mode != Stage03GateMode.Strict && mode != Stage03GateMode.Force)
        normalizedTechnicalCodes.Add(InvalidGateModeCode);
      if (forceReasonMissing)
      {
        candidates.Add(new Stage03BusinessBlocker(
          string.Empty,
          string.Empty,
          string.Empty,
          0,
          string.Empty,
          Stage03FieldStatus.NotEvaluated,
          Stage03BusinessBlockerCodes.ForceReasonRequired,
          string.Empty,
          ForceReasonRequiredMessage));
      }

      Stage03BusinessBlocker[] blockers = candidates
        .GroupBy(BlockerIdentity, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(blocker => blocker.Entity, StringComparer.Ordinal)
        .ThenBy(blocker => blocker.OwnerUniqueId, StringComparer.Ordinal)
        .ThenBy(blocker => blocker.PropertyId, StringComparer.Ordinal)
        .ThenBy(blocker => blocker.StatusCode, StringComparer.Ordinal)
        .ThenBy(blocker => blocker.Role, StringComparer.Ordinal)
        .ThenBy(blocker => blocker.ElementId)
        .ThenBy(blocker => blocker.Requirement, StringComparer.Ordinal)
        .ThenBy(blocker => blocker.Message, StringComparer.Ordinal)
        .ToArray();
      string[] technical = normalizedTechnicalCodes.ToArray();
      bool hasTechnicalFatal = technical.Length > 0;
      bool allow = !hasTechnicalFatal
        && !forceReasonMissing
        && (mode == Stage03GateMode.Force || blockers.Length == 0);
      bool forced = allow
        && mode == Stage03GateMode.Force;

      var messages = new SortedSet<string>(StringComparer.Ordinal);
      if (forceReasonMissing)
        messages.Add(ForceReasonRequiredMessage);
      foreach (Stage03BusinessBlocker blocker in blockers)
      {
        if (string.Equals(
          blocker.StatusCode,
          Stage03BusinessBlockerCodes.ForceReasonRequired,
          StringComparison.Ordinal))
        {
          continue;
        }
        messages.Add(
          "业务阻断 [" + blocker.StatusCode + "] "
          + DisplayProperty(blocker.PropertyId) + "：" + blocker.Message);
      }
      foreach (string code in technical)
        messages.Add(TechnicalMessage(code));
      if (messages.Count == 0)
        messages.Add("字段业务门禁与技术门禁均允许导出。");

      return new Stage03GateDecision(
        mode,
        allow,
        forced,
        reason,
        Freeze(blockers),
        Freeze(technical),
        Freeze(messages));
    }

    private static bool IsActiveBusinessBlocker(Stage03FieldResult field)
    {
      return field.Active
        && (field.IsBusinessBlocker
          || field.Status == Stage03FieldStatus.UnclassifiedRequirement)
        && field.Status != Stage03FieldStatus.Pass
        && field.Status != Stage03FieldStatus.NotApplicable;
    }

    private static bool HasUndefinedStatus(Stage03FieldResult field)
    {
      return !IsDefinedStatus(field.Status)
        || !IsDefinedStatus(field.CarrierStatus)
        || !IsDefinedStatus(field.ParameterStatus)
        || !IsDefinedStatus(field.RevitStatus)
        || !IsDefinedStatus(field.RawIfcStatus)
        || !IsDefinedStatus(field.FinalIfcStatus);
    }

    private static bool IsDefinedStatus(Stage03FieldStatus status)
    {
      return Enum.IsDefined(typeof(Stage03FieldStatus), status);
    }

    private static string ResolveBusinessMessage(Stage03FieldResult field)
    {
      string explicitMessage = (field.Messages ?? Array.Empty<string>())
        .Select(Normalize)
        .Where(value => value.Length > 0)
        .OrderBy(value => value, StringComparer.Ordinal)
        .FirstOrDefault();
      return explicitMessage ?? StatusMessage(field.Status);
    }

    private static string StatusMessage(Stage03FieldStatus status)
    {
      switch (status)
      {
        case Stage03FieldStatus.MissingCarrier:
          return "未找到字段载体。";
        case Stage03FieldStatus.CarrierCategoryMismatch:
          return "字段载体类别不匹配。";
        case Stage03FieldStatus.CarrierNameMismatch:
          return "字段载体名称不匹配。";
        case Stage03FieldStatus.AmbiguousCarrier:
          return "字段载体不唯一。";
        case Stage03FieldStatus.MissingParameter:
          return "未找到字段参数。";
        case Stage03FieldStatus.EmptyRequiredValue:
          return "必填字段为空。";
        case Stage03FieldStatus.InvalidValue:
          return "字段值不符合合同。";
        case Stage03FieldStatus.RuleNotImplemented:
          return "字段规则尚未实现。";
        case Stage03FieldStatus.UnclassifiedRequirement:
          return "激活字段尚未分类。";
        case Stage03FieldStatus.IfcOwnerNotFound:
          return "IFC 中未找到属性所有者。";
        case Stage03FieldStatus.IfcValueMismatch:
          return "IFC 值与字段合同不一致。";
        default:
          return "字段未通过业务门禁。";
      }
    }

    private static string TechnicalMessage(string code)
    {
      switch (code)
      {
        case Stage03TechnicalFatalCodes.WrongDocument:
          return "技术致命错误 [WRONG_DOCUMENT]：活动文档身份不一致。";
        case Stage03TechnicalFatalCodes.UnsupportedRevit:
          return "技术致命错误 [UNSUPPORTED_REVIT]：Revit 版本不受支持。";
        case Stage03TechnicalFatalCodes.DocumentUnavailable:
          return "技术致命错误 [DOCUMENT_UNAVAILABLE]：活动文档不可用。";
        case Stage03TechnicalFatalCodes.OutputExists:
          return "技术致命错误 [OUTPUT_EXISTS]：正式输出路径已存在。";
        case Stage03TechnicalFatalCodes.ExportFailed:
          return "技术致命错误 [EXPORT_FAILED]：IFC 导出失败。";
        case Stage03TechnicalFatalCodes.InvalidIfc:
          return "技术致命错误 [INVALID_IFC]：IFC 无效或无法解析。";
        case Stage03TechnicalFatalCodes.ReportFailed:
          return "技术致命错误 [REPORT_FAILED]：报告写入失败。";
        case Stage03TechnicalFatalCodes.InvalidFieldStatus:
          return "技术致命错误 [INVALID_FIELD_STATUS]：字段状态不在已定义合同内。";
        default:
          return "技术致命错误 [" + code + "]：未知非空技术码，已按 fail-closed 拒绝导出。";
      }
    }

    private static IEnumerable<string> NormalizeTechnicalCodes(
      IEnumerable<string> values)
    {
      return (values ?? Array.Empty<string>())
        .Select(value => Normalize(value).ToUpperInvariant())
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal);
    }

    private static string BlockerIdentity(Stage03BusinessBlocker blocker)
    {
      var builder = new StringBuilder();
      AppendIdentitySegment(builder, blocker.Entity);
      AppendIdentitySegment(builder, blocker.OwnerUniqueId);
      AppendIdentitySegment(builder, blocker.Role);
      AppendIdentitySegment(
        builder,
        blocker.ElementId.ToString(CultureInfo.InvariantCulture));
      AppendIdentitySegment(builder, blocker.PropertyId);
      AppendIdentitySegment(builder, blocker.StatusCode);
      AppendIdentitySegment(builder, blocker.Requirement);
      AppendIdentitySegment(builder, blocker.Message);
      return builder.ToString();
    }

    private static void AppendIdentitySegment(
      StringBuilder builder,
      string value)
    {
      string text = value ?? string.Empty;
      builder.Append(text.Length.ToString(CultureInfo.InvariantCulture));
      builder.Append(':');
      builder.Append(text);
    }

    private static string DisplayProperty(string propertyId)
    {
      return propertyId.Length == 0 ? "<未命名字段>" : propertyId;
    }

    private static string Normalize(string value)
    {
      return (value ?? string.Empty).Trim();
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
    {
      return new ReadOnlyCollection<T>((values ?? Array.Empty<T>()).ToArray());
    }
  }
}
