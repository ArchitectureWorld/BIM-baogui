using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.Stage01.Revit.Parameters
{
  public static class HbrBindingActions
  {
    public const string CreateAndBind = "CREATE_AND_BIND";
    public const string BindExisting = "BIND_EXISTING";
    public const string Reuse = "REUSE";
    public const string MergeCategories = "MERGE_CATEGORIES";
    public const string Blocked = "BLOCKED";
  }

  public static class HbrBindingBlockerCodes
  {
    public const string InvalidPlan = "INVALID_BINDING_PLAN";
    public const string UnknownSameGuidName = "UNKNOWN_SAME_GUID_NAME";
    public const string BindingScopeConflict = "BINDING_SCOPE_CONFLICT";
    public const string StorageTypeConflict = "STORAGE_TYPE_CONFLICT";
    public const string ParameterTypeConflict = "PARAMETER_TYPE_CONFLICT";
    public const string HiddenDefinition = "HIDDEN_DEFINITION";
    public const string NotUserModifiable = "NOT_USER_MODIFIABLE";
    public const string HideWhenNoValue = "HIDE_WHEN_NO_VALUE";
  }

  public sealed class HbrBindingPlanBlocker
  {
    public HbrBindingPlanBlocker(string code, string message)
    {
      Code = code ?? string.Empty;
      Message = message ?? string.Empty;
    }

    public string Code { get; }
    public string Message { get; }
  }

  public sealed class HbrBindingPlanState
  {
    public HbrBindingPlanState(
      string canonicalName,
      IEnumerable<string> legacyNames,
      string expectedBindingScope,
      string expectedStorageType,
      string expectedParameterType,
      bool definitionExists,
      string definitionName,
      bool bindingExists,
      string actualBindingScope,
      string actualStorageType,
      string actualParameterType,
      bool visible,
      bool userModifiable,
      bool hideWhenNoValue,
      IEnumerable<string> existingCategories,
      IEnumerable<string> requestedCategories)
    {
      CanonicalName = canonicalName ?? string.Empty;
      LegacyNames = Freeze(legacyNames);
      ExpectedBindingScope = expectedBindingScope ?? string.Empty;
      ExpectedStorageType = expectedStorageType ?? string.Empty;
      ExpectedParameterType = expectedParameterType ?? string.Empty;
      DefinitionExists = definitionExists;
      DefinitionName = definitionName ?? string.Empty;
      BindingExists = bindingExists;
      ActualBindingScope = actualBindingScope ?? string.Empty;
      ActualStorageType = actualStorageType ?? string.Empty;
      ActualParameterType = actualParameterType ?? string.Empty;
      Visible = visible;
      UserModifiable = userModifiable;
      HideWhenNoValue = hideWhenNoValue;
      ExistingCategories = Freeze(existingCategories);
      RequestedCategories = Freeze(requestedCategories);
    }

    public string CanonicalName { get; }
    public IReadOnlyList<string> LegacyNames { get; }
    public string ExpectedBindingScope { get; }
    public string ExpectedStorageType { get; }
    public string ExpectedParameterType { get; }
    public bool DefinitionExists { get; }
    public string DefinitionName { get; }
    public bool BindingExists { get; }
    public string ActualBindingScope { get; }
    public string ActualStorageType { get; }
    public string ActualParameterType { get; }
    public bool Visible { get; }
    public bool UserModifiable { get; }
    public bool HideWhenNoValue { get; }
    public IReadOnlyList<string> ExistingCategories { get; }
    public IReadOnlyList<string> RequestedCategories { get; }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values)
    {
      return new ReadOnlyCollection<string>((values ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray());
    }
  }

  public sealed class HbrBindingPlanDecision
  {
    internal HbrBindingPlanDecision(
      string action,
      IEnumerable<string> categories,
      IEnumerable<HbrBindingPlanBlocker> blockers)
    {
      Action = action ?? string.Empty;
      Categories = new ReadOnlyCollection<string>(
        (categories ?? Array.Empty<string>()).ToArray());
      Blockers = new ReadOnlyCollection<HbrBindingPlanBlocker>(
        (blockers ?? Array.Empty<HbrBindingPlanBlocker>()).ToArray());
    }

    public string Action { get; }
    public IReadOnlyList<string> Categories { get; }
    public IReadOnlyList<HbrBindingPlanBlocker> Blockers { get; }
  }

  public static class HbrBindingPlanPolicy
  {
    public static HbrBindingPlanDecision Evaluate(HbrBindingPlanState state)
    {
      if (state == null) throw new ArgumentNullException(nameof(state));

      string[] categories = state.ExistingCategories
        .Concat(state.RequestedCategories)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      var blockers = new List<HbrBindingPlanBlocker>();

      if (string.IsNullOrWhiteSpace(state.CanonicalName)
        || string.IsNullOrWhiteSpace(state.ExpectedBindingScope)
        || string.IsNullOrWhiteSpace(state.ExpectedStorageType)
        || string.IsNullOrWhiteSpace(state.ExpectedParameterType)
        || state.RequestedCategories.Count == 0)
      {
        blockers.Add(Blocker(
          HbrBindingBlockerCodes.InvalidPlan,
          "共享参数绑定计划缺少固定名称、类型、作用域或目标类别。"));
      }

      if (!state.DefinitionExists)
      {
        return Decision(
          blockers.Count == 0
            ? HbrBindingActions.CreateAndBind
            : HbrBindingActions.Blocked,
          state.RequestedCategories,
          blockers);
      }

      if (!IsAllowedDefinitionName(state))
      {
        blockers.Add(Blocker(
          HbrBindingBlockerCodes.UnknownSameGuidName,
          "同 GUID 的现有共享参数名称既不是 canonical 名，也不是规则声明的 legacyName。"));
      }
      if (!state.Visible)
      {
        blockers.Add(Blocker(
          HbrBindingBlockerCodes.HiddenDefinition,
          "同 GUID 的现有共享参数定义不可见。"));
      }
      if (!state.UserModifiable)
      {
        blockers.Add(Blocker(
          HbrBindingBlockerCodes.NotUserModifiable,
          "同 GUID 的现有共享参数不允许用户修改。"));
      }
      if (state.HideWhenNoValue)
      {
        blockers.Add(Blocker(
          HbrBindingBlockerCodes.HideWhenNoValue,
          "同 GUID 的现有共享参数会在无值时隐藏。"));
      }

      if (state.BindingExists)
      {
        Compare(
          blockers,
          state.ExpectedBindingScope,
          state.ActualBindingScope,
          HbrBindingBlockerCodes.BindingScopeConflict,
          "同 GUID 的现有参数绑定作用域与规则不一致。");
      }
      Compare(
        blockers,
        state.ExpectedStorageType,
        state.ActualStorageType,
        HbrBindingBlockerCodes.StorageTypeConflict,
        "同 GUID 的现有参数存储类型与规则不一致。");
      Compare(
        blockers,
        state.ExpectedParameterType,
        state.ActualParameterType,
        HbrBindingBlockerCodes.ParameterTypeConflict,
        "同 GUID 的现有参数语义类型与规则不一致。");

      if (blockers.Count > 0)
        return Decision(HbrBindingActions.Blocked, categories, blockers);
      if (!state.BindingExists)
        return Decision(HbrBindingActions.BindExisting, categories, blockers);
      if (state.RequestedCategories.All(category =>
        state.ExistingCategories.Contains(category, StringComparer.Ordinal)))
      {
        return Decision(HbrBindingActions.Reuse, categories, blockers);
      }
      return Decision(HbrBindingActions.MergeCategories, categories, blockers);
    }

    private static bool IsAllowedDefinitionName(HbrBindingPlanState state)
    {
      return string.Equals(
          state.DefinitionName,
          state.CanonicalName,
          StringComparison.Ordinal)
        || state.LegacyNames.Any(name => string.Equals(
          name,
          state.DefinitionName,
          StringComparison.Ordinal));
    }

    private static void Compare(
      ICollection<HbrBindingPlanBlocker> blockers,
      string expected,
      string actual,
      string code,
      string message)
    {
      if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        blockers.Add(Blocker(code, message));
    }

    private static HbrBindingPlanDecision Decision(
      string action,
      IEnumerable<string> categories,
      IEnumerable<HbrBindingPlanBlocker> blockers)
    {
      return new HbrBindingPlanDecision(action, categories, blockers);
    }

    private static HbrBindingPlanBlocker Blocker(string code, string message)
    {
      return new HbrBindingPlanBlocker(code, message);
    }
  }
}
