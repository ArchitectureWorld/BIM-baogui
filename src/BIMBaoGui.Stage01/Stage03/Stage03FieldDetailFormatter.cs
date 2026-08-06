using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Web.Script.Serialization;

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
      IEnumerable<Stage03Diagnostic> diagnostics)
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
          .Select(blocker => SerializeRecord(
            Property("kind", "业务阻断"),
            Property("entity", blocker.Entity),
            Property("ownerUniqueId", blocker.OwnerUniqueId),
            Property("role", blocker.Role),
            Property("elementId", blocker.ElementId),
            Property("propertyId", blocker.PropertyId),
            Property("status", blocker.StatusCode),
            Property("requirement", blocker.Requirement),
            Property("message", blocker.Message))));
      }
      result.AddRange((technicalFatalCodes ?? Array.Empty<string>())
        .Where(code => !string.IsNullOrWhiteSpace(code))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(code => code, StringComparer.Ordinal)
        .Select(code => SerializeRecord(
          Property("kind", "技术致命"),
          Property("code", code))));
      result.AddRange((diagnostics ?? Array.Empty<Stage03Diagnostic>())
        .Where(diagnostic => diagnostic != null
          && Stage03BlockingDiagnosticPolicy.IsBlocking(diagnostic))
        .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.Stage, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.Severity, StringComparer.Ordinal)
        .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
        .Select(diagnostic => SerializeRecord(
          Property("kind", "阻断级诊断"),
          Property("code", diagnostic.Code),
          Property("stage", diagnostic.Stage),
          Property("severity", diagnostic.Severity),
          Property("message", diagnostic.Message))));
      return Freeze(result);
    }

    internal static IReadOnlyList<string> FormatComponentFailure(
      string code,
      string stage,
      string message,
      IEnumerable<string> technicalFatalCodes = null)
    {
      return FormatAllBlockers(
        null,
        technicalFatalCodes,
        new[]
        {
          new Stage03Diagnostic
          {
            Code = code ?? string.Empty,
            Stage = stage ?? string.Empty,
            Severity = "ERROR",
            Message = message ?? string.Empty
          }
        });
    }

    private static string FormatField(Stage03FieldResult field)
    {
      return SerializeRecord(
        Property("propertyId", field.PropertyId),
        Property("contractKind", field.ContractKind),
        Property("requirement", field.Requirement),
        Property("applicability", field.Applicability),
        Property("entity", field.Entity),
        Property("propertySet", field.PropertySet),
        Property("ifcProperty", field.IfcProperty),
        Property("role", field.Role),
        Property("elementId", field.ElementId),
        Property("ownerUniqueId", field.OwnerUniqueId),
        Property("parameterGuid", field.ParameterGuid),
        Property("parameterName", field.ParameterName),
        Property("parameterScope", field.ParameterScope),
        Property(
          "carrierStatus",
          Stage03FieldStatusCodes.ToCode(field.CarrierStatus)),
        Property(
          "parameterStatus",
          Stage03FieldStatusCodes.ToCode(field.ParameterStatus)),
        Property(
          "revitStatus",
          Stage03FieldStatusCodes.ToCode(field.RevitStatus)),
        Property("revitRawValue", field.RevitRawValue),
        Property("revitNormalizedValue", field.RevitNormalizedValue),
        Property("revitValueSource", field.RevitValueSource),
        Property("rawIfcOwner", field.RawIfcOwner),
        Property("rawIfcPropertySet", field.RawIfcPropertySet),
        Property("rawIfcProperty", field.RawIfcProperty),
        Property("rawIfcType", field.RawIfcType),
        Property("rawIfcValue", field.RawIfcValue),
        Property(
          "rawIfcStatus",
          Stage03FieldStatusCodes.ToCode(field.RawIfcStatus)),
        Property("finalIfcOwner", field.FinalIfcOwner),
        Property("finalIfcPropertySet", field.FinalIfcPropertySet),
        Property("finalIfcProperty", field.FinalIfcProperty),
        Property("finalIfcType", field.FinalIfcType),
        Property("finalIfcValue", field.FinalIfcValue),
        Property(
          "finalIfcStatus",
          Stage03FieldStatusCodes.ToCode(field.FinalIfcStatus)),
        Property("status", Stage03FieldStatusCodes.ToCode(field.Status)),
        Property("active", field.Active),
        Property("isBusinessBlocker", field.IsBusinessBlocker),
        Property("messages", SortMessages(field.Messages)));
    }

    private static string[] SortMessages(IEnumerable<string> messages)
    {
      return (messages ?? Array.Empty<string>())
        .Select(message => message ?? string.Empty)
        .OrderBy(message => message, StringComparer.Ordinal)
        .ToArray();
    }

    private static KeyValuePair<string, object> Property(
      string name,
      object value)
    {
      return new KeyValuePair<string, object>(
        name,
        value ?? string.Empty);
    }

    private static string SerializeRecord(
      params KeyValuePair<string, object>[] properties)
    {
      var record = new SortedDictionary<string, object>(
        StringComparer.Ordinal);
      foreach (KeyValuePair<string, object> property in
        properties ?? Array.Empty<KeyValuePair<string, object>>())
      {
        record.Add(property.Key, property.Value);
      }
      return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
        .Serialize(record);
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
    {
      return new ReadOnlyCollection<T>(
        (values ?? Enumerable.Empty<T>()).ToArray());
    }
  }
}
