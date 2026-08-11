using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01Payload
  {
    internal string SchemaVersion { get; set; } = string.Empty;
    internal string WorkflowVersion { get; set; } = string.Empty;
    internal NativeStage01Model Model { get; set; }
  }

  internal static class NativeStage01PayloadCodec
  {
    internal static bool TryDecode(
      string payloadJson,
      out NativeStage01Payload payload,
      out string error)
    {
      payload = null;
      error = string.Empty;
      if (string.IsNullOrWhiteSpace(payloadJson))
      {
        error = "初始化 Payload 为空。";
        return false;
      }

      try
      {
        var serializer = new JavaScriptSerializer
        {
          MaxJsonLength = int.MaxValue,
          RecursionLimit = 512
        };
        Dictionary<string, object> root =
          serializer.Deserialize<Dictionary<string, object>>(payloadJson);
        if (root == null)
          throw new InvalidOperationException("Payload 根对象为空。");

        string schemaVersion = RequireString(root, "schemaVersion");
        string workflowVersion = RequireString(root, "workflowVersion");
        object valuesObject = Require(root, "values");
        object planningTargetsObject = Require(root, "planningTargets");
        object conditionsObject = Require(root, "conditions");
        object organizationsObject = Require(root, "organizations");

        var model = new NativeStage01Model
        {
          ActiveGroup = NativeRuleCatalog.Current.DefaultActiveGroup
        };
        model.Values.Clear();
        model.Conditions.Clear();
        model.PlanningTargets.Clear();
        model.Organizations.Clear();

        CopyStrings(valuesObject, model.Values, "values");
        CopyConditions(conditionsObject, model.Conditions);
        CopyPlanningTargets(planningTargetsObject, model.PlanningTargets);
        CopyOrganizations(organizationsObject, model.Organizations);
        if (model.Organizations.Count == 0)
        {
          model.Organizations.Add(
            new Dictionary<string, string>(StringComparer.Ordinal));
        }

        payload = new NativeStage01Payload
        {
          SchemaVersion = schemaVersion,
          WorkflowVersion = workflowVersion,
          Model = model
        };
        return true;
      }
      catch (Exception exception)
      {
        error = "初始化 Payload 解析失败：" + exception.Message;
        return false;
      }
    }

    private static object Require(
      IDictionary<string, object> root,
      string key)
    {
      if (!root.TryGetValue(key, out object value) || value == null)
        throw new InvalidOperationException("Payload 缺少 " + key + "。");
      return value;
    }

    private static string RequireString(
      IDictionary<string, object> root,
      string key)
    {
      string value = Convert.ToString(Require(root, key)) ?? string.Empty;
      if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException("Payload " + key + " 为空。");
      return value;
    }

    private static void CopyStrings(
      object source,
      IDictionary<string, string> target,
      string path)
    {
      if (!(source is IDictionary dictionary))
        throw new InvalidOperationException("Payload " + path + " 不是对象。");
      foreach (DictionaryEntry entry in dictionary)
      {
        string key = Convert.ToString(entry.Key) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
          throw new InvalidOperationException("Payload " + path + " 包含空键。");
        if (target.ContainsKey(key))
          throw new InvalidOperationException(
            "Payload " + path + " 包含重复键：" + key);
        target.Add(key, Convert.ToString(entry.Value) ?? string.Empty);
      }
    }

    private static void CopyConditions(
      object source,
      IDictionary<string, bool> target)
    {
      if (!(source is IDictionary dictionary))
        throw new InvalidOperationException("Payload conditions 不是对象。");
      foreach (DictionaryEntry entry in dictionary)
      {
        string key = Convert.ToString(entry.Key) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
          throw new InvalidOperationException("Payload conditions 包含空键。");
        if (target.ContainsKey(key))
          throw new InvalidOperationException(
            "Payload conditions 包含重复键：" + key);
        bool value;
        if (entry.Value is bool boolean)
        {
          value = boolean;
        }
        else if (!bool.TryParse(
          Convert.ToString(entry.Value),
          out value))
        {
          throw new InvalidOperationException(
            "Payload condition 不是布尔值：" + key);
        }
        target.Add(key, value);
      }
    }

    private static void CopyPlanningTargets(
      object source,
      IDictionary<string, NativePlanningTargetValue> target)
    {
      if (!(source is IDictionary dictionary))
        throw new InvalidOperationException(
          "Payload planningTargets 不是对象。");
      foreach (DictionaryEntry entry in dictionary)
      {
        string metricCode = Convert.ToString(entry.Key) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(metricCode))
          throw new InvalidOperationException(
            "Payload planningTargets 包含空键。");
        if (!(entry.Value is IDictionary data))
          throw new InvalidOperationException(
            "Payload planning target 不是对象：" + metricCode);
        if (target.ContainsKey(metricCode))
          throw new InvalidOperationException(
            "Payload planningTargets 包含重复键：" + metricCode);
        target.Add(
          metricCode,
          new NativePlanningTargetValue(
            ReadString(data, "operator"),
            ReadString(data, "value1"),
            ReadString(data, "value2"),
            ReadString(data, "unit"),
            ReadString(data, "source"),
            ReadString(data, "mvdText")));
      }
    }

    private static void CopyOrganizations(
      object source,
      ICollection<Dictionary<string, string>> target)
    {
      if (source is string || !(source is IEnumerable values))
        throw new InvalidOperationException(
          "Payload organizations 不是数组。");
      int index = 0;
      foreach (object item in values)
      {
        var organization = new Dictionary<string, string>(
          StringComparer.Ordinal);
        CopyStrings(
          item,
          organization,
          "organizations[" + index + "]");
        target.Add(organization);
        index++;
      }
    }

    private static string ReadString(IDictionary dictionary, string key)
    {
      if (!dictionary.Contains(key))
        throw new InvalidOperationException(
          "Payload planning target 缺少 " + key + "。");
      return Convert.ToString(dictionary[key]) ?? string.Empty;
    }
  }
}
