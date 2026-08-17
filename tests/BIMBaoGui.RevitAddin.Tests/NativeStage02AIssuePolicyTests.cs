using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02AIssuePolicyTests
  {
    [Fact]
    public void Pending_confirmation_and_failed_geometry_create_stable_stage02a_issues()
    {
      NativeStage02Preview preview = new NativeStage02Preview
      {
        DocumentFingerprint = "doc",
        Elements = new[]
        {
          new NativeStage02ElementPlan
          {
            Element = new NativeStage02ElementSnapshot
            {
              DocumentFingerprint = "doc",
              UniqueId = "A",
              ElementId = 1,
              CategoryName = "建筑地坪",
              ElementName = "绿地一"
            },
            RoleId = "SITE_GREEN_OBJECT",
            EffectiveRoleId = "SITE_GREEN_OBJECT",
            RoleConfirmation = new NativeStage02RoleConfirmationDecision
            {
              Confirmed = false,
              Code = "ROLE_CONFIRMATION_REQUIRED",
              ResolvedRoleId = "SITE_GREEN_OBJECT"
            },
            TaskGeometry = new NativeStage02TaskGeometryEvaluation
            {
              TaskId = "SITE.GREEN",
              ElementUniqueId = "A",
              EvaluationHash = new string('a', 64),
              Checks = new[]
              {
                new NativeStage02GeometryCheckEvidence
                {
                  CheckId = "STAGE02A.GEOMETRY.SITE.GREEN.closed",
                  RuleText = "绿地边界闭合",
                  State = NativeStage02GeometryCheckState.Failed,
                  Code = "GEOMETRY_BOUNDARY_OPEN"
                }
              }
            }
          }
        }
      };

      NativeIssueRecord[] first = NativeStage02IssueCompiler.Compile(preview).ToArray();
      NativeIssueRecord[] second = NativeStage02IssueCompiler.Compile(preview).ToArray();

      Assert.Equal(2, first.Length);
      Assert.Equal(first.Select(value => value.IssueId), second.Select(value => value.IssueId));
      Assert.All(first, value => Assert.Equal("STAGE02A", value.SourceFeature));
      Assert.Contains(first, value => value.Code == "ROLE_CONFIRMATION_REQUIRED");
      Assert.Contains(first, value => value.Code == "GEOMETRY_BOUNDARY_OPEN");
      Assert.All(first, value => Assert.Equal(NativeIssueNavigationAction.OpenStage02A, value.Route));
    }

    [Fact]
    public void One_failed_element_does_not_create_an_issue_for_a_green_element()
    {
      NativeStage02Preview preview = new NativeStage02Preview
      {
        DocumentFingerprint = "doc",
        Elements = new[]
        {
          Plan("bad", NativeStage02GeometryCheckState.Failed),
          Plan("good", NativeStage02GeometryCheckState.Passed)
        }
      };

      NativeIssueRecord issue = Assert.Single(NativeStage02IssueCompiler.Compile(preview));
      Assert.Equal("bad", Assert.Single(issue.Elements).UniqueId);
      Assert.DoesNotContain("good", issue.IssueId);
    }

    [Fact]
    public void Stage02a_issue_compiler_never_emits_project_metric_issues()
    {
      NativeStage02Preview preview = new NativeStage02Preview
      {
        DocumentFingerprint = "doc",
        Elements = new[] { Plan("bad", NativeStage02GeometryCheckState.Failed) }
      };

      string combined = string.Join("|", NativeStage02IssueCompiler.Compile(preview)
        .Select(value => value.CheckId + value.Code + value.Missing));

      Assert.DoesNotContain("STAGE02B", combined);
      Assert.DoesNotContain("总建筑面积", combined);
      Assert.DoesNotContain("建筑密度", combined);
      Assert.DoesNotContain("容积率", combined);
      Assert.DoesNotContain("绿地率", combined);
      Assert.DoesNotContain("停车位", combined);
    }

    private static NativeStage02ElementPlan Plan(
      string uniqueId,
      NativeStage02GeometryCheckState state)
    {
      return new NativeStage02ElementPlan
      {
        Element = new NativeStage02ElementSnapshot
        {
          DocumentFingerprint = "doc",
          UniqueId = uniqueId,
          ElementId = uniqueId.GetHashCode(),
          CategoryName = "建筑地坪",
          ElementName = uniqueId
        },
        RoleId = "SITE_GREEN_OBJECT",
        EffectiveRoleId = "SITE_GREEN_OBJECT",
        RoleConfirmation = new NativeStage02RoleConfirmationDecision
        {
          Confirmed = true,
          Code = "ROLE_CONFIRMED",
          ResolvedRoleId = "SITE_GREEN_OBJECT"
        },
        TaskGeometry = new NativeStage02TaskGeometryEvaluation
        {
          TaskId = "SITE.GREEN",
          ElementUniqueId = uniqueId,
          EvaluationHash = new string('b', 64),
          Checks = new[]
          {
            new NativeStage02GeometryCheckEvidence
            {
              CheckId = "STAGE02A.GEOMETRY.SITE.GREEN.closed",
              RuleText = "绿地边界闭合",
              State = state,
              Code = state == NativeStage02GeometryCheckState.Passed
                ? "GEOMETRY_CHECK_PASSED"
                : "GEOMETRY_BOUNDARY_OPEN"
            }
          }
        }
      };
    }
  }
}
