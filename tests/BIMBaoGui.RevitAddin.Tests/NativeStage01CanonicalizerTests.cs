using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01CanonicalizerTests
  {
    [Fact]
    public void CanonicalPayloadIsIndependentOfDictionaryInsertionOrder()
    {
      NativeStage01Model left = CreateModel(reverse: false);
      NativeStage01Model right = CreateModel(reverse: true);

      string leftJson = NativeStage01Canonicalizer.ToJson(left);
      string rightJson = NativeStage01Canonicalizer.ToJson(right);

      Assert.Equal(leftJson, rightJson);
      Assert.Equal(
        NativeStage01Canonicalizer.Sha256(leftJson),
        NativeStage01Canonicalizer.Sha256(rightJson));
    }

    [Fact]
    public void CanonicalPayloadMatchesTheStableStage01EnvelopeContract()
    {
      NativeStage01Model model = CreateModel(reverse: false);
      string json = NativeStage01Canonicalizer.ToJson(model);
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue
      };
      Dictionary<string, object> root =
        serializer.Deserialize<Dictionary<string, object>>(json);
      var values = (Dictionary<string, object>)root["values"];

      Assert.Equal(
        NativeStage01Canonicalizer.PayloadSchemaVersion,
        root["schemaVersion"]);
      Assert.Equal(
        NativeStage01Canonicalizer.PayloadSchemaVersion,
        root["workflowVersion"]);
      Assert.False(values.ContainsKey(NativeStage01Keys.InitializationStatus));
      Assert.True(root.ContainsKey("planningTargets"));
      Assert.True(root.ContainsKey("conditions"));
      Assert.True(root.ContainsKey("organizations"));
    }

    private static NativeStage01Model CreateModel(bool reverse)
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();
      model.Values.Clear();
      model.Conditions.Clear();
      model.PlanningTargets.Clear();
      model.Organizations.Clear();

      KeyValuePair<string, string>[] values =
      {
        new KeyValuePair<string, string>("z", "最后"),
        new KeyValuePair<string, string>("a", "第一"),
        new KeyValuePair<string, string>(
          NativeStage01Keys.WorkflowVersion,
          NativeStage01Canonicalizer.PayloadSchemaVersion),
        new KeyValuePair<string, string>(
          NativeStage01Keys.InitializationStatus,
          "初始化通过")
      };
      IEnumerable<KeyValuePair<string, string>> valueOrder = reverse
        ? new[] { values[3], values[2], values[1], values[0] }
        : values;
      foreach (KeyValuePair<string, string> pair in valueOrder)
        model.Values.Add(pair.Key, pair.Value);

      if (reverse)
      {
        model.Conditions.Add("b", true);
        model.Conditions.Add("a", false);
      }
      else
      {
        model.Conditions.Add("a", false);
        model.Conditions.Add("b", true);
      }

      var targets = new[]
      {
        new KeyValuePair<string, NativePlanningTargetValue>(
          "planning.a",
          new NativePlanningTargetValue(
            "LessOrEqual",
            "2.00",
            string.Empty,
            "Ratio",
            "测试",
            "≤2.00")),
        new KeyValuePair<string, NativePlanningTargetValue>(
          "planning.z",
          new NativePlanningTargetValue(
            "GreaterOrEqual",
            "35",
            string.Empty,
            "Percent",
            "测试",
            "≥35%"))
      };
      IEnumerable<KeyValuePair<string, NativePlanningTargetValue>> targetOrder =
        reverse ? new[] { targets[1], targets[0] } : targets;
      foreach (KeyValuePair<string, NativePlanningTargetValue> pair in targetOrder)
        model.PlanningTargets.Add(pair.Key, pair.Value);

      var organization = new Dictionary<string, string>(StringComparer.Ordinal);
      if (reverse)
      {
        organization.Add("z", "乙");
        organization.Add("a", "甲");
      }
      else
      {
        organization.Add("a", "甲");
        organization.Add("z", "乙");
      }
      model.Organizations.Add(organization);
      return model;
    }
  }
}
