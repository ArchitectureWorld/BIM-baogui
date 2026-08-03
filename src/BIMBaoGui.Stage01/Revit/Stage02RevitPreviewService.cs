using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Revit.Parameters;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage02;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage02RevitPreviewResult
  {
    internal Stage02RevitPreviewResult(
      Stage02Preview preview,
      IEnumerable<Stage02Blocker> blockers)
    {
      Preview = preview;
      Blockers = new ReadOnlyCollection<Stage02Blocker>(
        (blockers ?? Array.Empty<Stage02Blocker>()).ToArray());
    }

    internal Stage02Preview Preview { get; }
    internal IReadOnlyList<Stage02Blocker> Blockers { get; }
    internal bool Success => Preview != null && Blockers.Count == 0;
  }

  internal sealed class Stage02RevitPreviewService
  {
    private readonly HbrRuleDatabase _database;

    internal Stage02RevitPreviewService()
      : this(HbrRuleDatabase.Current)
    {
    }

    internal Stage02RevitPreviewService(HbrRuleDatabase database)
    {
      _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    internal Stage02RevitPreviewResult CreatePreview(
      HBRFileContext context,
      Stage02RevitSelectionResult selection,
      string nonce)
    {
      if (RevitHost.RunReadInHostContext(
        () => CreatePreviewCore(context, selection, nonce),
        out Stage02RevitPreviewResult result,
        out string error))
      {
        return result;
      }
      return Blocked("REVIT_PREVIEW_FAILED", error);
    }

    internal Stage02ConfirmationSnapshot BuildLiveConfirmationSnapshot(
      UIApplication uiApplication,
      Document document,
      HBRFileContext context,
      Stage02Preview preview,
      Stage02RevitSelectionResult currentSelectionEvidence)
    {
      if (preview == null) throw new ArgumentNullException(nameof(preview));
      Stage02RevitSelectionResult selection = ResolveLiveSelection(
        uiApplication,
        document,
        preview,
        currentSelectionEvidence);
      Stage02SelectionSetDecision selectionSet =
        Stage02SelectionSetPolicy.Evaluate(
          preview.Elements.Select(element => element.Element.UniqueId),
          selection.Items.Select(item => item.UniqueId));
      if (!selectionSet.Success)
      {
        throw new Stage02ContractException(
          selectionSet.Blocker.Code,
          selectionSet.Blocker.Message);
      }
      Stage02Preview rebuilt = BuildPreviewOrThrow(
        uiApplication,
        document,
        context,
        selection,
        preview.Nonce,
        preview);
      return new Stage02ConfirmationSnapshot(
        context,
        preview.PreviewHash,
        preview.Nonce,
        rebuilt.Elements.Select(element => new Stage02CurrentElementSnapshot(
          element.Element,
          element.RoleId,
          element.MatchSource,
          element.Stage01RecordIdentity,
          element.Operations.Select(operation =>
            new Stage02CurrentPropertySnapshot(operation)))));
    }

    private static Stage02RevitSelectionResult ResolveLiveSelection(
      UIApplication uiApplication,
      Document document,
      Stage02Preview preview,
      Stage02RevitSelectionResult currentSelectionEvidence)
    {
      if (currentSelectionEvidence == null
        || currentSelectionEvidence.Cancelled
        || currentSelectionEvidence.Messages.Count > 0)
      {
        throw new Stage02ContractException(
          Stage02Codes.InvalidSelectionEvidence,
          "确认写入缺少独立、有效的当前选择证据。");
      }
      if (!string.Equals(
        preview.SelectionMode,
        currentSelectionEvidence.SelectionMode,
        StringComparison.Ordinal))
      {
        throw new Stage02ContractException(
          Stage02Codes.InvalidSelectionEvidence,
          "当前选择证据的 SelectionMode 与预览不一致。");
      }

      switch (preview.SelectionMode)
      {
        case Stage02SelectionModes.CurrentSelection:
          UIDocument uiDocument = uiApplication.ActiveUIDocument;
          if (uiDocument == null || uiDocument.Document != document)
          {
            throw new Stage02ContractException(
              Stage02Codes.DocumentFingerprintChanged,
              "确认时活动 UIDocument 已变化。");
          }
          Stage02RevitSelectionResult live = Stage02RevitSelectionService
            .ReadCurrentSelectionInHostContext(
              uiApplication,
              uiDocument,
              document);
          return RehydrateSelection(
            uiApplication,
            document,
            live,
            currentSelectionEvidence,
            true);

        case Stage02SelectionModes.ExplicitPick:
          return RehydrateSelection(
            uiApplication,
            document,
            currentSelectionEvidence,
            currentSelectionEvidence,
            false);

        case Stage02SelectionModes.ProjectInformation:
          if (currentSelectionEvidence.Items.Count != 1
            || document.ProjectInformation == null)
          {
            throw new Stage02ContractException(
              Stage02Codes.InvalidSelectionEvidence,
              "ProjectInformation 确认证据必须且只能包含一个专用载体。");
          }
          Stage02RevitSelectionItem projectEvidence =
            currentSelectionEvidence.Items[0];
          return new Stage02RevitSelectionResult(
            Stage02SelectionModes.ProjectInformation,
            false,
            new[]
            {
              new Stage02RevitSelectionItem(
                Stage02RevitSelectionService.CreateReference(
                  uiApplication,
                  document,
                  document.ProjectInformation),
                projectEvidence.RoleHint,
                projectEvidence.Stage01RecordIdentity)
            },
            null);

        default:
          throw new Stage02ContractException(
            Stage02Codes.InvalidConfirmationSnapshot,
            "预览不包含可确认的 SelectionMode；必须重新预览。");
      }
    }

    private static Stage02RevitSelectionResult RehydrateSelection(
      UIApplication uiApplication,
      Document document,
      Stage02RevitSelectionResult liveSelection,
      Stage02RevitSelectionResult independentEvidence,
      bool liveSelectionWasReadFromHost)
    {
      if (liveSelection == null
        || liveSelection.Cancelled
        || liveSelection.Messages.Count > 0)
      {
        throw new Stage02ContractException(
          liveSelectionWasReadFromHost
            ? Stage02Codes.ElementSetChanged
            : Stage02Codes.InvalidSelectionEvidence,
          liveSelection == null
            ? "当前选择证据不可用。"
            : string.Join(" ", liveSelection.Messages));
      }
      Dictionary<string, Stage02RevitSelectionItem> evidenceByUniqueId =
        independentEvidence.Items
          .GroupBy(item => item.UniqueId, StringComparer.Ordinal)
          .ToDictionary(
            group => group.Key,
            group => group.First(),
            StringComparer.Ordinal);
      var items = new List<Stage02RevitSelectionItem>();
      foreach (Stage02RevitSelectionItem liveItem in liveSelection.Items)
      {
        Element live = document.GetElement(liveItem.UniqueId);
        if (live == null)
          continue;
        Stage02RevitSelectionItem evidence;
        evidenceByUniqueId.TryGetValue(liveItem.UniqueId, out evidence);
        items.Add(new Stage02RevitSelectionItem(
          Stage02RevitSelectionService.CreateReference(
            uiApplication,
            document,
            live),
          evidence == null ? string.Empty : evidence.RoleHint,
          evidence == null
            ? string.Empty
            : evidence.Stage01RecordIdentity));
      }
      return new Stage02RevitSelectionResult(
        liveSelection.SelectionMode,
        false,
        items,
        null);
    }

    private Stage02RevitPreviewResult CreatePreviewCore(
      HBRFileContext context,
      Stage02RevitSelectionResult selection,
      string nonce)
    {
      if (!RevitHost.TryGetContext(
        out UIApplication uiApplication,
        out _,
        out Document document,
        out string hostError))
      {
        return Blocked("REVIT_HOST_UNAVAILABLE", hostError);
      }
      try
      {
        Stage02Preview preview = BuildPreviewOrThrow(
          uiApplication,
          document,
          context,
          selection,
          nonce,
          null);
        Stage02Blocker[] operationBlockers = CollectOperationBlockers(preview);
        return new Stage02RevitPreviewResult(preview, operationBlockers);
      }
      catch (Stage02ContractException exception)
      {
        return Blocked(exception.Code, exception.Message);
      }
      catch (Exception exception)
      {
        return Blocked("REVIT_PREVIEW_FAILED", exception.Message);
      }
    }

    private Stage02Preview BuildPreviewOrThrow(
      UIApplication uiApplication,
      Document document,
      HBRFileContext context,
      Stage02RevitSelectionResult selection,
      string nonce,
      Stage02Preview expectedPreview)
    {
      if (document.IsFamilyDocument)
        throw new Stage02ContractException(
          "FAMILY_DOCUMENT",
          "族文档不能访问 Document.ParameterBindings。");
      if (selection == null || selection.Cancelled || selection.Items.Count == 0)
        throw new Stage02ContractException(
          expectedPreview == null
            ? Stage02Codes.InvalidSelectionEvidence
            : Stage02Codes.ElementSetChanged,
          "Stage02 预览缺少有效选择。");
      if (!IsSupportedSelectionMode(selection.SelectionMode))
      {
        throw new Stage02ContractException(
          expectedPreview == null
            ? Stage02Codes.InvalidSelectionEvidence
            : Stage02Codes.InvalidConfirmationSnapshot,
          "Stage02 选择缺少明确 SelectionMode。");
      }
      if (expectedPreview != null
        && !string.Equals(
          expectedPreview.SelectionMode,
          selection.SelectionMode,
          StringComparison.Ordinal))
      {
        throw new Stage02ContractException(
          Stage02Codes.ElementSetChanged,
          "确认时 SelectionMode 已变化，必须重新预览。");
      }
      RevitDocumentIdentity identity = RevitDocumentIdentityService.Read(
        uiApplication,
        document);
      if (identity.IsReadOnly)
      {
        throw new Stage02ContractException(
          Stage02Codes.DocumentReadOnly,
          "当前 Revit 文档为只读；禁止生成或确认可写预览。");
      }
      if (identity.PayloadIntegrityDecision == null
        || !identity.PayloadIntegrityDecision.Success)
      {
        throw new Stage02ContractException(
          identity.PayloadIntegrityDecision == null
            ? "CORRUPT_STAGE01_STORAGE"
            : identity.PayloadIntegrityDecision.ErrorCode,
          identity.PayloadIntegrityDecision == null
            ? "Stage01 初始化载荷完整性状态不可用。"
            : identity.PayloadIntegrityDecision.Message);
      }
      string liveFingerprint = HBRDocumentFingerprint.Compute(
        document.PathName,
        document.Title,
        uiApplication.Application.VersionNumber);
      if (!string.Equals(
        liveFingerprint,
        identity.DocumentFingerprint,
        StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          "Stage02 实时文档指纹计算结果不一致。");
      }
      var identityBlockers = new List<string>();
      identityBlockers.AddRange(HBRLiveContextPolicy.Validate(
        context == null ? string.Empty : context.FileGuid,
        context == null ? string.Empty : context.SourcePayloadHash,
        identity.StorageDecision != null
          && identity.StorageDecision.IsInitialized,
        identity.StoredInitialization == null
          ? string.Empty
          : identity.StoredInitialization.FileGuid,
        identity.StoredInitialization == null
          ? string.Empty
          : identity.StoredInitialization.PayloadHash,
        identity.StoredInitialization == null
          ? string.Empty
          : identity.StoredInitialization.WorkflowVersion));
      identityBlockers.AddRange(RevitDocumentIdentityService.Validate(
        context,
        identity));
      if (identityBlockers.Count > 0)
      {
        throw new Stage02ContractException(
          Stage02Codes.FileContextChanged,
          string.Join(" ", identityBlockers));
      }

      Stage01ProjectionData stage01Data = ReadStage01Values(
        identity.StoredInitialization == null
          ? string.Empty
          : identity.StoredInitialization.PayloadJson);
      var matchEngine = new Stage02MatchEngine(_database, context);
      var matchedElements = new List<Stage02MatchedElement>();
      foreach (Stage02RevitSelectionItem selected in selection.Items)
      {
        if (!string.Equals(
          selected.DocumentFingerprint,
          identity.DocumentFingerprint,
          StringComparison.Ordinal))
        {
          throw new Stage02ContractException(
            Stage02Codes.DocumentFingerprintChanged,
            "选择请求的 DocumentFingerprint 与当前文档不一致。");
        }
        Element live = document.GetElement(selected.UniqueId);
        if (live == null)
          throw new Stage02ContractException(
            Stage02Codes.ElementSetChanged,
            "确认时无法按 UniqueId 解析预览中的 Revit 元素。");
        Stage02ElementReference element =
          Stage02RevitSelectionService.CreateReference(
            uiApplication,
            document,
            live);
        string savedRole = Stage02MetadataStorage.ReadSavedRole(
          document,
          selected.UniqueId);
        Stage02MatchResult match = matchEngine.Match(
          element,
          selected.RoleHint,
          savedRole);
        if (!match.Success)
          throw new Stage02ContractException(
            match.Blockers.First().Code,
            string.Join(" ", match.Blockers.Select(blocker => blocker.Message)));
        HbrCarrierRole role = _database.CarrierRolesById[match.RoleId];
        Stage02Stage01ProjectionResult projection =
          Stage02Stage01ProjectionPolicy.Resolve(
            role.RoleId,
            selected.Stage01RecordIdentity,
            stage01Data.Values,
            stage01Data.Organizations);
        if (!projection.Success)
        {
          throw new Stage02ContractException(
            projection.Blockers[0].Code,
            string.Join(
              " ",
              projection.Blockers.Select(blocker => blocker.Message)));
        }
        HbrRuleProperty[] properties = _database.Package.Properties
          .Where(property => property.CarrierRoleIds.Contains(
            role.RoleId,
            StringComparer.Ordinal))
          .Where(property => property.StageOwnership.Contains(
            "STAGE02",
            StringComparer.Ordinal))
          .OrderBy(property => property.PropertyId, StringComparer.Ordinal)
          .ToArray();
        Stage02WriteOperation[] operations = properties
          .Select(property => BuildOperation(
            document,
            live,
            role,
            property,
            projection.Values,
            context.ProjectConditions))
          .ToArray();
        matchedElements.Add(new Stage02MatchedElement(
          element,
          match,
          projection.RecordIdentity,
          operations));
      }
      return new Stage02PreviewCompiler(_database).Compile(
        new Stage02PreviewRequest(
          context,
          nonce,
          selection.SelectionMode,
          matchedElements));
    }

    private Stage02WriteOperation BuildOperation(
      Document document,
      Element selected,
      HbrCarrierRole role,
      HbrRuleProperty property,
      IReadOnlyDictionary<string, string> stage01Values,
      IReadOnlyDictionary<string, bool> projectConditions)
    {
      Element target = ResolveTarget(document, selected, property);
      Parameter canonical = target.get_Parameter(property.Revit.ParameterGuid);
      SharedParameterElement shared = SharedParameterElement.Lookup(
        document,
        property.Revit.ParameterGuid);
      InternalDefinition definition = shared == null
        ? null
        : shared.GetDefinition();
      ElementBinding binding = definition == null
        ? null
        : FindBinding(document.ParameterBindings, definition);
      string[] boundCategories = BindingCategories(binding);
      string actualScope = binding is InstanceBinding
        ? "INSTANCE"
        : binding is TypeBinding ? "TYPE" : string.Empty;
      string actualStorage = canonical == null
        ? StorageForParameterType(property.Revit.ParameterType)
        : canonical.StorageType.ToString();
      string actualParameterType = definition == null
        ? property.Revit.ParameterType
        : definition.ParameterType.ToString();
      bool userModifiable = canonical == null || canonical.UserModifiable;
      var bindingState = new HbrBindingPlanState(
        property.Revit.ParameterName,
        property.Revit.LegacyNames,
        property.Revit.BindingScope,
        property.Revit.StorageType,
        property.Revit.ParameterType,
        definition != null,
        definition == null ? string.Empty : definition.Name,
        binding != null,
        actualScope,
        actualStorage,
        actualParameterType,
        definition == null || definition.Visible,
        userModifiable,
        shared != null && shared.ShouldHideWhenNoValue(),
        boundCategories,
        role.RevitCategories);
      HbrBindingPlanDecision bindingPlan = HbrBindingPlanPolicy.Evaluate(
        bindingState);

      var candidates = new List<HbrParameterValueCandidate>
      {
        new HbrParameterValueCandidate(
          HbrSuggestionSources.CanonicalGuid,
          property.Revit.ParameterGuid.ToString("D"),
          ReadTypedRaw(canonical),
          canonical == null ? 0 : 1,
          canonical == null ? string.Empty : canonical.StorageType.ToString(),
          ParameterTypeName(canonical),
          canonical == null
            ? string.Empty
            : property.Revit.ParameterGuid.ToString("D"),
          true,
          true,
          1)
      };
      AddNamedCandidates(
        target,
        role,
        property,
        property.Revit.LegacyNames,
        HbrSuggestionSources.LegacyName,
        candidates);
      AddNamedCandidates(
        target,
        role,
        property,
        property.Suggestion.Aliases,
        HbrSuggestionSources.RuleAlias,
        candidates);
      HbrStage01FieldRef stage01Field = _database.Package.Stage01.FieldRefs
        .FirstOrDefault(field => string.Equals(
          field.PropertyId,
          property.PropertyId,
          StringComparison.Ordinal));
      string stage01FieldKey = stage01Field == null
        ? string.Empty
        : stage01Field.FieldKey;
      string stage01Projection = string.Empty;
      if (!string.IsNullOrWhiteSpace(stage01FieldKey))
        stage01Values.TryGetValue(stage01FieldKey, out stage01Projection);
      candidates.Add(new HbrParameterValueCandidate(
        HbrSuggestionSources.Stage01Projection,
        "Stage01:" + stage01FieldKey,
        stage01Projection,
        1,
        string.Empty,
        string.Empty,
        string.Empty,
        true,
        false,
        1));
      HbrParameterSuggestionDecision suggestion =
        HbrParameterSuggestionPolicy.Resolve(candidates);
      HbrParameterConversionDecision conversion = HbrParameterValueConverter
        .TryToInternalRawString(
          property,
          suggestion.SuggestedValue,
          suggestion.SourceAlreadyUsesInternalUnits);
      string suggestedInternalRaw = conversion.Success
        ? conversion.InternalRawValue
        : string.Empty;
      Stage02RequirementDecision requirement =
        Stage02RequirementDecisionPolicy.Resolve(
          property.PropertyId,
          property.Requirement.Level,
          property.Requirement.ConditionId,
          _database.Package.Conditions.Select(condition =>
            condition.ConditionId),
          projectConditions);
      if (!requirement.Success)
      {
        throw new Stage02ContractException(
          requirement.ErrorCode,
          requirement.Message);
      }
      var blockers = new List<Stage02Blocker>();
      blockers.AddRange(bindingPlan.Blockers.Select(blocker =>
        new Stage02Blocker(blocker.Code, blocker.Message)));
      blockers.AddRange(suggestion.Blockers.Select(blocker =>
        new Stage02Blocker(blocker.Code, blocker.Message)));
      blockers.AddRange(requirement.Blockers);
      if (!conversion.Success)
      {
        blockers.Add(new Stage02Blocker(
          "INVALID_VALUE",
          "属性 " + property.PropertyId + " 的 "
          + suggestion.ValueSource + " 建议值无法转换："
          + conversion.Message));
      }
      if (canonical != null && canonical.IsReadOnly)
      {
        blockers.Add(new Stage02Blocker(
          "PARAMETER_READ_ONLY",
          "固定 GUID 参数存在但为只读，不能确认写入。"));
      }
      string oldValue = ReadTypedRaw(canonical);
      string valueAction = conversion.Success && !string.IsNullOrWhiteSpace(
          suggestedInternalRaw)
        && !string.Equals(
          oldValue,
          suggestedInternalRaw,
          StringComparison.Ordinal)
          ? "SET"
          : "NO_CHANGE";
      if (!conversion.Success)
        valueAction = "NO_WRITE";
      if (!string.IsNullOrWhiteSpace(requirement.ValueActionOverride))
        valueAction = requirement.ValueActionOverride;
      if (!conversion.Success || suggestion.Blockers.Count > 0)
        valueAction = "NO_WRITE";
      var observed = new Stage02ObservedParameterState(
        target.UniqueId,
        binding != null,
        canonical != null,
        canonical == null ? "MISSING" : "GUID",
        actualScope,
        actualStorage,
        boundCategories,
        canonical != null && canonical.IsReadOnly,
        property.Revit.StorageType,
        oldValue,
        oldValue,
        suggestion.SourceIdentity,
        suggestion.SuggestedValue);
      return new Stage02WriteOperation(
        property.PropertyId,
        property.Revit.ParameterGuid,
        property.Revit.ParameterName,
        observed,
        suggestedInternalRaw,
        suggestion.ValueSource,
        string.Equals(
          suggestion.ValueSource,
          HbrSuggestionSources.Blank,
          StringComparison.Ordinal)
          ? "BLANK"
          : conversion.Success ? "DETERMINISTIC" : "INVALID",
        bindingPlan.Action,
        valueAction,
        requirement.Applicability,
        blockers);
    }

    private void AddNamedCandidates(
      Element target,
      HbrCarrierRole role,
      HbrRuleProperty property,
      IEnumerable<string> names,
      string source,
      ICollection<HbrParameterValueCandidate> candidates)
    {
      foreach (string name in (names ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal))
      {
        IList<Parameter> matches = target.GetParameters(name);
        Parameter parameter = matches.Count == 1 ? matches[0] : null;
        string sourceStorageType = parameter == null
          ? string.Empty
          : parameter.StorageType.ToString();
        string sourceParameterType = ParameterTypeName(parameter);
        HbrNamedParameterCompatibilityDecision compatibility = parameter == null
          ? null
          : HbrNamedParameterCompatibilityPolicy.Evaluate(
            property.Revit.StorageType,
            property.Revit.ParameterType,
            sourceStorageType,
            sourceParameterType);
        int ruleAliasPropertyCount = string.Equals(
            source,
            HbrSuggestionSources.RuleAlias,
            StringComparison.Ordinal)
          ? _database.GetSuggestionAliasPropertyIds(role.RoleId, name).Count
          : 1;
        candidates.Add(new HbrParameterValueCandidate(
          source,
          name,
          ReadTypedRaw(parameter),
          matches.Count,
          sourceStorageType,
          sourceParameterType,
          ParameterGuid(parameter),
          compatibility == null || compatibility.Compatible,
          compatibility != null
            && compatibility.SourceAlreadyUsesInternalUnits,
          ruleAliasPropertyCount));
      }
    }

    private static string ParameterTypeName(Parameter parameter)
    {
      return parameter == null || parameter.Definition == null
        ? string.Empty
        : parameter.Definition.ParameterType.ToString();
    }

    private static string ParameterGuid(Parameter parameter)
    {
      if (parameter == null || !parameter.IsShared) return string.Empty;
      return parameter.GUID.ToString("D");
    }

    private static Element ResolveTarget(
      Document document,
      Element selected,
      HbrRuleProperty property)
    {
      if (string.Equals(
        property.Revit.BindingScope,
        "INSTANCE",
        StringComparison.Ordinal))
      {
        return selected;
      }
      if (string.Equals(
        property.Revit.BindingScope,
        "TYPE",
        StringComparison.Ordinal))
      {
        Element type = document.GetElement(selected.GetTypeId());
        if (type == null || string.IsNullOrWhiteSpace(type.UniqueId))
          throw new Stage02ContractException(
            Stage02Codes.ElementSnapshotChanged,
            "TYPE 绑定规则无法解析明确 TargetUniqueId。");
        return type;
      }
      throw new Stage02ContractException(
        Stage02Codes.RulePackageIdentityMismatch,
        "未知 HBR Revit bindingScope：" + property.Revit.BindingScope);
    }

    private static ElementBinding FindBinding(
      BindingMap bindingMap,
      InternalDefinition definition)
    {
      DefinitionBindingMapIterator iterator = bindingMap.ForwardIterator();
      iterator.Reset();
      while (iterator.MoveNext())
      {
        InternalDefinition candidate = iterator.Key as InternalDefinition;
        if (candidate != null && candidate.Id == definition.Id)
          return iterator.Current as ElementBinding;
      }
      return null;
    }

    private static string[] BindingCategories(ElementBinding binding)
    {
      if (binding == null) return Array.Empty<string>();
      return binding.Categories.Cast<Category>()
        .Select(category => category.Id.IntegerValue)
        .Where(id => Enum.IsDefined(typeof(BuiltInCategory), id))
        .Select(id => ((BuiltInCategory)id).ToString())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    }

    private static string ReadTypedRaw(Parameter parameter)
    {
      if (parameter == null || !parameter.HasValue) return string.Empty;
      switch (parameter.StorageType)
      {
        case StorageType.String:
          return parameter.AsString() ?? string.Empty;
        case StorageType.Integer:
          return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
        case StorageType.Double:
          return parameter.AsDouble().ToString("R", CultureInfo.InvariantCulture);
        default:
          return string.Empty;
      }
    }

    private static string StorageForParameterType(string parameterType)
    {
      switch ((parameterType ?? string.Empty).Trim().ToUpperInvariant())
      {
        case "TEXT": return "String";
        case "INTEGER":
        case "YESNO": return "Integer";
        default: return "Double";
      }
    }

    private static Stage01ProjectionData ReadStage01Values(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
        return Stage01ProjectionData.Empty();
      var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
      Stage01Envelope envelope = serializer.Deserialize<Stage01Envelope>(json);
      if (envelope == null) return Stage01ProjectionData.Empty();
      var values = new Dictionary<string, string>(
        envelope.values ?? new Dictionary<string, string>(),
        StringComparer.Ordinal);
      IDictionary<string, string>[] organizations =
        (envelope.organizations
          ?? new List<Dictionary<string, string>>())
          .Where(record => record != null)
          .Select(record => (IDictionary<string, string>)
            new Dictionary<string, string>(record, StringComparer.Ordinal))
          .ToArray();
      return new Stage01ProjectionData(values, organizations);
    }

    private static bool IsSupportedSelectionMode(string selectionMode)
    {
      return string.Equals(
          selectionMode,
          Stage02SelectionModes.CurrentSelection,
          StringComparison.Ordinal)
        || string.Equals(
          selectionMode,
          Stage02SelectionModes.ExplicitPick,
          StringComparison.Ordinal)
        || string.Equals(
          selectionMode,
          Stage02SelectionModes.ProjectInformation,
          StringComparison.Ordinal);
    }

    private static Stage02RevitPreviewResult Blocked(
      string code,
      string message)
    {
      return new Stage02RevitPreviewResult(
        null,
        new[] { new Stage02Blocker(code, message) });
    }

    private static Stage02Blocker[] CollectOperationBlockers(
      Stage02Preview preview)
    {
      return preview.Elements
        .SelectMany(element => element.Operations)
        .SelectMany(operation => operation.Blockers)
        .Where(blocker => blocker != null)
        .GroupBy(
          blocker => blocker.Code + "\n" + blocker.Message,
          StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(blocker => blocker.Code, StringComparer.Ordinal)
        .ThenBy(blocker => blocker.Message, StringComparer.Ordinal)
        .ToArray();
    }

    private sealed class Stage01Envelope
    {
      public Dictionary<string, string> values { get; set; }
      public List<Dictionary<string, string>> organizations { get; set; }
    }

    private sealed class Stage01ProjectionData
    {
      internal Stage01ProjectionData(
        IDictionary<string, string> values,
        IEnumerable<IDictionary<string, string>> organizations)
      {
        Values = new ReadOnlyDictionary<string, string>(
          new Dictionary<string, string>(
            values ?? new Dictionary<string, string>(),
            StringComparer.Ordinal));
        Organizations = new ReadOnlyCollection<IDictionary<string, string>>(
          (organizations ?? Array.Empty<IDictionary<string, string>>())
            .ToArray());
      }

      internal IReadOnlyDictionary<string, string> Values { get; }
      internal IReadOnlyList<IDictionary<string, string>> Organizations
      {
        get;
      }

      internal static Stage01ProjectionData Empty()
      {
        return new Stage01ProjectionData(
          new Dictionary<string, string>(StringComparer.Ordinal),
          Array.Empty<IDictionary<string, string>>());
      }
    }
  }
}
