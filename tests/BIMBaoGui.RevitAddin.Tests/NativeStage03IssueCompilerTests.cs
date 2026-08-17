using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage03;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage03IssueCompilerTests
  {
    [Fact]
    public void Project_metric_issue_routes_to_exact_02b_property()
    {
      NativeIssueRecord issue = NativeStage03IssueCompiler.Compile(
        new NativeStage03ChecklistItem
        {
          CheckId = "STAGE02B.METRIC.201a00ac-3672-5ded-83d2-ed96f81bfabf",
          SourceStage = NativeReportingSourceStage.Stage02B,
          Status = NativeStage03ChecklistStatus.Failed,
          IssueCode = "MISSING_REQUIRED_DATA",
          PropertyId = "201a00ac-3672-5ded-83d2-ed96f81bfabf",
          RemediationTarget = "OPEN_STAGE02B"
        });

      Assert.Equal(NativeIssueNavigationAction.OpenStage02B, issue.Route);
      Assert.Equal("201a00ac-3672-5ded-83d2-ed96f81bfabf", issue.PropertyId);
      Assert.False(string.IsNullOrWhiteSpace(issue.Missing));
      Assert.False(string.IsNullOrWhiteSpace(issue.Impact));
      Assert.False(string.IsNullOrWhiteSpace(issue.Remediation));
    }

    [Fact]
    public void Existing_stage02a_element_uses_elements_without_scalar_assumptions()
    {
      NativeIssueRecord issue = NativeStage03IssueCompiler.Compile(
        new NativeStage03ChecklistItem
        {
          CheckId = "STAGE02A.EXISTING",
          SourceStage = NativeReportingSourceStage.Stage02A,
          Status = NativeStage03ChecklistStatus.Failed,
          IssueCode = "BAD_ELEMENT",
          Elements = new[] { new NativeIssueElementReference
          {
            UniqueId = "element-unique-id",
            ElementName = "墙"
          } }
        });

      Assert.Equal(NativeIssueNavigationAction.Select, issue.Route);
      Assert.Single(issue.Elements);
      Assert.Equal("element-unique-id", issue.Elements[0].UniqueId);
    }

    [Fact]
    public void Missing_stage02a_semantic_role_opens_role_without_fabricated_element()
    {
      NativeIssueRecord issue = NativeStage03IssueCompiler.Compile(
        new NativeStage03ChecklistItem
        {
          CheckId = "STAGE02A.ROLE",
          SourceStage = NativeReportingSourceStage.Stage02A,
          Status = NativeStage03ChecklistStatus.Failed,
          IssueCode = "ROLE_MISSING",
          RoleId = "ROLE.WALL"
        });

      Assert.Equal(NativeIssueNavigationAction.OpenStage02A, issue.Route);
      Assert.Equal("ROLE.WALL", issue.RoleId);
      Assert.Empty(issue.Elements);
    }
  }
}
