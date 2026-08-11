using System;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01StoragePolicyTests
  {
    private const string FileGuid =
      "11111111-2222-4333-8444-555555555555";

    [Fact]
    public void MissingRecordIsAFirstInitialization()
    {
      NativeStage01StorageDecision decision =
        NativeStage01StoragePolicy.Evaluate(
          null,
          NativeStage01Canonicalizer.PayloadSchemaVersion);

      Assert.Equal(NativeStage01StorageState.NoRecord, decision.State);
      Assert.False(decision.IsInitialized);
      Assert.False(decision.RequiresMigration);
      Assert.True(decision.RequiresBlankModelGate);
    }

    [Fact]
    public void AcceptsCurrentCanonicalRecordWithMatchingHashAndIdentity()
    {
      NativeStoredInitialization record = CreateRecord("0.9.0");

      NativeStage01StorageDecision decision =
        NativeStage01StoragePolicy.Evaluate(record, "0.9.0");

      Assert.Equal(NativeStage01StorageState.Current, decision.State);
      Assert.True(decision.IsInitialized);
      Assert.False(decision.RequiresMigration);
      Assert.Equal(FileGuid, decision.Payload.Model.GetValue(
        NativeStage01Keys.FileGuid));
    }

    [Fact]
    public void AcceptsHashVerifiedOlderPayloadAsMigratable()
    {
      NativeStoredInitialization record = CreateRecord("0.8.0");

      NativeStage01StorageDecision decision =
        NativeStage01StoragePolicy.Evaluate(record, "0.9.0");

      Assert.Equal(
        NativeStage01StorageState.MigratableLegacy,
        decision.State);
      Assert.True(decision.IsInitialized);
      Assert.True(decision.RequiresMigration);
      Assert.False(decision.RequiresReinitializePermission);
    }

    [Fact]
    public void RejectsIncompleteHashMismatchAndNonCanonicalCurrentRecords()
    {
      NativeStoredInitialization incomplete = CreateRecord("0.9.0");
      incomplete.FileGuid = string.Empty;
      Assert.Equal(
        NativeStage01StorageState.Corrupt,
        NativeStage01StoragePolicy.Evaluate(
          incomplete,
          "0.9.0").State);

      NativeStoredInitialization mismatch = CreateRecord("0.9.0");
      mismatch.PayloadHash = new string('0', 64);
      NativeStage01StorageDecision mismatchDecision =
        NativeStage01StoragePolicy.Evaluate(mismatch, "0.9.0");
      Assert.Equal(NativeStage01StorageState.Corrupt, mismatchDecision.State);
      Assert.Equal(
        NativeStage01StorageCodes.PayloadHashMismatch,
        mismatchDecision.ErrorCode);

      NativeStoredInitialization nonCanonical = CreateRecord("0.9.0");
      nonCanonical.PayloadJson += Environment.NewLine;
      nonCanonical.PayloadHash = NativeStage01Canonicalizer.Sha256(
        nonCanonical.PayloadJson);
      NativeStage01StorageDecision canonicalDecision =
        NativeStage01StoragePolicy.Evaluate(nonCanonical, "0.9.0");
      Assert.Equal(NativeStage01StorageState.Corrupt, canonicalDecision.State);
      Assert.Equal(
        NativeStage01StorageCodes.NonCanonicalCurrentPayload,
        canonicalDecision.ErrorCode);
    }

    [Fact]
    public void RejectsFileGuidAndWorkflowVersionDrift()
    {
      NativeStoredInitialization fileGuidMismatch = CreateRecord("0.9.0");
      fileGuidMismatch.FileGuid =
        "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
      NativeStage01StorageDecision fileDecision =
        NativeStage01StoragePolicy.Evaluate(fileGuidMismatch, "0.9.0");
      Assert.Equal(NativeStage01StorageState.Corrupt, fileDecision.State);
      Assert.Equal(
        NativeStage01StorageCodes.FileGuidMismatch,
        fileDecision.ErrorCode);

      NativeStoredInitialization workflowMismatch = CreateRecord("0.8.0");
      workflowMismatch.WorkflowVersion = "0.7.0";
      NativeStage01StorageDecision workflowDecision =
        NativeStage01StoragePolicy.Evaluate(workflowMismatch, "0.9.0");
      Assert.Equal(NativeStage01StorageState.Corrupt, workflowDecision.State);
      Assert.Equal(
        NativeStage01StorageCodes.WorkflowVersionMismatch,
        workflowDecision.ErrorCode);
    }

    [Fact]
    public void RejectsFutureWorkflowVersionFailClosed()
    {
      NativeStoredInitialization record = CreateRecord("9.0.0");

      NativeStage01StorageDecision decision =
        NativeStage01StoragePolicy.Evaluate(record, "0.9.0");

      Assert.Equal(
        NativeStage01StorageState.UnsupportedFuture,
        decision.State);
      Assert.False(decision.IsInitialized);
      Assert.Equal(
        NativeStage01StorageCodes.UnsupportedFutureVersion,
        decision.ErrorCode);
    }

    private static NativeStoredInitialization CreateRecord(string version)
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();
      model.SetValue(NativeStage01Keys.FileGuid, FileGuid);
      model.SetValue(NativeStage01Keys.WorkflowVersion, version);
      string payload = NativeStage01Canonicalizer.ToJson(model);
      if (!string.Equals(
        version,
        NativeStage01Canonicalizer.PayloadSchemaVersion,
        StringComparison.Ordinal))
      {
        payload = payload.Replace(
          "\"schemaVersion\":\""
            + NativeStage01Canonicalizer.PayloadSchemaVersion
            + "\"",
          "\"schemaVersion\":\"" + version + "\"");
      }
      return new NativeStoredInitialization
      {
        HasRecord = true,
        PayloadJson = payload,
        PayloadHash = NativeStage01Canonicalizer.Sha256(payload),
        FileGuid = FileGuid,
        WorkflowVersion = version,
        InitializedUtc = "2026-08-11T00:00:00.0000000Z"
      };
    }
  }
}
