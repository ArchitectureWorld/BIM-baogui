using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.RevitAddin.Workflow
{
  internal static class NativeWorkflowResultCanonicalizer
  {
    internal static NativeWorkflowResultEnvelope Build(
      string runId,
      string sourceFeature,
      string sourceFunction,
      NativeWorkflowIdentity identity,
      string inputSnapshotHash,
      IEnumerable<NativeWorkflowItemEvidence> items,
      string updatedUtc)
    {
      Require(runId, nameof(runId));
      Require(sourceFeature, nameof(sourceFeature));
      Require(sourceFunction, nameof(sourceFunction));
      ValidateIdentity(identity);
      RequireHash(inputSnapshotHash, nameof(inputSnapshotHash));

      NativeWorkflowItemEvidence[] normalizedItems = (items
          ?? throw new ArgumentNullException(nameof(items)))
        .Select(CloneAndNormalizeItem)
        .OrderBy(value => value.Identity, StringComparer.Ordinal)
        .ToArray();
      string duplicateIdentity = normalizedItems
        .GroupBy(value => value.Identity, StringComparer.Ordinal)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .FirstOrDefault();
      if (duplicateIdentity != null)
      {
        throw new ArgumentException(
          "Workflow result contains a duplicate item identity: "
            + duplicateIdentity,
          nameof(items));
      }

      var result = new NativeWorkflowResultEnvelope
      {
        SchemaVersion = NativeWorkflowResultEnvelope.CurrentSchemaVersion,
        RunId = runId.Trim(),
        SourceFeature = sourceFeature.Trim(),
        SourceFunction = sourceFunction.Trim(),
        Identity = CloneIdentity(identity),
        InputSnapshotHash = inputSnapshotHash.Trim(),
        UpdatedUtc = NormalizeUtc(updatedUtc, nameof(updatedUtc)),
        Items = new ReadOnlyCollection<NativeWorkflowItemEvidence>(
          normalizedItems)
      };
      result.CanonicalJson = SerializeCanonical(result);
      result.ResultHash = NativeWorkflowIdentityFactory.Sha256(
        result.CanonicalJson);
      return result;
    }

    internal static string ComputeResultHash(
      NativeWorkflowResultEnvelope result)
    {
      if (result == null) throw new ArgumentNullException(nameof(result));
      ValidateIdentity(result.Identity);
      NativeWorkflowItemEvidence[] items = (result.Items
          ?? Array.Empty<NativeWorkflowItemEvidence>())
        .ToArray();
      if (items.Any(item => item == null))
        throw new ArgumentException("Workflow result contains a null item.", nameof(result));
      if (items.Any(item => !string.Equals(
        item.StableHash,
        ComputeItemHash(item),
        StringComparison.Ordinal)))
      {
        throw new ArgumentException(
          "Workflow item stable hash does not match its canonical content.",
          nameof(result));
      }
      return NativeWorkflowIdentityFactory.Sha256(SerializeCanonical(result));
    }

    internal static NativeWorkflowResultEnvelope ParseCanonical(
      string canonicalJson,
      string resultHash)
    {
      Require(canonicalJson, nameof(canonicalJson));
      RequireHash(resultHash, nameof(resultHash));
      Dictionary<string, object> root;
      try
      {
        root = new JavaScriptSerializer
        {
          MaxJsonLength = 8 * 1024 * 1024,
          RecursionLimit = 128
        }.Deserialize<Dictionary<string, object>>(canonicalJson);
      }
      catch (Exception exception)
      {
        throw new FormatException(
          "Workflow result canonical JSON cannot be parsed.",
          exception);
      }
      if (root == null)
        throw new FormatException("Workflow result canonical JSON is empty.");

      Dictionary<string, object> identityValue = GetObject(root, "identity");
      var identity = new NativeWorkflowIdentity
      {
        DocumentFingerprint = GetString(identityValue, "documentFingerprint"),
        ModelFileType = GetString(identityValue, "modelFileType"),
        RulePackageId = GetString(identityValue, "rulePackageId"),
        RulePackageVersion = GetString(identityValue, "rulePackageVersion"),
        RulePackageSha256 = GetString(identityValue, "rulePackageSha256")
      };
      NativeWorkflowItemEvidence[] items = GetArray(root, "items")
        .Select(value =>
        {
          Dictionary<string, object> item = AsObject(value, "items");
          return new NativeWorkflowItemEvidence
          {
            Identity = GetString(item, "identity"),
            CurrentValue = GetString(item, "currentValue"),
            Unit = GetString(item, "unit"),
            Source = GetString(item, "source"),
            WriteSucceeded = GetBoolean(item, "writeSucceeded"),
            ReadbackSucceeded = GetBoolean(item, "readbackSucceeded"),
            InputHash = GetString(item, "inputHash"),
            UpdatedUtc = GetString(item, "updatedUtc"),
            StableHash = GetString(item, "stableHash"),
            ErrorCode = GetString(item, "errorCode")
          };
        })
        .ToArray();
      var result = new NativeWorkflowResultEnvelope
      {
        SchemaVersion = GetString(root, "schemaVersion"),
        RunId = GetString(root, "runId"),
        SourceFeature = GetString(root, "sourceFeature"),
        SourceFunction = GetString(root, "sourceFunction"),
        Identity = identity,
        InputSnapshotHash = GetString(root, "inputSnapshotHash"),
        UpdatedUtc = GetString(root, "updatedUtc"),
        Items = new ReadOnlyCollection<NativeWorkflowItemEvidence>(items),
        CanonicalJson = canonicalJson,
        ResultHash = resultHash
      };
      return result;
    }

    private static NativeWorkflowItemEvidence CloneAndNormalizeItem(
      NativeWorkflowItemEvidence item)
    {
      if (item == null) throw new ArgumentException(
        "Workflow result contains a null item.", nameof(item));
      Require(item.Identity, "item.Identity");
      Require(item.Source, "item.Source");
      RequireHash(item.InputHash, "item.InputHash");
      var normalized = new NativeWorkflowItemEvidence
      {
        Identity = item.Identity.Trim(),
        CurrentValue = Clean(item.CurrentValue),
        Unit = Clean(item.Unit),
        Source = item.Source.Trim(),
        WriteSucceeded = item.WriteSucceeded,
        ReadbackSucceeded = item.ReadbackSucceeded,
        InputHash = item.InputHash.Trim(),
        UpdatedUtc = NormalizeUtc(item.UpdatedUtc, "item.UpdatedUtc"),
        ErrorCode = Clean(item.ErrorCode)
      };
      normalized.StableHash = ComputeItemHash(normalized);
      return normalized;
    }

    internal static string SerializeCanonical(
      NativeWorkflowResultEnvelope result)
    {
      var builder = new StringBuilder();
      builder.Append('{');
      AppendProperty(builder, "schemaVersion", result.SchemaVersion);
      builder.Append(',');
      AppendProperty(builder, "runId", result.RunId);
      builder.Append(',');
      AppendProperty(builder, "sourceFeature", result.SourceFeature);
      builder.Append(',');
      AppendProperty(builder, "sourceFunction", result.SourceFunction);
      builder.Append(",\"identity\":{");
      AppendProperty(
        builder,
        "documentFingerprint",
        result.Identity?.DocumentFingerprint);
      builder.Append(',');
      AppendProperty(builder, "modelFileType", result.Identity?.ModelFileType);
      builder.Append(',');
      AppendProperty(builder, "rulePackageId", result.Identity?.RulePackageId);
      builder.Append(',');
      AppendProperty(
        builder,
        "rulePackageVersion",
        result.Identity?.RulePackageVersion);
      builder.Append(',');
      AppendProperty(
        builder,
        "rulePackageSha256",
        result.Identity?.RulePackageSha256);
      builder.Append("},");
      AppendProperty(builder, "inputSnapshotHash", result.InputSnapshotHash);
      builder.Append(',');
      AppendProperty(builder, "updatedUtc", result.UpdatedUtc);
      builder.Append(",\"items\":[");
      NativeWorkflowItemEvidence[] items = (result.Items
          ?? Array.Empty<NativeWorkflowItemEvidence>())
        .OrderBy(value => value?.Identity, StringComparer.Ordinal)
        .ToArray();
      for (int index = 0; index < items.Length; index++)
      {
        if (index > 0) builder.Append(',');
        AppendItem(builder, items[index]);
      }
      builder.Append("]}");
      return builder.ToString();
    }

    private static string ComputeItemHash(NativeWorkflowItemEvidence item)
    {
      if (item == null) throw new ArgumentNullException(nameof(item));
      var builder = new StringBuilder();
      builder.Append('{');
      AppendProperty(builder, "identity", item.Identity);
      builder.Append(',');
      AppendProperty(builder, "currentValue", item.CurrentValue);
      builder.Append(',');
      AppendProperty(builder, "unit", item.Unit);
      builder.Append(',');
      AppendProperty(builder, "source", item.Source);
      builder.Append(",\"writeSucceeded\":");
      builder.Append(item.WriteSucceeded ? "true" : "false");
      builder.Append(",\"readbackSucceeded\":");
      builder.Append(item.ReadbackSucceeded ? "true" : "false");
      builder.Append(',');
      AppendProperty(builder, "inputHash", item.InputHash);
      builder.Append(',');
      AppendProperty(builder, "updatedUtc", item.UpdatedUtc);
      builder.Append(',');
      AppendProperty(builder, "errorCode", item.ErrorCode);
      builder.Append('}');
      return NativeWorkflowIdentityFactory.Sha256(builder.ToString());
    }

    private static void AppendItem(
      StringBuilder builder,
      NativeWorkflowItemEvidence item)
    {
      if (item == null) throw new ArgumentException(
        "Workflow result contains a null item.", nameof(item));
      builder.Append('{');
      AppendProperty(builder, "identity", item.Identity);
      builder.Append(',');
      AppendProperty(builder, "currentValue", item.CurrentValue);
      builder.Append(',');
      AppendProperty(builder, "unit", item.Unit);
      builder.Append(',');
      AppendProperty(builder, "source", item.Source);
      builder.Append(",\"writeSucceeded\":");
      builder.Append(item.WriteSucceeded ? "true" : "false");
      builder.Append(",\"readbackSucceeded\":");
      builder.Append(item.ReadbackSucceeded ? "true" : "false");
      builder.Append(',');
      AppendProperty(builder, "inputHash", item.InputHash);
      builder.Append(',');
      AppendProperty(builder, "updatedUtc", item.UpdatedUtc);
      builder.Append(',');
      AppendProperty(builder, "stableHash", item.StableHash);
      builder.Append(',');
      AppendProperty(builder, "errorCode", item.ErrorCode);
      builder.Append('}');
    }

    private static NativeWorkflowIdentity CloneIdentity(
      NativeWorkflowIdentity identity)
    {
      return new NativeWorkflowIdentity
      {
        DocumentFingerprint = identity.DocumentFingerprint.Trim(),
        ModelFileType = identity.ModelFileType.Trim(),
        RulePackageId = identity.RulePackageId.Trim(),
        RulePackageVersion = identity.RulePackageVersion.Trim(),
        RulePackageSha256 = identity.RulePackageSha256.Trim()
      };
    }

    private static void ValidateIdentity(NativeWorkflowIdentity identity)
    {
      if (identity == null) throw new ArgumentNullException(nameof(identity));
      Require(identity.DocumentFingerprint, "identity.DocumentFingerprint");
      Require(identity.ModelFileType, "identity.ModelFileType");
      Require(identity.RulePackageId, "identity.RulePackageId");
      Require(identity.RulePackageVersion, "identity.RulePackageVersion");
      RequireHash(identity.RulePackageSha256, "identity.RulePackageSha256");
    }

    private static string NormalizeUtc(string value, string name)
    {
      Require(value, name);
      DateTimeOffset parsed;
      if (!DateTimeOffset.TryParse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind,
        out parsed))
      {
        throw new ArgumentException("Workflow UTC value is invalid.", name);
      }
      return parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static void Require(string value, string name)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new ArgumentException("Workflow result value is required.", name);
    }

    private static void RequireHash(string value, string name)
    {
      Require(value, name);
      string normalized = value.Trim();
      if (normalized.Length != 64 || normalized.Any(character =>
        !((character >= '0' && character <= '9')
          || (character >= 'a' && character <= 'f'))))
      {
        throw new ArgumentException(
          "Workflow hash must be lowercase SHA-256.",
          name);
      }
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

    internal static string Escape(string value)
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
              builder.Append(((int)character).ToString(
                "x4",
                CultureInfo.InvariantCulture));
            }
            else builder.Append(character);
            break;
        }
      }
      return builder.ToString();
    }

    private static Dictionary<string, object> GetObject(
      IDictionary<string, object> value,
      string key)
    {
      object raw;
      if (!value.TryGetValue(key, out raw))
        throw new FormatException("Workflow result is missing object: " + key);
      return AsObject(raw, key);
    }

    private static Dictionary<string, object> AsObject(
      object value,
      string key)
    {
      var dictionary = value as Dictionary<string, object>;
      if (dictionary == null)
        throw new FormatException("Workflow result member is not an object: " + key);
      return dictionary;
    }

    private static IEnumerable<object> GetArray(
      IDictionary<string, object> value,
      string key)
    {
      object raw;
      if (!value.TryGetValue(key, out raw))
        throw new FormatException("Workflow result is missing array: " + key);
      var array = raw as IEnumerable;
      if (array == null || raw is string)
        throw new FormatException("Workflow result member is not an array: " + key);
      return array.Cast<object>();
    }

    private static string GetString(
      IDictionary<string, object> value,
      string key)
    {
      object raw;
      if (!value.TryGetValue(key, out raw) || !(raw is string))
        throw new FormatException("Workflow result member is not a string: " + key);
      return (string)raw;
    }

    private static bool GetBoolean(
      IDictionary<string, object> value,
      string key)
    {
      object raw;
      if (!value.TryGetValue(key, out raw) || !(raw is bool))
        throw new FormatException("Workflow result member is not a boolean: " + key);
      return (bool)raw;
    }
  }
}
