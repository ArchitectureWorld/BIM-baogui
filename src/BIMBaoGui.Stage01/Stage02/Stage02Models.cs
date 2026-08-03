using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BIMBaoGui.Stage01.Context;

namespace BIMBaoGui.Stage01.Stage02
{
  public static class Stage02Codes
  {
    public const string AmbiguousCarrier = "AMBIGUOUS_CARRIER";
    public const string CarrierNotFound = "CARRIER_NOT_FOUND";
    public const string CarrierCategoryMismatch =
      "CARRIER_CATEGORY_MISMATCH";
    public const string CarrierNotActive = "CARRIER_NOT_ACTIVE";
    public const string CarrierElementKindMismatch =
      "CARRIER_ELEMENT_KIND_MISMATCH";
    public const string UnknownCarrierRole = "UNKNOWN_CARRIER_ROLE";
    public const string UnknownModelProfile = "UNKNOWN_MODEL_PROFILE";
    public const string InvalidElementReference =
      "INVALID_ELEMENT_REFERENCE";
    public const string InvalidMatchEvidence = "INVALID_MATCH_EVIDENCE";
    public const string InvalidPreviewInput = "INVALID_PREVIEW_INPUT";
    public const string UnknownProperty = "UNKNOWN_PROPERTY";
    public const string PropertyIdentityMismatch =
      "PROPERTY_IDENTITY_MISMATCH";
    public const string PropertyCarrierMismatch =
      "PROPERTY_CARRIER_MISMATCH";
    public const string PropertyNotOwnedByStage02 =
      "PROPERTY_NOT_OWNED_BY_STAGE02";
    public const string TargetIdentityMismatch =
      "TARGET_IDENTITY_MISMATCH";
    public const string UnknownCondition = "UNKNOWN_CONDITION";
    public const string InvalidRequirementContract =
      "INVALID_REQUIREMENT_CONTRACT";
    public const string ConditionStateMismatch =
      "CONDITION_STATE_MISMATCH";
    public const string ConditionStateMissing =
      "CONDITION_STATE_MISSING";
    public const string RulePackageIdentityMismatch =
      "RULE_PACKAGE_IDENTITY_MISMATCH";
    public const string DuplicateElementIdentity =
      "DUPLICATE_ELEMENT_IDENTITY";
    public const string DuplicatePropertyOperation =
      "DUPLICATE_PROPERTY_OPERATION";
    public const string PropertySetMismatch = "PROPERTY_SET_MISMATCH";
    public const string PreviewAlreadyConsumed =
      "PREVIEW_ALREADY_CONSUMED";
    public const string DocumentFingerprintChanged =
      "DOCUMENT_FINGERPRINT_CHANGED";
    public const string FileContextChanged = "FILE_CONTEXT_CHANGED";
    public const string FileGuidChanged = "FILE_GUID_CHANGED";
    public const string ActiveProfileChanged = "ACTIVE_PROFILE_CHANGED";
    public const string RulePackageIdentityChanged =
      "RULE_PACKAGE_IDENTITY_CHANGED";
    public const string ElementSetChanged = "ELEMENT_SET_CHANGED";
    public const string ElementSnapshotChanged =
      "ELEMENT_SNAPSHOT_CHANGED";
    public const string RoleSnapshotChanged = "ROLE_SNAPSHOT_CHANGED";
    public const string OldValueChanged = "OLD_VALUE_CHANGED";
    public const string PreviewHashChanged = "PREVIEW_HASH_CHANGED";
    public const string NonceChanged = "NONCE_CHANGED";
    public const string InvalidConfirmationSnapshot =
      "INVALID_CONFIRMATION_SNAPSHOT";
    public const string InvalidFileContext = "INVALID_FILE_CONTEXT";
    public const string PreviewHasBlockers = "PREVIEW_HAS_BLOCKERS";
    public const string AmbiguousStage01Organization =
      "AMBIGUOUS_STAGE01_ORGANIZATION";
    public const string InvalidStage01OrganizationIdentity =
      "INVALID_STAGE01_ORGANIZATION_IDENTITY";
    public const string DocumentReadOnly = "DOCUMENT_READ_ONLY";
    public const string InvalidSelectionEvidence =
      "INVALID_SELECTION_EVIDENCE";
  }

  public static class Stage02MatchSources
  {
    public const string RoleHint = "ROLE_HINT";
    public const string SavedRole = "SAVED_ROLE";
    public const string Category = "CATEGORY";
    public const string NameAlias = "NAME_ALIAS";
    public const string UniqueCandidate = "UNIQUE_CANDIDATE";
  }

