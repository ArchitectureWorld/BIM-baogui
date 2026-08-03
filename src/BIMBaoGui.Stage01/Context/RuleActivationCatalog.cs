using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Context
{
  internal sealed class RuleActivationResult
  {
    public RuleActivationResult(
      IEnumerable<string> activated,
      IEnumerable<string> notApplicable)
    {
      Activated = (activated ?? Array.Empty<string>())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      NotApplicable = (notApplicable ?? Array.Empty<string>())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    }

    public IReadOnlyList<string> Activated { get; }
    public IReadOnlyList<string> NotApplicable { get; }
  }

  internal sealed class RuleActivationProjection
  {
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>>
      _profileRules;
    private readonly IReadOnlyList<string> _unknownModelRules;

    internal RuleActivationProjection(
      IReadOnlyDictionary<string, IReadOnlyList<string>> profileRules,
      IReadOnlyDictionary<string, string> conditionRules,
      IReadOnlyList<string> unknownModelRules)
    {
      _profileRules = profileRules
        ?? throw new ArgumentNullException(nameof(profileRules));
      ConditionRules = conditionRules
        ?? throw new ArgumentNullException(nameof(conditionRules));
      _unknownModelRules = unknownModelRules
        ?? throw new ArgumentNullException(nameof(unknownModelRules));
    }

    public IReadOnlyDictionary<string, string> ConditionRules { get; }

    public RuleActivationResult Compile(
      string modelFileType,
      IDictionary<string, bool> conditions)
    {
      var activated = new List<string>();
      var notApplicable = new List<string>();
      if (modelFileType != null
        && _profileRules.TryGetValue(
          modelFileType,
          out IReadOnlyList<string> profileRules))
        activated.AddRange(profileRules);
      else
        activated.AddRange(_unknownModelRules);

      foreach (KeyValuePair<string, string> rule in ConditionRules)
      {
        bool applies = conditions != null
          && conditions.TryGetValue(rule.Key, out bool enabled)
          && enabled;
        if (applies) activated.Add(rule.Value);
        else notApplicable.Add(rule.Value);
      }
      return new RuleActivationResult(activated, notApplicable);
    }
  }

  internal static class RuleActivationCatalog
  {
    private static readonly Lazy<RuleActivationProjection> LazyProjection =
      new Lazy<RuleActivationProjection>(() =>
        FromDatabase(HbrRuleDatabase.Current));

    internal static RuleActivationProjection FromDatabase(
      HbrRuleDatabase database)
    {
      if (database == null) throw new ArgumentNullException(nameof(database));
      var profiles = new Dictionary<string, IReadOnlyList<string>>(
        StringComparer.Ordinal);
      foreach (HbrModelProfile profile in database.Package.ModelProfiles)
      {
        if (profiles.ContainsKey(profile.ProfileId))
          throw new InvalidDataException(
            "HBR activation profile is duplicated: " + profile.ProfileId);
        profiles.Add(profile.ProfileId, profile.ActivationRuleIds.ToArray());
      }
      if (profiles.Count == 0)
        throw new InvalidDataException("HBR activation profiles are empty.");

      var conditions = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (HbrConditionRule condition in database.Package.Conditions)
      {
        if (string.IsNullOrWhiteSpace(condition.ActivationRuleId)) continue;
        if (conditions.ContainsKey(condition.ConditionId))
          throw new InvalidDataException(
            "HBR activation condition is duplicated: "
            + condition.ConditionId);
        conditions.Add(condition.ConditionId, condition.ActivationRuleId);
      }

      var commonRules = new HashSet<string>(
        profiles.First().Value,
        StringComparer.Ordinal);
      foreach (IReadOnlyList<string> profileRules in profiles.Values.Skip(1))
        commonRules.IntersectWith(profileRules);
      string[] unknownModelRules = database.Package.ModelProfiles[0]
        .ActivationRuleIds
        .Where(commonRules.Contains)
        .ToArray();
      return new RuleActivationProjection(
        new ReadOnlyDictionary<string, IReadOnlyList<string>>(profiles),
        new ReadOnlyDictionary<string, string>(conditions),
        unknownModelRules);
    }

    public static RuleActivationResult Compile(
      string modelFileType,
      IDictionary<string, bool> conditions)
    {
      return LazyProjection.Value.Compile(modelFileType, conditions);
    }
  }
}
