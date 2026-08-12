using System;
using System.Collections.Generic;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01PayloadCodecTests
  {
    [Fact]
    public void CanonicalPayloadRoundTripsWithoutIdentityDrift()
    {
      NativeStage01Model model = CreateModel();
      string json = NativeStage01Canonicalizer.ToJson(model);

      bool decoded = NativeStage01PayloadCodec.TryDecode(
        json,
        out NativeStage01Payload payload,
        out string error);

      Assert.True(decoded, error);
      Assert.Equal(
        NativeStage01Canonicalizer.PayloadSchemaVersion,
        payload.SchemaVersion);
      Assert.Equal(
        NativeStage01Canonicalizer.PayloadSchemaVersion,
        payload.WorkflowVersion);
      Assert.Equal(
        model.GetValue(NativeStage01Keys.FileGuid),
        payload.Model.GetValue(NativeStage01Keys.FileGuid));
      Assert.Equal(json, NativeStage01Canonicalizer.ToJson(payload.Model));
    }

    [Fact]
    public void RoundTripPreservesConditionsPlanningTargetsAndOrganizations()
    {
      NativeStage01Model model = CreateModel();
      model.SetCondition("site.rail", true);
      model.PlanningTargets["planning.floor_area_ratio"] =
        new NativePlanningTargetValue(
          "LessOrEqual",
          "2.00",
          string.Empty,
          "Ratio",
          "规划条件",
          "≤2.00");
      model.Organizations.Clear();
      model.Organizations.Add(new Dictionary<string, string>(
        StringComparer.Ordinal)
      {
        { "IfcOrganization|Pset_组织通用属性集|企业名称", "甲方" },
        { "IfcOrganization|Pset_组织通用属性集|企业编码", "ORG-001" }
      });
      model.Organizations.Add(new Dictionary<string, string>(
        StringComparer.Ordinal)
      {
        { "IfcOrganization|Pset_组织通用属性集|企业名称", "设计方" }
      });
      string json = NativeStage01Canonicalizer.ToJson(model);

      Assert.True(NativeStage01PayloadCodec.TryDecode(
        json,
        out NativeStage01Payload payload,
        out string error), error);
      Assert.True(payload.Model.GetCondition("site.rail"));
      Assert.Equal(
        "≤2.00",
        payload.Model.PlanningTargets["planning.floor_area_ratio"].MvdText);
      Assert.Equal(2, payload.Model.Organizations.Count);
      Assert.Equal(
        "甲方",
        payload.Model.GetOrganizationValue(
          0,
          "IfcOrganization|Pset_组织通用属性集|企业名称"));
      Assert.Equal(
        "设计方",
        payload.Model.GetOrganizationValue(
          1,
          "IfcOrganization|Pset_组织通用属性集|企业名称"));
    }

    [Fact]
    public void DecodePreservesAnExplicitlyEmptyOrganizationArray()
    {
      NativeStage01Model model = CreateModel();
      model.Organizations.Clear();
      string json = NativeStage01Canonicalizer.ToJson(model);

      Assert.True(NativeStage01PayloadCodec.TryDecode(
        json,
        out NativeStage01Payload payload,
        out string error), error);

      Assert.Empty(payload.Model.Organizations);
      Assert.Equal(json, NativeStage01Canonicalizer.ToJson(payload.Model));
    }

    [Fact]
    public void RejectsMalformedOrIncompletePayloads()
    {
      Assert.False(NativeStage01PayloadCodec.TryDecode(
        "{not-json}",
        out _,
        out string malformedError));
      Assert.Contains("解析", malformedError);

      Assert.False(NativeStage01PayloadCodec.TryDecode(
        "{\"schemaVersion\":\"0.9.0\"}",
        out _,
        out string incompleteError));
      Assert.Contains("workflowVersion", incompleteError);
    }

    private static NativeStage01Model CreateModel()
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();
      model.SetValue(
        NativeStage01Keys.FileGuid,
        "11111111-2222-4333-8444-555555555555");
      model.SetValue(
        NativeStage01Keys.WorkflowVersion,
        NativeStage01Canonicalizer.PayloadSchemaVersion);
      return model;
    }
  }
}