  public static class Stage02SelectionModes
  {
    public const string Legacy = "LEGACY";
    public const string CurrentSelection = "CURRENT_SELECTION";
    public const string ExplicitIds = "EXPLICIT_IDS";
    public const string ExplicitPick = "EXPLICIT_PICK";
    public const string ProjectInformation = "PROJECT_INFORMATION";
  }

  public static class Stage02HandoffStates
  {
    public const string Rejected = "REJECTED";
    public const string ConsumedForExecution = "CONSUMED_FOR_EXECUTION";
  }

  internal static class Stage02FileContextPolicy
  {
    internal static bool IsVerified(HBRFileContext context)
    {
      if (context == null
        || !context.IsReady
        || !string.Equals(
          context.SchemaVersion,
          HBRContextVersions.FileContextSchema,
          StringComparison.Ordinal))
        return false;
      try
      {
        return string.Equals(
          context.FileContextHash,
          HBRFileContextCanonicalizer.ComputeHash(context),
          StringComparison.OrdinalIgnoreCase);
      }
      catch (Exception)
      {
        return false;
      }
    }
  }

  public sealed class Stage02ContractException : InvalidOperationException
  {
    public Stage02ContractException(string code, string message)
      : base(message)
    {
      Code = code ?? string.Empty;
    }

    public string Code { get; }
  }

  public sealed class Stage02Blocker
  {
    public Stage02Blocker(string code, string message)
    {
      Code = code ?? string.Empty;
      Message = message ?? string.Empty;
    }

    public string Code { get; }
    public string Message { get; }
  }

  public sealed class Stage02ElementReference
  {
    internal Stage02ElementReference(
      string documentFingerprint,
      int elementId,
      string uniqueId,
      string category,
      string familyName,
      string typeName,
      string elementName)
      : this(
        documentFingerprint,
        string.Empty,
        elementId,
        uniqueId,
        category,
        string.Empty,
        familyName,
        typeName,
        elementName)
    {
    }

    public Stage02ElementReference(
      string documentFingerprint,
      string documentTitle,
      int elementId,
      string uniqueId,
      string category,
      string elementKind,
      string familyName,
      string typeName,
      string elementName)
    {
      DocumentFingerprint = documentFingerprint ?? string.Empty;
      DocumentTitle = documentTitle ?? string.Empty;
      ElementId = elementId;
      UniqueId = uniqueId ?? string.Empty;
      Category = category ?? string.Empty;
      ElementKind = elementKind ?? string.Empty;
      FamilyName = familyName ?? string.Empty;
      TypeName = typeName ?? string.Empty;
      ElementName = elementName ?? string.Empty;
    }

    public string DocumentFingerprint { get; }
    public string DocumentTitle { get; }
    public int ElementId { get; }
    public string UniqueId { get; }
    public string Category { get; }
    public string ElementKind { get; }
    public string FamilyName { get; }
    public string TypeName { get; }
    public string ElementName { get; }
  }

  public sealed class Stage02ObservedParameterState
  {
    public Stage02ObservedParameterState(
      string targetUniqueId,
      bool bindingExists,
      bool parameterExists,
      string parameterMatchSource,
      string observedBindingScope,
      string observedStorageType,
      IEnumerable<string> boundCategories,
      bool isReadOnly,
      string rawValueKind,
      string rawValue,
      string displayValue,
      string sourceParameterIdentity,
      string sourceValue)
    {
      TargetUniqueId = targetUniqueId ?? string.Empty;
      BindingExists = bindingExists;
      ParameterExists = parameterExists;
      ParameterMatchSource = parameterMatchSource ?? string.Empty;
      ObservedBindingScope = observedBindingScope ?? string.Empty;
      ObservedStorageType = observedStorageType ?? string.Empty;
      BoundCategories = Stage02Collections.Freeze(
        (boundCategories ?? Array.Empty<string>())
          .Where(x => !string.IsNullOrWhiteSpace(x))
          .Distinct(StringComparer.Ordinal)
          .OrderBy(x => x, StringComparer.Ordinal));
      IsReadOnly = isReadOnly;
      RawValueKind = rawValueKind ?? string.Empty;
      RawValue = rawValue ?? string.Empty;
      DisplayValue = displayValue ?? string.Empty;
      SourceParameterIdentity = sourceParameterIdentity ?? string.Empty;
      SourceValue = sourceValue ?? string.Empty;
    }

    public string TargetUniqueId { get; }
    public bool BindingExists { get; }
    public bool ParameterExists { get; }
    public string ParameterMatchSource { get; }
    public string ObservedBindingScope { get; }
    public string ObservedStorageType { get; }
    public IReadOnlyList<string> BoundCategories { get; }
    public bool IsReadOnly { get; }
    public string RawValueKind { get; }
    public string RawValue { get; }
    public string DisplayValue { get; }
    public string SourceParameterIdentity { get; }
    public string SourceValue { get; }

