using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.Context
{
  public static class HBRFileContextCanonicalizer
  {
    public static string ToJson(HBRFileContext context)
    {
      return Build(context, true);
    }

    public static string ComputeHash(HBRFileContext context)
    {
      return CanonicalPayload.Sha256(Build(context, false));
    }

    public static bool TryParse(string json, out HBRFileContext context, out string error)
    {
      context = null;
      error = string.Empty;
      if (string.IsNullOrWhiteSpace(json))
      {
        error = "文件上下文 JSON 为空。";
        return false;
      }

      try
      {
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        Dictionary<string, object> root = serializer.Deserialize<Dictionary<string, object>>(json);
        HBRSpatialReference spatial = ParseSpatial(ReadDictionary(root, "spatialReference"));
        IDictionary<string, PlanningTargetValue> targets = ParseTargets(ReadDictionary(root, "planningTargets"));
        IDictionary<string, bool> conditions = ParseConditions(ReadDictionary(root, "projectConditions"));
        var parsed = new HBRFileContext(
          ReadRootString(root, "schemaVersion"),
          ReadRootString(root, "workflowVersion"),
          ReadRootString(root, "fileGuid"),
          ReadRootString(root, "revitDocumentFingerprint"),
          ReadRootString(root, "revitDocumentTitle"),
          ReadRootString(root, "projectNumber"),
          ReadRootString(root, "projectName"),
          ReadRootString(root, "subitemCode"),
          ReadRootString(root, "subitemName"),
          ReadRootString(root, "modelFileType"),
          ReadRootString(root, "modelScope"),
          spatial,
          targets,
          conditions,
          ReadStrings(root, "activatedRuleIds"),
          ReadStrings(root, "notApplicableRuleIds"),
          ReadBoolean(root, "initializationPassed"),
          ReadRootString(root, "rulePackVersion"),
          ReadRootString(root, "sourcePayloadHash"),
          ReadRootString(root, "fileContextHash"));

        string expected = ComputeHash(parsed);
        if (!string.IsNullOrWhiteSpace(parsed.FileContextHash)
          && !string.Equals(parsed.FileContextHash, expected, StringComparison.OrdinalIgnoreCase))
        {
          error = "文件上下文哈希校验失败。";
          return false;
        }

        context = string.IsNullOrWhiteSpace(parsed.FileContextHash) ? parsed.WithHash(expected) : parsed;
        return true;
      }
      catch (Exception exception)
      {
        error = "文件上下文解析失败：" + exception.Message;
        return false;
      }
    }

    private static string Build(HBRFileContext context, bool includeHash)
    {
      if (context == null) return string.Empty;
      var builder = new StringBuilder(12288);
      builder.Append('{');
      AppendProperty(builder, "schemaVersion", context.SchemaVersion, true);
      AppendProperty(builder, "workflowVersion", context.WorkflowVersion, false);
      AppendProperty(builder, "fileGuid", context.FileGuid, false);
      AppendProperty(builder, "revitDocumentFingerprint", context.RevitDocumentFingerprint, false);
      AppendProperty(builder, "revitDocumentTitle", context.RevitDocumentTitle, false);
      AppendProperty(builder, "projectNumber", context.ProjectNumber, false);
      AppendProperty(builder, "projectName", context.ProjectName, false);
      AppendProperty(builder, "subitemCode", context.SubitemCode, false);
      AppendProperty(builder, "subitemName", context.SubitemName, false);
      AppendProperty(builder, "modelFileType", context.ModelFileType, false);
      AppendProperty(builder, "modelScope", context.ModelScope, false);
      builder.Append(",\"spatialReference\":");
      AppendSpatial(builder, context.SpatialReference);
      builder.Append(",\"planningTargets\":");
      AppendTargets(builder, context.PlanningTargets);
      builder.Append(",\"projectConditions\":");
      AppendConditions(builder, context.ProjectConditions);
      builder.Append(",\"activatedRuleIds\":");
      AppendStrings(builder, context.ActivatedRuleIds);
      builder.Append(",\"notApplicableRuleIds\":");
      AppendStrings(builder, context.NotApplicableRuleIds);
      builder.Append(",\"initializationPassed\":").Append(context.InitializationPassed ? "true" : "false");
      AppendProperty(builder, "rulePackVersion", context.RulePackVersion, false);
      AppendProperty(builder, "sourcePayloadHash", context.SourcePayloadHash, false);
      if (includeHash) AppendProperty(builder, "fileContextHash", context.FileContextHash, false);
      builder.Append('}');
      return builder.ToString();
    }

    private static void AppendSpatial(StringBuilder builder, HBRSpatialReference spatial)
    {
      builder.Append('{');
      AppendProperty(builder, "coordinateSystem", spatial?.CoordinateSystem ?? string.Empty, true);
      AppendProperty(builder, "elevationSystem", spatial?.ElevationSystem ?? string.Empty, false);
      AppendProperty(builder, "baseX", (spatial?.BaseX ?? 0m).ToString(CultureInfo.InvariantCulture), false);
      AppendProperty(builder, "baseY", (spatial?.BaseY ?? 0m).ToString(CultureInfo.InvariantCulture), false);
      AppendProperty(builder, "baseElevation", (spatial?.BaseElevation ?? 0m).ToString(CultureInfo.InvariantCulture), false);
      AppendProperty(builder, "trueNorthAngleDegrees", (spatial?.TrueNorthAngleDegrees ?? 0m).ToString(CultureInfo.InvariantCulture), false);
      AppendProperty(builder, "lengthUnit", spatial?.LengthUnit ?? string.Empty, false);
      AppendProperty(builder, "areaUnit", spatial?.AreaUnit ?? string.Empty, false);
      AppendProperty(builder, "angleUnit", spatial?.AngleUnit ?? string.Empty, false);
      builder.Append('}');
    }

    private static void AppendTargets(StringBuilder builder, IReadOnlyDictionary<string, PlanningTargetValue> targets)
    {
      builder.Append('{');
      bool first = true;
      foreach (KeyValuePair<string, PlanningTargetValue> pair in (targets ?? new Dictionary<string, PlanningTargetValue>()).OrderBy(x => x.Key, StringComparer.Ordinal))
      {
        if (pair.Value == null) continue;
        if (!first) builder.Append(',');
        CanonicalPayload.AppendEscaped(builder, pair.Key);
        builder.Append(':').Append('{');
        AppendProperty(builder, "operator", pair.Value.Operator.ToString(), true);
        AppendProperty(builder, "value1", pair.Value.Value1.ToString(CultureInfo.InvariantCulture), false);
        AppendProperty(builder, "value2", pair.Value.Value2.HasValue ? pair.Value.Value2.Value.ToString(CultureInfo.InvariantCulture) : string.Empty, false);
        AppendProperty(builder, "unit", pair.Value.Unit.ToString(), false);
        AppendProperty(builder, "source", pair.Value.Source, false);
        AppendProperty(builder, "mvdText", pair.Value.ToMvdText(), false);
        builder.Append('}');
        first = false;
      }
      builder.Append('}');
    }

    private static void AppendConditions(StringBuilder builder, IReadOnlyDictionary<string, bool> conditions)
    {
      builder.Append('{');
      bool first = true;
      foreach (KeyValuePair<string, bool> pair in (conditions ?? new Dictionary<string, bool>()).OrderBy(x => x.Key, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        CanonicalPayload.AppendEscaped(builder, pair.Key);
        builder.Append(':').Append(pair.Value ? "true" : "false");
        first = false;
      }
      builder.Append('}');
    }

    private static void AppendStrings(StringBuilder builder, IEnumerable<string> values)
    {
      builder.Append('[');
      bool first = true;
      foreach (string value in (values ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        CanonicalPayload.AppendEscaped(builder, value ?? string.Empty);
        first = false;
      }
      builder.Append(']');
    }

    private static void AppendProperty(StringBuilder builder, string key, string value, bool first)
    {
      if (!first) builder.Append(',');
      CanonicalPayload.AppendEscaped(builder, key);
      builder.Append(':');
      CanonicalPayload.AppendEscaped(builder, value ?? string.Empty);
    }

    private static HBRSpatialReference ParseSpatial(IDictionary dictionary)
    {
      return new HBRSpatialReference(
        ReadNestedString(dictionary, "coordinateSystem"),
        ReadNestedString(dictionary, "elevationSystem"),
        ReadDecimal(dictionary, "baseX"),
        ReadDecimal(dictionary, "baseY"),
        ReadDecimal(dictionary, "baseElevation"),
        ReadDecimal(dictionary, "trueNorthAngleDegrees"),
        ReadNestedString(dictionary, "lengthUnit"),
        ReadNestedString(dictionary, "areaUnit"),
        ReadNestedString(dictionary, "angleUnit"));
    }

    private static IDictionary<string, PlanningTargetValue> ParseTargets(IDictionary dictionary)
    {
      var result = new Dictionary<string, PlanningTargetValue>(StringComparer.Ordinal);
      if (dictionary == null) return result;
      foreach (DictionaryEntry entry in dictionary)
      {
        string metricCode = Convert.ToString(entry.Key) ?? string.Empty;
        if (!(entry.Value is IDictionary value)) continue;
        if (!Enum.TryParse(ReadNestedString(value, "operator"), true, out PlanningTargetOperator @operator)) continue;
        if (!Enum.TryParse(ReadNestedString(value, "unit"), true, out PlanningTargetUnit unit)) continue;
        if (PlanningTargetValue.TryCreate(
          metricCode,
          @operator,
          ReadNestedString(value, "value1"),
          ReadNestedString(value, "value2"),
          unit,
          ReadNestedString(value, "source"),
          out PlanningTargetValue target,
          out _))
          result[metricCode] = target;
      }
      return result;
    }

    private static IDictionary<string, bool> ParseConditions(IDictionary dictionary)
    {
      var result = new Dictionary<string, bool>(StringComparer.Ordinal);
      if (dictionary == null) return result;
      foreach (DictionaryEntry entry in dictionary)
      {
        string key = Convert.ToString(entry.Key) ?? string.Empty;
        bool value = entry.Value is bool boolean ? boolean : bool.TryParse(Convert.ToString(entry.Value), out bool parsed) && parsed;
        if (key.Length > 0) result[key] = value;
      }
      return result;
    }

    private static IDictionary ReadDictionary(IDictionary<string, object> root, string key)
    {
      return root != null && root.TryGetValue(key, out object value) ? value as IDictionary : null;
    }

    private static string ReadRootString(IDictionary<string, object> root, string key)
    {
      return root != null && root.TryGetValue(key, out object value) ? Convert.ToString(value) ?? string.Empty : string.Empty;
    }

    private static bool ReadBoolean(IDictionary<string, object> root, string key)
    {
      if (root == null || !root.TryGetValue(key, out object value)) return false;
      return value is bool boolean ? boolean : bool.TryParse(Convert.ToString(value), out bool parsed) && parsed;
    }

    private static IEnumerable<string> ReadStrings(IDictionary<string, object> root, string key)
    {
      if (root == null || !root.TryGetValue(key, out object value) || !(value is IEnumerable items)) return Array.Empty<string>();
      var result = new List<string>();
      foreach (object item in items) result.Add(Convert.ToString(item) ?? string.Empty);
      return result;
    }

    private static string ReadNestedString(IDictionary dictionary, string key)
    {
      return dictionary != null && dictionary.Contains(key) ? Convert.ToString(dictionary[key]) ?? string.Empty : string.Empty;
    }

    private static decimal ReadDecimal(IDictionary dictionary, string key)
    {
      decimal.TryParse(ReadNestedString(dictionary, key), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value);
      return value;
    }
  }
}
