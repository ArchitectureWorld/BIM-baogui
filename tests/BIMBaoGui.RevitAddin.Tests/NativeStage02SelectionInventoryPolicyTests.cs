using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02SelectionInventoryPolicyTests
  {
    [Fact]
    public void CurrentSelectionKeepsEligibleModelElementOutsideAutoCategoryWhitelist()
    {
      NativeStage02ElementSnapshot pad = Element(
        "pad",
        "UNVERIFIED_BUILDING_PAD_CATEGORY",
        "UnverifiedBuildingPadKind");

      NativeStage02InventoryDecision decision =
        NativeStage02InventoryPolicy.Resolve(
          NativeStage02ScopeMode.CustomSelection,
          new[] { pad },
          new[] { "pad" },
          new[] { "OST_Doors" });

      Assert.True(decision.Accepted);
      Assert.Single(decision.Elements);
      Assert.Equal("pad", decision.Elements[0].UniqueId);
    }

    [Fact]
    public void SameElementCanRemainUnsupportedByAutomaticInventory()
    {
      NativeStage02ElementSnapshot pad = Element(
        "pad",
        "UNVERIFIED_BUILDING_PAD_CATEGORY",
        "UnverifiedBuildingPadKind");

      NativeStage02InventoryDecision decision =
        NativeStage02InventoryPolicy.Resolve(
          NativeStage02ScopeMode.FullModel,
          new[] { pad },
          Array.Empty<string>(),
          new[] { "OST_Doors" });

      Assert.True(decision.Accepted);
      Assert.Empty(decision.Elements);
    }

    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public void IneligibleSelectionKindsAreRejected(
      bool isElementType,
      bool isViewSpecific,
      bool isImported,
      bool isLinked,
      bool notModelElement)
    {
      NativeStage02ElementSnapshot element = Element(
        "x",
        "OST_GenericModel",
        "Generic",
        isElementType,
        isViewSpecific,
        isImported,
        isLinked,
        !notModelElement);

      NativeStage02InventoryDecision decision =
        NativeStage02SelectionInventoryPolicy.Resolve(
          new[] { element },
          new[] { "x" });

      Assert.False(decision.Accepted);
      Assert.Equal(
        NativeStage02InventoryCodes.SelectionElementNotEligible,
        decision.ErrorCode);
    }

    [Fact]
    public void MissingElementAndIneligibleElementUseDifferentCodes()
    {
      NativeStage02InventoryDecision missing =
        NativeStage02SelectionInventoryPolicy.Resolve(
          new[] { Element("a", "OST_GenericModel", "Generic") },
          new[] { "missing" });
      NativeStage02InventoryDecision ineligible =
        NativeStage02SelectionInventoryPolicy.Resolve(
          new[]
          {
            Element(
              "a",
              "OST_GenericModel",
              "Generic",
              isLinked: true)
          },
          new[] { "a" });

      Assert.Equal(
        NativeStage02InventoryCodes.SelectionElementMissing,
        missing.ErrorCode);
      Assert.Equal(
        NativeStage02InventoryCodes.SelectionElementNotEligible,
        ineligible.ErrorCode);
      Assert.NotEqual(missing.ErrorCode, ineligible.ErrorCode);
    }

    [Fact]
    public void CurrentSelectionOrderIsCanonicalAndInputOrderIndependent()
    {
      NativeStage02ElementSnapshot[] inventory =
      {
        Element("c", "OST_GenericModel", "Generic"),
        Element("a", "OST_GenericModel", "Generic"),
        Element("b", "OST_GenericModel", "Generic")
      };

      NativeStage02InventoryDecision first =
        NativeStage02SelectionInventoryPolicy.Resolve(
          inventory,
          new[] { "c", "a", "b", "a" });
      NativeStage02InventoryDecision second =
        NativeStage02SelectionInventoryPolicy.Resolve(
          inventory.Reverse(),
          new[] { "b", "c", "a" });

      Assert.True(first.Accepted);
      Assert.True(second.Accepted);
      Assert.Equal(
        new[] { "a", "b", "c" },
        first.Elements.Select(value => value.UniqueId));
      Assert.Equal(
        first.Elements.Select(value => value.UniqueId),
        second.Elements.Select(value => value.UniqueId));
    }

    [Fact]
    public void New_selection_modes_never_fall_back_to_full_model_when_empty()
    {
      NativeStage02ScopeMode[] scopes =
      {
        NativeStage02ScopeMode.CurrentSelection,
        NativeStage02ScopeMode.InteractiveSelection
      };
      foreach (NativeStage02ScopeMode scope in scopes)
      {
        NativeStage02InventoryDecision decision =
          NativeStage02InventoryPolicy.Resolve(
            scope,
            new[] { Element("eligible", "OST_GenericModel", "Generic") },
            Array.Empty<string>(),
            new[] { "OST_GenericModel" });

        Assert.False(decision.Accepted);
        Assert.Equal(
          NativeStage02InventoryCodes.SelectionEmpty,
          decision.ErrorCode);
        Assert.Empty(decision.Elements);
      }
    }

    private static NativeStage02ElementSnapshot Element(
      string uniqueId,
      string category,
      string kind,
      bool isElementType = false,
      bool isViewSpecific = false,
      bool isImported = false,
      bool isLinked = false,
      bool isModel = true)
    {
      return new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = new string('a', 64),
        UniqueId = uniqueId,
        ElementId = uniqueId.GetHashCode(),
        Category = category,
        CategoryName = category,
        ClrType = "Test." + kind,
        ElementKind = kind,
        IsElementType = isElementType,
        IsViewSpecific = isViewSpecific,
        IsImported = isImported,
        IsLinked = isLinked,
        IsModelElement = isModel
      };
    }
  }
}