    internal Stage02ObservedParameterState With(
      string targetUniqueId = null,
      bool? bindingExists = null,
      string rawValue = null)
    {
      return new Stage02ObservedParameterState(
        targetUniqueId ?? TargetUniqueId,
        bindingExists ?? BindingExists,
        ParameterExists,
        ParameterMatchSource,
        ObservedBindingScope,
        ObservedStorageType,
        BoundCategories,
        IsReadOnly,
        RawValueKind,
        rawValue ?? RawValue,
        rawValue ?? DisplayValue,
        SourceParameterIdentity,
        rawValue ?? SourceValue);
    }
  }

  public sealed class Stage02WriteOperation
  {
    internal Stage02WriteOperation(
      string propertyId,
      Guid parameterGuid,
      string parameterName,
      string oldValue,
      string suggestedValue,
      string valueSource,
      string action)
      : this(
        propertyId,
        parameterGuid,
        parameterName,
        LegacyState(oldValue),
        suggestedValue,
        valueSource,
        "UNSPECIFIED",
        "NO_CHANGE",
        action,
        "APPLICABLE",
        Array.Empty<Stage02Blocker>(),
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty)
    {
    }

    internal Stage02WriteOperation(
      string propertyId,
      Guid parameterGuid,
      string parameterName,
      string oldValue,
      string suggestedValue,
      string valueSource,
      string bindingAction,
      string valueAction,
      string applicability,
      IEnumerable<Stage02Blocker> blockers)
      : this(
        propertyId,
        parameterGuid,
        parameterName,
        LegacyState(oldValue),
        suggestedValue,
        valueSource,
        "UNSPECIFIED",
        bindingAction,
        valueAction,
        applicability,
        blockers,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty)
    {
    }

    public Stage02WriteOperation(
      string propertyId,
      Guid parameterGuid,
      string parameterName,
      Stage02ObservedParameterState observedState,
      string suggestedValue,
      string valueSource,
      string suggestionConfidence,
      string bindingAction,
      string valueAction)
      : this(
        propertyId,
        parameterGuid,
        parameterName,
        observedState,
        suggestedValue,
        valueSource,
        suggestionConfidence,
        bindingAction,
        valueAction,
        "APPLICABLE",
        Array.Empty<Stage02Blocker>(),
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty)
    {
    }

    public Stage02WriteOperation(
      string propertyId,
      Guid parameterGuid,
      string parameterName,
      Stage02ObservedParameterState observedState,
      string suggestedValue,
      string valueSource,
      string suggestionConfidence,
      string bindingAction,
      string valueAction,
      string applicability,
      IEnumerable<Stage02Blocker> blockers)
      : this(
        propertyId,
        parameterGuid,
        parameterName,
        observedState,
        suggestedValue,
        valueSource,
        suggestionConfidence,
        bindingAction,
        valueAction,
        applicability,
        blockers,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty)
    {
    }

    private Stage02WriteOperation(
      string propertyId,
      Guid parameterGuid,
      string parameterName,
      Stage02ObservedParameterState observedState,
      string suggestedValue,
      string valueSource,
      string suggestionConfidence,
      string bindingAction,
      string valueAction,
      string applicability,
      IEnumerable<Stage02Blocker> blockers,
      string bindingScope,
      string storageType,
      string parameterType,
      string requirementLevel,
      string conditionId)
    {
      PropertyId = propertyId ?? string.Empty;
      ParameterGuid = parameterGuid;
      ParameterName = parameterName ?? string.Empty;
      ObservedState = observedState ?? throw new ArgumentNullException(
        nameof(observedState));
      SuggestedValue = suggestedValue ?? string.Empty;
      ValueSource = valueSource ?? string.Empty;
      SuggestionConfidence = suggestionConfidence ?? string.Empty;
      BindingAction = bindingAction ?? string.Empty;
      ValueAction = valueAction ?? string.Empty;
      BindingScope = bindingScope ?? string.Empty;
      StorageType = storageType ?? string.Empty;
      ParameterType = parameterType ?? string.Empty;
      RequirementLevel = requirementLevel ?? string.Empty;
      ConditionId = conditionId ?? string.Empty;
      Applicability = applicability ?? string.Empty;
      Blockers = Stage02Collections.Freeze(
        (blockers ?? Array.Empty<Stage02Blocker>())
          .OrderBy(x => x == null ? string.Empty : x.Code,
            StringComparer.Ordinal)
          .ThenBy(x => x == null ? string.Empty : x.Message,
            StringComparer.Ordinal));
      OldValueHash = Stage02Hash.Sha256(OldValue);
    }

