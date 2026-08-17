using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal sealed class NativeStage02BResolvedProjectionCarrier
  {
    internal Element Element { get; set; }
    internal Guid ParameterGuid { get; set; }
    internal string BindingScope { get; set; } = string.Empty;
    internal string CarrierId { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02SemanticAssignmentSnapshot
  {
    internal bool Current { get; set; }
    internal string CurrentDocumentFingerprint { get; set; } = string.Empty;
    internal string AssignmentDocumentFingerprint { get; set; } = string.Empty;
    internal IReadOnlyList<NativeStage02SemanticAssignmentRecord> Assignments
    {
      get;
      set;
    } = Array.Empty<NativeStage02SemanticAssignmentRecord>();
  }

  internal sealed class NativeStage02BProjectionCarrierCandidate
  {
    internal string UniqueId { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;
    internal string CategoryBuiltInId { get; set; } = string.Empty;
    internal string ElementClass { get; set; } = string.Empty;
    internal Element Element { get; set; }
  }

  internal sealed class NativeStage02BProjectionCarrierDecision
  {
    internal bool Accepted { get; set; }
    internal string ErrorCode { get; set; } = string.Empty;
    internal string UniqueId { get; set; } = string.Empty;
    internal NativeStage02BProjectionCarrierCandidate Candidate { get; set; }
  }

  internal static class NativeStage02BProjectionCarrierResolver
  {
    internal static NativeStage02BResolvedProjectionCarrier Resolve(
      Document document,
      NativeOfficialProjectionCarrierDefinition definition,
      NativeStage02SemanticAssignmentSnapshot assignments)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      var candidates = new List<NativeStage02BProjectionCarrierCandidate>();
      if (string.Equals(definition?.SelectorKind,
        "PROJECT_INFORMATION", StringComparison.Ordinal))
      {
        ProjectInfo projectInformation = document.ProjectInformation;
        if (projectInformation != null)
        {
          candidates.Add(ToCandidate(
            projectInformation,
            "PROJECT_INFORMATION",
            string.Empty));
        }
      }
      else if (string.Equals(definition?.SelectorKind,
        "CONFIRMED_SEMANTIC_ROLE", StringComparison.Ordinal))
      {
        foreach (NativeStage02SemanticAssignmentRecord assignment in
          assignments?.Assignments ?? Array.Empty<NativeStage02SemanticAssignmentRecord>())
        {
          if (assignment == null || !string.Equals(
            assignment.RoleId, definition.RoleId, StringComparison.Ordinal))
            continue;
          Element element = document.GetElement(assignment.ElementUniqueId);
          if (element == null) continue;
          candidates.Add(ToCandidate(
            element,
            assignment.ElementUniqueId,
            assignment.RoleId));
        }
      }

      NativeStage02BProjectionCarrierDecision decision = Decide(
        definition, assignments, candidates);
      if (!decision.Accepted)
        throw new InvalidOperationException(decision.ErrorCode);
      return new NativeStage02BResolvedProjectionCarrier
      {
        Element = decision.Candidate.Element,
        ParameterGuid = Guid.Parse(definition.ParameterGuid),
        BindingScope = definition.BindingScope,
        CarrierId = definition.CarrierId
      };
    }

    internal static NativeStage02BProjectionCarrierDecision Decide(
      NativeOfficialProjectionCarrierDefinition definition,
      NativeStage02SemanticAssignmentSnapshot assignments,
      IEnumerable<NativeStage02BProjectionCarrierCandidate> candidates)
    {
      string contractError = ValidateContract(definition);
      if (contractError.Length > 0) return Reject(contractError);
      NativeStage02BProjectionCarrierCandidate[] all = (candidates
          ?? Array.Empty<NativeStage02BProjectionCarrierCandidate>())
        .Where(value => value != null).ToArray();

      if (string.Equals(definition.SelectorKind,
        "PROJECT_INFORMATION", StringComparison.Ordinal))
      {
        NativeStage02BProjectionCarrierCandidate[] project = all
          .Where(value => string.Equals(value.UniqueId,
              "PROJECT_INFORMATION", StringComparison.Ordinal)
            && string.Equals(value.CategoryBuiltInId,
              definition.CategoryBuiltInId, StringComparison.Ordinal))
          .ToArray();
        return SelectAndValidate(definition, project);
      }

      if (assignments == null || !assignments.Current
        || string.IsNullOrWhiteSpace(assignments.CurrentDocumentFingerprint)
        || !string.Equals(
          assignments.CurrentDocumentFingerprint,
          assignments.AssignmentDocumentFingerprint,
          StringComparison.Ordinal))
        return Reject("OFFICIAL_CARRIER_NOT_FOUND");

      var assignedIds = new HashSet<string>((assignments.Assignments
          ?? Array.Empty<NativeStage02SemanticAssignmentRecord>())
        .Where(value => value != null && string.Equals(
          value.RoleId, definition.RoleId, StringComparison.Ordinal))
        .Select(value => value.ElementUniqueId), StringComparer.Ordinal);
      NativeStage02BProjectionCarrierCandidate[] roleCandidates = all
        .Where(value => string.Equals(value.RoleId,
            definition.RoleId, StringComparison.Ordinal)
          && assignedIds.Contains(value.UniqueId))
        .ToArray();
      return SelectAndValidate(definition, roleCandidates);
    }

    private static NativeStage02BProjectionCarrierDecision SelectAndValidate(
      NativeOfficialProjectionCarrierDefinition definition,
      NativeStage02BProjectionCarrierCandidate[] candidates)
    {
      if (candidates.Length == 0) return Reject("OFFICIAL_CARRIER_NOT_FOUND");
      if (candidates.Length > 1) return Reject("OFFICIAL_CARRIER_AMBIGUOUS");
      NativeStage02BProjectionCarrierCandidate candidate = candidates[0];
      if (!string.Equals(candidate.CategoryBuiltInId,
          definition.CategoryBuiltInId, StringComparison.Ordinal)
        || !string.Equals(candidate.ElementClass,
          definition.ElementClass, StringComparison.Ordinal))
        return Reject("OFFICIAL_CARRIER_TYPE_MISMATCH");
      return new NativeStage02BProjectionCarrierDecision
      {
        Accepted = true,
        UniqueId = candidate.UniqueId,
        Candidate = candidate
      };
    }

    private static string ValidateContract(
      NativeOfficialProjectionCarrierDefinition definition)
    {
      if (definition == null
        || string.IsNullOrWhiteSpace(definition.CarrierId)
        || string.IsNullOrWhiteSpace(definition.PropertyId)
        || string.IsNullOrWhiteSpace(definition.ParameterGuid)
        || !string.Equals(definition.BindingScope, "INSTANCE",
          StringComparison.Ordinal)
        || !Guid.TryParse(definition.PropertyId, out Guid propertyGuid)
        || !Guid.TryParse(definition.ParameterGuid, out Guid parameterGuid)
        || propertyGuid != parameterGuid)
        return "OFFICIAL_CARRIER_CONTRACT_MISMATCH";
      if (string.Equals(definition.SelectorKind,
        "PROJECT_INFORMATION", StringComparison.Ordinal))
      {
        return string.Equals(definition.CategoryBuiltInId,
            "OST_ProjectInformation", StringComparison.Ordinal)
          && string.Equals(definition.ElementClass,
            "Autodesk.Revit.DB.ProjectInfo", StringComparison.Ordinal)
          ? string.Empty : "OFFICIAL_CARRIER_CONTRACT_MISMATCH";
      }
      if (string.Equals(definition.SelectorKind,
        "CONFIRMED_SEMANTIC_ROLE", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(definition.RoleId)
        && !string.IsNullOrWhiteSpace(definition.CategoryBuiltInId)
        && !string.IsNullOrWhiteSpace(definition.ElementClass))
        return string.Empty;
      return "OFFICIAL_CARRIER_CONTRACT_MISMATCH";
    }

    private static NativeStage02BProjectionCarrierCandidate ToCandidate(
      Element element,
      string uniqueId,
      string roleId)
    {
      return new NativeStage02BProjectionCarrierCandidate
      {
        UniqueId = uniqueId ?? string.Empty,
        RoleId = roleId ?? string.Empty,
        CategoryBuiltInId = CategoryKey(element?.Category),
        ElementClass = element?.GetType().FullName ?? string.Empty,
        Element = element
      };
    }

    private static string CategoryKey(Category category)
    {
      if (category == null) return string.Empty;
      int id = category.Id.IntegerValue;
      return Enum.IsDefined(typeof(BuiltInCategory), id)
        ? ((BuiltInCategory)id).ToString()
        : string.Empty;
    }

    private static NativeStage02BProjectionCarrierDecision Reject(string code)
    {
      return new NativeStage02BProjectionCarrierDecision
      {
        ErrorCode = code ?? string.Empty
      };
    }
  }
}
