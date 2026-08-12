using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01MigrationServiceTests
  {
    [Fact]
    public void Legacy090CandidatePreservesBusinessFactsAndAddsOnlySchemaState()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01Model source = catalog.CreateDefaultStage01Model();
      source.SetValue(NativeStage01Keys.WorkflowVersion, "0.9.0");
      source.SetValue(NativeStage01Keys.ProjectName, "既有项目");
      source.SetValue(NativeStage01Keys.SubitemName, "既有子项");
      source.SetValue(NativeStage01Keys.BaseX, "123.45");
      source.SetCondition(catalog.Conditions.First().ConditionId, true);
      source.Conditions.Remove(catalog.Conditions.Last().ConditionId);
      source.Conditions.Remove(
        NativeProjectConditionDeclarationPolicy.NoneConditionId);
      source.PlanningTargets["planning.floor_area_ratio"] =
        new NativePlanningTargetValue(
          "LessOrEqual", "2.0", string.Empty, "Ratio", "规划条件", "≤2.0");
      source.Organizations.Clear();
      source.Organizations.Add(new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["IfcOrganization|Pset_组织通用属性集|企业名称"] = "设计单位"
      });
      var payload = new NativeStage01Payload
      {
        SchemaVersion = "0.9.0",
        WorkflowVersion = "0.9.0",
        Model = source
      };

      NativeStage01MigrationResult result =
        NativeStage01MigrationService.CreateCandidate(payload, catalog);

      Assert.True(result.Success, result.Message);
      Assert.Equal("0.9.0", result.SourceVersion);
      Assert.Equal("0.9.1", result.TargetVersion);
      Assert.Equal("既有项目", result.Model.GetValue(NativeStage01Keys.ProjectName));
      Assert.Equal("既有子项", result.Model.GetValue(NativeStage01Keys.SubitemName));
      Assert.Equal("123.45", result.Model.GetValue(NativeStage01Keys.BaseX));
      Assert.True(result.Model.GetCondition(catalog.Conditions.First().ConditionId));
      Assert.True(result.Model.Conditions.ContainsKey(
        catalog.Conditions.Last().ConditionId));
      Assert.False(result.Model.GetCondition(catalog.Conditions.Last().ConditionId));
      Assert.True(result.Model.Conditions.ContainsKey(
        NativeProjectConditionDeclarationPolicy.NoneConditionId));
      Assert.False(result.Model.GetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId));
      Assert.Equal("0.9.1", result.Model.GetValue(NativeStage01Keys.WorkflowVersion));
      Assert.Equal("≤2.0", result.Model.PlanningTargets[
        "planning.floor_area_ratio"].MvdText);
      Assert.Equal("设计单位", result.Model.GetOrganizationValue(
        0, "IfcOrganization|Pset_组织通用属性集|企业名称"));

      Assert.Equal("0.9.0", source.GetValue(NativeStage01Keys.WorkflowVersion));
      Assert.False(source.Conditions.ContainsKey(
        NativeProjectConditionDeclarationPolicy.NoneConditionId));
    }

    [Fact]
    public void MigrationPreservesAnExplicitlyEmptyOrganizationArray()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01Model source = catalog.CreateDefaultStage01Model();
      source.SetValue(NativeStage01Keys.WorkflowVersion, "0.9.0");
      source.Organizations.Clear();
      var payload = new NativeStage01Payload
      {
        SchemaVersion = "0.9.0",
        WorkflowVersion = "0.9.0",
        Model = source
      };

      NativeStage01MigrationResult result =
        NativeStage01MigrationService.CreateCandidate(payload, catalog);

      Assert.True(result.Success, result.Message);
      Assert.Empty(result.Model.Organizations);
      Assert.Empty(source.Organizations);
    }

    [Fact]
    public void MigrationPreservesConflictingDeclarationForValidation()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01Model source = catalog.CreateDefaultStage01Model();
      source.SetValue(NativeStage01Keys.WorkflowVersion, "0.9.0");
      string actualConditionId = catalog.Conditions.First().ConditionId;
      source.SetCondition(actualConditionId, true);
      source.SetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId,
        true);
      var payload = new NativeStage01Payload
      {
        SchemaVersion = "0.9.0",
        WorkflowVersion = "0.9.0",
        Model = source
      };

      NativeStage01MigrationResult result =
        NativeStage01MigrationService.CreateCandidate(payload, catalog);

      Assert.True(result.Success, result.Message);
      Assert.True(result.Model.GetCondition(actualConditionId));
      Assert.True(result.Model.GetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId));
      Assert.Equal(
        NativeProjectConditionDeclarationState.Conflict,
        NativeProjectConditionDeclarationPolicy.Evaluate(
          result.Model,
          catalog).State);
    }

    [Fact]
    public void UnsupportedLegacyVersionFailsClosedWithoutCandidate()
    {
      NativeStage01Model source =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();
      source.SetValue(NativeStage01Keys.WorkflowVersion, "0.8.0");

      NativeStage01MigrationResult result =
        NativeStage01MigrationService.CreateCandidate(
          new NativeStage01Payload
          {
            SchemaVersion = "0.8.0",
            WorkflowVersion = "0.8.0",
            Model = source
          },
          NativeRuleCatalog.Current);

      Assert.False(result.Success);
      Assert.Null(result.Model);
      Assert.Equal(
        NativeStage01MigrationCodes.UnsupportedSourceVersion,
        result.ErrorCode);
    }
  }
}
