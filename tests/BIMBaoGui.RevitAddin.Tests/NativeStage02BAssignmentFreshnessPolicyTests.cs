using System;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Stage02B;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02BAssignmentFreshnessPolicyTests
  {
    private const string RuleSha =
      "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void ExactPersistedDocumentRuleAndLiveFactSnapshotAreCurrent()
    {
      NativeStage02ElementSnapshot live = Element("document", "uid-1", "绿地");
      NativeStage02SemanticAssignmentSnapshot result = Evaluate(
        Payload("document", live, RuleSha),
        "document",
        RuleSha,
        live);

      Assert.True(result.Current);
      Assert.Equal("document", result.AssignmentDocumentFingerprint);
      Assert.Single(result.Assignments);
    }

    [Fact]
    public void CrossDocumentRuleDriftAndLiveFactDriftAreRejectedBeforeResolver()
    {
      NativeStage02ElementSnapshot confirmed = Element(
        "document-a", "uid-1", "绿地");

      Assert.False(Evaluate(
        Payload("document-a", confirmed, RuleSha),
        "document-b",
        RuleSha,
        Element("document-b", "uid-1", "绿地")).Current);
      Assert.False(Evaluate(
        Payload("document-a", confirmed, RuleSha),
        "document-a",
        new string('b', 64),
        confirmed).Current);
      Assert.False(Evaluate(
        Payload("document-a", confirmed, RuleSha),
        "document-a",
        RuleSha,
        Element("document-a", "uid-1", "已变更绿地")).Current);
    }

    private static NativeStage02SemanticAssignmentSnapshot Evaluate(
      NativeStage02SemanticAssignmentPayload payload,
      string currentDocumentFingerprint,
      string currentRuleSha,
      params NativeStage02ElementSnapshot[] live)
    {
      NativeStage02SemanticAssignmentStorageSnapshot stored =
        NativeStage02SemanticAssignmentStoragePolicy.CreateSnapshot(
          payload,
          "2026-08-14T00:00:00Z");
      NativeStage02SemanticAssignmentStorageDecision decision =
        NativeStage02SemanticAssignmentStoragePolicy.Evaluate(
          stored,
          new[] { "uid-1" });
      return NativeStage02BAssignmentFreshnessPolicy.Evaluate(
        decision,
        currentDocumentFingerprint,
        currentRuleSha,
        live);
    }

    private static NativeStage02SemanticAssignmentPayload Payload(
      string documentFingerprint,
      NativeStage02ElementSnapshot confirmed,
      string ruleSha)
    {
      return new NativeStage02SemanticAssignmentPayload
      {
        SchemaVersion = NativeStage02SemanticAssignmentSchema.Version,
        DocumentFingerprint = documentFingerprint,
        RulePackageId = "HBR-WUHAN-PLANNING",
        RulePackageVersion = "1.0.0",
        Assignments = new[]
        {
          new NativeStage02SemanticAssignmentRecord
          {
            ElementUniqueId = confirmed.UniqueId,
            RoleId = "SITE_GREEN_OBJECT",
            AssignmentMode = NativeStage02AssignmentMode.Manual,
            CarrierCategory = confirmed.Category,
            CarrierElementKind = confirmed.ElementKind,
            RulePackageSha256 = ruleSha,
            ElementSnapshotHash = NativeStage02ElementSnapshotCanonicalizer
              .Sha256(confirmed),
            ConfirmedUtc = "2026-08-14T00:00:00Z"
          }
        }
      };
    }

    private static NativeStage02ElementSnapshot Element(
      string documentFingerprint,
      string uniqueId,
      string name)
    {
      return new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = documentFingerprint,
        UniqueId = uniqueId,
        ElementId = 42,
        Category = "OST_BuildingPad",
        CategoryName = "建筑地坪",
        ClrType = "Autodesk.Revit.DB.Architecture.BuildingPad",
        ElementKind = "BuildingPad",
        ElementName = name,
        IsModelElement = true
      };
    }
  }
}
