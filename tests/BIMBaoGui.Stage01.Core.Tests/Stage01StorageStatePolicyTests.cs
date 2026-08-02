using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01StorageStatePolicyTests
  {
    [Fact]
    public void Evaluate_NoRecordRequiresFirstInitializationGates()
    {
      Stage01StorageDecision decision = Stage01StorageStatePolicy.Evaluate(
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        HBRContextVersions.FileContextSchema);

      Assert.Equal(Stage01StorageState.NoRecord, decision.State);
      Assert.False(decision.IsInitialized);
      Assert.Equal(Stage01ValidationMode.FirstInitialization, decision.ValidationMode);
      Assert.True(decision.RequiresBlankConfirmation);
      Assert.True(decision.RequiresBlankModelGate);
      Assert.False(decision.RequiresWorkflowMigration);
      Assert.False(decision.RequiresReinitializePermission);
    }

    [Fact]
    public void Evaluate_CompleteCurrentRecordIsValidInitialization()
    {
      Stage01StorageDecision decision = EvaluateComplete(HBRContextVersions.FileContextSchema);

      Assert.Equal(Stage01StorageState.ValidInitialization, decision.State);
      Assert.True(decision.IsInitialized);
      Assert.Equal(Stage01ValidationMode.ExistingInitialization, decision.ValidationMode);
      Assert.False(decision.RequiresBlankConfirmation);
      Assert.False(decision.RequiresBlankModelGate);
      Assert.False(decision.RequiresWorkflowMigration);
      Assert.True(decision.RequiresReinitializePermission);
    }

    [Theory]
    [InlineData("PayloadJson")]
    [InlineData("PayloadHash")]
    [InlineData("FileGuid")]
    [InlineData("WorkflowVersion")]
    public void Evaluate_RecordMissingRequiredIdentityFieldIsCorrupt(string missingField)
    {
      string payloadJson = missingField == "PayloadJson" ? " " : "{\"values\":{}}";
      string payloadHash = missingField == "PayloadHash" ? " " : "ABC123";
      string fileGuid = missingField == "FileGuid" ? " " : "11111111-1111-1111-1111-111111111111";
      string workflowVersion = missingField == "WorkflowVersion"
        ? " "
        : HBRContextVersions.FileContextSchema;

      Stage01StorageDecision decision = Stage01StorageStatePolicy.Evaluate(
        true,
        payloadJson,
        payloadHash,
        fileGuid,
        workflowVersion,
        HBRContextVersions.FileContextSchema);

      Assert.Equal(Stage01StorageState.CorruptInitialization, decision.State);
      Assert.False(decision.IsInitialized);
      Assert.Equal(Stage01ValidationMode.ExistingInitialization, decision.ValidationMode);
      Assert.False(decision.RequiresBlankConfirmation);
      Assert.False(decision.RequiresBlankModelGate);
      Assert.False(decision.RequiresWorkflowMigration);
      Assert.False(decision.RequiresReinitializePermission);
    }

    [Fact]
    public void Evaluate_ValidOlderRecordRequiresWorkflowMigrationWithoutOverwritePermission()
    {
      Stage01StorageDecision decision = EvaluateComplete("0.8.2");

      Assert.Equal(Stage01StorageState.ValidInitialization, decision.State);
      Assert.True(decision.IsInitialized);
      Assert.True(decision.RequiresWorkflowMigration);
      Assert.False(decision.RequiresReinitializePermission);
    }

    [Fact]
    public void Evaluate_CorruptOlderRecordIsNotMigration()
    {
      Stage01StorageDecision decision = Stage01StorageStatePolicy.Evaluate(
        true,
        string.Empty,
        "ABC123",
        "11111111-1111-1111-1111-111111111111",
        "0.8.2",
        HBRContextVersions.FileContextSchema);

      Assert.Equal(Stage01StorageState.CorruptInitialization, decision.State);
      Assert.False(decision.RequiresWorkflowMigration);
      Assert.False(decision.RequiresReinitializePermission);
    }

    private static Stage01StorageDecision EvaluateComplete(string workflowVersion)
    {
      return Stage01StorageStatePolicy.Evaluate(
        true,
        "{\"values\":{}}",
        "ABC123",
        "11111111-1111-1111-1111-111111111111",
        workflowVersion,
        HBRContextVersions.FileContextSchema);
    }
  }
}
