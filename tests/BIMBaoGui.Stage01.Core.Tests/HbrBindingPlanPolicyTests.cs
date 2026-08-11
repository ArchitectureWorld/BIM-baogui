using System;
using System.Linq;
using BIMBaoGui.Stage01.Revit.Parameters;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrBindingPlanPolicyTests
  {
    [Fact]
    public void CreatesAndBindsWhenDefinitionDoesNotExist()
    {
      HbrBindingPlanDecision decision = Evaluate(
        definitionExists: false,
        bindingExists: false);

      Assert.Equal(HbrBindingActions.CreateAndBind, decision.Action);
      Assert.Equal(new[] { "OST_Floors" }, decision.Categories);
      Assert.Empty(decision.Blockers);
    }

    [Fact]
    public void BindsExistingDefinitionWithoutDeletingAnything()
    {
      HbrBindingPlanDecision decision = Evaluate(
        definitionExists: true,
        bindingExists: false);

      Assert.Equal(HbrBindingActions.BindExisting, decision.Action);
      Assert.DoesNotContain("DELETE", decision.Action, StringComparison.OrdinalIgnoreCase);
      Assert.Empty(decision.Blockers);
    }

    [Fact]
    public void ReusesSatisfiedBinding()
    {
      HbrBindingPlanDecision decision = Evaluate(
        existingCategories: new[] { "OST_Floors", "OST_Walls" },
        requestedCategories: new[] { "OST_Floors" });

      Assert.Equal(HbrBindingActions.Reuse, decision.Action);
      Assert.Equal(new[] { "OST_Floors", "OST_Walls" }, decision.Categories);
    }

    [Fact]
    public void MergesRequestedCategoriesWithoutNarrowingExistingOnes()
    {
      HbrBindingPlanDecision decision = Evaluate(
        existingCategories: new[] { "OST_Walls", "OST_Floors" },
        requestedCategories: new[] { "OST_Roofs", "OST_Floors" });

      Assert.Equal(HbrBindingActions.MergeCategories, decision.Action);
      Assert.Equal(
        new[] { "OST_Floors", "OST_Roofs", "OST_Walls" },
        decision.Categories);
    }

    [Theory]
    [InlineData("TYPE", "String", "Text", HbrBindingBlockerCodes.BindingScopeConflict)]
    [InlineData("INSTANCE", "Integer", "Text", HbrBindingBlockerCodes.StorageTypeConflict)]
    [InlineData("INSTANCE", "String", "Length", HbrBindingBlockerCodes.ParameterTypeConflict)]
    public void BlocksBindingAndTypeConflicts(
      string actualScope,
      string actualStorage,
      string actualParameterType,
      string expectedCode)
    {
      HbrBindingPlanDecision decision = Evaluate(
        actualScope: actualScope,
        actualStorage: actualStorage,
        actualParameterType: actualParameterType);

      Assert.Contains(decision.Blockers, blocker => blocker.Code == expectedCode);
    }

    [Fact]
    public void BlocksHiddenOrNonEditableDefinitions()
    {
      HbrBindingPlanDecision decision = Evaluate(
        visible: false,
        userModifiable: false,
        hideWhenNoValue: true);

      Assert.Contains(decision.Blockers, x => x.Code == HbrBindingBlockerCodes.HiddenDefinition);
      Assert.Contains(decision.Blockers, x => x.Code == HbrBindingBlockerCodes.NotUserModifiable);
      Assert.Contains(decision.Blockers, x => x.Code == HbrBindingBlockerCodes.HideWhenNoValue);
    }

    [Theory]
    [InlineData("Integer", "Text", HbrBindingBlockerCodes.StorageTypeConflict)]
    [InlineData("String", "Length", HbrBindingBlockerCodes.ParameterTypeConflict)]
    public void BlocksUnboundDefinitionTypeConflicts(
      string actualStorage,
      string actualParameterType,
      string expectedCode)
    {
      HbrBindingPlanDecision decision = Evaluate(
        bindingExists: false,
        actualStorage: actualStorage,
        actualParameterType: actualParameterType);

      Assert.Contains(decision.Blockers, blocker => blocker.Code == expectedCode);
    }

    [Fact]
    public void AcceptsCanonicalOrDeclaredLegacyNameOnly()
    {
      Assert.Empty(Evaluate(definitionName: "HBR｜Pset｜属性").Blockers);
      Assert.Empty(Evaluate(definitionName: "HIFC.Legacy").Blockers);

      HbrBindingPlanDecision unknown = Evaluate(definitionName: "UnknownSameGuidName");
      Assert.Contains(
        unknown.Blockers,
        x => x.Code == HbrBindingBlockerCodes.UnknownSameGuidName);
    }

    [Fact]
    public void SupportsSyntheticTypeBindingPlan()
    {
      var state = new HbrBindingPlanState(
        "HBR｜Pset｜类型属性",
        new[] { "LegacyType" },
        "TYPE",
        "Double",
        "Length",
        true,
        "HBR｜Pset｜类型属性",
        true,
        "TYPE",
        "Double",
        "Length",
        true,
        true,
        false,
        new[] { "OST_Walls" },
        new[] { "OST_Walls", "OST_Floors" });

      HbrBindingPlanDecision decision = HbrBindingPlanPolicy.Evaluate(state);

      Assert.Equal(HbrBindingActions.MergeCategories, decision.Action);
      Assert.Empty(decision.Blockers);
    }

    private static HbrBindingPlanDecision Evaluate(
      bool definitionExists = true,
      string definitionName = "HBR｜Pset｜属性",
      bool bindingExists = true,
      string actualScope = "INSTANCE",
      string actualStorage = "String",
      string actualParameterType = "Text",
      bool visible = true,
      bool userModifiable = true,
      bool hideWhenNoValue = false,
      string[] existingCategories = null,
      string[] requestedCategories = null)
    {
      return HbrBindingPlanPolicy.Evaluate(new HbrBindingPlanState(
        "HBR｜Pset｜属性",
        new[] { "HIFC.Legacy" },
        "INSTANCE",
        "String",
        "Text",
        definitionExists,
        definitionName,
        bindingExists,
        actualScope,
        actualStorage,
        actualParameterType,
        visible,
        userModifiable,
        hideWhenNoValue,
        existingCategories ?? new[] { "OST_Floors" },
        requestedCategories ?? new[] { "OST_Floors" }));
    }
  }
}