    public Stage02ObservedParameterState ObservedState { get; }
    public string TargetUniqueId => ObservedState.TargetUniqueId;
    public string PropertyId { get; }
    public Guid ParameterGuid { get; }
    public string ParameterName { get; }
    public bool BindingExists => ObservedState.BindingExists;
    public bool ParameterExists => ObservedState.ParameterExists;
    public string ParameterMatchSource => ObservedState.ParameterMatchSource;
    public string ObservedBindingScope => ObservedState.ObservedBindingScope;
    public string ObservedStorageType => ObservedState.ObservedStorageType;
    public IReadOnlyList<string> BoundCategories =>
      ObservedState.BoundCategories;
    public bool IsReadOnly => ObservedState.IsReadOnly;
    public string OldValueKind => ObservedState.RawValueKind;
    public string OldValue => ObservedState.RawValue;
    public string OldDisplayValue => ObservedState.DisplayValue;
    public string SourceParameterIdentity =>
      ObservedState.SourceParameterIdentity;
    public string SourceValue => ObservedState.SourceValue;
    public string OldValueHash { get; }
    public string SuggestedValue { get; }
    public string ValueSource { get; }
    public string SuggestionConfidence { get; }
    public string BindingAction { get; }
    public string ValueAction { get; }
    public string Action => ValueAction;
    public string BindingScope { get; }
    public string StorageType { get; }
    public string ParameterType { get; }
    public string RequirementLevel { get; }
    public string ConditionId { get; }
    public string Applicability { get; }
    public IReadOnlyList<Stage02Blocker> Blockers { get; }

    internal Stage02WriteOperation WithRuleMetadata(
      Stage02ObservedParameterState observedState,
      string bindingScope,
      string storageType,
      string parameterType,
      string requirementLevel,
      string conditionId)
    {
      return new Stage02WriteOperation(
        PropertyId,
        ParameterGuid,
        ParameterName,
        observedState,
        SuggestedValue,
        ValueSource,
        SuggestionConfidence,
        BindingAction,
        ValueAction,
        Applicability,
        Blockers,
        bindingScope,
        storageType,
        parameterType,
        requirementLevel,
        conditionId);
    }

    internal Stage02WriteOperation WithObservedState(
      Stage02ObservedParameterState observedState,
      string suggestedValue = null,
      string valueSource = null,
      string suggestionConfidence = null,
      string bindingAction = null,
      string valueAction = null,
      string applicability = null,
      IEnumerable<Stage02Blocker> blockers = null)
    {
      return new Stage02WriteOperation(
        PropertyId,
        ParameterGuid,
        ParameterName,
        observedState,
        suggestedValue ?? SuggestedValue,
        valueSource ?? ValueSource,
        suggestionConfidence ?? SuggestionConfidence,
        bindingAction ?? BindingAction,
        valueAction ?? ValueAction,
        applicability ?? Applicability,
        blockers ?? Blockers,
        BindingScope,
        StorageType,
        ParameterType,
        RequirementLevel,
        ConditionId);
    }

    private static Stage02ObservedParameterState LegacyState(string oldValue)
    {
      return new Stage02ObservedParameterState(
        string.Empty,
        true,
        true,
        "GUID",
        string.Empty,
        string.Empty,
        Array.Empty<string>(),
        false,
        "STRING",
        oldValue,
        oldValue,
        string.Empty,
        oldValue);
    }
  }

  public sealed class Stage02MatchedElement
  {
    internal Stage02MatchedElement(
      Stage02ElementReference element,
      string roleId,
      IEnumerable<Stage02WriteOperation> operations)
      : this(
        element,
        roleId,
        string.Empty,
        null,
        string.Empty,
        operations)
    {
    }

    internal Stage02MatchedElement(
      Stage02ElementReference element,
      string roleId,
      string matchSource,
      IEnumerable<Stage02WriteOperation> operations)
      : this(
        element,
        roleId,
        matchSource,
        null,
        string.Empty,
        operations)
    {
    }

    public Stage02MatchedElement(
      Stage02ElementReference element,
      Stage02MatchResult matchResult,
      IEnumerable<Stage02WriteOperation> operations)
      : this(element, matchResult, string.Empty, operations)
    {
    }

