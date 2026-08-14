using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02ManualCarrierPolicyTests
  {
    [Fact]
    public void ApprovedBuildingPadCanCarryGreenRole()
    {
      NativeStage02ManualCarrierDecision decision = Evaluate(
        Element("OST_BuildingPad", "BuildingPad"));

      Assert.True(decision.Accepted);
      Assert.Equal("SITE_GREEN_OBJECT", decision.Role.RoleId);
    }

    [Fact]
    public void SameCategoryWithWrongElementKindIsBlocked()
    {
      NativeStage02ManualCarrierDecision decision = Evaluate(
        Element("OST_BuildingPad", "FamilyInstance"));

      Assert.False(decision.Accepted);
      Assert.Equal(
        NativeStage02ManualCarrierCodes.CarrierNotAllowed,
        decision.ErrorCode);
    }

    [Fact]
    public void InactiveProjectConditionIsBlocked()
    {
      NativeStage02ManualCarrierDecision decision = Evaluate(
        Element("OST_BuildingPad", "BuildingPad"),
        conditions: new Dictionary<string, bool>
        {
          ["site.green"] = false
        });

      Assert.False(decision.Accepted);
      Assert.Equal(
        NativeStage02ManualCarrierCodes.ConditionInactive,
        decision.ErrorCode);
    }

    [Fact]
    public void WrongModelFileTypeIsBlocked()
    {
      NativeStage02ManualCarrierDecision decision = Evaluate(
        Element("OST_BuildingPad", "BuildingPad"),
        modelFileType: "单体建筑地上模型");

      Assert.False(decision.Accepted);
      Assert.Equal(
        NativeStage02ManualCarrierCodes.ModelTypeNotAllowed,
        decision.ErrorCode);
    }

    [Fact]
    public void UnknownRoleIsBlocked()
    {
      NativeStage02ManualCarrierDecision decision =
        NativeStage02ManualCarrierPolicy.Evaluate(
          "UNKNOWN_ROLE",
          "总平模型",
          new Dictionary<string, bool> { ["site.green"] = true },
          Element("OST_BuildingPad", "BuildingPad"),
          Roles());

      Assert.False(decision.Accepted);
      Assert.Equal(NativeStage02ManualCarrierCodes.RoleUnknown, decision.ErrorCode);
    }

    [Fact]
    public void AutomaticCarrierDoesNotBecomeManualCarrierImplicitly()
    {
      NativeStage02ManualCarrierDecision decision = Evaluate(
        Element("OST_Floors", "Floor"));

      Assert.False(decision.Accepted);
      Assert.Equal(
        NativeStage02ManualCarrierCodes.CarrierNotAllowed,
        decision.ErrorCode);
    }

    [Fact]
    public void MissingTemplateOrOwnerStrategyIsBlocked()
    {
      NativeStage02ManualRoleContract withoutTemplate = Role();
      withoutTemplate.HasPropertyTemplate = false;
      NativeStage02ManualCarrierDecision noTemplate = Evaluate(
        Element("OST_BuildingPad", "BuildingPad"),
        roles: new[] { withoutTemplate });
      Assert.Equal(
        NativeStage02ManualCarrierCodes.TemplateUnavailable,
        noTemplate.ErrorCode);

      NativeStage02ManualRoleContract withoutOwner = Role();
      withoutOwner.IfcOwnerStrategy = string.Empty;
      NativeStage02ManualCarrierDecision noOwner = Evaluate(
        Element("OST_BuildingPad", "BuildingPad"),
        roles: new[] { withoutOwner });
      Assert.Equal(
        NativeStage02ManualCarrierCodes.OwnerStrategyUnavailable,
        noOwner.ErrorCode);
    }

    [Fact]
    public void CarrierCanonicalizationIsStableAndDeduplicated()
    {
      NativeStage02ManualCarrierDefinition[] first =
      {
        Carrier(" OST_BuildingPad ", "BuildingPad", "BuildingPad"),
        Carrier("OST_Floors", "Floor")
      };
      NativeStage02ManualCarrierDefinition[] second =
      {
        Carrier("OST_Floors", " Floor "),
        Carrier("OST_BuildingPad", " BuildingPad ")
      };

      string[] firstKeys = NativeStage02ManualCarrierPolicy
        .CanonicalizeCarriers(first)
        .SelectMany(value => value.ElementKinds.Select(kind =>
          value.Category + "|" + kind))
        .ToArray();
      string[] secondKeys = NativeStage02ManualCarrierPolicy
        .CanonicalizeCarriers(second)
        .SelectMany(value => value.ElementKinds.Select(kind =>
          value.Category + "|" + kind))
        .ToArray();

      Assert.Equal(firstKeys, secondKeys);
      Assert.Equal(
        new[]
        {
          "OST_BuildingPad|BuildingPad",
          "OST_Floors|Floor"
        },
        firstKeys);
    }

    private static NativeStage02ManualCarrierDecision Evaluate(
      NativeStage02ElementSnapshot element,
      string modelFileType = "总平模型",
      IReadOnlyDictionary<string, bool> conditions = null,
      IEnumerable<NativeStage02ManualRoleContract> roles = null)
    {
      return NativeStage02ManualCarrierPolicy.Evaluate(
        "SITE_GREEN_OBJECT",
        modelFileType,
        conditions ?? new Dictionary<string, bool>
        {
          ["site.green"] = true
        },
        element,
        roles ?? Roles());
    }

    private static IEnumerable<NativeStage02ManualRoleContract> Roles()
    {
      return new[] { Role() };
    }

    private static NativeStage02ManualRoleContract Role()
    {
      return new NativeStage02ManualRoleContract
      {
        RoleId = "SITE_GREEN_OBJECT",
        DisplayName = "绿地",
        ModelFileTypes = new[] { "总平模型" },
        ConditionId = "site.green",
        ManualCarriers = new[]
        {
          Carrier("OST_BuildingPad", "BuildingPad")
        },
        HasPropertyTemplate = true,
        IfcOwnerStrategy = "BY_EXPORT_GUID"
      };
    }

    private static NativeStage02ManualCarrierDefinition Carrier(
      string category,
      params string[] kinds)
    {
      return new NativeStage02ManualCarrierDefinition
      {
        Category = category,
        ElementKinds = kinds ?? Array.Empty<string>()
      };
    }

    private static NativeStage02ElementSnapshot Element(
      string category,
      string kind)
    {
      return new NativeStage02ElementSnapshot
      {
        UniqueId = "element-1",
        Category = category,
        ElementKind = kind,
        IsModelElement = true
      };
    }
  }
}
