using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02ProjectInformationTests
  {
    private static readonly string[] ProjectInformationRoleIds =
    {
      "BUILDING",
      "PROJECT",
      "SITE"
    };

    [Fact]
    public void ProjectInformationCarriesAllApprovedSingleEntityRoles()
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      NativeCarrierRoleDefinition[] roles = ProjectInformationRoleIds
        .Select(value => catalog.CarrierRolesById[value])
        .ToArray();

      NativeStage02RoleMatchResult result = NativeStage02RoleMatcher.Match(
        ProjectInformationCandidate(),
        roles,
        "总平模型");

      Assert.Equal(NativeStage02RoleMatchStatus.Matched, result.Status);
      Assert.Equal("SINGLE_ENTITY_BY_TYPE", result.MatchSource);
      Assert.Equal(ProjectInformationRoleIds, result.CandidateRoleIds);
      Assert.Equal(
        string.Join("+", ProjectInformationRoleIds),
        result.RoleId);
    }

    [Fact]
    public void ProjectInformationPreviewUnionsProjectSiteAndBuildingFields()
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      NativeStage02PropertyDefinition[] properties =
        ProjectInformationRoleIds
          .Select(roleId => catalog.PropertiesForRole(roleId)
            .First(IsPreparableWithoutCondition))
          .ToArray();
      var parameterEvidence = new Dictionary<Guid, NativeStage02ParameterEvidence>();
      foreach (NativeStage02PropertyDefinition property in properties)
      {
        parameterEvidence[property.ParameterGuid] =
          new NativeStage02ParameterEvidence
          {
            ParameterGuid = property.ParameterGuid,
            Exists = false,
            ContractCompatible = true,
            BindingIncludesCategory = false,
            AliasValues = new Dictionary<string, string>(StringComparer.Ordinal)
          };
      }

      NativeStage02Preview preview = NativeStage02PreviewCompiler.Compile(
        new NativeStage02PreviewInput
        {
          DocumentFingerprint = new string('a', 64),
          ModelProfile = "总平模型",
          Conditions = new Dictionary<string, bool>(StringComparer.Ordinal),
          Elements = new[]
          {
            new NativeStage02ElementEvidence
            {
              Element = ProjectInformationCandidate(),
              Parameters = parameterEvidence
            }
          }
        },
        catalog);

      NativeStage02ElementPlan plan = Assert.Single(preview.Elements);
      Assert.Equal(NativeStage02RoleMatchStatus.Matched, plan.RoleMatchStatus);
      Assert.Equal(
        string.Join("+", ProjectInformationRoleIds),
        plan.RoleId);
      Assert.Equal(
        properties.Select(value => value.PropertyId)
          .OrderBy(value => value, StringComparer.Ordinal),
        plan.Fields.Select(value => value.Property.PropertyId)
          .OrderBy(value => value, StringComparer.Ordinal));
      Assert.All(
        plan.Fields,
        field => Assert.Equal(
          NativeStage02FieldStatus.PendingBinding,
          field.Status));
    }

    private static bool IsPreparableWithoutCondition(
      NativeStage02PropertyDefinition property)
    {
      return string.IsNullOrWhiteSpace(property.ConditionId)
        && property.RuntimeDecision != null
        && (property.RuntimeDecision.Status == NativeRuntimeStatuses.Supported
          || property.RuntimeDecision.Status
            == NativeRuntimeStatuses.UnclassifiedRequirement);
    }

    private static NativeStage02ElementSnapshot ProjectInformationCandidate()
    {
      return new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = new string('a', 64),
        UniqueId = "project-information",
        ElementId = 1,
        Category = "OST_ProjectInformation",
        ElementKind = "ProjectInformation",
        ElementName = "项目信息",
        FamilyName = string.Empty,
        TypeName = string.Empty,
        AssignedRoleId = string.Empty,
        IsModelElement = true
      };
    }
  }
}