    public Stage02MatchedElement(
      Stage02ElementReference element,
      Stage02MatchResult matchResult,
      string stage01RecordIdentity,
      IEnumerable<Stage02WriteOperation> operations)
    {
      if (matchResult == null
        || !matchResult.Success
        || matchResult.MatchProof == null)
      {
        throw new ArgumentException(
          "Stage02MatchedElement 必须使用 MatchEngine 返回的成功结果创建。",
          nameof(matchResult));
      }
      Element = element;
      RoleId = matchResult.RoleId;
      MatchSource = matchResult.MatchSource;
      MatchProof = matchResult.MatchProof;
      Stage01RecordIdentity = stage01RecordIdentity ?? string.Empty;
      Operations = Stage02Collections.Freeze(operations);
    }

    private Stage02MatchedElement(
      Stage02ElementReference element,
      string roleId,
      string matchSource,
      object matchProof,
      string stage01RecordIdentity,
      IEnumerable<Stage02WriteOperation> operations)
    {
      Element = element;
      RoleId = roleId ?? string.Empty;
      MatchSource = matchSource ?? string.Empty;
      MatchProof = matchProof;
      Stage01RecordIdentity = stage01RecordIdentity ?? string.Empty;
      Operations = Stage02Collections.Freeze(operations);
    }

    public Stage02ElementReference Element { get; }
    public string RoleId { get; }
    public string MatchSource { get; }
    public string Stage01RecordIdentity { get; }
    public IReadOnlyList<Stage02WriteOperation> Operations { get; }
    internal object MatchProof { get; }

    internal Stage02MatchedElement WithOperations(
      IEnumerable<Stage02WriteOperation> operations)
    {
      return new Stage02MatchedElement(
        Element,
        RoleId,
        MatchSource,
        MatchProof,
        Stage01RecordIdentity,
        operations);
    }
  }

  public sealed class Stage02MatchResult
  {
    private Stage02MatchResult(
      bool success,
      string roleId,
      string matchSource,
      object matchProof,
      IEnumerable<Stage02Blocker> blockers)
    {
      Success = success;
      RoleId = roleId ?? string.Empty;
      MatchSource = matchSource ?? string.Empty;
      MatchProof = matchProof;
      Blockers = Stage02Collections.Freeze(blockers);
    }

    public bool Success { get; }
    public string RoleId { get; }
    public string MatchSource { get; }
    public IReadOnlyList<Stage02Blocker> Blockers { get; }
    internal object MatchProof { get; }

    internal static Stage02MatchResult Matched(
      string roleId,
      string matchSource,
      object matchProof)
    {
      if (matchProof == null) throw new ArgumentNullException(nameof(matchProof));
      return new Stage02MatchResult(
        true,
        roleId,
        matchSource,
        matchProof,
        Array.Empty<Stage02Blocker>());
    }

    public static Stage02MatchResult Blocked(
      IEnumerable<Stage02Blocker> blockers)
    {
      return new Stage02MatchResult(
        false,
        string.Empty,
        string.Empty,
        null,
        blockers);
    }
  }

  public sealed class Stage02PreviewRequest
  {
    internal Stage02PreviewRequest(
      string fileGuid,
      string documentFingerprint,
      string fileContextHash,
      string activeProfileId,
      string rulePackageId,
      string rulePackageVersion,
      string rulePackageSha256,
      string nonce,
      IEnumerable<Stage02MatchedElement> elements)
      : this(
        fileGuid,
        documentFingerprint,
        string.Empty,
        fileContextHash,
        activeProfileId,
        rulePackageId,
        rulePackageVersion,
        rulePackageSha256,
        nonce,
        elements)
    {
    }

    internal Stage02PreviewRequest(
      string fileGuid,
      string documentFingerprint,
      string documentTitle,
      string fileContextHash,
      string activeProfileId,
      string rulePackageId,
      string rulePackageVersion,
      string rulePackageSha256,
      string nonce,
      IEnumerable<Stage02MatchedElement> elements)
      : this(
        fileGuid,
        documentFingerprint,
        documentTitle,
        fileContextHash,
        activeProfileId,
        rulePackageId,
        rulePackageVersion,
        rulePackageSha256,
        nonce,
        new Dictionary<string, bool>(StringComparer.Ordinal),
        elements)
    {
    }

