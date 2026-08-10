using System.Linq;
using BIMBaoGui.Stage01.Rules;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrRuntimeStatusProjectionTests
  {
    [Fact]
    public void Decision_uses_owner_precedence_for_real_frozen_properties()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      HbrRuntimeStatusDecision ownerBlocked =
        database.GetRuntimeStatusDecision(
          database.PropertiesById[
            "d9ae268e-8d11-59e7-bbff-dc7521ec7889"]);
      HbrRuntimeStatusDecision unclassified =
        database.GetRuntimeStatusDecision(
          database.PropertiesById[
            "ee41f5a8-562b-56f4-b8ef-331783746e09"]);

      Assert.Equal("NOT_IMPLEMENTED", ownerBlocked.Status);
      Assert.Equal(
        "OWNER_STRATEGY_NOT_IMPLEMENTED",
        ownerBlocked.ReasonCode);
      Assert.Contains("CANONICAL_SPATIAL_ZONE_RECORD", ownerBlocked.Reason);
      Assert.Equal("UNCLASSIFIED_REQUIREMENT", unclassified.Status);
      Assert.Equal(
        "REQUIREMENT_LEVEL_UNCLASSIFIED",
        unclassified.ReasonCode);
      Assert.Contains("UNCLASSIFIED", unclassified.Reason);
      Assert.Equal(
        ownerBlocked.Status,
        database.GetEffectiveRuntimeStatus(
          database.PropertiesById[
            "d9ae268e-8d11-59e7-bbff-dc7521ec7889"]));
    }

    [Fact]
    public void All_frozen_properties_have_non_empty_typed_decisions()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      HbrRuntimeStatusDecision[] decisions = database.Package.Properties
        .Select(database.GetRuntimeStatusDecision)
        .ToArray();

      Assert.Equal(359, decisions.Length);
      Assert.Equal(
        57,
        decisions.Count(x => x.Status == "NOT_IMPLEMENTED"));
      Assert.Equal(
        302,
        decisions.Count(x => x.Status == "UNCLASSIFIED_REQUIREMENT"));
      Assert.All(decisions, decision =>
      {
        Assert.False(string.IsNullOrWhiteSpace(decision.Status));
        Assert.False(string.IsNullOrWhiteSpace(decision.ReasonCode));
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
      });
    }
  }
}
