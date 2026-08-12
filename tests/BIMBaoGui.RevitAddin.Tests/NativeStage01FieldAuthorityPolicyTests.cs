using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01FieldAuthorityPolicyTests
  {
    [Fact]
    public void NoRecordUsesLiveRevitFieldsAsInitialValuesOnly()
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();
      model.SetValue(NativeStage01Keys.SubitemName, "用户子项");
      var evidence = CreateEvidence();

      NativeStage01FieldAuthorityPolicy.ApplyInitialValues(model, evidence);

      Assert.Equal("现场项目", model.GetValue(NativeStage01Keys.ProjectName));
      Assert.Equal("P-001", model.GetValue(NativeStage01Keys.ProjectNumber));
      Assert.Equal("123.5", model.GetValue(NativeStage01Keys.BaseX));
      Assert.Equal("456.25", model.GetValue(NativeStage01Keys.BaseY));
      Assert.Equal("35.8", model.GetValue(NativeStage01Keys.BaseElevation));
      Assert.Equal("17.5", model.GetValue(NativeStage01Keys.TrueNorthAngle));
      Assert.Equal("m", model.GetValue(NativeStage01Keys.LengthUnit));
      Assert.Equal("m²", model.GetValue(NativeStage01Keys.AreaUnit));
      Assert.Equal("°", model.GetValue(NativeStage01Keys.AngleUnit));
      Assert.Equal("用户子项", model.GetValue(NativeStage01Keys.SubitemName));
    }

    [Fact]
    public void NoRecordKeepsTargetUnitsWhenLiveRevitUnitsDiffer()
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();
      NativeStage01LiveEvidence evidence = CreateEvidence();
      evidence.LengthUnit = "DUT_DECIMAL_FEET";
      evidence.AreaUnit = "DUT_SQUARE_FEET";
      evidence.AngleUnit = "DUT_RADIANS";

      NativeStage01FieldAuthorityPolicy.ApplyInitialValues(model, evidence);

      Assert.Equal("m", model.GetValue(NativeStage01Keys.LengthUnit));
      Assert.Equal("m²", model.GetValue(NativeStage01Keys.AreaUnit));
      Assert.Equal("°", model.GetValue(NativeStage01Keys.AngleUnit));
    }

    [Fact]
    public void CurrentPayloadIsComparedWithoutMutation()
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();
      model.SetValue(NativeStage01Keys.ProjectName, "上次项目");
      model.SetValue(NativeStage01Keys.BaseX, "100");
      model.SetValue(NativeStage01Keys.BaseY, "200");
      NativeStage01Model before = model.Clone();
      NativeStage01LiveEvidence evidence = CreateEvidence();

      var drifts = NativeStage01FieldAuthorityPolicy.Compare(model, evidence);

      Assert.Equal(
        before.GetValue(NativeStage01Keys.ProjectName),
        model.GetValue(NativeStage01Keys.ProjectName));
      Assert.Equal(
        before.GetValue(NativeStage01Keys.BaseX),
        model.GetValue(NativeStage01Keys.BaseX));
      Assert.Contains(drifts, drift => string.Equals(
        drift.FieldKey,
        NativeStage01Keys.ProjectName,
        StringComparison.Ordinal));
      Assert.Contains(drifts, drift => string.Equals(
        drift.FieldKey,
        NativeStage01Keys.BaseX,
        StringComparison.Ordinal));
      Assert.Contains(drifts, drift => string.Equals(
        drift.FieldKey,
        NativeStage01Keys.BaseY,
        StringComparison.Ordinal));
    }

    [Fact]
    public void NumericEquivalentRepresentationsDoNotCreateDrift()
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();
      NativeStage01LiveEvidence evidence = CreateEvidence();
      model.SetValue(NativeStage01Keys.ProjectName, evidence.ProjectName);
      model.SetValue(NativeStage01Keys.ProjectNumber, evidence.ProjectNumber);
      model.SetValue(NativeStage01Keys.BaseX, "123.5000");
      model.SetValue(NativeStage01Keys.BaseY, "456.250000");
      model.SetValue(NativeStage01Keys.BaseElevation, "35.8000");
      model.SetValue(NativeStage01Keys.TrueNorthAngle, "17.50000");
      model.SetValue(NativeStage01Keys.LengthUnit, evidence.LengthUnit);
      model.SetValue(NativeStage01Keys.AreaUnit, evidence.AreaUnit);
      model.SetValue(NativeStage01Keys.AngleUnit, evidence.AngleUnit);

      var drifts = NativeStage01FieldAuthorityPolicy.Compare(model, evidence);

      Assert.Empty(drifts);
    }

    [Fact]
    public void CoordinateDriftLabelsFreezeXNorthingAndYEasting()
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();
      model.SetValue(NativeStage01Keys.BaseX, "0");
      model.SetValue(NativeStage01Keys.BaseY, "0");

      var drifts = NativeStage01FieldAuthorityPolicy.Compare(
        model,
        CreateEvidence());

      NativeStage01Drift x = drifts.Single(value => string.Equals(
        value.FieldKey,
        NativeStage01Keys.BaseX,
        StringComparison.Ordinal));
      NativeStage01Drift y = drifts.Single(value => string.Equals(
        value.FieldKey,
        NativeStage01Keys.BaseY,
        StringComparison.Ordinal));
      Assert.Contains("南北", x.Label);
      Assert.Contains("东西", y.Label);
      Assert.Equal("REVIT_PROJECT_POSITION", x.AuthoritySource);
      Assert.Equal("REVIT_PROJECT_POSITION", y.AuthoritySource);
    }

    private static NativeStage01LiveEvidence CreateEvidence()
    {
      return new NativeStage01LiveEvidence
      {
        ProjectInformationAvailable = true,
        ProjectName = "现场项目",
        ProjectNumber = "P-001",
        ProjectPositionAvailable = true,
        BaseX = "123.5",
        BaseY = "456.25",
        BaseElevation = "35.8",
        TrueNorthAngle = "17.5",
        UnitsAvailable = true,
        LengthUnit = "m",
        AreaUnit = "m²",
        AngleUnit = "°"
      };
    }
  }
}
