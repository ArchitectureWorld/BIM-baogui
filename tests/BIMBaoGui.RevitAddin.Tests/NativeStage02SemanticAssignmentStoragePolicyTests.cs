using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02SemanticAssignmentStoragePolicyTests
  {
    [Fact]
    public void EmptyStorageIsNoRecord()
    {
      NativeStage02SemanticAssignmentStorageDecision decision =
        NativeStage02SemanticAssignmentStoragePolicy.Evaluate(
          null,
          Array.Empty<string>());

      Assert.Equal(
        NativeStage02SemanticAssignmentStorageState.NoRecord,
        decision.State);
      Assert.NotNull(decision.Payload);
      Assert.Empty(decision.Payload.Assignments);
    }

    [Fact]
    public void ValidSnapshotIsCurrentAndReportsStaleRecords()
    {
      NativeStage02SemanticAssignmentStorageSnapshot snapshot =
        NativeStage02SemanticAssignmentStoragePolicy.CreateSnapshot(
          Payload(Record("alive"), Record("stale")),
          "2026-08-14T00:00:00Z");

      NativeStage02SemanticAssignmentStorageDecision decision =
        NativeStage02SemanticAssignmentStoragePolicy.Evaluate(
          snapshot,
          new[] { "alive" });

      Assert.Equal(
        NativeStage02SemanticAssignmentStorageState.Current,
        decision.State);
      Assert.Equal(new[] { "stale" }, decision.StaleElementUniqueIds);
      Assert.Equal(2, decision.Payload.Assignments.Count);
    }

    [Fact]
    public void HashMismatchIsCorrupt()
    {
      NativeStage02SemanticAssignmentStorageSnapshot snapshot =
        NativeStage02SemanticAssignmentStoragePolicy.CreateSnapshot(
          Payload(Record("a")),
          "2026-08-14T00:00:00Z");
      snapshot.PayloadSha256 = new string('0', 64);

      NativeStage02SemanticAssignmentStorageDecision decision =
        NativeStage02SemanticAssignmentStoragePolicy.Evaluate(
          snapshot,
          new[] { "a" });

      Assert.Equal(
        NativeStage02SemanticAssignmentStorageState.Corrupt,
        decision.State);
    }

    [Fact]
    public void CanonicalJsonMismatchIsCorrupt()
    {
      NativeStage02SemanticAssignmentStorageSnapshot snapshot =
        NativeStage02SemanticAssignmentStoragePolicy.CreateSnapshot(
          Payload(Record("a")),
          "2026-08-14T00:00:00Z");
      snapshot.CanonicalJson += " ";
      snapshot.PayloadSha256 = NativeStage02SemanticAssignmentCanonicalizer
        .Sha256(snapshot.CanonicalJson);

      NativeStage02SemanticAssignmentStorageDecision decision =
        NativeStage02SemanticAssignmentStoragePolicy.Evaluate(
          snapshot,
          new[] { "a" });

      Assert.Equal(
        NativeStage02SemanticAssignmentStorageState.Corrupt,
        decision.State);
    }

    [Fact]
    public void FutureSchemaIsUnsupported()
    {
      NativeStage02SemanticAssignmentStorageSnapshot snapshot =
        NativeStage02SemanticAssignmentStoragePolicy.CreateSnapshot(
          Payload(Record("a")),
          "2026-08-14T00:00:00Z");
      snapshot.SchemaVersion = "2.0.0";

      NativeStage02SemanticAssignmentStorageDecision decision =
        NativeStage02SemanticAssignmentStoragePolicy.Evaluate(
          snapshot,
          new[] { "a" });

      Assert.Equal(
        NativeStage02SemanticAssignmentStorageState.UnsupportedFuture,
        decision.State);
    }

    [Fact]
    public void SnapshotContainsNoUserOrMachineIdentityFields()
    {
      NativeStage02SemanticAssignmentStorageSnapshot snapshot =
        NativeStage02SemanticAssignmentStoragePolicy.CreateSnapshot(
          Payload(Record("a")),
          "2026-08-14T00:00:00Z");

      Assert.DoesNotContain("user", snapshot.CanonicalJson.ToLowerInvariant());
      Assert.DoesNotContain("path", snapshot.CanonicalJson.ToLowerInvariant());
      Assert.DoesNotContain("assignedUtc", snapshot.CanonicalJson);
    }

    private static NativeStage02SemanticAssignmentPayload Payload(
      params NativeStage02SemanticAssignmentRecord[] records)
    {
      return new NativeStage02SemanticAssignmentPayload
      {
        SchemaVersion = NativeStage02SemanticAssignmentSchema.Version,
        RulePackageId = "HBR-WUHAN-PLANNING",
        RulePackageVersion = "1.0.0",
        Assignments = records ?? Array.Empty<NativeStage02SemanticAssignmentRecord>()
      };
    }

    private static NativeStage02SemanticAssignmentRecord Record(string uniqueId)
    {
      return new NativeStage02SemanticAssignmentRecord
      {
        ElementUniqueId = uniqueId,
        RoleId = "SITE_GREEN_OBJECT",
        AssignmentMode = NativeStage02AssignmentMode.Manual,
        CarrierCategory = "OST_BuildingPad",
        CarrierElementKind = "BuildingPad"
      };
    }
  }
}
