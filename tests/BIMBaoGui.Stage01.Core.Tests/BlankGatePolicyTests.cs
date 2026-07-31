using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class BlankGatePolicyTests
  {
    [Fact]
    public void IsBlocking_AllowsTemplateMetadataEvenWhenItReportsALocation()
    {
      var facts = new BlankGateFacts(
        isExplicitModelContent: false,
        isKnownPlacedModelContent: false,
        isViewSpecific: false,
        isModelCategory: true,
        hasLocation: true,
        hasSpatialExtent: false,
        hasPhysicalGeometry: false);

      Assert.False(BlankGatePolicy.IsBlocking(facts));
    }

    [Fact]
    public void IsBlocking_BlocksKnownPlacedModelContentWithoutInspectingGeometry()
    {
      var facts = new BlankGateFacts(
        isExplicitModelContent: false,
        isKnownPlacedModelContent: true,
        isViewSpecific: false,
        isModelCategory: true,
        hasLocation: false,
        hasSpatialExtent: false,
        hasPhysicalGeometry: false);

      Assert.True(BlankGatePolicy.IsBlocking(facts));
    }

    [Fact]
    public void IsBlocking_BlocksExternalOrDirectModelContent()
    {
      var facts = new BlankGateFacts(
        isExplicitModelContent: true,
        isKnownPlacedModelContent: false,
        isViewSpecific: false,
        isModelCategory: false,
        hasLocation: false,
        hasSpatialExtent: false,
        hasPhysicalGeometry: false);

      Assert.True(BlankGatePolicy.IsBlocking(facts));
    }

    [Fact]
    public void IsBlocking_BlocksUnknownModelElementOnlyWhenItHasSpatialExtentAndGeometry()
    {
      var facts = new BlankGateFacts(
        isExplicitModelContent: false,
        isKnownPlacedModelContent: false,
        isViewSpecific: false,
        isModelCategory: true,
        hasLocation: true,
        hasSpatialExtent: true,
        hasPhysicalGeometry: true);

      Assert.True(BlankGatePolicy.IsBlocking(facts));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void IsBlocking_DoesNotTreatLocationAloneAsModelContent(bool hasSpatialExtent, bool hasPhysicalGeometry)
    {
      var facts = new BlankGateFacts(
        isExplicitModelContent: false,
        isKnownPlacedModelContent: false,
        isViewSpecific: false,
        isModelCategory: true,
        hasLocation: true,
        hasSpatialExtent: hasSpatialExtent,
        hasPhysicalGeometry: hasPhysicalGeometry);

      Assert.False(BlankGatePolicy.IsBlocking(facts));
    }

    [Fact]
    public void IsBlocking_AllowsViewSpecificContentInTemplateViews()
    {
      var facts = new BlankGateFacts(
        isExplicitModelContent: false,
        isKnownPlacedModelContent: false,
        isViewSpecific: true,
        isModelCategory: true,
        hasLocation: true,
        hasSpatialExtent: true,
        hasPhysicalGeometry: true);

      Assert.False(BlankGatePolicy.IsBlocking(facts));
    }
  }
}