    internal Stage02PreviewRequest(
      string fileGuid,
      string documentFingerprint,
      string documentTitle,
      string fileContextHash,
      string activeProfileId,
      string rulePackageId,
      string rulePackageVersion,
      string rulePackageSha256,
      string nonce,
      IEnumerable<KeyValuePair<string, bool>> projectConditions,
      IEnumerable<Stage02MatchedElement> elements)
    {
      FileGuid = fileGuid ?? string.Empty;
      DocumentFingerprint = documentFingerprint ?? string.Empty;
      DocumentTitle = documentTitle ?? string.Empty;
      FileContextHash = fileContextHash ?? string.Empty;
      ActiveProfileId = activeProfileId ?? string.Empty;
      RulePackageId = rulePackageId ?? string.Empty;
      RulePackageVersion = rulePackageVersion ?? string.Empty;
      RulePackageSha256 = rulePackageSha256 ?? string.Empty;
      Nonce = nonce ?? string.Empty;
      SelectionMode = Stage02SelectionModes.Legacy;
      ProjectConditions = Stage02Collections.FreezeDictionary(
        projectConditions);
      Elements = Stage02Collections.Freeze(elements);
    }

    internal Stage02PreviewRequest(
      string fileGuid,
      string documentFingerprint,
      string documentTitle,
      string fileContextHash,
      string activeProfileId,
      string rulePackageId,
      string rulePackageVersion,
      string rulePackageSha256,
      string nonce,
      IEnumerable<KeyValuePair<string, bool>> projectConditions,
      string selectionMode,
      IEnumerable<Stage02MatchedElement> elements)
      : this(
        fileGuid,
        documentFingerprint,
        documentTitle,
        fileContextHash,
        activeProfileId,
        rulePackageId,
        rulePackageVersion,
        rulePackageSha256,
        nonce,
        projectConditions,
        elements)
    {
      SelectionMode = NormalizeSelectionMode(selectionMode);
    }

    public Stage02PreviewRequest(
      HBRFileContext context,
      string nonce,
      IEnumerable<Stage02MatchedElement> elements)
      : this(
        RequireContext(context).FileGuid,
        context.RevitDocumentFingerprint,
        context.RevitDocumentTitle,
        context.FileContextHash,
        context.ModelFileType,
        context.RulePackageId,
        context.RulePackageVersion,
        context.RulePackageSha256,
        nonce,
        context.ProjectConditions,
        elements)
    {
    }

    public Stage02PreviewRequest(
      HBRFileContext context,
      string nonce,
      string selectionMode,
      IEnumerable<Stage02MatchedElement> elements)
      : this(context, nonce, elements)
    {
      SelectionMode = NormalizeSelectionMode(selectionMode);
    }

    public string FileGuid { get; }
    public string DocumentFingerprint { get; }
    public string DocumentTitle { get; }
    public string FileContextHash { get; }
    public string ActiveProfileId { get; }
    public string RulePackageId { get; }
    public string RulePackageVersion { get; }
    public string RulePackageSha256 { get; }
    public string Nonce { get; }
    public string SelectionMode { get; }
    public IReadOnlyDictionary<string, bool> ProjectConditions { get; }
    public IReadOnlyList<Stage02MatchedElement> Elements { get; }

    private static HBRFileContext RequireContext(HBRFileContext context)
    {
      if (!Stage02FileContextPolicy.IsVerified(context))
      {
        throw new Stage02ContractException(
          Stage02Codes.InvalidFileContext,
          "HBRFileContext 无效、哈希被篡改、schema 不兼容或尚未通过 Stage01 初始化校验。");
      }
      return context;
    }

    private static string NormalizeSelectionMode(string selectionMode)
    {
      string value = (selectionMode ?? string.Empty).Trim();
      return value.Length == 0 ? Stage02SelectionModes.Legacy : value;
    }
  }

  public sealed class Stage02Preview
  {
    internal Stage02Preview(
      Stage02PreviewRequest request,
      IEnumerable<Stage02MatchedElement> elements,
      string canonicalPayload,
      string previewHash)
    {
      FileGuid = request.FileGuid;
      DocumentFingerprint = request.DocumentFingerprint;
      DocumentTitle = request.DocumentTitle;
      FileContextHash = request.FileContextHash;
      ActiveProfileId = request.ActiveProfileId;
      RulePackageId = request.RulePackageId;
      RulePackageVersion = request.RulePackageVersion;
      RulePackageSha256 = request.RulePackageSha256;
      Nonce = request.Nonce;
      SelectionMode = request.SelectionMode;
      ProjectConditions = Stage02Collections.FreezeDictionary(
        request.ProjectConditions);
      Elements = Stage02Collections.Freeze(elements);
      CanonicalPayload = canonicalPayload ?? string.Empty;
      PreviewHash = previewHash ?? string.Empty;
    }

