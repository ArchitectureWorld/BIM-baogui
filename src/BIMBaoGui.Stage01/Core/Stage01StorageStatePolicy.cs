using System;

namespace BIMBaoGui.Stage01.Core
{
  internal enum Stage01StorageState
  {
    NoRecord,
    ValidInitialization,
    CorruptInitialization
  }

  internal sealed class Stage01StorageDecision
  {
    public static readonly Stage01StorageDecision NoRecord = new Stage01StorageDecision(
      Stage01StorageState.NoRecord,
      false);

    public Stage01StorageDecision(
      Stage01StorageState state,
      bool requiresWorkflowMigration)
    {
      State = state;
      RequiresWorkflowMigration = requiresWorkflowMigration;
    }

    public Stage01StorageState State { get; }
    public Stage01ValidationMode ValidationMode =>
      State == Stage01StorageState.NoRecord
        ? Stage01ValidationMode.FirstInitialization
        : Stage01ValidationMode.ExistingInitialization;
    public bool IsInitialized => State == Stage01StorageState.ValidInitialization;
    public bool RequiresBlankConfirmation => State == Stage01StorageState.NoRecord;
    public bool RequiresBlankModelGate => State == Stage01StorageState.NoRecord;
    public bool RequiresWorkflowMigration { get; }
    public bool RequiresReinitializePermission => IsInitialized && !RequiresWorkflowMigration;
  }

  internal static class Stage01StorageStatePolicy
  {
    public static Stage01StorageDecision Evaluate(
      bool hasRecord,
      string payloadJson,
      string payloadHash,
      string fileGuid,
      string workflowVersion,
      string currentWorkflowVersion)
    {
      if (!hasRecord)
        return Stage01StorageDecision.NoRecord;

      if (string.IsNullOrWhiteSpace(payloadJson)
        || string.IsNullOrWhiteSpace(payloadHash)
        || string.IsNullOrWhiteSpace(fileGuid)
        || string.IsNullOrWhiteSpace(workflowVersion))
      {
        return new Stage01StorageDecision(
          Stage01StorageState.CorruptInitialization,
          false);
      }

      bool requiresWorkflowMigration = !string.Equals(
        workflowVersion,
        currentWorkflowVersion ?? string.Empty,
        StringComparison.Ordinal);
      if (requiresWorkflowMigration)
      {
        return new Stage01StorageDecision(
          Stage01StorageState.ValidInitialization,
          true);
      }

      Stage01StoredPayloadIntegrityDecision payloadIntegrity =
        Stage01StoredPayloadIntegrityPolicy.Evaluate(
          payloadJson,
          payloadHash);
      if (!payloadIntegrity.Success)
      {
        return new Stage01StorageDecision(
          Stage01StorageState.CorruptInitialization,
          false);
      }

      return new Stage01StorageDecision(
        Stage01StorageState.ValidInitialization,
        false);
    }
  }
}
