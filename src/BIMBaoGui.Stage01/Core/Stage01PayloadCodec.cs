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
        model.Values.Clear();
        model.Conditions.Clear();
        model.Organizations.Clear();

        if (root.TryGetValue("values", out object valuesObject))
          CopyStrings(valuesObject, model.Values);
        if (root.TryGetValue("conditions", out object conditionsObject))
          CopyBooleans(conditionsObject, model.Conditions);
        if (root.TryGetValue("organizations", out object organizationsObject) && organizationsObject is IEnumerable organizations)
        {
          foreach (object organizationObject in organizations)
          {
            var organization = new Dictionary<string, string>(StringComparer.Ordinal);
            CopyStrings(organizationObject, organization);
            model.Organizations.Add(organization);
          }
        }
        if (model.Organizations.Count == 0)
          model.Organizations.Add(new Dictionary<string, string>(StringComparer.Ordinal));
        return true;
      }
      catch (Exception exception)
      {
        error = "初始化载荷解析失败：" + exception.Message;
        return false;
      }
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