    public string FileGuid { get; }
    public string DocumentFingerprint { get; }
    public string DocumentTitle { get; }
    public string FileContextHash { get; }
    public string ActiveProfileId { get; }
    public string RulePackageId { get; }
    public string RulePackageVersion { get; }
    public string RulePackageSha256 { get; }
    public string Nonce { get; }
    public string SelectionMode { get; }
    public IReadOnlyDictionary<string, bool> ProjectConditions { get; }
    public IReadOnlyList<Stage02MatchedElement> Elements { get; }
    public string CanonicalPayload { get; }
    public string PreviewHash { get; }
  }

  public sealed class Stage02CurrentPropertySnapshot
  {
    internal Stage02CurrentPropertySnapshot(string propertyId, string oldValue)
      : this(
        new Stage02WriteOperation(
          propertyId,
          Guid.Empty,
          string.Empty,
          oldValue,
          string.Empty,
          string.Empty,
          string.Empty),
        Stage02Hash.Sha256(oldValue ?? string.Empty))
    {
    }

    internal Stage02CurrentPropertySnapshot(
      string propertyId,
      string oldValue,
      string oldValueHash)
      : this(
        new Stage02WriteOperation(
          propertyId,
          Guid.Empty,
          string.Empty,
          oldValue,
          string.Empty,
          string.Empty,
          string.Empty),
        oldValueHash)
    {
    }

    public Stage02CurrentPropertySnapshot(Stage02WriteOperation operation)
      : this(operation, null)
    {
    }

    public Stage02CurrentPropertySnapshot(
      Stage02WriteOperation operation,
      string observedOldValueHash)
    {
      Operation = operation ?? throw new ArgumentNullException(
        nameof(operation));
      OldValueHash = observedOldValueHash ?? operation.OldValueHash;
    }

    public Stage02WriteOperation Operation { get; }
    public string TargetUniqueId => Operation.TargetUniqueId;
    public string PropertyId => Operation.PropertyId;
    public string OldValue => Operation.OldValue;
    public string OldValueHash { get; }
  }

  public sealed class Stage02CurrentElementSnapshot
  {
    internal Stage02CurrentElementSnapshot(
      Stage02ElementReference element,
      string roleId,
      IEnumerable<Stage02CurrentPropertySnapshot> properties)
      : this(element, roleId, string.Empty, string.Empty, properties)
    {
    }

    public Stage02CurrentElementSnapshot(
      Stage02ElementReference element,
      string roleId,
      string matchSource,
      IEnumerable<Stage02CurrentPropertySnapshot> properties)
      : this(
        element,
        roleId,
        matchSource,
        string.Empty,
        properties)
    {
    }

    public Stage02CurrentElementSnapshot(
      Stage02ElementReference element,
      string roleId,
      string matchSource,
      string stage01RecordIdentity,
      IEnumerable<Stage02CurrentPropertySnapshot> properties)
    {
      Element = element;
      RoleId = roleId ?? string.Empty;
      MatchSource = matchSource ?? string.Empty;
      Stage01RecordIdentity = stage01RecordIdentity ?? string.Empty;
      Properties = Stage02Collections.Freeze(properties);
    }

    public Stage02ElementReference Element { get; }
    public string RoleId { get; }
    public string MatchSource { get; }
    public string Stage01RecordIdentity { get; }
    public IReadOnlyList<Stage02CurrentPropertySnapshot> Properties { get; }
  }

  public sealed class Stage02ConfirmationSnapshot
  {
    internal Stage02ConfirmationSnapshot(
      string previewHash,
      string nonce,
      string documentFingerprint,
      string fileContextHash,
      string rulePackageId,
      string rulePackageVersion,
      string rulePackageSha256,
      IEnumerable<Stage02CurrentElementSnapshot> elements)
      : this(
        previewHash,
        nonce,
        string.Empty,
        documentFingerprint,
        fileContextHash,
        string.Empty,
        rulePackageId,
        rulePackageVersion,
        rulePackageSha256,
        elements)
    {
    }

    internal Stage02ConfirmationSnapshot(
      string previewHash,
      string nonce,
      string fileGuid,
      string documentFingerprint,
      string fileContextHash,
      string activeProfileId,
      string rulePackageId,
      string rulePackageVersion,
      string rulePackageSha256,
      IEnumerable<Stage02CurrentElementSnapshot> elements)
      : this(
        previewHash,
        nonce,
        fileGuid,
        documentFingerprint,
        fileContextHash,
        activeProfileId,
        rulePackageId,
        rulePackageVersion,
        rulePackageSha256,
        new Dictionary<string, bool>(StringComparer.Ordinal),
        elements)
    {
    }

