using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01StorageStatePolicyTests
  {
    private const string StoredFileGuid =
      "11111111-1111-1111-1111-111111111111";
    private const string LegacyBaseX =
      "IfcProject|Pset_申报信息属性集|基点坐标 X";
    private const string LegacyBaseY =
      "IfcProject|Pset_申报信息属性集|基点坐标 Y";

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
      string payload = BuildCurrentPayload();
      Stage01StorageDecision decision = EvaluatePayload(
        payload,
        CanonicalPayload.Sha256(payload),
        HBRContextVersions.FileContextSchema);

      Assert.Equal(Stage01StorageState.ValidInitialization, decision.State);
      Assert.True(decision.IsInitialized);
      Assert.Equal(Stage01ValidationMode.ExistingInitialization, decision.ValidationMode);
      Assert.False(decision.RequiresBlankConfirmation);
      Assert.False(decision.RequiresBlankModelGate);
      Assert.False(decision.RequiresWorkflowMigration);
      Assert.True(decision.RequiresReinitializePermission);
    }

    [Fact]
    public void Evaluate_CurrentCanonicalPayloadWithWrongHashIsCorrupt()
    {
      string payload = BuildCurrentPayload();

      Stage01StorageDecision decision = EvaluatePayload(
        payload,
        CanonicalPayload.Sha256(payload + "tampered"),
        HBRContextVersions.FileContextSchema);

      Assert.Equal(Stage01StorageState.CorruptInitialization, decision.State);
      Assert.False(decision.IsInitialized);
      Assert.False(decision.RequiresWorkflowMigration);
      Assert.False(decision.RequiresReinitializePermission);
    }

    [Fact]
    public void Evaluate_CurrentNoncanonicalPayloadWithMatchingSelfHashIsCorrupt()
    {
      string noncanonicalPayload = BuildCurrentPayload() + " ";

      Stage01StorageDecision decision = EvaluatePayload(
        noncanonicalPayload,
        CanonicalPayload.Sha256(noncanonicalPayload),
        HBRContextVersions.FileContextSchema);

      Assert.Equal(Stage01StorageState.CorruptInitialization, decision.State);
      Assert.False(decision.IsInitialized);
      Assert.False(decision.RequiresWorkflowMigration);
      Assert.False(decision.RequiresReinitializePermission);
    }

    [Theory]
    [InlineData("PayloadJson")]
    [InlineData("PayloadHash")]
    [InlineData("FileGuid")]
    [InlineData("WorkflowVersion")]
    public void Evaluate_RecordMissingRequiredIdentityFieldIsCorrupt(string missingField)
    {
      string validPayload = BuildCurrentPayload();
      string payloadJson = missingField == "PayloadJson" ? " " : validPayload;
      string payloadHash = missingField == "PayloadHash"
        ? " "
        : CanonicalPayload.Sha256(validPayload);
      string fileGuid = missingField == "FileGuid" ? " " : StoredFileGuid;
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
      const string oldPayload =
        "{\"schemaVersion\":\"0.8.2\",\"workflowVersion\":\"0.8.2\"," +
        "\"values\":{\"HBR|Workflow|Version\":\"0.8.2\"}," +
        "\"planningTargets\":{},\"conditions\":{},\"organizations\":[{}]}";
      Stage01StorageDecision decision = EvaluatePayload(
        oldPayload,
        CanonicalPayload.Sha256(oldPayload),
        "0.8.2");

      Assert.Equal(Stage01StorageState.ValidInitialization, decision.State);
      Assert.True(decision.IsInitialized);
      Assert.True(decision.RequiresWorkflowMigration);
      Assert.False(decision.RequiresReinitializePermission);
    }

    [Fact]
    public void Evaluate_LegacySpacedCoordinatesWithOriginalHashRemainValidWithoutMutation()
    {
      var model = new Stage01Model();
      model.SetValue(Stage01Keys.WorkflowVersion, HBRContextVersions.FileContextSchema);
      model.SetValue(LegacyBaseX, "3353559.52");
      model.SetValue(LegacyBaseY, "38345264.397");
      string payload = CanonicalPayload.Build(model);

      Stage01StorageDecision decision = EvaluatePayload(
        payload,
        CanonicalPayload.Sha256(payload),
        HBRContextVersions.FileContextSchema);

      Assert.Equal(Stage01StorageState.ValidInitialization, decision.State);
      Assert.True(decision.IsInitialized);
      Assert.Equal(payload, CanonicalPayload.Build(model));
      Assert.Contains(LegacyBaseX, model.Values.Keys);
      Assert.Contains(LegacyBaseY, model.Values.Keys);
      Assert.DoesNotContain(Stage01Keys.BaseX, model.Values.Keys);
      Assert.DoesNotContain(Stage01Keys.BaseY, model.Values.Keys);
    }

    [Fact]
    public void Evaluate_CorruptOlderRecordIsNotMigration()
    {
      Stage01StorageDecision decision = Stage01StorageStatePolicy.Evaluate(
        true,
        string.Empty,
        new string('0', 64),
        StoredFileGuid,
        "0.8.2",
        HBRContextVersions.FileContextSchema);

      Assert.Equal(Stage01StorageState.CorruptInitialization, decision.State);
      Assert.False(decision.RequiresWorkflowMigration);
      Assert.False(decision.RequiresReinitializePermission);
    }

    private static string BuildCurrentPayload()
    {
      var model = new Stage01Model();
      model.SetValue(Stage01Keys.WorkflowVersion, HBRContextVersions.FileContextSchema);
      model.SetValue("test-key", "safe-value");
      return CanonicalPayload.Build(model);
    }

    private static Stage01StorageDecision EvaluatePayload(
      string payload,
      string payloadHash,
      string workflowVersion)
    {
      return Stage01StorageStatePolicy.Evaluate(
        true,
        payload,
        payloadHash,
        StoredFileGuid,
        workflowVersion,
        HBRContextVersions.FileContextSchema);
    }
  }
}
