using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Stage02
{
  public sealed class Stage02PreviewCompiler
  {
    private readonly HbrRuleDatabase _database;

    public Stage02PreviewCompiler(HbrRuleDatabase database)
    {
      _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Stage02Preview Compile(Stage02PreviewRequest request)
    {
      ValidateRequest(request);

      var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
      var sortedElements = new List<Stage02MatchedElement>();
      foreach (Stage02MatchedElement matched in request.Elements)
      {
        Stage02WriteOperation[] operations = ValidateMatchedElement(
          request,
          matched,
          uniqueIds);
        sortedElements.Add(matched.WithOperations(operations));
      }
      sortedElements.Sort((left, right) => StringComparer.Ordinal.Compare(
        left.Element.UniqueId,
        right.Element.UniqueId));

      string canonicalPayload = Stage02Canonicalizer.BuildPreview(
        request,
        sortedElements);
      return new Stage02Preview(
        request,
        sortedElements,
        canonicalPayload,
        Stage02Hash.Sha256(canonicalPayload));
    }

    private void ValidateRequest(Stage02PreviewRequest request)
    {
      if (request == null)
        Throw(Stage02Codes.InvalidPreviewInput, "预览请求不能为空。");
      Require(request.FileGuid, "FileGuid");
      Require(request.DocumentFingerprint, "DocumentFingerprint");
      Require(request.DocumentTitle, "DocumentTitle");
      Require(request.FileContextHash, "FileContextHash");
      Require(request.ActiveProfileId, "ActiveProfileId");
      Require(request.RulePackageId, "RulePackageId");
      Require(request.RulePackageVersion, "RulePackageVersion");
      Require(request.RulePackageSha256, "RulePackageSha256");
      Require(request.Nonce, "nonce");
      HbrRulePackage package = _database.Package;
      if (!string.Equals(
          request.RulePackageId,
          package.PackageId,
          StringComparison.Ordinal)
        || !string.Equals(
          request.RulePackageVersion,
          package.PackageVersion,
          StringComparison.Ordinal)
        || !string.Equals(
          request.RulePackageSha256,
          package.RulePackageSha256,
          StringComparison.Ordinal))
      {
        Throw(
          Stage02Codes.RulePackageIdentityMismatch,
          "预览请求的规则包 ID、版本或 SHA-256 与注入数据库不一致。");
      }
      if (!_database.ProfilesByModelFileType.ContainsKey(
        request.ActiveProfileId))
      {
        Throw(
          Stage02Codes.UnknownModelProfile,
          "预览引用了未知的活动模型 profile："
          + request.ActiveProfileId + "。");
      }
      if (request.Elements.Count == 0)
        Throw(Stage02Codes.InvalidPreviewInput, "预览至少需要一个元素。");
    }

    private Stage02WriteOperation[] ValidateMatchedElement(
      Stage02PreviewRequest request,
      Stage02MatchedElement matched,
      ISet<string> uniqueIds)
    {
      if (matched == null || matched.Element == null)
        Throw(Stage02Codes.InvalidElementReference, "预览包含空元素引用。");
      Stage02ElementReference element = matched.Element;
      RequireElement(element.DocumentFingerprint, "DocumentFingerprint");
      RequireElement(element.DocumentTitle, "DocumentTitle");
      RequireElement(element.UniqueId, "UniqueId");
      RequireElement(element.ElementKind, "ElementKind");
      RequireElement(matched.MatchSource, "MatchSource");
      if (!string.Equals(
        request.DocumentFingerprint,
        element.DocumentFingerprint,
        StringComparison.Ordinal))
      {
        Throw(
          Stage02Codes.InvalidElementReference,
          "元素 " + element.UniqueId + " 的文档指纹与预览上下文不一致。");
      }
      if (!string.Equals(
        request.DocumentTitle,
        element.DocumentTitle,
        StringComparison.Ordinal))
      {
        Throw(
          Stage02Codes.InvalidElementReference,
          "元素 " + element.UniqueId + " 的文档标题与预览上下文不一致。");
      }
      if (!uniqueIds.Add(element.UniqueId))
      {
        Throw(
          Stage02Codes.DuplicateElementIdentity,
          "预览存在重复的 Revit UniqueId：" + element.UniqueId + "。");
      }

      HbrCarrierRole role;
      if (!_database.CarrierRolesById.TryGetValue(matched.RoleId, out role))
      {
        Throw(
          Stage02Codes.UnknownCarrierRole,
          "预览引用了未知载体角色：" + matched.RoleId + "。");
      }
      if (!role.ModelFileTypes.Any(profile => string.Equals(
        profile,
        request.ActiveProfileId,
        StringComparison.Ordinal)))
      {
        Throw(
          Stage02Codes.CarrierNotActive,
          "载体角色 " + matched.RoleId + " 未在当前模型 profile 中激活。");
      }
      if (!role.RevitCategories.Any(category => string.Equals(
        category,
        element.Category,
        StringComparison.Ordinal)))
      {
        Throw(
          Stage02Codes.CarrierCategoryMismatch,
          "元素 " + element.UniqueId + " 的 Revit 类别与载体角色不兼容。");
      }
      if (!role.AllowedElementKinds.Any(kind => string.Equals(
        kind,
        element.ElementKind,
        StringComparison.Ordinal)))
      {
        Throw(
          Stage02Codes.CarrierElementKindMismatch,
          "元素 " + element.UniqueId + " 的 ElementKind 与载体角色不兼容。");
      }
      ValidateMatchEvidence(request.ActiveProfileId, matched);

      var propertyIds = new HashSet<string>(StringComparer.Ordinal);
      var expectedPropertyIds = new HashSet<string>(
        _database.Package.Properties
          .Where(property => property.CarrierRoleIds.Any(roleId =>
            string.Equals(roleId, matched.RoleId, StringComparison.Ordinal)))
          .Where(property => property.StageOwnership.Any(stage =>
            string.Equals(stage, "STAGE02", StringComparison.Ordinal)))
          .Select(property => property.PropertyId),
        StringComparer.Ordinal);
      var normalized = new List<Stage02WriteOperation>();
      foreach (Stage02WriteOperation operation in matched.Operations)
      {
        if (operation == null)
          Throw(Stage02Codes.InvalidPreviewInput, "预览包含空写入操作。");
        HbrRuleProperty property;
        if (!_database.PropertiesById.TryGetValue(
          operation.PropertyId,
          out property))
        {
          Throw(
            Stage02Codes.UnknownProperty,
            "预览引用了未知规则属性：" + operation.PropertyId + "。");
        }
        if (!propertyIds.Add(operation.PropertyId))
        {
          Throw(
            Stage02Codes.DuplicatePropertyOperation,
            "元素 " + element.UniqueId + " 对属性 "
            + operation.PropertyId + " 存在重复写入操作。");
        }
        if (!property.CarrierRoleIds.Any(roleId => string.Equals(
          roleId,
          matched.RoleId,
          StringComparison.Ordinal)))
        {
          Throw(
            Stage02Codes.PropertyCarrierMismatch,
            "属性 " + operation.PropertyId + " 不允许写入载体角色 "
            + matched.RoleId + "。");
        }
        if (!property.StageOwnership.Any(stage => string.Equals(
          stage,
          "STAGE02",
          StringComparison.Ordinal)))
        {
          Throw(
            Stage02Codes.PropertyNotOwnedByStage02,
            "属性 " + operation.PropertyId + " 不属于 STAGE02。");
        }
        string conditionId = property.Requirement.ConditionId
          ?? string.Empty;
        if (property.Revit.ParameterGuid != operation.ParameterGuid
          || !string.Equals(
            property.Revit.ParameterName,
            operation.ParameterName,
            StringComparison.Ordinal))
        {
          Throw(
            Stage02Codes.PropertyIdentityMismatch,
            "属性 " + operation.PropertyId
            + " 的参数 GUID 或名称与规则包不一致。");
        }
        Require(operation.ValueSource, "ValueSource");
        Require(operation.SuggestionConfidence, "SuggestionConfidence");
        Require(operation.BindingAction, "BindingAction");
        Require(operation.ValueAction, "ValueAction");
        Require(operation.Applicability, "Applicability");
        if (operation.Blockers.Any(blocker => blocker == null))
          Throw(Stage02Codes.InvalidPreviewInput, "写入操作包含空 blocker。");
        Stage02WriteOperation requirementNormalized =
          NormalizeRequirementState(request, property, operation);
        string targetUniqueId = requirementNormalized.TargetUniqueId;
        if (string.IsNullOrWhiteSpace(targetUniqueId))
        {
          if (!string.Equals(
            property.Revit.BindingScope,
            "INSTANCE",
            StringComparison.Ordinal))
          {
            Throw(
              Stage02Codes.TargetIdentityMismatch,
              "属性 " + operation.PropertyId
              + " 的非实例写入缺少明确 TargetUniqueId。");
          }
          targetUniqueId = element.UniqueId;
        }
        if (string.Equals(
            property.Revit.BindingScope,
            "INSTANCE",
            StringComparison.Ordinal)
          && !string.Equals(
            targetUniqueId,
            element.UniqueId,
            StringComparison.Ordinal))
        {
          Throw(
            Stage02Codes.TargetIdentityMismatch,
            "属性 " + operation.PropertyId
            + " 的实例写入 TargetUniqueId 与所选元素不一致。");
        }
        normalized.Add(requirementNormalized.WithRuleMetadata(
          requirementNormalized.ObservedState.With(
            targetUniqueId: targetUniqueId),
          property.Revit.BindingScope,
          property.Revit.StorageType,
          property.Revit.ParameterType,
          property.Requirement.Level,
          conditionId));
      }
      if (!propertyIds.SetEquals(expectedPropertyIds))
      {
        string missing = string.Join(", ", expectedPropertyIds
          .Except(propertyIds, StringComparer.Ordinal)
          .OrderBy(x => x, StringComparer.Ordinal));
        string extra = string.Join(", ", propertyIds
          .Except(expectedPropertyIds, StringComparer.Ordinal)
          .OrderBy(x => x, StringComparer.Ordinal));
        Throw(
          Stage02Codes.PropertySetMismatch,
          "元素 " + element.UniqueId + " 的 STAGE02 属性闭包不完整。"
          + " 缺少：[" + missing + "]；额外：[" + extra + "]。");
      }
      return normalized
        .OrderBy(x => x.PropertyId, StringComparer.Ordinal)
        .ToArray();
    }

    private Stage02WriteOperation NormalizeRequirementState(
      Stage02PreviewRequest request,
      HbrRuleProperty property,
      Stage02WriteOperation operation)
    {
      Stage02RequirementDecision decision =
        Stage02RequirementDecisionPolicy.Resolve(
          property.PropertyId,
          property.Requirement.Level,
          property.Requirement.ConditionId,
          _database.Package.Conditions.Select(condition =>
            condition.ConditionId),
          request.ProjectConditions);
      if (!decision.Success)
        Throw(decision.ErrorCode, decision.Message);

      RequireOperationState(
        operation,
        decision.Applicability,
        string.IsNullOrWhiteSpace(decision.ValueActionOverride)
          ? null
          : decision.ValueActionOverride,
        property.PropertyId);
      bool expectsMissingBlocker = decision.Blockers.Any(blocker =>
        string.Equals(
          blocker.Code,
          Stage02Codes.ConditionStateMissing,
          StringComparison.Ordinal));
      bool hasMissingBlocker = operation.Blockers.Any(blocker =>
        string.Equals(
          blocker.Code,
          Stage02Codes.ConditionStateMissing,
          StringComparison.Ordinal));
      if (hasMissingBlocker && !expectsMissingBlocker)
      {
        Throw(
          Stage02Codes.ConditionStateMismatch,
          "属性 " + property.PropertyId
          + " 的项目条件已有明确状态，却仍携带 CONDITION_STATE_MISSING blocker。");
      }
      IEnumerable<Stage02Blocker> blockers = operation.Blockers
        .Where(blocker => !string.Equals(
          blocker.Code,
          Stage02Codes.ConditionStateMissing,
          StringComparison.Ordinal))
        .Concat(decision.Blockers);
      return operation.WithObservedState(
        operation.ObservedState,
        valueAction: string.IsNullOrWhiteSpace(decision.ValueActionOverride)
          ? operation.ValueAction
          : decision.ValueActionOverride,
        applicability: decision.Applicability,
        blockers: blockers);
    }

    private static void RequireOperationState(
      Stage02WriteOperation operation,
      string expectedApplicability,
      string expectedValueAction,
      string propertyId)
    {
      if (!string.Equals(
          operation.Applicability,
          expectedApplicability,
          StringComparison.Ordinal)
        || (expectedValueAction != null
          && !string.Equals(
            operation.ValueAction,
            expectedValueAction,
            StringComparison.Ordinal)))
      {
        Throw(
          Stage02Codes.ConditionStateMismatch,
          "属性 " + propertyId + " 的 Applicability/ValueAction 与规则 requirement"
          + " 和当前 ProjectConditions 推导结果不一致；期望 "
          + expectedApplicability + "/"
          + (expectedValueAction ?? "<CALLER_ACTION>") + "，实际 "
          + operation.Applicability + "/" + operation.ValueAction + "。");
      }
    }

    private void ValidateMatchEvidence(
      string activeProfileId,
      Stage02MatchedElement matched)
    {
      if (!Stage02MatchEngine.HasValidMatchProof(
        _database,
        activeProfileId,
        matched))
      {
        Throw(
          Stage02Codes.InvalidMatchEvidence,
          "元素 " + matched.Element.UniqueId
          + " 缺少由 MatchEngine 生成且与规则、profile、元素快照一致的匹配凭据。");
      }
      var engine = new Stage02MatchEngine(_database, activeProfileId);
      Stage02MatchResult verified;
      switch (matched.MatchSource)
      {
        case Stage02MatchSources.RoleHint:
          verified = engine.Match(
            matched.Element,
            roleHint: matched.RoleId,
            savedRoleId: null);
          break;
        case Stage02MatchSources.SavedRole:
          verified = engine.Match(
            matched.Element,
            roleHint: null,
            savedRoleId: matched.RoleId);
          break;
        case Stage02MatchSources.Category:
        case Stage02MatchSources.NameAlias:
        case Stage02MatchSources.UniqueCandidate:
          verified = engine.Match(matched.Element);
          break;
        default:
          Throw(
            Stage02Codes.InvalidMatchEvidence,
            "元素 " + matched.Element.UniqueId
            + " 使用了未知角色匹配来源：" + matched.MatchSource + "。");
          return;
      }

      if (!verified.Success)
      {
        Stage02Blocker blocker = verified.Blockers.FirstOrDefault();
        Throw(
          blocker == null || string.IsNullOrWhiteSpace(blocker.Code)
            ? Stage02Codes.InvalidMatchEvidence
            : blocker.Code,
          "元素 " + matched.Element.UniqueId + " 的角色匹配证据重验失败："
          + (blocker == null ? "没有可验证证据。" : blocker.Message));
      }
      if (!string.Equals(
          verified.RoleId,
          matched.RoleId,
          StringComparison.Ordinal)
        || !string.Equals(
          verified.MatchSource,
          matched.MatchSource,
          StringComparison.Ordinal))
      {
        Throw(
          Stage02Codes.InvalidMatchEvidence,
          "元素 " + matched.Element.UniqueId
          + " 的 roleId 或 MatchSource 与重新匹配结果不一致。");
      }
    }

    private static void Require(string value, string fieldName)
    {
      if (string.IsNullOrWhiteSpace(value))
      {
        Throw(
          Stage02Codes.InvalidPreviewInput,
          "预览字段 " + fieldName + " 不能为空。");
      }
    }

    private static void RequireElement(string value, string fieldName)
    {
      if (string.IsNullOrWhiteSpace(value))
      {
        Throw(
          Stage02Codes.InvalidElementReference,
          "元素引用字段 " + fieldName + " 不能为空。");
      }
    }

    private static void Throw(string code, string message)
    {
      throw new Stage02ContractException(code, message);
    }
  }

  internal static class Stage02Canonicalizer
  {
    internal static string BuildPreview(
      Stage02PreviewRequest request,
      IEnumerable<Stage02MatchedElement> elements)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      var builder = new StringBuilder(4096);
      builder.Append('{');
      AppendProperty(builder, "schema", "stage02-preview/v1", true);
      AppendProperty(builder, "fileGuid", request.FileGuid, false);
      AppendProperty(
        builder,
        "documentFingerprint",
        request.DocumentFingerprint,
        false);
      AppendProperty(builder, "documentTitle", request.DocumentTitle, false);
      AppendProperty(builder, "fileContextHash", request.FileContextHash, false);
      AppendProperty(builder, "activeProfileId", request.ActiveProfileId, false);
      AppendProperty(builder, "rulePackageId", request.RulePackageId, false);
      AppendProperty(
        builder,
        "rulePackageVersion",
        request.RulePackageVersion,
        false);
      AppendProperty(
        builder,
        "rulePackageSha256",
        request.RulePackageSha256,
        false);
      AppendProperty(builder, "nonce", request.Nonce, false);
      AppendProperty(builder, "selectionMode", request.SelectionMode, false);
      builder.Append(",\"elements\":[");
      bool firstElement = true;
      foreach (Stage02MatchedElement matched in
        elements ?? Array.Empty<Stage02MatchedElement>())
      {
        if (matched == null || matched.Element == null)
          throw new InvalidOperationException("Stage02 预览元素快照无效。");
        if (!firstElement) builder.Append(',');
        builder.Append('{');
        AppendProperty(
          builder,
          "documentFingerprint",
          matched.Element.DocumentFingerprint,
          true);
        AppendProperty(
          builder,
          "documentTitle",
          matched.Element.DocumentTitle,
          false);
        AppendProperty(builder, "uniqueId", matched.Element.UniqueId, false);
        AppendProperty(builder, "category", matched.Element.Category, false);
        AppendProperty(
          builder,
          "elementKind",
          matched.Element.ElementKind,
          false);
        AppendProperty(builder, "familyName", matched.Element.FamilyName, false);
        AppendProperty(builder, "typeName", matched.Element.TypeName, false);
        AppendProperty(builder, "elementName", matched.Element.ElementName, false);
        AppendProperty(builder, "roleId", matched.RoleId, false);
        AppendProperty(builder, "matchSource", matched.MatchSource, false);
        AppendProperty(
          builder,
          "stage01RecordIdentity",
          matched.Stage01RecordIdentity,
          false);
        builder.Append(",\"operations\":[");
        bool firstOperation = true;
        foreach (Stage02WriteOperation operation in matched.Operations)
        {
          if (!firstOperation) builder.Append(',');
          AppendOperation(builder, operation, null);
          firstOperation = false;
        }
        builder.Append("]}");
        firstElement = false;
      }
      builder.Append("]}");
      return builder.ToString();
    }

    internal static string BuildPreview(Stage02Preview preview)
    {
      if (preview == null) throw new ArgumentNullException(nameof(preview));
      var request = new Stage02PreviewRequest(
        preview.FileGuid,
        preview.DocumentFingerprint,
        preview.DocumentTitle,
        preview.FileContextHash,
        preview.ActiveProfileId,
        preview.RulePackageId,
        preview.RulePackageVersion,
        preview.RulePackageSha256,
        preview.Nonce,
        preview.ProjectConditions,
        preview.SelectionMode,
        preview.Elements);
      return BuildPreview(request, preview.Elements);
    }

    internal static string BuildOperation(
      Stage02WriteOperation operation,
      string observedOldValueHash = null)
    {
      var builder = new StringBuilder(1024);
      AppendOperation(builder, operation, observedOldValueHash);
      return builder.ToString();
    }

    private static void AppendOperation(
      StringBuilder builder,
      Stage02WriteOperation operation,
      string observedOldValueHash)
    {
      if (operation == null)
        throw new InvalidOperationException("Stage02 写入操作快照无效。");
      builder.Append('{');
      AppendProperty(builder, "targetUniqueId", operation.TargetUniqueId, true);
      AppendProperty(builder, "propertyId", operation.PropertyId, false);
      AppendProperty(
        builder,
        "parameterGuid",
        operation.ParameterGuid.ToString("D").ToLowerInvariant(),
        false);
      AppendProperty(builder, "parameterName", operation.ParameterName, false);
      AppendBooleanProperty(
        builder,
        "bindingExists",
        operation.BindingExists,
        false);
      AppendBooleanProperty(
        builder,
        "parameterExists",
        operation.ParameterExists,
        false);
      AppendProperty(
        builder,
        "parameterMatchSource",
        operation.ParameterMatchSource,
        false);
      AppendProperty(
        builder,
        "observedBindingScope",
        operation.ObservedBindingScope,
        false);
      AppendProperty(
        builder,
        "observedStorageType",
        operation.ObservedStorageType,
        false);
      AppendStringArrayProperty(
        builder,
        "boundCategories",
        operation.BoundCategories,
        false);
      AppendBooleanProperty(builder, "isReadOnly", operation.IsReadOnly, false);
      AppendProperty(builder, "oldValueKind", operation.OldValueKind, false);
      AppendProperty(builder, "oldValue", operation.OldValue, false);
      AppendProperty(
        builder,
        "oldDisplayValue",
        operation.OldDisplayValue,
        false);
      AppendProperty(
        builder,
        "sourceParameterIdentity",
        operation.SourceParameterIdentity,
        false);
      AppendProperty(builder, "sourceValue", operation.SourceValue, false);
      AppendProperty(
        builder,
        "oldValueHash",
        observedOldValueHash ?? operation.OldValueHash,
        false);
      AppendProperty(
        builder,
        "suggestedValue",
        operation.SuggestedValue,
        false);
      AppendProperty(builder, "valueSource", operation.ValueSource, false);
      AppendProperty(
        builder,
        "suggestionConfidence",
        operation.SuggestionConfidence,
        false);
      AppendProperty(builder, "bindingScope", operation.BindingScope, false);
      AppendProperty(builder, "storageType", operation.StorageType, false);
      AppendProperty(builder, "parameterType", operation.ParameterType, false);
      AppendProperty(
        builder,
        "requirementLevel",
        operation.RequirementLevel,
        false);
      AppendProperty(builder, "conditionId", operation.ConditionId, false);
      AppendProperty(
        builder,
        "applicability",
        operation.Applicability,
        false);
      AppendProperty(
        builder,
        "bindingAction",
        operation.BindingAction,
        false);
      AppendProperty(builder, "valueAction", operation.ValueAction, false);
      AppendProperty(builder, "action", operation.Action, false);
      builder.Append(",\"blockers\":[");
      bool firstBlocker = true;
      foreach (Stage02Blocker blocker in operation.Blockers)
      {
        if (blocker == null)
          throw new InvalidOperationException("Stage02 blocker 快照无效。");
        if (!firstBlocker) builder.Append(',');
        builder.Append('{');
        AppendProperty(builder, "code", blocker.Code, true);
        AppendProperty(builder, "message", blocker.Message, false);
        builder.Append('}');
        firstBlocker = false;
      }
      builder.Append("]}");
    }

    private static void AppendProperty(
      StringBuilder builder,
      string name,
      string value,
      bool first)
    {
      if (!first) builder.Append(',');
      AppendEscaped(builder, name);
      builder.Append(':');
      AppendEscaped(builder, value ?? string.Empty);
    }

    private static void AppendBooleanProperty(
      StringBuilder builder,
      string name,
      bool value,
      bool first)
    {
      if (!first) builder.Append(',');
      AppendEscaped(builder, name);
      builder.Append(':').Append(value ? "true" : "false");
    }

    private static void AppendStringArrayProperty(
      StringBuilder builder,
      string name,
      IEnumerable<string> values,
      bool first)
    {
      if (!first) builder.Append(',');
      AppendEscaped(builder, name);
      builder.Append(":[");
      bool firstValue = true;
      foreach (string value in values ?? Array.Empty<string>())
      {
        if (!firstValue) builder.Append(',');
        AppendEscaped(builder, value ?? string.Empty);
        firstValue = false;
      }
      builder.Append(']');
    }

    private static void AppendEscaped(StringBuilder builder, string value)
    {
      builder.Append('"');
      foreach (char character in value ?? string.Empty)
      {
        switch (character)
        {
          case '"': builder.Append("\\\""); break;
          case '\\': builder.Append("\\\\"); break;
          case '\b': builder.Append("\\b"); break;
          case '\f': builder.Append("\\f"); break;
          case '\n': builder.Append("\\n"); break;
          case '\r': builder.Append("\\r"); break;
          case '\t': builder.Append("\\t"); break;
          default:
            if (character < 32)
              builder.Append("\\u").Append(
                ((int)character).ToString("x4", CultureInfo.InvariantCulture));
            else
              builder.Append(character);
            break;
        }
      }
      builder.Append('"');
    }
  }
}
