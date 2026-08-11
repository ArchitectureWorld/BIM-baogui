using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.TaskPlanning
{
  public static class HBRTaskPlanCanonicalizer
  {
    internal const string LegacyUpgradeMessage =
      "规则数据库已升级，请重新运行任务规划。";

    public static string ToJson(HBRTaskPlan plan)
    {
      return Build(plan, true);
    }

    public static string ComputeHash(HBRTaskPlan plan)
    {
      return CanonicalPayload.Sha256(Build(plan, false));
    }

    public static bool TryParse(string json, out HBRTaskPlan plan, out string error)
    {
      plan = null;
      error = string.Empty;
      if (string.IsNullOrWhiteSpace(json))
      {
        error = "任务计划 JSON 为空。";
        return false;
      }

      try
      {
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        Dictionary<string, object> root = serializer.Deserialize<Dictionary<string, object>>(json);
        int identityFieldCount = CountIdentityFields(root);
        if (identityFieldCount == 0)
          return TryParseLegacy(root, out plan, out error);
        if (identityFieldCount != 3
          || string.IsNullOrWhiteSpace(ReadRootString(root, "rulePackageId"))
          || string.IsNullOrWhiteSpace(ReadRootString(root, "rulePackageVersion"))
          || string.IsNullOrWhiteSpace(ReadRootString(root, "rulePackageSha256")))
        {
          error = "任务计划缺少完整规则数据库身份，数据损坏。";
          return false;
        }
        if (!HasProperty(root, "taskPlanHash")
          || string.IsNullOrWhiteSpace(ReadRootString(root, "taskPlanHash")))
        {
          error = "任务计划缺少哈希值，数据损坏。";
          return false;
        }

        HBRTaskPlan parsed = Parse(root);

        string expected = ComputeHash(parsed);
        if (!string.Equals(parsed.TaskPlanHash, expected, StringComparison.OrdinalIgnoreCase))
        {
          error = "任务计划哈希校验失败，数据损坏。";
          return false;
        }

        plan = parsed;
        return true;
      }
      catch (Exception exception)
      {
        error = "任务计划解析失败：" + exception.Message;
        return false;
      }
    }

    internal static bool IsLegacyUpgradeError(string error)
    {
      return string.Equals(error, LegacyUpgradeMessage, StringComparison.Ordinal);
    }

    private static bool TryParseLegacy(
      IDictionary<string, object> root,
      out HBRTaskPlan plan,
      out string error)
    {
      plan = null;
      error = string.Empty;
      if (!HasProperty(root, "taskPlanHash")
        || string.IsNullOrWhiteSpace(ReadRootString(root, "taskPlanHash")))
      {
        error = "旧版任务计划缺少哈希值，数据损坏。";
        return false;
      }

      HBRTaskPlan legacy = Parse(root);
      string expected = HBRTaskPlanLegacyCanonicalizer.ComputeHash(legacy);
      if (!string.Equals(
        legacy.TaskPlanHash,
        expected,
        StringComparison.OrdinalIgnoreCase))
      {
        error = "旧版任务计划哈希无效，数据损坏。";
        return false;
      }

      error = LegacyUpgradeMessage;
      return false;
    }

    private static HBRTaskPlan Parse(IDictionary<string, object> root)
    {
      return new HBRTaskPlan(
        ReadRootString(root, "schemaVersion"),
        ReadRootString(root, "fileContextHash"),
        ReadRootString(root, "rulePackageId"),
        ReadRootString(root, "rulePackageVersion"),
        ReadRootString(root, "rulePackageSha256"),
        ReadRootString(root, "modelFileType"),
        ReadRootString(root, "skeletonPath"),
        ParseItems(root, "activeTasks"),
        ParseItems(root, "notApplicableTasks"),
        ReadRootString(root, "taskPlanHash"));
    }

    private static string Build(HBRTaskPlan plan, bool includeHash)
    {
      if (plan == null) return string.Empty;
      var builder = new StringBuilder(16384);
      builder.Append('{');
      AppendProperty(builder, "schemaVersion", plan.SchemaVersion, true);
      AppendProperty(builder, "fileContextHash", plan.FileContextHash, false);
      AppendProperty(builder, "rulePackageId", plan.RulePackageId, false);
      AppendProperty(builder, "rulePackageVersion", plan.RulePackageVersion, false);
      AppendProperty(builder, "rulePackageSha256", plan.RulePackageSha256, false);
      AppendProperty(builder, "modelFileType", plan.ModelFileType, false);
      AppendProperty(builder, "skeletonPath", plan.SkeletonPath, false);
      builder.Append(",\"activeTasks\":");
      AppendItems(builder, plan.ActiveTasks);
      builder.Append(",\"notApplicableTasks\":");
      AppendItems(builder, plan.NotApplicableTasks);
      if (includeHash) AppendProperty(builder, "taskPlanHash", plan.TaskPlanHash, false);
      builder.Append('}');
      return builder.ToString();
    }

    private static int CountIdentityFields(IDictionary<string, object> root)
    {
      int count = 0;
      if (HasProperty(root, "rulePackageId")) count++;
      if (HasProperty(root, "rulePackageVersion")) count++;
      if (HasProperty(root, "rulePackageSha256")) count++;
      return count;
    }

    private static bool HasProperty(
      IDictionary<string, object> root,
      string key)
    {
      return root != null && root.ContainsKey(key);
    }

    private static void AppendItems(StringBuilder builder, IEnumerable<HBRTaskPlanItem> items)
    {
      builder.Append('[');
      bool first = true;
      foreach (HBRTaskPlanItem item in (items ?? Array.Empty<HBRTaskPlanItem>())
        .Where(value => value != null)
        .OrderBy(value => value.Sequence)
        .ThenBy(value => value.TaskId, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        builder.Append('{');
        AppendProperty(builder, "taskId", item.TaskId, true);
        AppendProperty(builder, "name", item.Name, false);
        AppendProperty(builder, "objectCode", item.ObjectCode, false);
        AppendProperty(builder, "requirement", item.Requirement.ToString(), false);
        AppendProperty(builder, "conditionKey", item.ConditionKey, false);
        AppendProperty(builder, "sequence", item.Sequence.ToString(CultureInfo.InvariantCulture), false);
        builder.Append(",\"skeletonTask\":").Append(item.SkeletonTask ? "true" : "false");
        builder.Append(",\"attributeRequirements\":"); AppendStrings(builder, item.AttributeRequirements);
        builder.Append(",\"dependencies\":"); AppendStrings(builder, item.Dependencies);
        builder.Append(",\"geometryChecks\":"); AppendStrings(builder, item.GeometryChecks);
        builder.Append(",\"propertyChecks\":"); AppendStrings(builder, item.PropertyChecks);
        builder.Append(",\"targetComparisons\":"); AppendStrings(builder, item.TargetComparisons);
        builder.Append('}');
        first = false;
      }
      builder.Append(']');
    }

    private static IEnumerable<HBRTaskPlanItem> ParseItems(IDictionary<string, object> root, string key)
    {
      var result = new List<HBRTaskPlanItem>();
      if (root == null || !root.TryGetValue(key, out object source) || !(source is IEnumerable items)) return result;
      foreach (object entry in items)
      {
        if (!(entry is IDictionary dictionary)) continue;
        if (!Enum.TryParse(ReadNestedString(dictionary, "requirement"), true, out HBRTaskRequirement requirement))
          requirement = HBRTaskRequirement.Required;
        int.TryParse(ReadNestedString(dictionary, "sequence"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sequence);
        bool skeleton = ReadNestedBoolean(dictionary, "skeletonTask");
        result.Add(new HBRTaskPlanItem(
          ReadNestedString(dictionary, "taskId"),
          ReadNestedString(dictionary, "name"),
          ReadNestedString(dictionary, "objectCode"),
          requirement,
          ReadNestedString(dictionary, "conditionKey"),
          sequence,
          skeleton,
          ReadNestedStrings(dictionary, "attributeRequirements"),
          ReadNestedStrings(dictionary, "dependencies"),
          ReadNestedStrings(dictionary, "geometryChecks"),
          ReadNestedStrings(dictionary, "propertyChecks"),
          ReadNestedStrings(dictionary, "targetComparisons")));
      }
      return result;
    }

    private static void AppendStrings(StringBuilder builder, IEnumerable<string> values)
    {
      builder.Append('[');
      bool first = true;
      foreach (string value in (values ?? Array.Empty<string>()).OrderBy(item => item, StringComparer.Ordinal))
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

    private static string ReadRootString(IDictionary<string, object> root, string key)
    {
      return root != null && root.TryGetValue(key, out object value) ? Convert.ToString(value) ?? string.Empty : string.Empty;
    }

    private static string ReadNestedString(IDictionary dictionary, string key)
    {
      return dictionary != null && dictionary.Contains(key) ? Convert.ToString(dictionary[key]) ?? string.Empty : string.Empty;
    }

    private static bool ReadNestedBoolean(IDictionary dictionary, string key)
    {
      if (dictionary == null || !dictionary.Contains(key)) return false;
      object value = dictionary[key];
      return value is bool boolean ? boolean : bool.TryParse(Convert.ToString(value), out bool parsed) && parsed;
    }

    private static IEnumerable<string> ReadNestedStrings(IDictionary dictionary, string key)
    {
      if (dictionary == null || !dictionary.Contains(key) || !(dictionary[key] is IEnumerable values)) return Array.Empty<string>();
      var result = new List<string>();
      foreach (object value in values) result.Add(Convert.ToString(value) ?? string.Empty);
      return result;
    }
  }

  internal static class HBRTaskPlanLegacyCanonicalizer
  {
    internal static string ComputeHash(HBRTaskPlan plan)
    {
      return CanonicalPayload.Sha256(Build(plan));
    }

    private static string Build(HBRTaskPlan plan)
    {
      if (plan == null) return string.Empty;
      var builder = new StringBuilder(16384);
      builder.Append('{');
      AppendProperty(builder, "schemaVersion", plan.SchemaVersion, true);
      AppendProperty(builder, "fileContextHash", plan.FileContextHash, false);
      AppendProperty(builder, "modelFileType", plan.ModelFileType, false);
      AppendProperty(builder, "skeletonPath", plan.SkeletonPath, false);
      builder.Append(",\"activeTasks\":");
      AppendItems(builder, plan.ActiveTasks);
      builder.Append(",\"notApplicableTasks\":");
      AppendItems(builder, plan.NotApplicableTasks);
      builder.Append('}');
      return builder.ToString();
    }

    private static void AppendItems(
      StringBuilder builder,
      IEnumerable<HBRTaskPlanItem> items)
    {
      builder.Append('[');
      bool first = true;
      foreach (HBRTaskPlanItem item in
        (items ?? Array.Empty<HBRTaskPlanItem>())
          .Where(value => value != null)
          .OrderBy(value => value.Sequence)
          .ThenBy(value => value.TaskId, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        builder.Append('{');
        AppendProperty(builder, "taskId", item.TaskId, true);
        AppendProperty(builder, "name", item.Name, false);
        AppendProperty(builder, "objectCode", item.ObjectCode, false);
        AppendProperty(builder, "requirement", item.Requirement.ToString(), false);
        AppendProperty(builder, "conditionKey", item.ConditionKey, false);
        AppendProperty(
          builder,
          "sequence",
          item.Sequence.ToString(CultureInfo.InvariantCulture),
          false);
        builder.Append(",\"skeletonTask\":")
          .Append(item.SkeletonTask ? "true" : "false");
        builder.Append(",\"attributeRequirements\":");
        AppendStrings(builder, item.AttributeRequirements);
        builder.Append(",\"dependencies\":");
        AppendStrings(builder, item.Dependencies);
        builder.Append(",\"geometryChecks\":");
        AppendStrings(builder, item.GeometryChecks);
        builder.Append(",\"propertyChecks\":");
        AppendStrings(builder, item.PropertyChecks);
        builder.Append(",\"targetComparisons\":");
        AppendStrings(builder, item.TargetComparisons);
        builder.Append('}');
        first = false;
      }
      builder.Append(']');
    }

    private static void AppendStrings(
      StringBuilder builder,
      IEnumerable<string> values)
    {
      builder.Append('[');
      bool first = true;
      foreach (string value in
        (values ?? Array.Empty<string>()).OrderBy(item => item, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        CanonicalPayload.AppendEscaped(builder, value ?? string.Empty);
        first = false;
      }
      builder.Append(']');
    }

    private static void AppendProperty(
      StringBuilder builder,
      string key,
      string value,
      bool first)
    {
      if (!first) builder.Append(',');
      CanonicalPayload.AppendEscaped(builder, key);
      builder.Append(':');
      CanonicalPayload.AppendEscaped(builder, value ?? string.Empty);
    }
  }
}
