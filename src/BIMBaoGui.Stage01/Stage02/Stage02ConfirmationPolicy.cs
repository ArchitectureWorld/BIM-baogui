using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMBaoGui.Stage01.Stage02
{
  internal sealed class Stage02ConfirmationConsumptionStore
  {
    private readonly object _sync = new object();
    private readonly HashSet<string> _consumed =
      new HashSet<string>(StringComparer.Ordinal);

    internal bool TryConsume(string previewHash, string nonce)
    {
      string key = (previewHash ?? string.Empty)
        + "|"
        + (nonce ?? string.Empty);
      lock (_sync)
      {
        return _consumed.Add(key);
      }
    }
  }

  public sealed class Stage02ConfirmationPolicy
  {
    private static readonly Stage02ConfirmationConsumptionStore SharedStore =
      new Stage02ConfirmationConsumptionStore();

    private readonly Stage02ConfirmationConsumptionStore _consumptionStore;

    public Stage02ConfirmationPolicy()
      : this(SharedStore)
    {
    }

    internal Stage02ConfirmationPolicy(
      Stage02ConfirmationConsumptionStore consumptionStore)
    {
      _consumptionStore = consumptionStore
        ?? throw new ArgumentNullException(nameof(consumptionStore));
    }

    public Stage02ConfirmationResult ValidateAndConsumeForExecution(
      Stage02Preview preview,
      Stage02ConfirmationSnapshot current)
    {
      IReadOnlyList<Stage02Blocker> blockers = Validate(preview, current);
      if (blockers.Count > 0)
        return Stage02ConfirmationResult.Reject(blockers);

      if (!_consumptionStore.TryConsume(preview.PreviewHash, preview.Nonce))
      {
        return Reject(
          Stage02Codes.PreviewAlreadyConsumed,
          "该预览确认令牌已被消费，不能重复执行。");
      }
      return Stage02ConfirmationResult.AcceptForExecution(
        preview.PreviewHash + "|" + preview.Nonce);
    }

    private static IReadOnlyList<Stage02Blocker> Validate(
      Stage02Preview preview,
      Stage02ConfirmationSnapshot current)
    {
      var blockers = new List<Stage02Blocker>();
      if (preview == null || current == null)
      {
        blockers.Add(new Stage02Blocker(
          Stage02Codes.InvalidConfirmationSnapshot,
          "预览或当前确认快照不能为空。"));
        return Stage02Collections.Freeze(blockers);
      }

      if (preview.Elements.Any(element =>
        element != null
        && element.Operations.Any(operation =>
          operation != null && operation.Blockers.Count > 0)))
      {
        blockers.Add(new Stage02Blocker(
          Stage02Codes.PreviewHasBlockers,
          "预览包含未解决 blocker，不能确认执行。"));
      }

      bool canonicalInvalid;
      try
      {
        string rebuiltCanonical = Stage02Canonicalizer.BuildPreview(preview);
        canonicalInvalid = !Equal(
            rebuiltCanonical,
            preview.CanonicalPayload)
          || !Equal(
            Stage02Hash.Sha256(preview.CanonicalPayload),
            preview.PreviewHash);
      }
      catch (Exception)
      {
        canonicalInvalid = true;
      }
      if (canonicalInvalid)
      {
        blockers.Add(new Stage02Blocker(
          Stage02Codes.PreviewHashChanged,
          "预览对象图、canonical payload 或 PreviewHash 不一致。"));
      }

      Compare(
        blockers,
        preview.PreviewHash,
        current.PreviewHash,
        Stage02Codes.PreviewHashChanged,
        "当前确认快照的 PreviewHash 与预览不一致。");
      Compare(
        blockers,
        preview.Nonce,
        current.Nonce,
        Stage02Codes.NonceChanged,
        "当前确认快照的 nonce 与预览不一致。");
      Compare(
        blockers,
        preview.FileGuid,
        current.FileGuid,
        Stage02Codes.FileGuidChanged,
        "HBR FileGuid 已变化，必须重新生成预览。");
      Compare(
        blockers,
        preview.DocumentFingerprint,
        current.DocumentFingerprint,
        Stage02Codes.DocumentFingerprintChanged,
        "Revit 文档指纹已变化，必须重新生成预览。");
      Compare(
        blockers,
        preview.FileContextHash,
        current.FileContextHash,
        Stage02Codes.FileContextChanged,
        "FileContextHash 已变化，必须重新生成预览。");
      if (!ConditionsEqual(
        preview.ProjectConditions,
        current.ProjectConditions))
      {
        blockers.Add(new Stage02Blocker(
          Stage02Codes.FileContextChanged,
          "ProjectConditions 已变化，必须重新生成预览。"));
      }
      Compare(
        blockers,
        preview.ActiveProfileId,
        current.ActiveProfileId,
        Stage02Codes.ActiveProfileChanged,
        "活动模型 profile 已变化，必须重新生成预览。");

      if (!Equal(preview.RulePackageId, current.RulePackageId)
        || !Equal(
          preview.RulePackageVersion,
          current.RulePackageVersion)
        || !Equal(
          preview.RulePackageSha256,
          current.RulePackageSha256))
      {
        blockers.Add(new Stage02Blocker(
          Stage02Codes.RulePackageIdentityChanged,
          "规则包 ID、版本或 SHA-256 已变化，必须重新生成预览。"));
      }

      ValidateElements(preview, current, blockers);
      return Stage02Collections.Freeze(blockers);
    }

    private static void ValidateElements(
      Stage02Preview preview,
      Stage02ConfirmationSnapshot current,
      ICollection<Stage02Blocker> blockers)
    {
      if (current.Elements.Any(item =>
        item == null
        || item.Element == null
        || !Equal(
          current.DocumentFingerprint,
          item.Element.DocumentFingerprint)))
      {
        blockers.Add(new Stage02Blocker(
          Stage02Codes.DocumentFingerprintChanged,
          "当前元素引用不属于待确认的 Revit 文档。"));
        return;
      }

      Dictionary<string, Stage02MatchedElement> expected = ToExpectedMap(
        preview.Elements);
      Dictionary<string, Stage02CurrentElementSnapshot> actual = ToCurrentMap(
        current.Elements);
      if (expected == null
        || actual == null
        || !expected.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
          actual.Keys.OrderBy(x => x, StringComparer.Ordinal),
          StringComparer.Ordinal))
      {
        blockers.Add(new Stage02Blocker(
          Stage02Codes.ElementSetChanged,
          "Revit UniqueId 集合已变化，必须重新生成预览。"));
        return;
      }

      foreach (KeyValuePair<string, Stage02MatchedElement> pair in expected)
      {
        Stage02CurrentElementSnapshot currentElement = actual[pair.Key];
        if (!Equal(pair.Value.RoleId, currentElement.RoleId))
        {
          blockers.Add(new Stage02Blocker(
            Stage02Codes.RoleSnapshotChanged,
            "元素 " + pair.Key + " 的载体角色已变化。"));
        }
        if (!Equal(pair.Value.MatchSource, currentElement.MatchSource))
        {
          blockers.Add(new Stage02Blocker(
            Stage02Codes.RoleSnapshotChanged,
            "元素 " + pair.Key + " 的角色匹配来源已变化。"));
        }
        if (!Equal(
          pair.Value.Stage01RecordIdentity,
          currentElement.Stage01RecordIdentity))
        {
          blockers.Add(new Stage02Blocker(
            Stage02Codes.RoleSnapshotChanged,
            "元素 " + pair.Key + " 的 Stage01 业务记录身份已变化。"));
        }
        if (!ElementSnapshotsEqual(
          pair.Value.Element,
          currentElement.Element))
        {
          blockers.Add(new Stage02Blocker(
            Stage02Codes.ElementSnapshotChanged,
            "元素 " + pair.Key + " 的匹配输入快照已变化。"));
        }
        if (!OperationsEqual(pair.Value.Operations, currentElement.Properties))
        {
          blockers.Add(new Stage02Blocker(
            Stage02Codes.OldValueChanged,
            "元素 " + pair.Key + " 的旧值快照已变化。"));
        }
      }
    }

    private static Dictionary<string, Stage02MatchedElement> ToExpectedMap(
      IEnumerable<Stage02MatchedElement> elements)
    {
      var result = new Dictionary<string, Stage02MatchedElement>(
        StringComparer.Ordinal);
      foreach (Stage02MatchedElement item in elements)
      {
        if (item == null
          || item.Element == null
          || string.IsNullOrWhiteSpace(item.Element.UniqueId)
          || result.ContainsKey(item.Element.UniqueId))
          return null;
        result.Add(item.Element.UniqueId, item);
      }
      return result;
    }

    private static Dictionary<string, Stage02CurrentElementSnapshot>
      ToCurrentMap(IEnumerable<Stage02CurrentElementSnapshot> elements)
    {
      var result = new Dictionary<string, Stage02CurrentElementSnapshot>(
        StringComparer.Ordinal);
      foreach (Stage02CurrentElementSnapshot item in elements)
      {
        if (item == null
          || item.Element == null
          || string.IsNullOrWhiteSpace(item.Element.UniqueId)
          || result.ContainsKey(item.Element.UniqueId))
          return null;
        result.Add(item.Element.UniqueId, item);
      }
      return result;
    }

    private static bool OperationsEqual(
      IEnumerable<Stage02WriteOperation> expected,
      IEnumerable<Stage02CurrentPropertySnapshot> actual)
    {
      Dictionary<string, Stage02WriteOperation> expectedByProperty =
        ToOperationMap(expected);
      Dictionary<string, Stage02CurrentPropertySnapshot> actualByProperty =
        ToPropertyMap(actual);
      if (expectedByProperty == null
        || actualByProperty == null
        || expectedByProperty.Count != actualByProperty.Count)
        return false;
      foreach (KeyValuePair<string, Stage02WriteOperation> pair in
        expectedByProperty)
      {
        Stage02CurrentPropertySnapshot current;
        if (!actualByProperty.TryGetValue(pair.Key, out current))
          return false;
        try
        {
          if (!Equal(
            Stage02Canonicalizer.BuildOperation(pair.Value),
            Stage02Canonicalizer.BuildOperation(
              current.Operation,
              current.OldValueHash)))
            return false;
        }
        catch (Exception)
        {
          return false;
        }
      }
      return true;
    }

    private static bool ElementSnapshotsEqual(
      Stage02ElementReference expected,
      Stage02ElementReference actual)
    {
      return expected != null
        && actual != null
        && Equal(expected.DocumentFingerprint, actual.DocumentFingerprint)
        && Equal(expected.DocumentTitle, actual.DocumentTitle)
        && Equal(expected.UniqueId, actual.UniqueId)
        && Equal(expected.Category, actual.Category)
        && Equal(expected.ElementKind, actual.ElementKind)
        && Equal(expected.FamilyName, actual.FamilyName)
        && Equal(expected.TypeName, actual.TypeName)
        && Equal(expected.ElementName, actual.ElementName);
    }

    private static Dictionary<string, Stage02WriteOperation> ToOperationMap(
      IEnumerable<Stage02WriteOperation> operations)
    {
      var result = new Dictionary<string, Stage02WriteOperation>(
        StringComparer.Ordinal);
      foreach (Stage02WriteOperation operation in operations)
      {
        if (operation == null
          || string.IsNullOrWhiteSpace(operation.PropertyId)
          || result.ContainsKey(operation.PropertyId))
          return null;
        result.Add(operation.PropertyId, operation);
      }
      return result;
    }

    private static Dictionary<string, Stage02CurrentPropertySnapshot>
      ToPropertyMap(IEnumerable<Stage02CurrentPropertySnapshot> properties)
    {
      var result = new Dictionary<string, Stage02CurrentPropertySnapshot>(
        StringComparer.Ordinal);
      foreach (Stage02CurrentPropertySnapshot property in properties)
      {
        if (property == null
          || string.IsNullOrWhiteSpace(property.PropertyId)
          || result.ContainsKey(property.PropertyId))
          return null;
        result.Add(property.PropertyId, property);
      }
      return result;
    }

    private static void Compare(
      ICollection<Stage02Blocker> blockers,
      string expected,
      string actual,
      string code,
      string message)
    {
      if (!Equal(expected, actual))
        blockers.Add(new Stage02Blocker(code, message));
    }

    private static bool Equal(string left, string right)
    {
      return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static bool ConditionsEqual(
      IReadOnlyDictionary<string, bool> left,
      IReadOnlyDictionary<string, bool> right)
    {
      if (left == null || right == null || left.Count != right.Count)
        return false;
      foreach (KeyValuePair<string, bool> pair in left)
      {
        bool value;
        if (!right.TryGetValue(pair.Key, out value) || value != pair.Value)
          return false;
      }
      return true;
    }

    private static Stage02ConfirmationResult Reject(
      string code,
      string message)
    {
      return Stage02ConfirmationResult.Reject(
        new[] { new Stage02Blocker(code, message) });
    }
  }
}
