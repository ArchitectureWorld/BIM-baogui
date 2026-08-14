using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02SemanticAssignmentSchema
  {
    internal const string Version = "1.0.0";
  }

  internal sealed class NativeStage02SemanticAssignmentRecord
  {
    internal string ElementUniqueId { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;
    internal NativeStage02AssignmentMode AssignmentMode { get; set; }
    internal string CarrierCategory { get; set; } = string.Empty;
    internal string CarrierElementKind { get; set; } = string.Empty;

    internal NativeStage02SemanticAssignmentRecord Clone()
    {
      return new NativeStage02SemanticAssignmentRecord
      {
        ElementUniqueId = ElementUniqueId ?? string.Empty,
        RoleId = RoleId ?? string.Empty,
        AssignmentMode = AssignmentMode,
        CarrierCategory = CarrierCategory ?? string.Empty,
        CarrierElementKind = CarrierElementKind ?? string.Empty
      };
    }
  }

  internal sealed class NativeStage02SemanticAssignmentPayload
  {
    internal string SchemaVersion { get; set; } =
      NativeStage02SemanticAssignmentSchema.Version;
    internal string RulePackageId { get; set; } = string.Empty;
    internal string RulePackageVersion { get; set; } = string.Empty;
    internal IReadOnlyList<NativeStage02SemanticAssignmentRecord> Assignments
    {
      get;
      set;
    } = Array.Empty<NativeStage02SemanticAssignmentRecord>();
  }

  internal static class NativeStage02SemanticAssignmentCanonicalizer
  {
    internal static NativeStage02SemanticAssignmentPayload Normalize(
      NativeStage02SemanticAssignmentPayload payload)
    {
      if (payload == null) throw new ArgumentNullException(nameof(payload));
      var byElement = new Dictionary<string, NativeStage02SemanticAssignmentRecord>(
        StringComparer.Ordinal);
      foreach (NativeStage02SemanticAssignmentRecord raw in
        payload.Assignments ?? Array.Empty<NativeStage02SemanticAssignmentRecord>())
      {
        if (raw == null) continue;
        var record = new NativeStage02SemanticAssignmentRecord
        {
          ElementUniqueId = Clean(raw.ElementUniqueId),
          RoleId = Clean(raw.RoleId),
          AssignmentMode = raw.AssignmentMode,
          CarrierCategory = Clean(raw.CarrierCategory),
          CarrierElementKind = Clean(raw.CarrierElementKind)
        };
        if (record.ElementUniqueId.Length == 0)
          throw new InvalidOperationException(
            "SEMANTIC_ASSIGNMENT_ELEMENT_ID_REQUIRED");
        if (record.RoleId.Length == 0)
          throw new InvalidOperationException(
            "SEMANTIC_ASSIGNMENT_ROLE_ID_REQUIRED");
        NativeStage02SemanticAssignmentRecord existing;
        if (byElement.TryGetValue(record.ElementUniqueId, out existing))
        {
          if (!Equivalent(existing, record))
            throw new InvalidOperationException(
              "SEMANTIC_ASSIGNMENT_DUPLICATE_CONFLICT:"
                + record.ElementUniqueId);
          continue;
        }
        byElement[record.ElementUniqueId] = record;
      }

      return new NativeStage02SemanticAssignmentPayload
      {
        SchemaVersion = Clean(payload.SchemaVersion),
        RulePackageId = Clean(payload.RulePackageId),
        RulePackageVersion = Clean(payload.RulePackageVersion),
        Assignments = new ReadOnlyCollection<NativeStage02SemanticAssignmentRecord>(
          byElement.Values
            .OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)
            .Select(value => value.Clone())
            .ToArray())
      };
    }

    internal static string SerializeCanonical(
      NativeStage02SemanticAssignmentPayload payload)
    {
      NativeStage02SemanticAssignmentPayload normalized = Normalize(payload);
      var builder = new StringBuilder();
      builder.Append('{');
      AppendProperty(builder, "schemaVersion", normalized.SchemaVersion);
      builder.Append(',');
      AppendProperty(builder, "rulePackageId", normalized.RulePackageId);
      builder.Append(',');
      AppendProperty(builder, "rulePackageVersion", normalized.RulePackageVersion);
      builder.Append(",\"assignments\":[");
      for (int index = 0; index < normalized.Assignments.Count; index++)
      {
        if (index > 0) builder.Append(',');
        NativeStage02SemanticAssignmentRecord record = normalized.Assignments[index];
        builder.Append('{');
        AppendProperty(builder, "elementUniqueId", record.ElementUniqueId);
        builder.Append(',');
        AppendProperty(builder, "roleId", record.RoleId);
        builder.Append(',');
        AppendProperty(
          builder,
          "assignmentMode",
          record.AssignmentMode == NativeStage02AssignmentMode.Manual
            ? "MANUAL"
            : "AUTO");
        builder.Append(',');
        AppendProperty(builder, "carrierCategory", record.CarrierCategory);
        builder.Append(',');
        AppendProperty(builder, "carrierElementKind", record.CarrierElementKind);
        builder.Append('}');
      }
      builder.Append("]}");
      return builder.ToString();
    }

    internal static string Sha256(string value)
    {
      using (SHA256 sha = SHA256.Create())
      {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        return string.Concat(sha.ComputeHash(bytes).Select(valueByte =>
          valueByte.ToString("x2", CultureInfo.InvariantCulture)));
      }
    }

    internal static NativeStage02SemanticAssignmentPayload Upsert(
      NativeStage02SemanticAssignmentPayload payload,
      NativeStage02SemanticAssignmentRecord record)
    {
      NativeStage02SemanticAssignmentPayload normalized = Normalize(payload);
      var records = normalized.Assignments
        .Where(value => !string.Equals(
          value.ElementUniqueId,
          Clean(record?.ElementUniqueId),
          StringComparison.Ordinal))
        .Select(value => value.Clone())
        .ToList();
      if (record != null) records.Add(record.Clone());
      normalized.Assignments = records;
      return Normalize(normalized);
    }

    internal static NativeStage02SemanticAssignmentPayload Remove(
      NativeStage02SemanticAssignmentPayload payload,
      string elementUniqueId)
    {
      NativeStage02SemanticAssignmentPayload normalized = Normalize(payload);
      string key = Clean(elementUniqueId);
      normalized.Assignments = new ReadOnlyCollection<NativeStage02SemanticAssignmentRecord>(
        normalized.Assignments
          .Where(value => !string.Equals(
            value.ElementUniqueId,
            key,
            StringComparison.Ordinal))
          .Select(value => value.Clone())
          .ToArray());
      return normalized;
    }

    private static bool Equivalent(
      NativeStage02SemanticAssignmentRecord left,
      NativeStage02SemanticAssignmentRecord right)
    {
      return string.Equals(left.RoleId, right.RoleId, StringComparison.Ordinal)
        && left.AssignmentMode == right.AssignmentMode
        && string.Equals(
          left.CarrierCategory,
          right.CarrierCategory,
          StringComparison.Ordinal)
        && string.Equals(
          left.CarrierElementKind,
          right.CarrierElementKind,
          StringComparison.Ordinal);
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }

    private static void AppendProperty(
      StringBuilder builder,
      string name,
      string value)
    {
      builder.Append('"');
      builder.Append(Escape(name));
      builder.Append("\":\"");
      builder.Append(Escape(value ?? string.Empty));
      builder.Append('"');
    }

    private static string Escape(string value)
    {
      var builder = new StringBuilder();
      foreach (char character in value ?? string.Empty)
      {
        switch (character)
        {
          case '"': builder.Append("\\\""); break;
          case '\\': builder.Append("\\\\"); break;
          case '\b': builder.Append("\\b"); break;
          case '\f': builder.Append("\\f"); break;
          case '\n': builder.Append("\\n"); break;
          case '\r': builder.Append("\\r"); break;
          case '\t': builder.Append("\\t"); break;
          default:
            if (character < 0x20)
            {
              builder.Append("\\u");
              builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            }
            else builder.Append(character);
            break;
        }
      }
      return builder.ToString();
    }
  }
}
