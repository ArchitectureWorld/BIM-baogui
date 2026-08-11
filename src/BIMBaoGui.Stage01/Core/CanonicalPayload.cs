using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BIMBaoGui.Stage01.Context;

namespace BIMBaoGui.Stage01.Core
{
  internal static class CanonicalPayload
  {
    public static string Build(Stage01Model model)
    {
      var builder = new StringBuilder(12288);
      builder.Append('{');
      AppendProperty(builder, "schemaVersion", HBRContextVersions.FileContextSchema, true);
      AppendProperty(builder, "workflowVersion", model.GetValue(Stage01Keys.WorkflowVersion), false);
      builder.Append(",\"values\":");
      AppendStringDictionary(builder, model.Values
        .Where(x => !string.Equals(
          x.Key,
          Stage01Keys.InitializationStatus,
          StringComparison.Ordinal))
        .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
      builder.Append(",\"planningTargets\":");
      AppendPlanningTargets(builder, model.PlanningTargets);
      builder.Append(",\"conditions\":");
      AppendBooleanDictionary(builder, model.Conditions);
      builder.Append(",\"organizations\":[");
      for (int i = 0; i < model.Organizations.Count; ++i)
      {
        if (i > 0) builder.Append(',');
        AppendStringDictionary(builder, model.Organizations[i]);
      }
      builder.Append(']');
      builder.Append('}');
      return builder.ToString();
    }

    public static string Sha256(string text)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (byte value in hash)
          builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        return builder.ToString();
      }
    }

    private static void AppendPlanningTargets(
      StringBuilder builder,
      IDictionary<string, PlanningTargetValue> targets)
    {
      builder.Append('{');
      bool firstTarget = true;
      foreach (KeyValuePair<string, PlanningTargetValue> pair in
        (targets ?? new Dictionary<string, PlanningTargetValue>())
          .OrderBy(x => x.Key, StringComparer.Ordinal))
      {
        PlanningTargetValue target = pair.Value;
        if (target == null) continue;
        if (!firstTarget) builder.Append(',');
        AppendEscaped(builder, pair.Key);
        builder.Append(':').Append('{');
        AppendProperty(builder, "operator", target.Operator.ToString(), true);
        AppendProperty(
          builder,
          "value1",
          target.Value1.ToString(CultureInfo.InvariantCulture),
          false);
        AppendProperty(
          builder,
          "value2",
          target.Value2.HasValue
            ? target.Value2.Value.ToString(CultureInfo.InvariantCulture)
            : string.Empty,
          false);
        AppendProperty(builder, "unit", target.Unit.ToString(), false);
        AppendProperty(builder, "source", target.Source, false);
        AppendProperty(builder, "mvdText", target.ToMvdText(), false);
        builder.Append('}');
        firstTarget = false;
      }
      builder.Append('}');
    }

    private static void AppendStringDictionary(
      StringBuilder builder,
      IDictionary<string, string> values)
    {
      builder.Append('{');
      bool first = true;
      foreach (KeyValuePair<string, string> pair in
        values.OrderBy(x => x.Key, StringComparer.Ordinal))
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
      foreach (KeyValuePair<string, bool> pair in
        values.OrderBy(x => x.Key, StringComparer.Ordinal))
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

    internal static void AppendEscaped(StringBuilder builder, string value)
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
              builder.Append("\\u")
                .Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            else
              builder.Append(character);
            break;
        }
      }
      builder.Append('"');
    }
  }
}
