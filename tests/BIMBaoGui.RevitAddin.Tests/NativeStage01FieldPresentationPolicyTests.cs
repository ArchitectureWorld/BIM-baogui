using System;
using System.Collections.Generic;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01FieldPresentationPolicyTests
  {
    private const string TotalBuildingArea =
      "IfcProject|Pset_登记信息属性集|总建筑面积";

    [Fact]
    public void TotalBuildingAreaIsAStage02BReferenceNotAnEditor()
    {
      NativeStage01FieldDefinition field =
        NativeRuleCatalog.Current.Stage01FieldsByKey[TotalBuildingArea];
      var model = new NativeStage01Model();
      model.SetValue(NativeStage01Keys.ModelFileType, "总平模型");

      NativeStage01FieldPresentation card =
        NativeStage01FieldPresentationPolicy.Build(
          field,
          model,
          new NativeStage01LiveEvidence(),
          new Dictionary<string, NativeStage01FieldOutcome>(),
          null);

      Assert.True(card.ReadOnly);
      Assert.Equal("STAGE02B_REFERENCE", card.Source);
      Assert.Equal("02B", card.NavigationTarget);
      Assert.Equal(TotalBuildingArea, card.Identity);
      Assert.True(card.InCurrentChecklist);
    }

    [Fact]
    public void CurrentStage02BReferenceOutranksOutcomeAndManualValue()
    {
      NativeStage01FieldDefinition field =
        NativeRuleCatalog.Current.Stage01FieldsByKey[TotalBuildingArea];
      var model = new NativeStage01Model();
      model.SetValue(TotalBuildingArea, "100");
      var outcomes = new Dictionary<string, NativeStage01FieldOutcome>
      {
        [TotalBuildingArea] = new NativeStage01FieldOutcome
        {
          FieldKey = TotalBuildingArea,
          Identity = TotalBuildingArea,
          CurrentValue = "200",
          Source = "STAGE01",
          WriteState = NativeStage01FieldOperationState.Succeeded,
          ReadbackState = NativeStage01FieldOperationState.Succeeded
        }
      };
      NativeWorkflowResultEnvelope stage02B = BuildStage02B("300", true);

      NativeStage01FieldPresentation card =
        NativeStage01FieldPresentationPolicy.Build(
          field,
          model,
          new NativeStage01LiveEvidence(),
          outcomes,
          stage02B);

      Assert.Equal("300", card.CurrentValue);
      Assert.Equal("STAGE02B_REFERENCE", card.Source);
      Assert.Equal(NativeStage01FieldOperationState.Succeeded, card.ReadbackState);
    }

    [Fact]
    public void MissingOrFailedStage02BReferenceNeverFallsBackToOldStage01Value()
    {
      NativeStage01FieldDefinition field =
        NativeRuleCatalog.Current.Stage01FieldsByKey[TotalBuildingArea];
      var model = new NativeStage01Model();
      model.SetValue(TotalBuildingArea, "100");
      var outcomes = new Dictionary<string, NativeStage01FieldOutcome>
      {
        [TotalBuildingArea] = new NativeStage01FieldOutcome
        {
          FieldKey = TotalBuildingArea,
          Identity = TotalBuildingArea,
          CurrentValue = "200",
          Source = "STAGE01",
          WriteState = NativeStage01FieldOperationState.Succeeded,
          ReadbackState = NativeStage01FieldOperationState.Succeeded
        }
      };

      NativeStage01FieldPresentation missing =
        NativeStage01FieldPresentationPolicy.Build(
          field,
          model,
          new NativeStage01LiveEvidence(),
          outcomes,
          null);
      NativeStage01FieldPresentation failed =
        NativeStage01FieldPresentationPolicy.Build(
          field,
          model,
          new NativeStage01LiveEvidence(),
          outcomes,
          BuildStage02B("300", false));

      Assert.Equal(string.Empty, missing.CurrentValue);
      Assert.Equal(string.Empty, failed.CurrentValue);
      Assert.Equal("STAGE02B_REFERENCE", missing.Source);
      Assert.Equal("STAGE02B_NOT_COMPLETED", failed.IssueCode);
      Assert.Equal(
        NativeStage01FieldOperationState.NotAttempted,
        failed.ReadbackState);
    }

    [Fact]
    public void RevitLiveValueOutranksLatestOutcomeAndManualValue()
    {
      const string longitude = "IfcProject|Pset_申报信息属性集|经度";
      NativeStage01FieldDefinition field =
        NativeRuleCatalog.Current.Stage01FieldsByKey[longitude];
      var model = new NativeStage01Model();
      model.SetValue(longitude, "113");
      var outcomes = new Dictionary<string, NativeStage01FieldOutcome>
      {
        [longitude] = new NativeStage01FieldOutcome
        {
          FieldKey = longitude,
          Identity = longitude,
          CurrentValue = "114",
          Source = "STAGE01",
          WriteState = NativeStage01FieldOperationState.Succeeded,
          ReadbackState = NativeStage01FieldOperationState.Succeeded
        }
      };
      var live = new NativeStage01LiveEvidence
      {
        GeoLocationAvailable = true,
        Longitude = "115"
      };

      NativeStage01FieldPresentation card =
        NativeStage01FieldPresentationPolicy.Build(
          field,
          model,
          live,
          outcomes,
          null);

      Assert.Equal("115", card.CurrentValue);
      Assert.Equal("REVIT_LIVE", card.Source);
    }

    private static NativeWorkflowResultEnvelope BuildStage02B(
      string value,
      bool readbackSucceeded)
    {
      return NativeWorkflowResultCanonicalizer.Build(
        "run-02b",
        "STAGE02B",
        "METRIC_INPUT",
        new NativeWorkflowIdentity
        {
          DocumentFingerprint = "document",
          ModelFileType = "总平模型",
          RulePackageId = "HBR-WUHAN-PLANNING",
          RulePackageVersion = "1.0.0",
          RulePackageSha256 = new string('a', 64)
        },
        new string('b', 64),
        new[]
        {
          new NativeWorkflowItemEvidence
          {
            Identity = TotalBuildingArea,
            CurrentValue = value,
            Unit = "m²",
            Source = "MANUAL_INPUT",
            WriteSucceeded = true,
            ReadbackSucceeded = readbackSucceeded,
            InputHash = new string('c', 64),
            UpdatedUtc = "2026-08-14T00:00:00.0000000Z"
          }
        },
        "2026-08-14T00:00:00.0000000Z");
    }
  }
}