    internal Stage02ConfirmationSnapshot(
      string previewHash,
      string nonce,
      string fileGuid,
      string documentFingerprint,
      string fileContextHash,
      string activeProfileId,
      string rulePackageId,
      string rulePackageVersion,
      string rulePackageSha256,
      IEnumerable<KeyValuePair<string, bool>> projectConditions,
      IEnumerable<Stage02CurrentElementSnapshot> elements)
    {
      PreviewHash = previewHash ?? string.Empty;
      Nonce = nonce ?? string.Empty;
      FileGuid = fileGuid ?? string.Empty;
      DocumentFingerprint = documentFingerprint ?? string.Empty;
      FileContextHash = fileContextHash ?? string.Empty;
      ActiveProfileId = activeProfileId ?? string.Empty;
      RulePackageId = rulePackageId ?? string.Empty;
      RulePackageVersion = rulePackageVersion ?? string.Empty;
      RulePackageSha256 = rulePackageSha256 ?? string.Empty;
      ProjectConditions = Stage02Collections.FreezeDictionary(
        projectConditions);
      Elements = Stage02Collections.Freeze(elements);
    }

    public Stage02ConfirmationSnapshot(
      HBRFileContext context,
      string previewHash,
      string nonce,
      IEnumerable<Stage02CurrentElementSnapshot> elements)
      : this(
        previewHash,
        nonce,
        RequireContext(context).FileGuid,
        context.RevitDocumentFingerprint,
        context.FileContextHash,
        context.ModelFileType,
        context.RulePackageId,
        context.RulePackageVersion,
        context.RulePackageSha256,
        context.ProjectConditions,
        elements)
    {
    }

    public string PreviewHash { get; }
    public string Nonce { get; }
    public string FileGuid { get; }
    public string DocumentFingerprint { get; }
    public string FileContextHash { get; }
    public string ActiveProfileId { get; }
    public string RulePackageId { get; }
    public string RulePackageVersion { get; }
    public string RulePackageSha256 { get; }
    public IReadOnlyDictionary<string, bool> ProjectConditions { get; }
    public IReadOnlyList<Stage02CurrentElementSnapshot> Elements { get; }

    private static HBRFileContext RequireContext(HBRFileContext context)
    {
      if (!Stage02FileContextPolicy.IsVerified(context))
      {
        throw new Stage02ContractException(
          Stage02Codes.InvalidFileContext,
          "HBRFileContext 无效、哈希被篡改、schema 不兼容或尚未通过 Stage01 初始化校验。");
      }
      return context;
    }
  }

  public sealed class Stage02ConfirmationResult
  {
    private Stage02ConfirmationResult(
      bool accepted,
      string handoffState,
      string consumptionKey,
      bool requiresNewPreviewAfterExecutionFailure,
      IEnumerable<Stage02Blocker> blockers)
    {
      Accepted = accepted;
      HandoffState = handoffState ?? string.Empty;
      ConsumptionKey = consumptionKey ?? string.Empty;
      RequiresNewPreviewAfterExecutionFailure =
        requiresNewPreviewAfterExecutionFailure;
      Blockers = Stage02Collections.Freeze(blockers);
    }

    public bool Accepted { get; }
    public string HandoffState { get; }
    public string ConsumptionKey { get; }
    public bool RequiresNewPreviewAfterExecutionFailure { get; }
    public IReadOnlyList<Stage02Blocker> Blockers { get; }

    internal static Stage02ConfirmationResult AcceptForExecution(
      string consumptionKey)
    {
      return new Stage02ConfirmationResult(
        true,
        Stage02HandoffStates.ConsumedForExecution,
        consumptionKey,
        true,
        Array.Empty<Stage02Blocker>());
    }

    public static Stage02ConfirmationResult Reject(
      IEnumerable<Stage02Blocker> blockers)
    {
      return new Stage02ConfirmationResult(
        false,
        Stage02HandoffStates.Rejected,
        string.Empty,
        false,
        blockers);
    }
  }

  internal static class Stage02Collections
  {
    public static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
    {
      var copy = values == null
        ? new List<T>()
        : new List<T>(values);
      return new ReadOnlyCollection<T>(copy);
    }

    public static IReadOnlyDictionary<string, bool> FreezeDictionary(
      IEnumerable<KeyValuePair<string, bool>> values)
    {
      var copy = new Dictionary<string, bool>(StringComparer.Ordinal);
      foreach (KeyValuePair<string, bool> pair in
        values ?? Array.Empty<KeyValuePair<string, bool>>())
      {
        copy.Add(pair.Key, pair.Value);
      }
      return new ReadOnlyDictionary<string, bool>(copy);
    }
  }

  internal static class Stage02Hash
  {
    public static string Sha256(string value)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        byte[] hash = algorithm.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (byte item in hash)
          builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        return builder.ToString();
      }
    }
  }
}
