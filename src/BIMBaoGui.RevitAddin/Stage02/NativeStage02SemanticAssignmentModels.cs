using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal enum NativeStage02IdentificationMode
  {
    Automatic,
    Manual
  }

  internal enum NativeStage02AssignmentMode
  {
    Auto,
    Manual
  }

  internal sealed class NativeStage02SemanticCandidate
  {
    internal string RoleId { get; set; } = string.Empty;
    internal string Confidence { get; set; } = string.Empty;
    internal IReadOnlyList<string> Evidence { get; set; } =
      Array.Empty<string>();
  }

  internal sealed class NativeStage02RoleConfirmation
  {
    internal string ElementUniqueId { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;
    internal string ElementSnapshotHash { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
    internal string ConfirmedUtc { get; set; } = string.Empty;

    internal NativeStage02RoleConfirmation Clone()
    {
      return new NativeStage02RoleConfirmation
      {
        ElementUniqueId = ElementUniqueId ?? string.Empty,
        RoleId = RoleId ?? string.Empty,
        ElementSnapshotHash = ElementSnapshotHash ?? string.Empty,
        RulePackageSha256 = RulePackageSha256 ?? string.Empty,
        ConfirmedUtc = ConfirmedUtc ?? string.Empty
      };
    }
  }

  internal sealed class NativeStage02RoleOverride
  {
    internal string ElementUniqueId { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;

    internal NativeStage02RoleOverride Clone()
    {
      return new NativeStage02RoleOverride
      {
        ElementUniqueId = ElementUniqueId ?? string.Empty,
        RoleId = RoleId ?? string.Empty
      };
    }
  }

  internal sealed class NativeStage02ResolvedAssignment
  {
    internal string ElementUniqueId { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;
    internal NativeStage02AssignmentMode AssignmentMode { get; set; }
    internal string Source { get; set; } = string.Empty;
  }

  internal static class NativeStage02RoleAssignmentCodes
  {
    internal const string ScopeInputConflict =
      "ROLE_ASSIGNMENT_SCOPE_CONFLICT";
    internal const string AutomaticModeInputConflict =
      "ROLE_ASSIGNMENT_AUTO_INPUT_CONFLICT";
    internal const string ManualRoleRequired =
      "ROLE_ASSIGNMENT_MANUAL_ROLE_REQUIRED";
    internal const string RoleAssignmentConflict =
      "ROLE_ASSIGNMENT_CONFLICT";
    internal const string OverrideElementNotSelected =
      "ROLE_OVERRIDE_ELEMENT_NOT_SELECTED";
    internal const string RoleIdRequired =
      "ROLE_ASSIGNMENT_ROLE_ID_REQUIRED";
  }

  internal sealed class NativeStage02RoleAssignmentDecision
  {
    private NativeStage02RoleAssignmentDecision(
      bool accepted,
      string errorCode,
      string message,
      IEnumerable<string> selectedUniqueIds,
      IEnumerable<NativeStage02ResolvedAssignment> assignments)
    {
      Accepted = accepted;
      ErrorCode = errorCode ?? string.Empty;
      Message = message ?? string.Empty;
      SelectedUniqueIds = new ReadOnlyCollection<string>(
        (selectedUniqueIds ?? Array.Empty<string>()).ToArray());
      Assignments = new ReadOnlyCollection<NativeStage02ResolvedAssignment>(
        (assignments ?? Array.Empty<NativeStage02ResolvedAssignment>())
          .ToArray());
    }

    internal bool Accepted { get; }
    internal string ErrorCode { get; }
    internal string Message { get; }
    internal IReadOnlyList<string> SelectedUniqueIds { get; }
    internal IReadOnlyList<NativeStage02ResolvedAssignment> Assignments
    {
      get;
    }

    internal static NativeStage02RoleAssignmentDecision Success(
      IEnumerable<string> selectedUniqueIds,
      IEnumerable<NativeStage02ResolvedAssignment> assignments)
    {
      return new NativeStage02RoleAssignmentDecision(
        true,
        string.Empty,
        string.Empty,
        selectedUniqueIds,
        assignments);
    }

    internal static NativeStage02RoleAssignmentDecision Failure(
      string errorCode,
      string message,
      IEnumerable<string> selectedUniqueIds)
    {
      return new NativeStage02RoleAssignmentDecision(
        false,
        errorCode,
        message,
        selectedUniqueIds,
        Array.Empty<NativeStage02ResolvedAssignment>());
    }
  }
}
