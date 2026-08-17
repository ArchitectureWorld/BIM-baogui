using System;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02RoleConfirmationPolicy
  {
    internal static NativeStage02RoleConfirmationDecision Resolve(
      NativeStage02ElementSnapshot element,
      NativeStage02SemanticCandidate candidate,
      NativeStage02SemanticAssignmentRecord persisted,
      NativeStage02RoleConfirmation explicitConfirmation,
      NativeWorkflowIdentity identity,
      string currentElementSnapshotHash)
    {
      if (element == null) throw new ArgumentNullException(nameof(element));
      if (identity == null) throw new ArgumentNullException(nameof(identity));
      string candidateRole = Clean(candidate?.RoleId);
      if (explicitConfirmation != null)
      {
        if (IsCurrent(
          explicitConfirmation,
          element,
          candidateRole,
          identity,
          currentElementSnapshotHash))
        {
          return Confirmed(explicitConfirmation, "ExplicitConfirmation");
        }
        return Blocked("ROLE_CONFIRMATION_STALE", candidateRole);
      }

      if (persisted != null)
      {
        var saved = new NativeStage02RoleConfirmation
        {
          ElementUniqueId = persisted.ElementUniqueId,
          RoleId = persisted.RoleId,
          ElementSnapshotHash = persisted.ElementSnapshotHash,
          RulePackageSha256 = persisted.RulePackageSha256,
          ConfirmedUtc = persisted.ConfirmedUtc
        };
        if (IsCurrent(
          saved,
          element,
          candidateRole,
          identity,
          currentElementSnapshotHash))
          return Confirmed(saved, "PersistedConfirmation");
      }

      return Blocked("ROLE_CONFIRMATION_REQUIRED", candidateRole);
    }

    private static bool IsCurrent(
      NativeStage02RoleConfirmation confirmation,
      NativeStage02ElementSnapshot element,
      string candidateRole,
      NativeWorkflowIdentity identity,
      string snapshotHash)
    {
      return Clean(confirmation.ElementUniqueId) == Clean(element.UniqueId)
        && Clean(confirmation.RoleId).Length > 0
        && (candidateRole.Length == 0
          || Clean(confirmation.RoleId) == candidateRole)
        && Clean(element.DocumentFingerprint) == Clean(identity.DocumentFingerprint)
        && Clean(confirmation.ElementSnapshotHash) == Clean(snapshotHash)
        && Clean(snapshotHash).Length > 0
        && Clean(confirmation.RulePackageSha256)
          == Clean(identity.RulePackageSha256)
        && Clean(identity.RulePackageSha256).Length == 64;
    }

    private static NativeStage02RoleConfirmationDecision Confirmed(
      NativeStage02RoleConfirmation confirmation,
      string source)
    {
      return new NativeStage02RoleConfirmationDecision
      {
        Confirmed = true,
        Code = "ROLE_CONFIRMED",
        ResolvedRoleId = Clean(confirmation.RoleId),
        Source = source,
        Confirmation = confirmation.Clone()
      };
    }

    private static NativeStage02RoleConfirmationDecision Blocked(
      string code,
      string roleId)
    {
      return new NativeStage02RoleConfirmationDecision
      {
        Confirmed = false,
        Code = code,
        ResolvedRoleId = Clean(roleId)
      };
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }
}
