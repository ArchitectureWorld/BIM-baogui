using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01ConditionSchemaPolicyTests
  {
    [Fact]
    public void Missing_rule_condition_keys_are_reconciled_as_unselected_without_inventing_a_declaration()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      Assert.True(catalog.Conditions.Count >= 2);
      NativeConditionDefinition preserved = catalog.Conditions.First();
      NativeConditionDefinition missing = catalog.Conditions.Last();
      NativeStage01Model model = catalog.CreateDefaultStage01Model();
      model.SetCondition(preserved.ConditionId, true);
      model.SetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId,
        false);
      model.Conditions.Remove(missing.ConditionId);

      NativeStage01ConditionSchemaReconciliation result =
        NativeStage01ConditionSchemaPolicy.Reconcile(model, catalog);

      Assert.True(result.Changed);
      Assert.Contains(missing.ConditionId, result.AddedConditionIds);
      Assert.True(model.GetCondition(preserved.ConditionId));
      Assert.True(model.Conditions.ContainsKey(missing.ConditionId));
      Assert.False(model.GetCondition(missing.ConditionId));
      Assert.True(model.Conditions.ContainsKey(
        NativeProjectConditionDeclarationPolicy.NoneConditionId));
      Assert.False(model.GetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId));
    }

    [Fact]
    public void Complete_condition_schema_is_left_unchanged()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01Model model = catalog.CreateDefaultStage01Model();

      NativeStage01ConditionSchemaReconciliation result =
        NativeStage01ConditionSchemaPolicy.Reconcile(model, catalog);

      Assert.False(result.Changed);
      Assert.Empty(result.AddedConditionIds);
    }
  }
}
