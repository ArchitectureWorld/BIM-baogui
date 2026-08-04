using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class Stage03FieldDetail
  {
    internal Stage03FieldDetail(
      int carrierIndex,
      int fieldIndex,
      string propertyId,
      Stage03FieldStatus status,
      string text)
    {
      CarrierIndex = carrierIndex;
      FieldIndex = fieldIndex;
      PropertyId = propertyId ?? string.Empty;
      Status = status;
      Text = text ?? string.Empty;
    }

    internal int CarrierIndex { get; }
    internal int FieldIndex { get; }
    internal string PropertyId { get; }
    internal Stage03FieldStatus Status { get; }
    internal string Text { get; }
  }

  internal static class Stage03FieldDetailFormatter
  {
    internal static IReadOnlyList<Stage03FieldDetail> Format(
      IEnumerable<Stage03FieldResult> fields)
    {
      var result = new List<Stage03FieldDetail>();
      var carriers = (fields ?? Array.Empty<Stage03FieldResult>())
        .Where(field => field != null)
        .GroupBy(field => new
        {
          Entity = field.Entity ?? string.Empty,
          Role = field.Role ?? string.Empty,
          field.ElementId,
          Owner = field.OwnerUniqueId ?? string.Empty
        })
        .OrderBy(group => group.Key.Entity, StringComparer.Ordinal)
        .ThenBy(group => group.Key.Role, StringComparer.Ordinal)
        .ThenBy(group => group.Key.ElementId)
        .ThenBy(group => group.Key.Owner, StringComparer.Ordinal)
        .ToArray();
      for (int carrierIndex = 0;
        carrierIndex < carriers.Length;
        carrierIndex++)
      {
        Stage03FieldResult[] orderedFields = carriers[carrierIndex]
          .OrderBy(field => field.PropertyId, StringComparer.Ordinal)
          .ThenBy(field => field.PropertySet, StringComparer.Ordinal)
          .ThenBy(field => field.IfcProperty, StringComparer.Ordinal)
          .ToArray();
        for (int fieldIndex = 0;
          fieldIndex < orderedFields.Length;
          fieldIndex++)
        {
          Stage03FieldResult field = orderedFields[fieldIndex];
          result.Add(new Stage03FieldDetail(
            carrierIndex,
            fieldIndex,
            field.PropertyId,
            field.Status,
            FormatField(field)));
        }
      }
      return Freeze(result);
    }

    internal static IReadOnlyList<string> FormatAllBlockers(
      Stage03GateDecision gate,
      IEnumerable<string> technicalFatalCodes,
      IEnumerable<Stage03Diagnostic> diagnostics,
      IEnumerable<string> messages)
    {
      var result = new List<string>();
      if (gate != null)
      {
        result.AddRange(gate.BusinessBlockers
          .Where(blocker => blocker != null)
          .OrderBy(blocker => blocker.Entity, StringComparer.Ordinal)
          .ThenBy(blocker => blocker.OwnerUniqueId, StringComparer.Ordinal)
          .ThenBy(blocker => blocker.Role, StringComparer.Ordinal)
          .ThenBy(blocker => blocker.ElementId)
          .ThenBy(blocker => blocker.PropertyId, StringComparer.Ordinal)
          .ThenBy(blocker => blocker.StatusCode, StringComparer.Ordinal)
          .Select(blocker => "业务阻断|" + blocker.Entity
            + "|owner=" + blocker.OwnerUniqueId
            + "|role=" + blocker.Role
            + "|element=" + blocker.ElementId.ToString(
              CultureInfo.InvariantCulture)
            + "|property=" + blocker.PropertyId
            + "|status=" + blocker.StatusCode
            + "|requirement=" + blocker.Requirement
            + "|message=" + blocker.Message));
      }
      result.AddRange((technicalFatalCodes ?? Array.Empty<string>())
        .Where(code => !string.IsNullOrWhiteSpace(code))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(code => code, StringComparer.Ordinal)
        .Select(code => "技术致命|" + code));
      result.AddRange((diagnostics ?? Array.Empty<Stage03Diagnostic>())
        .Where(diagnostic => diagnostic != null)
        .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.Stage, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.Severity, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
        .Select(diagnostic => "诊断|" + (diagnostic.Code ?? string.Empty)
          + "|" + (diagnostic.Stage ?? string.Empty)
          + "|" + (diagnostic.Severity ?? string.Empty)
          + "|" + (diagnostic.Message ?? string.Empty)));
      result.AddRange((messages ?? Array.Empty<string>())
        .Where(message => !string.IsNullOrWhiteSpace(message))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(message => message, StringComparer.Ordinal)
        .Select(message => "消息|" + message));
      return Freeze(result);
    }

    private static string FormatField(Stage03FieldResult field)
    {
      return "实体=" + (field.Entity ?? string.Empty)
        + "｜owner=" + (field.OwnerUniqueId ?? string.Empty)
        + "｜role=" + (field.Role ?? string.Empty)
        + "｜element=" + field.ElementId.ToString(CultureInfo.InvariantCulture)
        + "｜property=" + (field.PropertyId ?? string.Empty)
        + "｜ifc=" + (field.PropertySet ?? string.Empty)
        + "." + (field.IfcProperty ?? string.Empty)
        + "｜status=" + Stage03FieldStatusCodes.ToCode(field.Status)
        + "｜RAW=" + FormatIfcEvidence(
          field.RawIfcStatus,
          field.RawIfcOwner,
          field.RawIfcPropertySet,
          field.RawIfcProperty,
          field.RawIfcType,
          field.RawIfcValue)
        + "｜FINAL=" + FormatIfcEvidence(
          field.FinalIfcStatus,
          field.FinalIfcOwner,
          field.FinalIfcPropertySet,
          field.FinalIfcProperty,
          field.FinalIfcType,
          field.FinalIfcValue);
    }

    private static string FormatIfcEvidence(
      Stage03FieldStatus status,
      string owner,
      string propertySet,
      string property,
      string declaredType,
      string value)
    {
      return Stage03FieldStatusCodes.ToCode(status)
        + "|" + (owner ?? string.Empty)
        + "|" + (propertySet ?? string.Empty)
        + "." + (property ?? string.Empty)
        + "|" + (declaredType ?? string.Empty)
        + "|" + (value ?? string.Empty);
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
    {
      return new ReadOnlyCollection<T>(
        (values ?? Enumerable.Empty<T>()).ToArray());
    }
  }
}
