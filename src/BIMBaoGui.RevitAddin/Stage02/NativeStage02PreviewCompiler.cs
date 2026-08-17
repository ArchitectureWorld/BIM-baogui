using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02PreviewCompiler
  {
    internal static NativeStage02Preview Compile(
      NativeStage02PreviewInput input,
      NativeStage02RuleCatalog catalog)
    {
      if (input == null) throw new ArgumentNullException(nameof(input));
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));
      if (string.IsNullOrWhiteSpace(input.DocumentFingerprint))
        throw new ArgumentException(
          "Stage02 预览必须冻结文档指纹。",
          nameof(input));
      if (string.IsNullOrWhiteSpace(input.ModelProfile))
        throw new ArgumentException(
          "Stage02 预览必须冻结模型类型。",
          nameof(input));

      var conditions = new SortedDictionary<string, bool>(
        input.Conditions
          ?? new Dictionary<string, bool>(StringComparer.Ordinal),
        StringComparer.Ordinal);
      NativeStage02ElementEvidence[] evidence = (input.Elements
          ?? Array.Empty<NativeStage02ElementEvidence>())
        .Where(value => value != null && value.Element != null)
        .GroupBy(value => value.Element.UniqueId ?? string.Empty,
          StringComparer.Ordinal)
        .Select(group => group
          .OrderBy(value => value.Element.ElementId)
          .First())
        .OrderBy(value => value.Element.UniqueId, StringComparer.Ordinal)
        .ToArray();

      var elements = new List<NativeStage02ElementPlan>(evidence.Length);
      foreach (NativeStage02ElementEvidence elementEvidence in evidence)
      {
        NativeStage02RoleMatchResult automaticRole =
          elementEvidence.AutomaticRoleMatch
          ?? NativeStage02RoleMatcher.Match(
            elementEvidence.Element,
            catalog.CarrierRoles,
            input.ModelProfile);
        NativeStage02RoleMatchResult role = elementEvidence.ResolvedRoleMatch
          ?? automaticRole;
        if (role.Status != NativeStage02RoleMatchStatus.Matched)
        {
          elements.Add(new NativeStage02ElementPlan
          {
            Element = elementEvidence.Element,
            RoleMatchStatus = role.Status,
            AutomaticRoleStatus = automaticRole.Status,
            AutomaticRoleId = automaticRole.RoleId,
            EffectiveRoleId = role.RoleId,
            RoleId = role.RoleId,
            RoleMatchSource = role.MatchSource,
            AssignmentMode = elementEvidence.AssignmentMode,
            AssignmentSource = elementEvidence.AssignmentSource,
            AssignmentAction = elementEvidence.AssignmentAction,
            ManualCarrierEvidence = elementEvidence.ManualCarrierEvidence,
            ElementSnapshotHash = elementEvidence.ElementSnapshotHash,
            Candidates = elementEvidence.Candidates
              ?? Array.Empty<NativeStage02SemanticCandidate>(),
            RoleConfirmation = elementEvidence.RoleConfirmation,
            TaskGeometry = elementEvidence.TaskGeometry,
            Message = role.Message,
            Fields = Array.Empty<NativeStage02FieldPlan>()
          });
          continue;
        }

        var parameterGuids = new HashSet<Guid>(
          (elementEvidence.Parameters
            ?? new Dictionary<Guid, NativeStage02ParameterEvidence>()).Keys);
        NativeStage02PropertyDefinition[] properties = role.CandidateRoleIds
          .SelectMany(roleId => catalog.PropertiesForRole(roleId))
          .GroupBy(value => value.PropertyId, StringComparer.Ordinal)
          .Select(group => group.First())
          .Where(value => parameterGuids.Contains(value.ParameterGuid))
          .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
          .ToArray();
        NativeStage02FieldPlan[] fields = properties
          .Select(property => PlanField(
            property,
            elementEvidence,
            conditions))
          .ToArray();
        elements.Add(new NativeStage02ElementPlan
        {
          Element = elementEvidence.Element,
          RoleMatchStatus = role.Status,
          AutomaticRoleStatus = automaticRole.Status,
          AutomaticRoleId = automaticRole.RoleId,
          RoleId = role.RoleId,
          EffectiveRoleId = role.RoleId,
          RoleMatchSource = role.MatchSource,
          AssignmentMode = elementEvidence.AssignmentMode,
          AssignmentSource = elementEvidence.AssignmentSource,
          AssignmentAction = elementEvidence.AssignmentAction,
          ManualCarrierEvidence = elementEvidence.ManualCarrierEvidence,
          ElementSnapshotHash = elementEvidence.ElementSnapshotHash,
          Candidates = elementEvidence.Candidates
            ?? Array.Empty<NativeStage02SemanticCandidate>(),
          RoleConfirmation = elementEvidence.RoleConfirmation,
          TaskGeometry = elementEvidence.TaskGeometry,
          Fields = new ReadOnlyCollection<NativeStage02FieldPlan>(fields)
        });
      }

      var preview = new NativeStage02Preview
      {
        ScopeMode = input.ScopeMode,
        RunId = input.RunId ?? string.Empty,
        RulePackageId = catalog.Identity.PackageId,
        RulePackageVersion = catalog.Identity.PackageVersion,
        RulePackageSha256 = catalog.Identity.RulePackageSha256,
        DocumentFingerprint = input.DocumentFingerprint,
        ModelProfile = input.ModelProfile,
        IdentificationMode = input.IdentificationMode,
        BulkRoleId = (input.BulkRoleId ?? string.Empty).Trim(),
        RoleOverrides = new ReadOnlyCollection<NativeStage02RoleOverride>(
          (input.RoleOverrides ?? Array.Empty<NativeStage02RoleOverride>())
            .Where(value => value != null)
            .Select(value => new NativeStage02RoleOverride
            {
              ElementUniqueId = (value.ElementUniqueId ?? string.Empty).Trim(),
              RoleId = (value.RoleId ?? string.Empty).Trim()
            })
            .OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)
            .ThenBy(value => value.RoleId, StringComparer.Ordinal)
            .ToArray()),
        Confirmations = new ReadOnlyCollection<NativeStage02RoleConfirmation>(
          (input.Confirmations ?? Array.Empty<NativeStage02RoleConfirmation>())
            .Where(value => value != null)
            .Select(value => value.Clone())
            .OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)
            .ThenBy(value => value.RoleId, StringComparer.Ordinal)
            .ToArray()),
        Conditions = new ReadOnlyDictionary<string, bool>(conditions),
        Elements = new ReadOnlyCollection<NativeStage02ElementPlan>(elements),
        BlockedElementCount = elements.Count(value => value.IsBlocked),
        ActionableElementCount = elements.Count(value =>
          !value.IsBlocked && value.HasActionableWork),
        CorrectFieldCount = Count(
          elements,
          NativeStage02FieldStatus.Correct),
        PendingBindingFieldCount = Count(
          elements,
          NativeStage02FieldStatus.PendingBinding),
        PendingWriteFieldCount = Count(
          elements,
          NativeStage02FieldStatus.PendingWrite),
        PendingInputFieldCount = Count(
          elements,
          NativeStage02FieldStatus.PendingInput),
        RuntimeBlockedFieldCount = Count(
          elements,
          NativeStage02FieldStatus.RuntimeBlocked)
      };
      preview.CanonicalJson = NativeStage02PreviewCanonicalizer.Build(preview);
      preview.PreviewHash = NativeStage02PreviewCanonicalizer.Sha256(
        preview.CanonicalJson);
      preview.Issues = new ReadOnlyCollection<
        BIMBaoGui.RevitAddin.Issues.NativeIssueRecord>(
        NativeStage02IssueCompiler.Compile(preview).ToArray());
      return preview;
    }

    private static NativeStage02FieldPlan PlanField(
      NativeStage02PropertyDefinition property,
      NativeStage02ElementEvidence evidence,
      IReadOnlyDictionary<string, bool> conditions)
    {
      if (evidence.RoleConfirmation?.Confirmed != true)
      {
        return Plan(
          property,
          NativeStage02FieldStatus.PendingConfirmation,
          NativeStage02BindingAction.None,
          NativeStage02ValueAction.None,
          string.Empty,
          string.Empty,
          string.Empty,
          (evidence.RoleConfirmation?.Code ?? "ROLE_CONFIRMATION_REQUIRED")
            + "：候选角色必须先显式确认。",
          false);
      }
      NativeRuntimeStatusDecision runtime = property.RuntimeDecision;
      if (runtime == null
        || (runtime.Status != NativeRuntimeStatuses.Supported
          && runtime.Status
            != NativeRuntimeStatuses.UnclassifiedRequirement))
      {
        string code = runtime == null
          ? "RUNTIME_STATUS_MISSING"
          : runtime.BlockCode;
        string reason = runtime == null
          ? "字段缺少运行能力判定。"
          : runtime.BlockReason;
        return Plan(
          property,
          NativeStage02FieldStatus.RuntimeBlocked,
          NativeStage02BindingAction.None,
          NativeStage02ValueAction.None,
          string.Empty,
          string.Empty,
          string.Empty,
          code + "：" + reason,
          false);
      }

      if (!string.IsNullOrWhiteSpace(property.ConditionId))
      {
        if (!conditions.TryGetValue(
          property.ConditionId,
          out bool conditionActive))
        {
          return Plan(
            property,
            NativeStage02FieldStatus.Blocked,
            NativeStage02BindingAction.Blocked,
            NativeStage02ValueAction.None,
            string.Empty,
            string.Empty,
            string.Empty,
            "CONDITION_MISSING：项目条件键缺失，不得静默按 false 处理。",
            false);
        }
        if (!conditionActive)
        {
          return Plan(
            property,
            NativeStage02FieldStatus.NotApplicable,
            NativeStage02BindingAction.None,
            NativeStage02ValueAction.None,
            string.Empty,
            string.Empty,
            string.Empty,
            "CONDITION_INACTIVE：当前项目条件未启用。",
            false);
        }
      }

      NativeStage02ParameterEvidence parameter = null;
      if (evidence.Parameters != null)
        evidence.Parameters.TryGetValue(property.ParameterGuid, out parameter);
      if (parameter == null)
      {
        parameter = new NativeStage02ParameterEvidence
        {
          ParameterGuid = property.ParameterGuid,
          Exists = false,
          ContractCompatible = true,
          BindingIncludesCategory = false
        };
      }
      if (parameter.ParameterGuid != Guid.Empty
        && parameter.ParameterGuid != property.ParameterGuid)
      {
        return Blocked(
          property,
          "PARAMETER_GUID_DRIFT：字段证据 GUID 与规则数据库不一致。");
      }
      if (!parameter.ContractCompatible)
      {
        return Blocked(
          property,
          "PARAMETER_CONTRACT_CONFLICT："
            + (parameter.ContractMessage ?? string.Empty));
      }

      NativeStage02BindingAction bindingAction = parameter.Exists
        ? parameter.BindingIncludesCategory
          ? NativeStage02BindingAction.None
          : NativeStage02BindingAction.MergeCategories
        : NativeStage02BindingAction.Create;
      string current = NormalizeValue(parameter.CurrentCanonicalValue);
      if (current.Length > 0)
      {
        NativeStage02FieldStatus status = bindingAction
          == NativeStage02BindingAction.None
          ? NativeStage02FieldStatus.Correct
          : NativeStage02FieldStatus.PendingBinding;
        bool strictReady = status == NativeStage02FieldStatus.Correct
          && runtime.Status == NativeRuntimeStatuses.Supported;
        return Plan(
          property,
          status,
          bindingAction,
          NativeStage02ValueAction.Keep,
          current,
          string.Empty,
          string.Empty,
          RuntimeMessage(runtime),
          strictReady);
      }

      string semanticSuggestion = NormalizeValue(
        parameter.SuggestedCanonicalValue);
      if (semanticSuggestion.Length > 0)
      {
        if (parameter.IsReadOnly)
          return Blocked(
            property,
            "PARAMETER_READ_ONLY：参数只读，无法写入批准的 Revit 事实建议值。");
        NativeStage02FieldStatus status = bindingAction
          == NativeStage02BindingAction.None
            ? NativeStage02FieldStatus.PendingWrite
            : NativeStage02FieldStatus.PendingBinding;
        return Plan(
          property,
          status,
          bindingAction,
          NativeStage02ValueAction.Set,
          string.Empty,
          semanticSuggestion,
          parameter.SuggestionSource,
          RuntimeMessage(runtime),
          false);
      }

      AliasDecision alias = ResolveAlias(property, parameter.AliasValues);
      if (alias.Conflict)
      {
        return Blocked(
          property,
          "ALIAS_VALUE_CONFLICT：批准别名存在多个不一致的非空值。" );
      }
      if (alias.Value.Length > 0)
      {
        if (parameter.IsReadOnly)
        {
          return Blocked(
            property,
            "PARAMETER_READ_ONLY：参数只读，无法写入批准别名值。" );
        }
        NativeStage02FieldStatus status = bindingAction
          == NativeStage02BindingAction.None
          ? NativeStage02FieldStatus.PendingWrite
          : NativeStage02FieldStatus.PendingBinding;
        return Plan(
          property,
          status,
          bindingAction,
          NativeStage02ValueAction.Set,
          string.Empty,
          alias.Value,
          alias.Source,
          RuntimeMessage(runtime),
          false);
      }

      return Plan(
        property,
        bindingAction == NativeStage02BindingAction.None
          ? NativeStage02FieldStatus.PendingInput
          : NativeStage02FieldStatus.PendingBinding,
        bindingAction,
        NativeStage02ValueAction.PendingInput,
        string.Empty,
        string.Empty,
        string.Empty,
        "PENDING_INPUT：未发现可靠值来源；只准备参数，不伪造业务值。"
          + RuntimeMessage(runtime),
        false);
    }

    private static AliasDecision ResolveAlias(
      NativeStage02PropertyDefinition property,
      IDictionary<string, string> aliasValues)
    {
      var approved = new HashSet<string>(
        property.SuggestionAliases
          .Concat(property.LegacyParameterNames)
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .Select(NativeStage02RoleMatcher.NormalizeAlias),
        StringComparer.OrdinalIgnoreCase);
      var candidates = (aliasValues
          ?? new Dictionary<string, string>(StringComparer.Ordinal))
        .Where(pair => approved.Contains(
          NativeStage02RoleMatcher.NormalizeAlias(pair.Key)))
        .Select(pair => new
        {
          Source = pair.Key ?? string.Empty,
          Value = NormalizeValue(pair.Value)
        })
        .Where(value => value.Value.Length > 0)
        .OrderBy(value =>
          NativeStage02RoleMatcher.NormalizeAlias(value.Source),
          StringComparer.OrdinalIgnoreCase)
        .ToArray();
      string[] values = candidates
        .Select(value => value.Value)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
      if (values.Length > 1)
        return new AliasDecision(true, string.Empty, string.Empty);
      if (values.Length == 0)
        return new AliasDecision(false, string.Empty, string.Empty);
      return new AliasDecision(
        false,
        values[0],
        candidates.First(value => value.Value == values[0]).Source);
    }

    private static NativeStage02FieldPlan Blocked(
      NativeStage02PropertyDefinition property,
      string message)
    {
      return Plan(
        property,
        NativeStage02FieldStatus.Blocked,
        NativeStage02BindingAction.Blocked,
        NativeStage02ValueAction.None,
        string.Empty,
        string.Empty,
        string.Empty,
        message,
        false);
    }

    private static NativeStage02FieldPlan Plan(
      NativeStage02PropertyDefinition property,
      NativeStage02FieldStatus status,
      NativeStage02BindingAction bindingAction,
      NativeStage02ValueAction valueAction,
      string current,
      string proposed,
      string source,
      string message,
      bool strictReady)
    {
      return new NativeStage02FieldPlan
      {
        Property = property,
        Status = status,
        BindingAction = bindingAction,
        ValueAction = valueAction,
        CurrentCanonicalValue = current ?? string.Empty,
        ProposedCanonicalValue = proposed ?? string.Empty,
        ValueSource = source ?? string.Empty,
        Message = message ?? string.Empty,
        StrictExportReady = strictReady
      };
    }

    private static int Count(
      IEnumerable<NativeStage02ElementPlan> elements,
      NativeStage02FieldStatus status)
    {
      return elements.SelectMany(value => value.Fields)
        .Count(value => value.Status == status);
    }

    private static string RuntimeMessage(NativeRuntimeStatusDecision runtime)
    {
      return runtime.Status == NativeRuntimeStatuses.Supported
        ? string.Empty
        : "｜" + runtime.BlockCode + "：" + runtime.BlockReason;
    }

    private static string NormalizeValue(string value)
    {
      return (value ?? string.Empty)
        .Normalize(NormalizationForm.FormKC)
        .Trim();
    }

    private sealed class AliasDecision
    {
      internal AliasDecision(bool conflict, string value, string source)
      {
        Conflict = conflict;
        Value = value ?? string.Empty;
        Source = source ?? string.Empty;
      }

      internal bool Conflict { get; }
      internal string Value { get; }
      internal string Source { get; }
    }
  }

  internal static class NativeStage02PreviewCanonicalizer
  {
    private static readonly JavaScriptSerializer Serializer =
      new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
      };

    internal static string Build(NativeStage02Preview preview)
    {
      if (preview == null) throw new ArgumentNullException(nameof(preview));
      var builder = new StringBuilder(32768);
      builder.Append('{');
      Property(builder, "schemaVersion", preview.SchemaVersion, false);
      Property(builder, "scopeMode", preview.ScopeMode.ToString(), true);
      Property(builder, "rulePackageId", preview.RulePackageId, true);
      Property(builder, "rulePackageVersion", preview.RulePackageVersion, true);
      Property(builder, "rulePackageSha256", preview.RulePackageSha256, true);
      Property(
        builder,
        "documentFingerprint",
        preview.DocumentFingerprint,
        true);
      Property(builder, "modelProfile", preview.ModelProfile, true);
      Property(
        builder,
        "identificationMode",
        preview.IdentificationMode.ToString(),
        true);
      Property(builder, "bulkRoleId", preview.BulkRoleId, true);
      builder.Append(",\"roleOverrides\":[");
      bool first = true;
      foreach (NativeStage02RoleOverride roleOverride in preview.RoleOverrides
        .OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)
        .ThenBy(value => value.RoleId, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        first = false;
        builder.Append('{');
        Property(
          builder,
          "elementUniqueId",
          roleOverride.ElementUniqueId,
          false);
        Property(builder, "roleId", roleOverride.RoleId, true);
        builder.Append('}');
      }
      builder.Append(']');
      builder.Append(",\"confirmations\":[");
      first = true;
      foreach (NativeStage02RoleConfirmation confirmation in
        (preview.Confirmations ?? Array.Empty<NativeStage02RoleConfirmation>())
          .OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)
          .ThenBy(value => value.RoleId, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        first = false;
        builder.Append('{');
        Property(builder, "elementUniqueId", confirmation.ElementUniqueId, false);
        Property(builder, "roleId", confirmation.RoleId, true);
        Property(builder, "elementSnapshotHash", confirmation.ElementSnapshotHash, true);
        Property(builder, "rulePackageSha256", confirmation.RulePackageSha256, true);
        builder.Append('}');
      }
      builder.Append(']');
      builder.Append(",\"conditions\":[");
      first = true;
      foreach (KeyValuePair<string, bool> condition in preview.Conditions
        .OrderBy(value => value.Key, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        first = false;
        builder.Append("{\"id\":")
          .Append(Q(condition.Key))
          .Append(",\"active\":")
          .Append(condition.Value ? "true" : "false")
          .Append('}');
      }
      builder.Append("],\"elements\":[");
      first = true;
      foreach (NativeStage02ElementPlan element in preview.Elements
        .OrderBy(value => value.Element.UniqueId, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        first = false;
        builder.Append('{');
        Property(builder, "uniqueId", element.Element.UniqueId, false);
        NumberProperty(builder, "elementId", element.Element.ElementId);
        Property(builder, "category", element.Element.Category, true);
        Property(builder, "categoryName", element.Element.CategoryName, true);
        Property(builder, "clrType", element.Element.ClrType, true);
        Property(builder, "elementKind", element.Element.ElementKind, true);
        Property(builder, "elementName", element.Element.ElementName, true);
        Property(builder, "familyName", element.Element.FamilyName, true);
        Property(builder, "typeName", element.Element.TypeName, true);
        Property(builder, "levelName", element.Element.LevelName, true);
        Property(builder, "elementSnapshotHash", element.ElementSnapshotHash, true);
        Property(
          builder,
          "geometryEvidenceHash",
          element.Element.Geometry?.EvidenceHash ?? string.Empty,
          true);
        Property(
          builder,
          "roleMatchStatus",
          element.RoleMatchStatus.ToString(),
          true);
        Property(builder, "roleId", element.RoleId, true);
        Property(builder, "roleMatchSource", element.RoleMatchSource, true);
        Property(
          builder,
          "automaticRoleStatus",
          element.AutomaticRoleStatus.ToString(),
          true);
        Property(builder, "automaticRoleId", element.AutomaticRoleId, true);
        Property(builder, "effectiveRoleId", element.EffectiveRoleId, true);
        Property(
          builder,
          "assignmentMode",
          element.AssignmentMode.ToString(),
          true);
        Property(builder, "assignmentSource", element.AssignmentSource, true);
        Property(builder, "assignmentAction", element.AssignmentAction, true);
        Property(
          builder,
          "manualCarrierEvidence",
          element.ManualCarrierEvidence,
          true);
        builder.Append(",\"candidates\":[");
        bool firstCandidate = true;
        foreach (NativeStage02SemanticCandidate candidate in
          (element.Candidates ?? Array.Empty<NativeStage02SemanticCandidate>())
            .OrderBy(value => value.RoleId, StringComparer.Ordinal))
        {
          if (!firstCandidate) builder.Append(',');
          firstCandidate = false;
          builder.Append('{');
          Property(builder, "roleId", candidate.RoleId, false);
          Property(builder, "confidence", candidate.Confidence, true);
          builder.Append(",\"evidence\":[");
          bool firstEvidence = true;
          foreach (string item in (candidate.Evidence ?? Array.Empty<string>())
            .OrderBy(value => value, StringComparer.Ordinal))
          {
            if (!firstEvidence) builder.Append(',');
            firstEvidence = false;
            builder.Append(Q(item));
          }
          builder.Append("]}");
        }
        builder.Append(']');
        builder.Append(",\"roleConfirmation\":{");
        Property(
          builder,
          "confirmed",
          element.RoleConfirmation?.Confirmed == true ? "true" : "false",
          false);
        Property(builder, "code", element.RoleConfirmation?.Code, true);
        Property(
          builder,
          "resolvedRoleId",
          element.RoleConfirmation?.ResolvedRoleId,
          true);
        Property(builder, "source", element.RoleConfirmation?.Source, true);
        builder.Append('}');
        Property(
          builder,
          "taskGeometryEvaluationHash",
          element.TaskGeometry?.EvaluationHash ?? string.Empty,
          true);
        builder.Append(",\"fields\":[");
        bool firstField = true;
        foreach (NativeStage02FieldPlan field in element.Fields
          .OrderBy(value => value.Property.PropertyId, StringComparer.Ordinal))
        {
          if (!firstField) builder.Append(',');
          firstField = false;
          builder.Append('{');
          Property(builder, "propertyId", field.Property.PropertyId, false);
          Property(
            builder,
            "parameterGuid",
            field.Property.ParameterGuid.ToString("D"),
            true);
          Property(builder, "status", field.Status.ToString(), true);
          Property(
            builder,
            "bindingAction",
            field.BindingAction.ToString(),
            true);
          Property(
            builder,
            "valueAction",
            field.ValueAction.ToString(),
            true);
          Property(
            builder,
            "currentCanonicalValue",
            field.CurrentCanonicalValue,
            true);
          Property(
            builder,
            "proposedCanonicalValue",
            field.ProposedCanonicalValue,
            true);
          Property(builder, "valueSource", field.ValueSource, true);
          Property(
            builder,
            "runtimeStatus",
            field.Property.RuntimeDecision == null
              ? string.Empty
              : field.Property.RuntimeDecision.Status,
            true);
          builder.Append(",\"strictExportReady\":")
            .Append(field.StrictExportReady ? "true" : "false")
            .Append('}');
        }
        builder.Append("]}");
      }
      builder.Append("]}");
      return builder.ToString();
    }

    internal static string Sha256(string value)
    {
      using (SHA256 sha = SHA256.Create())
      {
        byte[] bytes = new UTF8Encoding(false).GetBytes(value ?? string.Empty);
        return string.Concat(sha.ComputeHash(bytes)
          .Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
      }
    }

    private static void Property(
      StringBuilder builder,
      string name,
      string value,
      bool prefixComma)
    {
      if (prefixComma) builder.Append(',');
      builder.Append(Q(name)).Append(':').Append(Q(value));
    }

    private static void NumberProperty(
      StringBuilder builder,
      string name,
      int value)
    {
      builder.Append(',')
        .Append(Q(name))
        .Append(':')
        .Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static string Q(string value)
    {
      return Serializer.Serialize(value ?? string.Empty);
    }
  }
}
