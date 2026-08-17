using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02CandidatePolicyTests
  {
    [Fact]
    public void Approved_alias_and_carrier_create_candidate_not_assignment()
    {
      NativeReportingSemanticRole role = NativeReportingRuleCatalog.Current
        .GetSemanticRoles("总平模型")
        .Single(value => value.RoleId == "SITE_GREEN_OBJECT");
      NativeStage02SemanticCandidate candidate = Assert.Single(
        NativeStage02CandidatePolicy.Suggest(
          new NativeStage02ElementSnapshot
          {
            DocumentFingerprint = "doc",
            UniqueId = "A",
            ElementId = 1,
            Category = "OST_BuildingPad",
            ElementKind = "BuildingPad",
            ElementName = role.CandidateAliases[0],
            IsModelElement = true
          },
          new[] { role }));

      Assert.Equal("SITE_GREEN_OBJECT", candidate.RoleId);
      Assert.NotEmpty(candidate.Evidence);
      Assert.True(string.IsNullOrEmpty(candidate.Confidence)
        || candidate.Confidence == "HIGH"
        || candidate.Confidence == "LOW");
    }

    [Fact]
    public void Name_hit_on_unapproved_carrier_never_becomes_candidate()
    {
      NativeReportingSemanticRole role = NativeReportingRuleCatalog.Current
        .GetSemanticRoles("总平模型")
        .Single(value => value.RoleId == "SITE_GREEN_OBJECT");

      Assert.Empty(NativeStage02CandidatePolicy.Suggest(
        new NativeStage02ElementSnapshot
        {
          DocumentFingerprint = "doc",
          UniqueId = "A",
          ElementId = 1,
          Category = "OST_TextNotes",
          ElementKind = "TextNote",
          ElementName = role.CandidateAliases[0],
          IsModelElement = false
        },
        new[] { role }));
    }

    [Fact]
    public void Candidates_never_expose_stage02b_metric_identities()
    {
      NativeStage02SemanticCandidate[] candidates = NativeStage02CandidatePolicy
        .Suggest(
          new NativeStage02ElementSnapshot
          {
            DocumentFingerprint = "doc",
            UniqueId = "A",
            ElementId = 1,
            Category = "OST_BuildingPad",
            ElementKind = "BuildingPad",
            ElementName = "集中绿地",
            IsModelElement = true
          },
          NativeReportingRuleCatalog.Current.GetSemanticRoles("总平模型"))
        .ToArray();
      string all = string.Join("|", candidates.SelectMany(value =>
        value.Evidence.Concat(new[] { value.RoleId })));

      Assert.DoesNotContain("总建筑面积", all);
      Assert.DoesNotContain("建筑密度", all);
      Assert.DoesNotContain("容积率", all);
      Assert.DoesNotContain("绿地率", all);
      Assert.DoesNotContain("停车位", all);
    }
  }
}
