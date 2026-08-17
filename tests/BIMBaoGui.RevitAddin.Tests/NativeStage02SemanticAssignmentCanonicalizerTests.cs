using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02SemanticAssignmentCanonicalizerTests
  {
    [Fact]
    public void CanonicalJsonIsDeterministicAcrossInputOrder()
    {
      NativeStage02SemanticAssignmentPayload first = Payload(
        Record("b", "SITE_GREEN_OBJECT"),
        Record("a", "SITE_FIRE_FIELD"));
      NativeStage02SemanticAssignmentPayload second = Payload(
        Record(" a ", " SITE_FIRE_FIELD "),
        Record(" b ", " SITE_GREEN_OBJECT "));

      string firstJson = NativeStage02SemanticAssignmentCanonicalizer
        .SerializeCanonical(first);
      string secondJson = NativeStage02SemanticAssignmentCanonicalizer
        .SerializeCanonical(second);

      Assert.Equal(firstJson, secondJson);
      Assert.Equal(
        NativeStage02SemanticAssignmentCanonicalizer.Sha256(firstJson),
        NativeStage02SemanticAssignmentCanonicalizer.Sha256(secondJson));
      Assert.True(firstJson.IndexOf("a", StringComparison.Ordinal)
        < firstJson.IndexOf("b", StringComparison.Ordinal));
    }

    [Fact]
    public void CurrentPayloadBindsDocumentFingerprintIntoCanonicalHash()
    {
      NativeStage02SemanticAssignmentPayload first = Payload(
        Record("a", "SITE_GREEN_OBJECT"));
      NativeStage02SemanticAssignmentPayload second = Payload(
        Record("a", "SITE_GREEN_OBJECT"));
      first.DocumentFingerprint = "document-a";
      second.DocumentFingerprint = "document-b";

      string firstJson = NativeStage02SemanticAssignmentCanonicalizer
        .SerializeCanonical(first);
      string secondJson = NativeStage02SemanticAssignmentCanonicalizer
        .SerializeCanonical(second);

      Assert.Contains("\"documentFingerprint\":\"document-a\"", firstJson);
      Assert.NotEqual(firstJson, secondJson);
      Assert.NotEqual(
        NativeStage02SemanticAssignmentCanonicalizer.Sha256(firstJson),
        NativeStage02SemanticAssignmentCanonicalizer.Sha256(secondJson));
    }

    [Fact]
    public void DuplicateEquivalentRecordIsDeduplicated()
    {
      NativeStage02SemanticAssignmentPayload normalized =
        NativeStage02SemanticAssignmentCanonicalizer.Normalize(Payload(
          Record("a", "SITE_GREEN_OBJECT"),
          Record(" a ", " SITE_GREEN_OBJECT ")));

      Assert.Single(normalized.Assignments);
    }

    [Fact]
    public void DuplicateConflictingRecordIsRejected()
    {
      InvalidOperationException error = Assert.Throws<InvalidOperationException>(
        () => NativeStage02SemanticAssignmentCanonicalizer.Normalize(Payload(
          Record("a", "SITE_GREEN_OBJECT"),
          Record("a", "SITE_FIRE_FIELD"))));

      Assert.Contains("SEMANTIC_ASSIGNMENT_DUPLICATE_CONFLICT", error.Message);
    }

    [Fact]
    public void UpsertAndRemoveDoNotMutateOriginalPayload()
    {
      NativeStage02SemanticAssignmentPayload original = Payload(
        Record("a", "SITE_GREEN_OBJECT"));
      NativeStage02SemanticAssignmentPayload updated =
        NativeStage02SemanticAssignmentCanonicalizer.Upsert(
          original,
          Record("a", "SITE_FIRE_FIELD"));
      NativeStage02SemanticAssignmentPayload removed =
        NativeStage02SemanticAssignmentCanonicalizer.Remove(updated, "a");

      Assert.Equal("SITE_GREEN_OBJECT", original.Assignments.Single().RoleId);
      Assert.Equal("SITE_FIRE_FIELD", updated.Assignments.Single().RoleId);
      Assert.Empty(removed.Assignments);
    }

    private static NativeStage02SemanticAssignmentPayload Payload(
      params NativeStage02SemanticAssignmentRecord[] records)
    {
      return new NativeStage02SemanticAssignmentPayload
      {
        SchemaVersion = NativeStage02SemanticAssignmentSchema.Version,
        DocumentFingerprint = "document",
        RulePackageId = "HBR-WUHAN-PLANNING",
        RulePackageVersion = "1.0.0",
        Assignments = records ?? Array.Empty<NativeStage02SemanticAssignmentRecord>()
      };
    }

    private static NativeStage02SemanticAssignmentRecord Record(
      string uniqueId,
      string roleId)
    {
      return new NativeStage02SemanticAssignmentRecord
      {
        ElementUniqueId = uniqueId,
        RoleId = roleId,
        AssignmentMode = NativeStage02AssignmentMode.Manual,
        CarrierCategory = "OST_BuildingPad",
        CarrierElementKind = "BuildingPad"
      };
    }
  }
}
