using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Core
{
  internal sealed class Stage01Model
  {
    public Stage01Model()
    {
      Values = new Dictionary<string, string>(StringComparer.Ordinal);
      Conditions = new Dictionary<string, bool>(StringComparer.Ordinal);
      Organizations = new List<Dictionary<string, string>>
      {
        new Dictionary<string, string>(StringComparer.Ordinal)
      };
    }

    public Dictionary<string, string> Values { get; }
    public Dictionary<string, bool> Conditions { get; }
    public List<Dictionary<string, string>> Organizations { get; }
    public bool ConfirmBlankProject { get; set; }
    public bool AllowReinitialize { get; set; }
    public bool ShowAllFields { get; set; }
    public string ActiveGroup { get; set; } = "01_文件与项目身份";
    public int ScrollOffset { get; set; }
    public int OrganizationIndex { get; set; }

    public string GetValue(string key)
    {
      return Values.TryGetValue(key, out string value) ? value ?? string.Empty : string.Empty;
    }

    public void SetValue(string key, string value)
    {
      Values[key] = value ?? string.Empty;
    }

    public bool GetCondition(string key)
    {
      return Conditions.TryGetValue(key, out bool value) && value;
    }

    public void SetCondition(string key, bool value)
    {
      Conditions[key] = value;
    }

    public Dictionary<string, string> CurrentOrganization
    {
      get
      {
        if (Organizations.Count == 0)
          Organizations.Add(new Dictionary<string, string>(StringComparer.Ordinal));
        OrganizationIndex = Math.Max(0, Math.Min(OrganizationIndex, Organizations.Count - 1));
        return Organizations[OrganizationIndex];
      }
    }

    public string GetOrganizationValue(string key)
    {
      Dictionary<string, string> record = CurrentOrganization;
      return record.TryGetValue(key, out string value) ? value ?? string.Empty : string.Empty;
    }

    public void SetOrganizationValue(string key, string value)
    {
      CurrentOrganization[key] = value ?? string.Empty;
    }

    public Stage01Model Clone()
    {
      var clone = new Stage01Model
      {
        ConfirmBlankProject = ConfirmBlankProject,
        AllowReinitialize = AllowReinitialize,
        ShowAllFields = ShowAllFields,
        ActiveGroup = ActiveGroup,
        ScrollOffset = ScrollOffset,
        OrganizationIndex = OrganizationIndex
      };
      clone.Values.Clear();
      foreach (KeyValuePair<string, string> pair in Values)
        clone.Values[pair.Key] = pair.Value;
      clone.Conditions.Clear();
      foreach (KeyValuePair<string, bool> pair in Conditions)
        clone.Conditions[pair.Key] = pair.Value;
      clone.Organizations.Clear();
      foreach (Dictionary<string, string> record in Organizations)
        clone.Organizations.Add(record.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
      return clone;
    }
  }
}
