using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Stage02
{
  public sealed class Stage02MatchEngine
  {
    private sealed class MatchProof
    {
      internal MatchProof(
        HbrRuleDatabase database,
        string activeProfileId,
        Stage02ElementReference element,
        string roleId,
        string matchSource)
      {
        HbrRulePackage package = database.Package;
        PackageId = package.PackageId;
        PackageVersion = package.PackageVersion;
        PackageSha256 = package.RulePackageSha256;
        ActiveProfileId = activeProfileId ?? string.Empty;
        Element = element;
        RoleId = roleId ?? string.Empty;
        MatchSource = matchSource ?? string.Empty;
      }

      internal string PackageId { get; }
      internal string PackageVersion { get; }
      internal string PackageSha256 { get; }
      internal string ActiveProfileId { get; }
      internal Stage02ElementReference Element { get; }
      internal string RoleId { get; }
      internal string MatchSource { get; }
    }

    private readonly HbrRuleDatabase _database;
    private readonly string _activeProfileId;
    private readonly IReadOnlyList<HbrCarrierRole> _candidateRoles;
    private readonly IReadOnlyList<Stage02Blocker> _initializationBlockers;
    private readonly string _expectedDocumentFingerprint;
    private readonly string _expectedDocumentTitle;

    internal Stage02MatchEngine(
      HbrRuleDatabase database,
      string activeProfileId)
    {
      _database = database ?? throw new ArgumentNullException(nameof(database));
      _activeProfileId = activeProfileId ?? string.Empty;
      _expectedDocumentFingerprint = string.Empty;
      _expectedDocumentTitle = string.Empty;

      var blockers = new List<Stage02Blocker>();
      HbrModelProfile profile;
      if (!_database.ProfilesByModelFileType.TryGetValue(
        _activeProfileId,
        out profile))
      {
        blockers.Add(Blocker(
          Stage02Codes.UnknownModelProfile,
          "未知的活动模型 profile：" + Display(_activeProfileId) + "。"));
        _candidateRoles = Stage02Collections.Freeze(
          Array.Empty<HbrCarrierRole>());
        _initializationBlockers = Stage02Collections.Freeze(blockers);
        return;
      }

      IEnumerable<string> requestedRoleIds = _database.Package.CarrierRoles
        .Where(role => IsActive(role, _activeProfileId))
        .Select(role => role.RoleId);

      var roles = new List<HbrCarrierRole>();
      foreach (string roleId in requestedRoleIds
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(x => x, StringComparer.Ordinal))
      {
        HbrCarrierRole role;
        if (!_database.CarrierRolesById.TryGetValue(roleId, out role))
        {
          blockers.Add(Blocker(
            Stage02Codes.UnknownCarrierRole,
            "候选集合引用了未知载体角色：" + roleId + "。"));
          continue;
        }

        if (!IsActive(role, _activeProfileId))
        {
          blockers.Add(Blocker(
            Stage02Codes.CarrierNotActive,
            "载体角色 " + roleId + " 未在当前模型 profile 中激活。"));
          continue;
        }
        roles.Add(role);
      }

      _candidateRoles = Stage02Collections.Freeze(roles);
      _initializationBlockers = Stage02Collections.Freeze(blockers);
    }

    public Stage02MatchEngine(
      HbrRuleDatabase database,
      HBRFileContext context)
      : this(
        database,
        context == null ? string.Empty : context.ModelFileType)
    {
      var blockers = new List<Stage02Blocker>(_initializationBlockers);
      if (!Stage02FileContextPolicy.IsVerified(context))
      {
        blockers.Add(Blocker(
          Stage02Codes.InvalidFileContext,
          "HBRFileContext 无效、哈希被篡改、schema 不兼容或尚未通过 Stage01 初始化校验。"));
      }
      else
      {
        _expectedDocumentFingerprint = context.RevitDocumentFingerprint;
        _expectedDocumentTitle = context.RevitDocumentTitle;
        HbrRulePackage package = _database.Package;
        if (!string.Equals(
            context.RulePackageId,
            package.PackageId,
            StringComparison.Ordinal)
          || !string.Equals(
            context.RulePackageVersion,
            package.PackageVersion,
            StringComparison.Ordinal)
          || !string.Equals(
            context.RulePackageSha256,
            package.RulePackageSha256,
            StringComparison.Ordinal))
        {
          blockers.Add(Blocker(
            Stage02Codes.RulePackageIdentityMismatch,
            "HBRFileContext 的规则包身份与当前数据库不一致。"));
        }
      }
      _initializationBlockers = Stage02Collections.Freeze(blockers);
    }

    public Stage02MatchResult Match(
      Stage02ElementReference element,
      string roleHint = null,
      string savedRoleId = null)
    {
      if (_initializationBlockers.Count > 0)
        return Stage02MatchResult.Blocked(_initializationBlockers);

      if (element == null
        || string.IsNullOrWhiteSpace(element.DocumentFingerprint)
        || string.IsNullOrWhiteSpace(element.DocumentTitle)
        || string.IsNullOrWhiteSpace(element.UniqueId)
        || string.IsNullOrWhiteSpace(element.ElementKind))
      {
        return Blocked(
          Stage02Codes.InvalidElementReference,
          "元素引用缺少文档指纹、文档标题、ElementKind 或 Revit UniqueId。");
      }

      if (string.IsNullOrWhiteSpace(element.Category))
      {
        return Blocked(
          Stage02Codes.CarrierCategoryMismatch,
          "元素引用缺少 Revit 类别；系统不会仅凭角色、名称或 ElementKind 猜测载体。");
      }

      if ((!string.IsNullOrEmpty(_expectedDocumentFingerprint)
          && !string.Equals(
            _expectedDocumentFingerprint,
            element.DocumentFingerprint,
            StringComparison.Ordinal))
        || (!string.IsNullOrEmpty(_expectedDocumentTitle)
          && !string.Equals(
            _expectedDocumentTitle,
            element.DocumentTitle,
            StringComparison.Ordinal)))
      {
        return Blocked(
          Stage02Codes.InvalidElementReference,
          "元素引用不属于当前 HBRFileContext 对应的 Revit 文档。");
      }

      if (!string.IsNullOrWhiteSpace(roleHint))
        return MatchDeclaredRole(
          element,
          roleHint,
          Stage02MatchSources.RoleHint,
          "显式角色提示");

      if (!string.IsNullOrWhiteSpace(savedRoleId))
        return MatchDeclaredRole(
          element,
          savedRoleId,
          Stage02MatchSources.SavedRole,
          "已保存角色元数据");

      List<HbrCarrierRole> categoryMatches = _candidateRoles
        .Where(role => ContainsOrdinal(role.RevitCategories, element.Category))
        .ToList();
      List<HbrCarrierRole> kindMatches = categoryMatches
        .Where(role => ContainsOrdinal(
          role.AllowedElementKinds,
          element.ElementKind))
        .ToList();
      if (categoryMatches.Count > 0 && kindMatches.Count == 0)
      {
        return Blocked(
          Stage02Codes.CarrierElementKindMismatch,
          "Revit 类别可识别，但 ElementKind 不在载体角色允许集合中。");
      }
      if (kindMatches.Count == 1)
        return Matched(
          element,
          kindMatches[0].RoleId,
          Stage02MatchSources.Category);
      if (kindMatches.Count > 1)
      {
        List<HbrCarrierRole> narrowedByAlias = kindMatches
          .Where(role => MatchesAlias(role, element))
          .ToList();
        if (narrowedByAlias.Count == 1)
          return Matched(
            element,
            narrowedByAlias[0].RoleId,
            Stage02MatchSources.NameAlias);
        return Ambiguous("Revit 类别", kindMatches);
      }

      return Blocked(
        Stage02Codes.CarrierCategoryMismatch,
        "Revit 类别不在当前活动载体角色的兼容集合中；名称别名不能越过类别边界。");
    }

    private Stage02MatchResult MatchDeclaredRole(
      Stage02ElementReference element,
      string roleId,
      string matchSource,
      string sourceLabel)
    {
      HbrCarrierRole role;
      if (!_database.CarrierRolesById.TryGetValue(roleId, out role))
      {
        return Blocked(
          Stage02Codes.UnknownCarrierRole,
          sourceLabel + "引用了未知载体角色：" + roleId + "。");
      }

      if (!_candidateRoles.Any(candidate =>
        string.Equals(candidate.RoleId, roleId, StringComparison.Ordinal)))
      {
        return Blocked(
          Stage02Codes.CarrierNotActive,
          sourceLabel + "中的角色 " + roleId
          + " 不属于当前活动 profile 的允许集合。");
      }

      if (!ContainsOrdinal(role.RevitCategories, element.Category))
      {
        return Blocked(
          Stage02Codes.CarrierCategoryMismatch,
          sourceLabel + "中的角色 " + roleId
          + " 不允许用于当前 Revit 类别 " + element.Category + "。");
      }

      if (!ContainsOrdinal(role.AllowedElementKinds, element.ElementKind))
      {
        return Blocked(
          Stage02Codes.CarrierElementKindMismatch,
          sourceLabel + "中的角色 " + roleId
          + " 不允许用于当前 ElementKind " + element.ElementKind + "。");
      }

      return Matched(element, role.RoleId, matchSource);
    }

    internal static bool HasValidMatchProof(
      HbrRuleDatabase database,
      string activeProfileId,
      Stage02MatchedElement matched)
    {
      var proof = matched == null ? null : matched.MatchProof as MatchProof;
      if (database == null
        || proof == null
        || proof.Element == null
        || matched.Element == null)
        return false;
      HbrRulePackage package = database.Package;
      return Equal(proof.PackageId, package.PackageId)
        && Equal(proof.PackageVersion, package.PackageVersion)
        && Equal(proof.PackageSha256, package.RulePackageSha256)
        && Equal(proof.ActiveProfileId, activeProfileId)
        && Equal(proof.RoleId, matched.RoleId)
        && Equal(proof.MatchSource, matched.MatchSource)
        && ElementSnapshotsEqual(proof.Element, matched.Element);
    }

    private Stage02MatchResult Matched(
      Stage02ElementReference element,
      string roleId,
      string matchSource)
    {
      return Stage02MatchResult.Matched(
        roleId,
        matchSource,
        new MatchProof(
          _database,
          _activeProfileId,
          element,
          roleId,
          matchSource));
    }

    private static bool ElementSnapshotsEqual(
      Stage02ElementReference left,
      Stage02ElementReference right)
    {
      return Equal(left.DocumentFingerprint, right.DocumentFingerprint)
        && Equal(left.DocumentTitle, right.DocumentTitle)
        && Equal(left.UniqueId, right.UniqueId)
        && Equal(left.Category, right.Category)
        && Equal(left.ElementKind, right.ElementKind)
        && Equal(left.FamilyName, right.FamilyName)
        && Equal(left.TypeName, right.TypeName)
        && Equal(left.ElementName, right.ElementName);
    }

    private static bool Equal(string left, string right)
    {
      return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static bool MatchesAlias(
      HbrCarrierRole role,
      Stage02ElementReference element)
    {
      return MatchesAlias(role.NameAliases, element.ElementName)
        || MatchesAlias(role.FamilyAliases, element.FamilyName)
        || MatchesAlias(role.TypeAliases, element.TypeName);
    }

    private static bool MatchesAlias(
      IEnumerable<string> aliases,
      string value)
    {
      string normalizedValue = NormalizeAlias(value);
      return normalizedValue.Length > 0 && aliases.Any(alias =>
        string.Equals(
          NormalizeAlias(alias),
          normalizedValue,
          StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeAlias(string value)
    {
      return string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Normalize(NormalizationForm.FormKC);
    }

    private static bool ContainsOrdinal(
      IEnumerable<string> values,
      string value)
    {
      return !string.IsNullOrWhiteSpace(value)
        && values.Any(item =>
          string.Equals(item, value, StringComparison.Ordinal));
    }

    private static bool IsActive(HbrCarrierRole role, string profileId)
    {
      return role.ModelFileTypes.Any(item =>
        string.Equals(item, profileId, StringComparison.Ordinal));
    }

    private static Stage02MatchResult Ambiguous(
      string evidence,
      IEnumerable<HbrCarrierRole> roles)
    {
      return Blocked(
        Stage02Codes.AmbiguousCarrier,
        evidence + "对应多个载体角色（"
        + string.Join(", ", roles.Select(x => x.RoleId)
          .OrderBy(x => x, StringComparer.Ordinal))
        + "）；必须提供明确的角色提示，系统不会猜测。");
    }

    private static Stage02MatchResult Blocked(string code, string message)
    {
      return Stage02MatchResult.Blocked(new[] { Blocker(code, message) });
    }

    private static Stage02Blocker Blocker(string code, string message)
    {
      return new Stage02Blocker(code, message);
    }

    private static string Display(string value)
    {
      return string.IsNullOrWhiteSpace(value) ? "<空>" : value;
    }
  }
}
