using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.Stage01.Revit.Parameters
{
  public static class HbrSuggestionSources
  {
    public const string CanonicalGuid = "CANONICAL_GUID";
    public const string LegacyName = "LEGACY_NAME";
    public const string RuleAlias = "RULE_ALIAS";
    public const string Stage01Projection = "STAGE01_PROJECTION";
    public const string Blank = "BLANK";
  }

  public static class HbrSuggestionBlockerCodes
  {
    public const string InvalidCandidate = "INVALID_VALUE_CANDIDATE";
    public const string AmbiguousNamedParameter =
      "AMBIGUOUS_NAMED_PARAMETER";
    public const string MultipleLegacyValues = "MULTIPLE_LEGACY_VALUES";
    public const string MultipleAliasValues = "MULTIPLE_ALIAS_VALUES";
    public const string SourceTypeMismatch =
      "SUGGESTION_SOURCE_TYPE_MISMATCH";
    public const string AmbiguousSuggestionAliasRule =
      "AMBIGUOUS_SUGGESTION_ALIAS_RULE";
  }

  public sealed class HbrParameterValueCandidate
  {
    public HbrParameterValueCandidate(
      string source,
      string identity,
      string value,
      int matchCount)
      : this(
        source,
        identity,
        value,
        matchCount,
        string.Empty,
        string.Empty,
        string.Empty,
        true,
        !string.Equals(
          source,
          HbrSuggestionSources.Stage01Projection,
          StringComparison.Ordinal),
        1)
    {
    }

    public HbrParameterValueCandidate(
      string source,
      string identity,
      string value,
      int matchCount,
      string sourceStorageType,
      string sourceParameterType,
      string sourceParameterGuid,
      bool sourceTypeCompatible,
      bool sourceAlreadyUsesInternalUnits,
      int ruleAliasPropertyCount)
    {
      Source = source ?? string.Empty;
      Identity = identity ?? string.Empty;
      Value = value ?? string.Empty;
      MatchCount = matchCount;
      SourceStorageType = sourceStorageType ?? string.Empty;
      SourceParameterType = sourceParameterType ?? string.Empty;
      SourceParameterGuid = sourceParameterGuid ?? string.Empty;
      SourceTypeCompatible = sourceTypeCompatible;
      SourceAlreadyUsesInternalUnits = sourceAlreadyUsesInternalUnits;
      RuleAliasPropertyCount = ruleAliasPropertyCount;
    }

    public string Source { get; }
    public string Identity { get; }
    public string Value { get; }
    public int MatchCount { get; }
    public string SourceStorageType { get; }
    public string SourceParameterType { get; }
    public string SourceParameterGuid { get; }
    public bool SourceTypeCompatible { get; }
    public bool SourceAlreadyUsesInternalUnits { get; }
    public int RuleAliasPropertyCount { get; }
  }

  public sealed class HbrSuggestionBlocker
  {
    public HbrSuggestionBlocker(string code, string message)
    {
      Code = code ?? string.Empty;
      Message = message ?? string.Empty;
    }

    public string Code { get; }
    public string Message { get; }
  }

  public sealed class HbrParameterSuggestionDecision
  {
    internal HbrParameterSuggestionDecision(
      string suggestedValue,
      string valueSource,
      string sourceIdentity,
      bool sourceAlreadyUsesInternalUnits,
      IEnumerable<HbrSuggestionBlocker> blockers)
    {
      SuggestedValue = suggestedValue ?? string.Empty;
      ValueSource = valueSource ?? string.Empty;
      SourceIdentity = sourceIdentity ?? string.Empty;
      SourceAlreadyUsesInternalUnits = sourceAlreadyUsesInternalUnits;
      Blockers = new ReadOnlyCollection<HbrSuggestionBlocker>(
        (blockers ?? Array.Empty<HbrSuggestionBlocker>()).ToArray());
    }

    public string SuggestedValue { get; }
    public string ValueSource { get; }
    public string SourceIdentity { get; }
    public bool SourceAlreadyUsesInternalUnits { get; }
    public IReadOnlyList<HbrSuggestionBlocker> Blockers { get; }
  }

  public static class HbrParameterSuggestionPolicy
  {
    public static HbrParameterSuggestionDecision Resolve(
      IEnumerable<HbrParameterValueCandidate> candidates)
    {
      HbrParameterValueCandidate[] items = (candidates
        ?? Array.Empty<HbrParameterValueCandidate>()).ToArray();
      foreach (string source in new[]
      {
        HbrSuggestionSources.CanonicalGuid,
        HbrSuggestionSources.LegacyName,
        HbrSuggestionSources.RuleAlias,
        HbrSuggestionSources.Stage01Projection
      })
      {
        HbrParameterValueCandidate[] layer = items
          .Where(item => item != null && string.Equals(
            item.Source,
            source,
            StringComparison.Ordinal))
          .ToArray();
        var blockers = ValidateLayer(layer, source);
        if (blockers.Count > 0)
        {
          return new HbrParameterSuggestionDecision(
            string.Empty,
            HbrSuggestionSources.Blank,
            string.Empty,
            false,
            blockers);
        }
        HbrParameterValueCandidate selected = layer.FirstOrDefault(item =>
          item.MatchCount == 1 && !string.IsNullOrWhiteSpace(item.Value));
        if (selected != null)
        {
          return new HbrParameterSuggestionDecision(
            selected.Value,
            source,
            selected.Identity,
            selected.SourceAlreadyUsesInternalUnits,
            blockers);
        }
      }

      var trailingBlockers = new List<HbrSuggestionBlocker>();
      if (items.Any(item => item == null
        || string.IsNullOrWhiteSpace(item.Source)
        || string.IsNullOrWhiteSpace(item.Identity)
        || item.MatchCount < 0))
      {
        trailingBlockers.Add(Blocker(
          HbrSuggestionBlockerCodes.InvalidCandidate,
          "建议值候选缺少来源、身份或有效匹配计数。"));
      }

      return new HbrParameterSuggestionDecision(
        string.Empty,
        HbrSuggestionSources.Blank,
        string.Empty,
        false,
        trailingBlockers);
    }

    private static List<HbrSuggestionBlocker> ValidateLayer(
      IEnumerable<HbrParameterValueCandidate> layer,
      string source)
    {
      var items = (layer ?? Array.Empty<HbrParameterValueCandidate>())
        .ToArray();
      var blockers = new List<HbrSuggestionBlocker>();
      if (items.Any(item => string.IsNullOrWhiteSpace(item.Identity)
        || item.MatchCount < 0))
      {
        blockers.Add(Blocker(
          HbrSuggestionBlockerCodes.InvalidCandidate,
          "建议值候选缺少身份或有效匹配计数。"));
      }
      if (items.Any(item => item.MatchCount > 1))
      {
        blockers.Add(Blocker(
          HbrSuggestionBlockerCodes.AmbiguousNamedParameter,
          "当前最高优先级候选名称在目标上匹配到多个参数。"));
      }
      if (items.Any(item => item.MatchCount == 1
        && !string.IsNullOrWhiteSpace(item.Value)
        && !item.SourceTypeCompatible))
      {
        blockers.Add(Blocker(
          HbrSuggestionBlockerCodes.SourceTypeMismatch,
          "命名建议值参数的 StorageType/ParameterType 与目标规则不兼容。"));
      }
      int nonBlankCount = items.Count(item => item.MatchCount == 1
        && !string.IsNullOrWhiteSpace(item.Value));
      if (nonBlankCount > 1 && string.Equals(
        source,
        HbrSuggestionSources.LegacyName,
        StringComparison.Ordinal))
      {
        blockers.Add(Blocker(
          HbrSuggestionBlockerCodes.MultipleLegacyValues,
          "多个 legacyName 同时存在非空值，系统不会猜测。"));
      }
      if (nonBlankCount > 1 && string.Equals(
        source,
        HbrSuggestionSources.RuleAlias,
        StringComparison.Ordinal))
      {
        blockers.Add(Blocker(
          HbrSuggestionBlockerCodes.MultipleAliasValues,
          "多个规则 alias 同时存在非空值，系统不会猜测。"));
      }
      if (string.Equals(
          source,
          HbrSuggestionSources.RuleAlias,
          StringComparison.Ordinal)
        && items.Any(item => item.MatchCount == 1
          && !string.IsNullOrWhiteSpace(item.Value)
          && item.RuleAliasPropertyCount > 1))
      {
        blockers.Add(Blocker(
          HbrSuggestionBlockerCodes.AmbiguousSuggestionAliasRule,
          "当前非空规则 alias 在同一载体角色中映射多个属性，禁止广播写入。"));
      }
      return blockers;
    }

    private static HbrSuggestionBlocker Blocker(string code, string message)
    {
      return new HbrSuggestionBlocker(code, message);
    }
  }
}
