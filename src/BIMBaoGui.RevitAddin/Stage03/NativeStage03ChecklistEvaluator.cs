using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Stage02B;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal static class NativeStage03ChecklistEvaluator
  {
    internal static IReadOnlyList<NativeStage03ChecklistItem> Evaluate(
      IReadOnlyList<NativeReportingCheckDefinition> definitions,
      NativeStage03SourceEvidenceBundle evidence)
    {
      if (evidence == null) throw new ArgumentNullException(nameof(evidence));
      return new ReadOnlyCollection<NativeStage03ChecklistItem>((definitions
          ?? Array.Empty<NativeReportingCheckDefinition>())
        .Where(value => value != null)
        .Select(value => Evaluate(value, evidence))
        .ToArray());
    }

    private static NativeStage03ChecklistItem Evaluate(
      NativeReportingCheckDefinition definition,
      NativeStage03SourceEvidenceBundle evidence)
    {
      NativeStage03ChecklistItem item = Create(definition);
      if (!evidence.ScanExecuted) return item;

      string freshnessCode = FreshnessCode(definition, evidence);
      if (freshnessCode.Length > 0) return Fail(item, freshnessCode);

      switch (definition.CheckKind)
      {
        case NativeReportingCheckKind.Stage01Field:
          return EvaluateStage01Field(item, definition, evidence);
        case NativeReportingCheckKind.PlanningTarget:
          return EvaluatePlanningTarget(item, definition, evidence);
        case NativeReportingCheckKind.SemanticRole:
          return EvaluateRole(item, definition, evidence);
        case NativeReportingCheckKind.AttributeRequirement:
          return EvaluateAttribute(item, definition, evidence);
        case NativeReportingCheckKind.Geometry:
        case NativeReportingCheckKind.PropertyConsistency:
          return EvaluateGeometry(item, definition, evidence);
        case NativeReportingCheckKind.Stage02BMetric:
          return EvaluateMetric(item, definition, evidence);
        case NativeReportingCheckKind.TargetComparison:
          return EvaluateTarget(item, definition, evidence);
        case NativeReportingCheckKind.System:
          return EvaluateSystem(item, definition, evidence);
        default:
          return Fail(item, "MISSING_REQUIRED_DATA");
      }
    }

    private static NativeStage03ChecklistItem EvaluateStage01Field(
      NativeStage03ChecklistItem item,
      NativeReportingCheckDefinition definition,
      NativeStage03SourceEvidenceBundle evidence)
    {
      string value = evidence.Stage01?.Model?.GetValue(definition.FieldKey)
        ?? string.Empty;
      if (string.IsNullOrWhiteSpace(value))
        return Fail(item, "MISSING_REQUIRED_DATA");
      item.CurrentValue = value;
      NativeWorkflowItemEvidence resultItem = FindWorkflowItem(
        evidence.Stage01Result, definition.FieldKey, definition.PropertyId);
      if (resultItem == null) return Fail(item, "MISSING_REQUIRED_DATA");
      if (!resultItem.WriteSucceeded) return Fail(item, "WRITE_FAILED");
      if (!resultItem.ReadbackSucceeded) return Fail(item, "READBACK_FAILED");
      return InternalPass(item);
    }

    private static NativeStage03ChecklistItem EvaluatePlanningTarget(
      NativeStage03ChecklistItem item,
      NativeReportingCheckDefinition definition,
      NativeStage03SourceEvidenceBundle evidence)
    {
      NativeWorkflowItemEvidence resultItem = FindWorkflowItem(
        evidence.Stage01Result, definition.PropertyId, definition.TargetKey);
      if (resultItem == null
        || string.IsNullOrWhiteSpace(resultItem.CurrentValue))
        return Fail(item, "MISSING_REQUIRED_DATA");
      if (!resultItem.WriteSucceeded) return Fail(item, "WRITE_FAILED");
      if (!resultItem.ReadbackSucceeded) return Fail(item, "READBACK_FAILED");
      item.CurrentValue = resultItem.CurrentValue;
      return InternalPass(item);
    }

    private static NativeStage03ChecklistItem EvaluateRole(
      NativeStage03ChecklistItem item,
      NativeReportingCheckDefinition definition,
      NativeStage03SourceEvidenceBundle evidence)
    {
      NativeStage02ElementPlan[] candidates = Plans(evidence)
        .Where(value => HasRoleCandidate(value, definition.RoleId))
        .ToArray();
      NativeStage02ElementPlan[] matched = candidates
        .Where(value => value.RoleMatchStatus == NativeStage02RoleMatchStatus.Matched
          && string.Equals(value.EffectiveRoleId.Length > 0
              ? value.EffectiveRoleId
              : value.RoleId,
            definition.RoleId,
            StringComparison.Ordinal))
        .ToArray();
      if (matched.Length == 0)
      {
        NativeStage02ElementPlan[] low = candidates.Where(value =>
          (value.Candidates ?? Array.Empty<NativeStage02SemanticCandidate>())
          .Any(candidate => candidate != null
            && string.Equals(candidate.RoleId, definition.RoleId,
              StringComparison.Ordinal)
            && string.Equals(candidate.Confidence, "LOW",
              StringComparison.OrdinalIgnoreCase))).ToArray();
        if (low.Length > 0)
          return Warning(SetElements(item, low), "LOW_CONFIDENCE_CANDIDATE");
        return Fail(SetElements(item, candidates), "MISSING_REQUIRED_ELEMENT");
      }
      SetElements(item, matched);
      foreach (NativeStage02ElementPlan plan in matched)
      {
        if (plan.RoleConfirmation?.Confirmed != true)
          return Fail(item, "ROLE_CONFIRMATION_REQUIRED");
        NativeWorkflowItemEvidence resultItem = FindWorkflowItem(
          evidence.Stage02AResult,
          plan.Element.UniqueId + "|ROLE_CONFIRMATION");
        if (resultItem == null || !resultItem.WriteSucceeded)
          return Fail(item, "WRITE_FAILED");
        if (!resultItem.ReadbackSucceeded)
          return Fail(item, "READBACK_FAILED");
      }
      return InternalPass(item);
    }

    private static NativeStage03ChecklistItem EvaluateAttribute(
      NativeStage03ChecklistItem item,
      NativeReportingCheckDefinition definition,
      NativeStage03SourceEvidenceBundle evidence)
    {
      if (string.IsNullOrWhiteSpace(definition.PropertyId))
        return Fail(item, "ATTRIBUTE_MAPPING_MISSING");
      NativeStage02ElementPlan[] plans = ElementsFor(definition, evidence);
      if (plans.Length == 0)
        return Fail(item, "MISSING_REQUIRED_ELEMENT");
      SetElements(item, plans);
      foreach (NativeStage02ElementPlan plan in plans)
      {
        NativeStage02FieldPlan field = (plan.Fields
            ?? Array.Empty<NativeStage02FieldPlan>())
          .SingleOrDefault(value => value?.Property != null
            && string.Equals(value.Property.PropertyId, definition.PropertyId,
              StringComparison.Ordinal));
        if (field == null) return Fail(item, "ATTRIBUTE_MAPPING_MISSING");
        if (string.IsNullOrWhiteSpace(field.CurrentCanonicalValue))
          return Fail(item, "ATTRIBUTE_VALUE_MISSING");
        string workflowIdentity = plan.Element.UniqueId + "|"
          + field.Property.ParameterGuid.ToString("D");
        NativeWorkflowItemEvidence resultItem = FindWorkflowItem(
          evidence.Stage02AResult, workflowIdentity);
        if (resultItem == null || !resultItem.WriteSucceeded)
          return Fail(item, "WRITE_FAILED");
        if (!resultItem.ReadbackSucceeded)
          return Fail(item, "READBACK_FAILED");
        item.CurrentValue = field.CurrentCanonicalValue;
      }
      return InternalPass(item);
    }

    private static NativeStage03ChecklistItem EvaluateGeometry(
      NativeStage03ChecklistItem item,
      NativeReportingCheckDefinition definition,
      NativeStage03SourceEvidenceBundle evidence)
    {
      NativeStage02ElementPlan[] plans = ElementsFor(definition, evidence);
      if (plans.Length == 0)
        return Fail(item, "MISSING_REQUIRED_ELEMENT");
      SetElements(item, plans);
      foreach (NativeStage02ElementPlan plan in plans)
      {
        NativeStage02GeometryCheckEvidence check =
          (plan.TaskGeometry?.Checks
            ?? Array.Empty<NativeStage02GeometryCheckEvidence>())
          .SingleOrDefault(value => value != null
            && (string.Equals(value.CheckId, definition.CheckId,
                StringComparison.Ordinal)
              || string.Equals(value.RuleText, definition.RuleText,
                StringComparison.Ordinal)));
        if (check == null)
        {
          string capture = plan.Element?.Geometry?.CaptureCode ?? string.Empty;
          return Fail(item, string.Equals(
            capture, "GEOMETRY_CAPTURE_AMBIGUOUS", StringComparison.Ordinal)
              ? "GEOMETRY_CAPTURE_AMBIGUOUS"
              : "GEOMETRY_CAPTURE_UNSUPPORTED");
        }
        string failure = GeometryFailureCode(definition.CheckKind, check);
        if (failure.Length > 0) return Fail(item, failure);
        NativeWorkflowItemEvidence resultItem = FindWorkflowItem(
          evidence.Stage02AResult,
          plan.Element.UniqueId + "|" + check.CheckId);
        if (resultItem == null || !resultItem.WriteSucceeded)
          return Fail(item, "WRITE_FAILED");
        if (!resultItem.ReadbackSucceeded)
          return Fail(item, "READBACK_FAILED");
        if (check.State == NativeStage02GeometryCheckState.ManualReviewApproved)
          item.IssueCode = "MANUAL_REVIEW_APPROVED_CURRENT";
      }
      return InternalPass(item);
    }

    private static NativeStage03ChecklistItem EvaluateMetric(
      NativeStage03ChecklistItem item,
      NativeReportingCheckDefinition definition,
      NativeStage03SourceEvidenceBundle evidence)
    {
      NativeStage02BMetricRecord record = (evidence.Stage02B?.Records
          ?? Array.Empty<NativeStage02BMetricRecord>())
        .SingleOrDefault(value => value != null
          && string.Equals(value.PropertyId, definition.PropertyId,
            StringComparison.Ordinal));
      if (record == null) return Fail(item, "MISSING_REQUIRED_DATA");
      NativeStage02BCurrentResultDecision current =
        NativeStage02BCurrentResultPolicy.Evaluate(
          record, evidence.CurrentIdentity);
      if (!current.Current)
      {
        return Fail(item,
          string.Equals(current.Code, "CURRENT_VALUE_MISSING",
            StringComparison.Ordinal)
            ? "MISSING_REQUIRED_DATA"
            : "READBACK_FAILED");
      }
      if (!current.ExportReady)
        return Fail(item, "OFFICIAL_CARRIER_PENDING_GOLDEN_RVT");
      item.CurrentValue = current.CurrentCanonicalValue;
      return InternalPass(item);
    }

    private static NativeStage03ChecklistItem EvaluateTarget(
      NativeStage03ChecklistItem item,
      NativeReportingCheckDefinition definition,
      NativeStage03SourceEvidenceBundle evidence)
    {
      if (string.IsNullOrWhiteSpace(definition.PropertyId))
        return Fail(item, "TARGET_COMPARISON_MAPPING_MISSING");
      if (evidence.Stage01?.Model?.PlanningTargets == null
        || !evidence.Stage01.Model.PlanningTargets.TryGetValue(
          definition.TargetKey ?? string.Empty,
          out NativePlanningTargetValue target))
        return Fail(item, "TARGET_VALUE_MISSING");
      NativeStage02BMetricRecord record = (evidence.Stage02B?.Records
          ?? Array.Empty<NativeStage02BMetricRecord>())
        .SingleOrDefault(value => value != null
          && string.Equals(value.PropertyId, definition.PropertyId,
            StringComparison.Ordinal));
      if (record == null) return Fail(item, "TARGET_VALUE_MISSING");
      NativeStage02BCurrentResultDecision current =
        NativeStage02BCurrentResultPolicy.Evaluate(record,
          evidence.CurrentIdentity);
      if (!current.Current) return Fail(item, "READBACK_FAILED");
      if (!TryCompare(target, current.CurrentCanonicalValue,
        out bool comparison))
        return Fail(item, "TARGET_VALUE_MISSING");
      item.CurrentValue = current.CurrentCanonicalValue;
      return comparison
        ? InternalPass(item)
        : Fail(item, "TARGET_COMPARISON_FAILED");
    }

    private static NativeStage03ChecklistItem EvaluateSystem(
      NativeStage03ChecklistItem item,
      NativeReportingCheckDefinition definition,
      NativeStage03SourceEvidenceBundle evidence)
    {
      NativeStage03TechnicalPreflightEvidence preflight =
        evidence.TechnicalPreflight;
      switch (definition.CheckId ?? string.Empty)
      {
        case "EXPORT.REVIT_DOCUMENT":
          return preflight?.DocumentReady == true
            ? InternalPass(item)
            : Fail(item, "DOCUMENT_UNAVAILABLE");
        case "EXPORT.OUTPUT_DIRECTORY":
          return preflight?.OutputDirectoryWritable == true
            ? InternalPass(item)
            : Fail(item, "OUTPUT_DIRECTORY_NOT_WRITABLE");
        case "EXPORT.RAW_IFC_PIPELINE":
          if (preflight?.RevitIfcExporterAvailable != true)
            return Fail(item, "IFC_EXPORTER_UNAVAILABLE");
          return preflight.TranslatorDependenciesAvailable
            ? InternalPass(item)
            : Fail(item, "TRANSLATOR_DEPENDENCY_UNAVAILABLE");
        case "EXPORT.REPORT_WRITER":
          return preflight?.ReportWriterAvailable == true
            ? InternalPass(item)
            : Fail(item, "REPORT_WRITER_UNAVAILABLE");
        default:
          return InternalPass(item);
      }
    }

    private static string FreshnessCode(
      NativeReportingCheckDefinition definition,
      NativeStage03SourceEvidenceBundle evidence)
    {
      if (definition.CheckKind == NativeReportingCheckKind.System
        && definition.SourceStage == NativeReportingSourceStage.ExportPreparation)
        return string.Empty;
      if (definition.SourceStage == NativeReportingSourceStage.Stage01)
        return Freshness(evidence.Stage01Result, evidence.CurrentIdentity,
          evidence.Stage01CurrentInputSnapshotHash);
      if (definition.SourceStage == NativeReportingSourceStage.Stage02A)
        return Freshness(evidence.Stage02AResult, evidence.CurrentIdentity,
          evidence.Stage02ACurrentInputSnapshotHash);
      if (definition.SourceStage == NativeReportingSourceStage.Stage02B)
        return Freshness(evidence.Stage02BResult, evidence.CurrentIdentity,
          evidence.Stage02BCurrentInputSnapshotHash);
      string first = Freshness(evidence.Stage01Result, evidence.CurrentIdentity,
        evidence.Stage01CurrentInputSnapshotHash);
      if (first.Length > 0) return first;
      string second = Freshness(evidence.Stage02AResult, evidence.CurrentIdentity,
        evidence.Stage02ACurrentInputSnapshotHash);
      if (second.Length > 0) return second;
      return Freshness(evidence.Stage02BResult, evidence.CurrentIdentity,
        evidence.Stage02BCurrentInputSnapshotHash);
    }

    private static string Freshness(
      NativeWorkflowResultEnvelope result,
      NativeWorkflowIdentity identity,
      string inputHash)
    {
      NativeWorkflowFreshnessDecision decision = NativeWorkflowFreshnessPolicy
        .Evaluate(result, identity, inputHash);
      return decision.State == NativeWorkflowFreshnessState.Current
        ? string.Empty
        : decision.Code;
    }

    private static NativeStage02ElementPlan[] ElementsFor(
      NativeReportingCheckDefinition definition,
      NativeStage03SourceEvidenceBundle evidence)
    {
      return Plans(evidence).Where(value =>
          string.Equals(value.TaskGeometry?.TaskId, definition.TaskId,
            StringComparison.Ordinal)
          || !string.IsNullOrWhiteSpace(definition.RoleId)
            && string.Equals(value.EffectiveRoleId.Length > 0
                ? value.EffectiveRoleId
                : value.RoleId,
              definition.RoleId,
              StringComparison.Ordinal)
          || !string.IsNullOrWhiteSpace(definition.PropertyId)
            && (value.Fields ?? Array.Empty<NativeStage02FieldPlan>())
              .Any(field => field?.Property != null
                && string.Equals(field.Property.PropertyId,
                  definition.PropertyId, StringComparison.Ordinal)))
        .ToArray();
    }

    private static NativeStage02ElementPlan[] Plans(
      NativeStage03SourceEvidenceBundle evidence)
    {
      return (evidence.Stage02A?.Elements
          ?? Array.Empty<NativeStage02ElementPlan>())
        .Where(value => value?.Element != null)
        .OrderBy(value => value.Element.UniqueId, StringComparer.Ordinal)
        .ToArray();
    }

    private static bool HasRoleCandidate(
      NativeStage02ElementPlan value,
      string roleId)
    {
      return string.Equals(value.EffectiveRoleId, roleId,
          StringComparison.Ordinal)
        || string.Equals(value.RoleId, roleId, StringComparison.Ordinal)
        || (value.Candidates ?? Array.Empty<NativeStage02SemanticCandidate>())
          .Any(candidate => candidate != null
            && string.Equals(candidate.RoleId, roleId,
              StringComparison.Ordinal));
    }

    private static string GeometryFailureCode(
      NativeReportingCheckKind kind,
      NativeStage02GeometryCheckEvidence check)
    {
      if (check.State == NativeStage02GeometryCheckState.Passed
        || check.State == NativeStage02GeometryCheckState.ManualReviewApproved)
        return string.Empty;
      string code = check.Code ?? string.Empty;
      if (string.Equals(code, "FULL_MODEL_RECHECK_REQUIRED",
        StringComparison.Ordinal)) return code;
      if (string.Equals(code, "MANUAL_REVIEW_REJECTED",
        StringComparison.Ordinal)) return code;
      if (string.Equals(code, "MANUAL_REVIEW_STALE",
        StringComparison.Ordinal)) return code;
      if (check.State == NativeStage02GeometryCheckState.ManualReviewRequired)
        return "MANUAL_REVIEW_REQUIRED";
      return kind == NativeReportingCheckKind.PropertyConsistency
        ? "PROPERTY_CHECK_FAILED"
        : "GEOMETRY_CHECK_FAILED";
    }

    private static bool TryCompare(
      NativePlanningTargetValue target,
      string actualText,
      out bool result)
    {
      result = false;
      if (!decimal.TryParse(target?.Value1, NumberStyles.Float,
          CultureInfo.InvariantCulture, out decimal first)
        || !decimal.TryParse(actualText, NumberStyles.Float,
          CultureInfo.InvariantCulture, out decimal actual)) return false;
      switch ((target.Operator ?? string.Empty).Trim().ToUpperInvariant())
      {
        case "<": result = actual < first; return true;
        case "<=": result = actual <= first; return true;
        case ">": result = actual > first; return true;
        case ">=": result = actual >= first; return true;
        case "=":
        case "==": result = actual == first; return true;
        case "BETWEEN":
          if (!decimal.TryParse(target.Value2, NumberStyles.Float,
            CultureInfo.InvariantCulture, out decimal second)) return false;
          result = actual >= Math.Min(first, second)
            && actual <= Math.Max(first, second);
          return true;
        default: return false;
      }
    }

    private static NativeWorkflowItemEvidence FindWorkflowItem(
      NativeWorkflowResultEnvelope result,
      params string[] identities)
    {
      var expected = new HashSet<string>((identities ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);
      return (result?.Items ?? Array.Empty<NativeWorkflowItemEvidence>())
        .SingleOrDefault(value => value != null
          && expected.Contains(value.Identity));
    }

    private static NativeStage03ChecklistItem SetElements(
      NativeStage03ChecklistItem item,
      IEnumerable<NativeStage02ElementPlan> plans)
    {
      NativeIssueElementReference[] elements = (plans
          ?? Array.Empty<NativeStage02ElementPlan>())
        .Where(value => value?.Element != null)
        .GroupBy(value => value.Element.UniqueId, StringComparer.Ordinal)
        .Select(group => group.First().Element)
        .OrderBy(value => value.UniqueId, StringComparer.Ordinal)
        .Select(value => new NativeIssueElementReference
        {
          ElementId = value.ElementId,
          UniqueId = value.UniqueId,
          ElementName = value.ElementName,
          CategoryName = value.CategoryName
        }).ToArray();
      item.Elements = new ReadOnlyCollection<NativeIssueElementReference>(
        elements);
      if (elements.Length == 1)
      {
        item.ElementId = elements[0].ElementId;
        item.ElementUniqueId = elements[0].UniqueId;
      }
      else
      {
        item.ElementId = null;
        item.ElementUniqueId = string.Empty;
      }
      return item;
    }

    private static NativeStage03ChecklistItem Create(
      NativeReportingCheckDefinition definition)
    {
      return new NativeStage03ChecklistItem
      {
        CheckId = definition.CheckId ?? string.Empty,
        DisplayName = definition.DisplayName ?? string.Empty,
        SourceStage = definition.SourceStage,
        CheckKind = definition.CheckKind,
        ApplicableBasis = definition.ApplicableBasis ?? string.Empty,
        Unit = definition.Unit ?? string.Empty,
        Status = NativeStage03ChecklistStatus.NotChecked,
        RemediationTarget = definition.RemediationTarget ?? string.Empty,
        FieldKey = definition.FieldKey ?? string.Empty,
        PropertyId = definition.PropertyId ?? string.Empty,
        RoleId = definition.RoleId ?? string.Empty,
        RuleText = definition.RuleText ?? string.Empty,
        TargetKey = definition.TargetKey ?? string.Empty,
        OfficialCarrierStatus = definition.OfficialCarrierStatus,
        OfficialProjectionCarrierId =
          definition.OfficialProjectionCarrierId ?? string.Empty,
        OfficialCarrierProbeRef =
          definition.OfficialCarrierProbeRef ?? string.Empty,
        OfficialEvidenceRef = definition.OfficialEvidenceRef ?? string.Empty
      };
    }

    private static NativeStage03ChecklistItem InternalPass(
      NativeStage03ChecklistItem item)
    {
      item.InternalValidationPassed = true;
      if (item.OfficialCarrierStatus
        == NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt)
        return Warning(item, "INTERNAL_PASS_OFFICIAL_PENDING");
      item.OfficialAcceptancePassed = item.OfficialCarrierStatus
        == NativeOfficialCarrierEvidenceStatus.Verified;
      item.Status = NativeStage03ChecklistStatus.Passed;
      return item;
    }

    private static NativeStage03ChecklistItem Fail(
      NativeStage03ChecklistItem item,
      string code)
    {
      item.Status = NativeStage03ChecklistStatus.Failed;
      item.IssueCode = code ?? string.Empty;
      item.IssueMessage = item.IssueCode;
      item.InternalValidationPassed = false;
      item.OfficialAcceptancePassed = false;
      return item;
    }

    private static NativeStage03ChecklistItem Warning(
      NativeStage03ChecklistItem item,
      string code)
    {
      item.Status = NativeStage03ChecklistStatus.Warning;
      item.IssueCode = code ?? string.Empty;
      item.IssueMessage = item.IssueCode;
      item.OfficialAcceptancePassed = false;
      return item;
    }
  }
}
