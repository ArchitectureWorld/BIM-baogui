using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02InventoryPolicyTests
  {
    [Fact]
    public void FullModelKeepsOnlyCurrentDocumentRuleRelatedModelInstances()
    {
      NativeStage02ElementSnapshot[] input =
      {
        Element("valid", "OST_Doors", "FamilyInstance"),
        Element("type", "OST_Doors", "FamilyInstance", isElementType: true),
        Element("view", "OST_Doors", "FamilyInstance", isViewSpecific: true),
        Element("import", "OST_Doors", "FamilyInstance", isImported: true),
        Element("link", "OST_Doors", "FamilyInstance", isLinked: true),
        Element("annotation", "OST_Doors", "FamilyInstance", isModel: false),
        Element("unknown-category", "OST_TextNotes", "TextNote")
      };

      NativeStage02InventoryDecision decision =
        NativeStage02InventoryPolicy.Resolve(
          NativeStage02ScopeMode.FullModel,
          input,
          Array.Empty<string>(),
          new[] { "OST_Doors" });

      Assert.True(decision.Accepted);
      Assert.Single(decision.Elements);
      Assert.Equal("valid", decision.Elements[0].UniqueId);
    }

    [Fact]
    public void CustomScopeRequiresExplicitNonEmptyCurrentDocumentUniqueIds()
    {
      NativeStage02ElementSnapshot[] inventory =
      {
        Element("a", "OST_Doors", "FamilyInstance"),
        Element("b", "OST_Doors", "FamilyInstance")
      };

      NativeStage02InventoryDecision empty =
        NativeStage02InventoryPolicy.Resolve(
          NativeStage02ScopeMode.CustomSelection,
          inventory,
          Array.Empty<string>(),
          new[] { "OST_Doors" });
      Assert.False(empty.Accepted);
      Assert.Equal(
        NativeStage02InventoryCodes.CustomScopeEmpty,
        empty.ErrorCode);

      NativeStage02InventoryDecision missing =
        NativeStage02InventoryPolicy.Resolve(
          NativeStage02ScopeMode.CustomSelection,
          inventory,
          new[] { "a", "not-current" },
          new[] { "OST_Doors" });
      Assert.False(missing.Accepted);
      Assert.Equal(
        NativeStage02InventoryCodes.CustomElementUnavailable,
        missing.ErrorCode);

      NativeStage02InventoryDecision accepted =
        NativeStage02InventoryPolicy.Resolve(
          NativeStage02ScopeMode.CustomSelection,
          inventory,
          new[] { "b", "a", "a" },
          new[] { "OST_Doors" });
      Assert.True(accepted.Accepted);
      Assert.Equal(new[] { "a", "b" },
        accepted.Elements.Select(value => value.UniqueId));
    }

    [Fact]
    public void FullModelModeRejectsStaleCustomSelectionInput()
    {
      NativeStage02InventoryDecision decision =
        NativeStage02InventoryPolicy.Resolve(
          NativeStage02ScopeMode.FullModel,
          new[] { Element("a", "OST_Doors", "FamilyInstance") },
          new[] { "a" },
          new[] { "OST_Doors" });

      Assert.False(decision.Accepted);
      Assert.Equal(
        NativeStage02InventoryCodes.ScopeInputConflict,
        decision.ErrorCode);
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
