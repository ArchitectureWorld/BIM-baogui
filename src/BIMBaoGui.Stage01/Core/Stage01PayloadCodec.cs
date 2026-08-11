using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace BIMBaoGui.Stage01.Core
{
  internal static class Stage01PayloadCodec
  {
    public static bool TryApply(string payloadJson, Stage01Model model, out string error)
    {
      error = string.Empty;
      if (model == null)
      {
        error = "文件初始化模型为空。";
        return false;
      }
      if (string.IsNullOrWhiteSpace(payloadJson))
      {
        error = "Revit 文件中没有可读取的初始化载荷。";
        return false;
      }

      try
      {
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        Dictionary<string, object> root = serializer.Deserialize<Dictionary<string, object>>(payloadJson);
        var parsed = new Stage01Model();
        parsed.Values.Clear();
        parsed.Conditions.Clear();
        parsed.PlanningTargets.Clear();
        parsed.Organizations.Clear();

        if (root.TryGetValue("values", out object valuesObject))
          CopyStrings(valuesObject, parsed.Values);
        if (root.TryGetValue("planningTargets", out object planningTargetsObject))
          CopyPlanningTargets(planningTargetsObject, parsed);
        if (root.TryGetValue("conditions", out object conditionsObject))
          CopyBooleans(conditionsObject, parsed.Conditions);
        if (root.TryGetValue("organizations", out object organizationsObject) && organizationsObject is IEnumerable organizations)
        {
          foreach (object organizationObject in organizations)
          {
            var organization = new Dictionary<string, string>(StringComparer.Ordinal);
            CopyStrings(organizationObject, organization);
            parsed.Organizations.Add(organization);
          }
        }
        if (parsed.Organizations.Count == 0)
          parsed.Organizations.Add(new Dictionary<string, string>(StringComparer.Ordinal));

        RestoreLegacyPlanningTargets(parsed);
        ApplyParsedData(parsed, model);
        return true;
      }
      catch (Exception exception)
      {
        error = "初始化载荷解析失败：" + exception.Message;
        return false;
      }
    }

    private static void ApplyParsedData(Stage01Model parsed, Stage01Model model)
    {
      model.Values.Clear();
      foreach (KeyValuePair<string, string> pair in parsed.Values)
        model.Values[pair.Key] = pair.Value;

      model.Conditions.Clear();
      foreach (KeyValuePair<string, bool> pair in parsed.Conditions)
        model.Conditions[pair.Key] = pair.Value;

      model.PlanningTargets.Clear();
      foreach (KeyValuePair<string, PlanningTargetValue> pair in parsed.PlanningTargets)
        model.PlanningTargets[pair.Key] = pair.Value;

      model.Organizations.Clear();
      foreach (Dictionary<string, string> organization in parsed.Organizations)
        model.Organizations.Add(organization);
    }

    private static void CopyPlanningTargets(object source, Stage01Model model)
    {
      if (!(source is IDictionary dictionary)) return;
      foreach (DictionaryEntry entry in dictionary)
      {
        string metricCode = Convert.ToString(entry.Key) ?? string.Empty;
        if (metricCode.Length == 0 || !(entry.Value is IDictionary targetData)) continue;

        string operatorText = ReadString(targetData, "operator");
        string unitText = ReadString(targetData, "unit");
        if (!Enum.TryParse(operatorText, true, out PlanningTargetOperator @operator))
          throw new InvalidOperationException(metricCode + " 的运算符无效：" + operatorText);
        if (!Enum.TryParse(unitText, true, out PlanningTargetUnit unit))
          throw new InvalidOperationException(metricCode + " 的单位无效：" + unitText);

        if (!PlanningTargetValue.TryCreate(
          metricCode,
          @operator,
          ReadString(targetData, "value1"),
          ReadString(targetData, "value2"),
          unit,
          ReadString(targetData, "source"),
          out PlanningTargetValue target,
          out string error))
          throw new InvalidOperationException(metricCode + "：" + error);

        model.SetPlanningTarget(target);
      }
    }

    private static void RestoreLegacyPlanningTargets(Stage01Model model)
    {
      foreach (PlanningTargetDefinition definition in PlanningTargetCatalog.All)
      {
        if (model.GetPlanningTarget(definition.MetricCode) != null) continue;
        string legacy = model.GetValue(definition.MvdFieldKey);
        if (string.IsNullOrWhiteSpace(legacy)) continue;
        if (PlanningTargetValue.TryParseMvdText(
          definition.MetricCode,
          legacy,
          definition.Unit,
          "兼容旧版初始化载荷",
          out PlanningTargetValue target,
          out _))
          model.SetPlanningTarget(target);
      }
    }

    private static string ReadString(IDictionary dictionary, string key)
    {
      return dictionary.Contains(key) ? Convert.ToString(dictionary[key]) ?? string.Empty : string.Empty;
    }

    private static void CopyStrings(object source, IDictionary<string, string> target)
    {
      if (!(source is IDictionary dictionary)) return;
      foreach (DictionaryEntry entry in dictionary)
      {
        string key = Convert.ToString(entry.Key) ?? string.Empty;
        if (key.Length == 0) continue;
        target[key] = Convert.ToString(entry.Value) ?? string.Empty;
      }
    }

    private static void CopyBooleans(object source, IDictionary<string, bool> target)
    {
      if (!(source is IDictionary dictionary)) return;
      foreach (DictionaryEntry entry in dictionary)
      {
        string key = Convert.ToString(entry.Key) ?? string.Empty;
        if (key.Length == 0) continue;
        bool value;
        if (entry.Value is bool boolean)
          value = boolean;
        else
          bool.TryParse(Convert.ToString(entry.Value), out value);
        target[key] = value;
      }
    }
  }
}
