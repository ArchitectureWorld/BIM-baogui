using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeProjectConditionDeclarationPolicyTests
  {
    [Fact]
    public void DefaultModelIsUnconfirmedEvenThoughAllActualConditionsAreFalse()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01Model model = catalog.CreateDefaultStage01Model();

      NativeProjectConditionDeclarationDecision decision =
        NativeProjectConditionDeclarationPolicy.Evaluate(model, catalog);

      Assert.Equal(
        NativeProjectConditionDeclarationState.Missing,
        decision.State);
      Assert.False(decision.IsValid);
      Assert.Equal(0, decision.SelectedActualCount);
      Assert.False(model.GetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId));
    }

    [Fact]
    public void ActualAndNoneSelectionsAreMutuallyExclusive()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01Model model = catalog.CreateDefaultStage01Model();
      string actualConditionId = catalog.Conditions.First().ConditionId;

      NativeProjectConditionDeclarationPolicy.SetActualCondition(
        model,
        catalog,
        actualConditionId,
        true);

      Assert.True(model.GetCondition(actualConditionId));
      Assert.False(model.GetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId));
      Assert.Equal(
        NativeProjectConditionDeclarationState.ActualConditionsSelected,
        NativeProjectConditionDeclarationPolicy.Evaluate(model, catalog).State);

      NativeProjectConditionDeclarationPolicy.SetNoConditions(
        model,
        catalog,
        true);

      Assert.True(model.GetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId));
      Assert.All(catalog.Conditions, condition =>
        Assert.False(model.GetCondition(condition.ConditionId)));
      Assert.Equal(
        NativeProjectConditionDeclarationState.NoConditionsSelected,
        NativeProjectConditionDeclarationPolicy.Evaluate(model, catalog).State);
    }

    [Fact]
    public void DirectPayloadConflictIsDetectedInsteadOfSilentlyResolved()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01Model model = catalog.CreateDefaultStage01Model();
      model.SetCondition(catalog.Conditions.First().ConditionId, true);
      model.SetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId,
        true);

      NativeProjectConditionDeclarationDecision decision =
        NativeProjectConditionDeclarationPolicy.Evaluate(model, catalog);

      Assert.Equal(
        NativeProjectConditionDeclarationState.Conflict,
        decision.State);
      Assert.False(decision.IsValid);
    }
  }
}
