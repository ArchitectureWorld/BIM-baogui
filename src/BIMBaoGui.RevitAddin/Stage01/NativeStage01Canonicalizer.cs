using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal static class NativeStage01Canonicalizer
  {
    internal const string PayloadSchemaVersion = "0.9.1";

    internal static string ToJson(NativeStage01Model model)
    {
      return ToJson(model, PayloadSchemaVersion);
    }

    internal static string ToJson(NativeStage01Model model, string schemaVersion)
    {
      if (model == null) throw new ArgumentNullException(nameof(model));
      string version = RequireVersion(schemaVersion);
      string modelVersion = model.GetValue(NativeStage01Keys.WorkflowVersion);
      if (!string.Equals(modelVersion, version, StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          "Stage01 canonical schemaVersion 与模型 WorkflowVersion 不一致。" );
      }

      var builder = new StringBuilder(16384);
      builder.Append('{');
      AppendProperty(builder, "schemaVersion", version, true);
      AppendProperty(builder, "workflowVersion", modelVersion, false);
      builder.Append(",\"values\":");
      AppendStringDictionary(
        builder,
        model.Values
          .Where(pair => !string.Equals(
            pair.Key,
            NativeStage01Keys.InitializationStatus,
            StringComparison.Ordinal))
          .ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal));
      builder.Append(",\"planningTargets\":");
      AppendPlanningTargets(builder, model.PlanningTargets);
      builder.Append(",\"conditions\":");
      AppendBooleanDictionary(builder, model.Conditions);
      builder.Append(",\"organizations\":[");
      for (int index = 0; index < model.Organizations.Count; index++)
      {
        if (index > 0) builder.Append(',');
        AppendStringDictionary(builder, model.Organizations[index]);
      }
      builder.Append(']').Append('}');
      return builder.ToString();
    }

    internal static string Sha256(string text)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        byte[] hash = algorithm.ComputeHash(
          Encoding.UTF8.GetBytes(text ?? string.Empty));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (byte value in hash)
          builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        return builder.ToString();
      }
    }

    private static string RequireVersion(string value)
    {
      string version = (value ?? string.Empty).Trim();
      if (version.Length == 0 || !Version.TryParse(version, out _))
        throw new ArgumentException("Stage01 schemaVersion 无效。", nameof(value));
      return version;
    }

    private static void AppendPlanningTargets(
      StringBuilder builder,
      IDictionary<string, NativePlanningTargetValue> values)
    {
      builder.Append('{');
      bool first = true;
      foreach (KeyValuePair<string, NativePlanningTargetValue> pair in
        (values ?? new Dictionary<string, NativePlanningTargetValue>())
          .OrderBy(value => value.Key, StringComparer.Ordinal))
      {
        if (pair.Value == null) continue;
        if (!first) builder.Append(',');
        AppendEscaped(builder, pair.Key);
        builder.Append(':').Append('{');
        AppendProperty(builder, "operator", pair.Value.Operator, true);
        AppendProperty(builder, "value1", pair.Value.Value1, false);
        AppendProperty(builder, "value2", pair.Value.Value2, false);
        AppendProperty(builder, "unit", pair.Value.Unit, false);
        AppendProperty(builder, "source", pair.Value.Source, false);
        AppendProperty(builder, "mvdText", pair.Value.MvdText, false);
        builder.Append('}');
        first = false;
      }
      builder.Append('}');
    }

    private static void AppendStringDictionary(
      StringBuilder builder,
      IDictionary<string, string> values)
    {
      builder.Append('{');
      bool first = true;
      foreach (KeyValuePair<string, string> pair in (values
        ?? new Dictionary<string, string>())
        .OrderBy(value => value.Key, StringComparer.Ordinal))
      {
        AppendProperty(builder, pair.Key, pair.Value ?? string.Empty, first);
        first = false;
      }
      builder.Append('}');
    }

    private static void AppendBooleanDictionary(
      StringBuilder builder,
      IDictionary<string, bool> values)
    {
      builder.Append('{');
      bool first = true;
      foreach (KeyValuePair<string, bool> pair in (values
        ?? new Dictionary<string, bool>())
        .OrderBy(value => value.Key, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        AppendEscaped(builder, pair.Key);
        builder.Append(':').Append(pair.Value ? "true" : "false");
        first = false;
      }
      builder.Append('}');
    }

    private static void AppendProperty(
      StringBuilder builder,
      string key,
      string value,
      bool first)
    {
      if (!first) builder.Append(',');
      AppendEscaped(builder, key);
      builder.Append(':');
      AppendEscaped(builder, value ?? string.Empty);
    }

    private static void AppendEscaped(StringBuilder builder, string value)
    {
      builder.Append('"');
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
            if (character < 32)
            {
              builder.Append("\\u").Append(
                ((int)character).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
              builder.Append(character);
            }
            break;
        }
      }
      builder.Append('"');
    }
  }
}
