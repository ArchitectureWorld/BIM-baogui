using System;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Stage02B;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02BProjectionCarrierResolverTests
  {
    [Fact]
    public void Verified_site_owner_enables_only_structural_element_projection()
    {
      NativeStage02BMetricDefinition metric = NativeStage02BMetricCatalog.Current
        .MetricsFor("总平模型")[1];
      var verifiedMetric = new NativeStage02BMetricDefinition
      {
        PropertyId = metric.PropertyId,
        Identity = metric.Identity,
        Sequence = metric.Sequence,
        Source = metric.Source,
        Property = metric.Property,
        OfficialCarrierStatus = NativeOfficialCarrierEvidenceStatus.Verified,
        OfficialProjectionCarrierId = "carrier",
        OfficialCarrierProbeRef = "probe"
      };
      NativeStage02BOwnerDecision decision = NativeStage02BOwnerPolicy.Resolve(
        verifiedMetric,
        new NativeOfficialCarrierPolicy
        {
          IfcEntity = "IfcSite",
          EvidenceStatus = NativeOfficialCarrierEvidenceStatus.Verified
        },
        new NativeOfficialProjectionCarrierDefinition
        {
          CarrierId = "carrier", PropertyId = metric.PropertyId
        },
        new NativeOfficialCarrierProbeRecord
        {
          ProbeId = "probe", PropertyId = metric.PropertyId
        },
        null);

      Assert.True(decision.ParameterProjectionAllowed);
      Assert.Equal(NativeStage02BProjectionMode.VerifiedElementParameter,
        decision.ProjectionMode);
    }

    [Fact]
    public void Project_information_selector_is_structural_and_guid_bound()
    {
      NativeStage02BProjectionCarrierDecision decision =
        NativeStage02BProjectionCarrierResolver.Decide(
          Definition("PROJECT_INFORMATION", string.Empty,
            "Autodesk.Revit.DB.ProjectInfo"),
          Snapshot(true),
          new[]
          {
            Candidate("PROJECT_INFORMATION", string.Empty,
              "OST_ProjectInformation", "Autodesk.Revit.DB.ProjectInfo")
          });

      Assert.True(decision.Accepted, decision.ErrorCode);
      Assert.Equal("PROJECT_INFORMATION", decision.UniqueId);
    }

    [Fact]
    public void Confirmed_role_requires_current_same_document_assignment()
    {
      NativeOfficialProjectionCarrierDefinition definition = Definition(
        "CONFIRMED_SEMANTIC_ROLE", "SITE_GREEN_OBJECT",
        "Autodesk.Revit.DB.Architecture.BuildingPad");
      NativeStage02BProjectionCarrierCandidate candidate = Candidate(
        "uid-1", "SITE_GREEN_OBJECT", "OST_BuildingPad",
        "Autodesk.Revit.DB.Architecture.BuildingPad");

      Assert.Equal("OFFICIAL_CARRIER_NOT_FOUND",
        NativeStage02BProjectionCarrierResolver.Decide(
          definition, Snapshot(false), new[] { candidate }).ErrorCode);
      NativeStage02SemanticAssignmentSnapshot crossDocument = Snapshot(true);
      crossDocument.AssignmentDocumentFingerprint = "different-document";
      Assert.Equal("OFFICIAL_CARRIER_NOT_FOUND",
        NativeStage02BProjectionCarrierResolver.Decide(
          definition, crossDocument, new[] { candidate }).ErrorCode);
    }

    [Fact]
    public void Confirmed_role_rejects_zero_multiple_and_type_drift()
    {
      NativeOfficialProjectionCarrierDefinition definition = Definition(
        "CONFIRMED_SEMANTIC_ROLE", "SITE_GREEN_OBJECT",
        "Autodesk.Revit.DB.Architecture.BuildingPad");
      NativeStage02SemanticAssignmentSnapshot snapshot = Snapshot(true);

      Assert.Equal("OFFICIAL_CARRIER_NOT_FOUND",
        NativeStage02BProjectionCarrierResolver.Decide(
          definition, snapshot, Array.Empty<NativeStage02BProjectionCarrierCandidate>())
          .ErrorCode);
      Assert.Equal("OFFICIAL_CARRIER_AMBIGUOUS",
        NativeStage02BProjectionCarrierResolver.Decide(
          definition, snapshot,
          new[]
          {
            Candidate("uid-1", "SITE_GREEN_OBJECT", "OST_BuildingPad",
              "Autodesk.Revit.DB.Architecture.BuildingPad"),
            Candidate("uid-2", "SITE_GREEN_OBJECT", "OST_BuildingPad",
              "Autodesk.Revit.DB.Architecture.BuildingPad")
          }).ErrorCode);
      Assert.Equal("OFFICIAL_CARRIER_TYPE_MISMATCH",
        NativeStage02BProjectionCarrierResolver.Decide(
          definition, snapshot,
          new[]
          {
            Candidate("uid-1", "SITE_GREEN_OBJECT", "OST_BuildingPad",
              "Autodesk.Revit.DB.Floor")
          }).ErrorCode);
    }

    [Fact]
    public void Scope_and_parameter_guid_must_match_structural_contract()
    {
      NativeOfficialProjectionCarrierDefinition definition = Definition(
        "CONFIRMED_SEMANTIC_ROLE", "SITE_GREEN_OBJECT",
        "Autodesk.Revit.DB.Architecture.BuildingPad");
      definition.BindingScope = "TYPE";
      Assert.Equal("OFFICIAL_CARRIER_CONTRACT_MISMATCH",
        NativeStage02BProjectionCarrierResolver.Decide(
          definition, Snapshot(true),
          new[]
          {
            Candidate("uid-1", "SITE_GREEN_OBJECT", "OST_BuildingPad",
              "Autodesk.Revit.DB.Architecture.BuildingPad")
          }).ErrorCode);

      definition.BindingScope = "INSTANCE";
      definition.ParameterGuid = Guid.NewGuid().ToString("D");
      Assert.Equal("OFFICIAL_CARRIER_CONTRACT_MISMATCH",
        NativeStage02BProjectionCarrierResolver.Decide(
          definition, Snapshot(true),
          new[]
          {
            Candidate("uid-1", "SITE_GREEN_OBJECT", "OST_BuildingPad",
              "Autodesk.Revit.DB.Architecture.BuildingPad")
          }).ErrorCode);
    }

    private static NativeOfficialProjectionCarrierDefinition Definition(
      string selector,
      string role,
      string elementClass)
    {
      const string propertyId = "93e51676-237e-56a8-8f28-2da845422e2e";
      return new NativeOfficialProjectionCarrierDefinition
      {
        CarrierId = "carrier",
        PropertyId = propertyId,
        SelectorKind = selector,
        RoleId = role,
        CategoryBuiltInId = selector == "PROJECT_INFORMATION"
          ? "OST_ProjectInformation" : "OST_BuildingPad",
        ElementClass = elementClass,
        BindingScope = "INSTANCE",
        ParameterGuid = propertyId
      };
    }

    private static NativeStage02SemanticAssignmentSnapshot Snapshot(bool current)
    {
      return new NativeStage02SemanticAssignmentSnapshot
      {
        Current = current,
        CurrentDocumentFingerprint = "document",
        AssignmentDocumentFingerprint = "document",
        Assignments = new[]
        {
          new NativeStage02SemanticAssignmentRecord
          {
            ElementUniqueId = "uid-1",
            RoleId = "SITE_GREEN_OBJECT"
          },
          new NativeStage02SemanticAssignmentRecord
          {
            ElementUniqueId = "uid-2",
            RoleId = "SITE_GREEN_OBJECT"
          }
        }
      };
    }

    private static NativeStage02BProjectionCarrierCandidate Candidate(
      string uniqueId,
      string role,
      string category,
      string elementClass)
    {
      return new NativeStage02BProjectionCarrierCandidate
      {
        UniqueId = uniqueId,
        RoleId = role,
        CategoryBuiltInId = category,
        ElementClass = elementClass
      };
    }
  }
}
