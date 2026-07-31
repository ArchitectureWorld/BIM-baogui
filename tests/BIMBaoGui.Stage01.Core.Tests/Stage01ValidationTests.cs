using System.Collections.Generic;
using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01ValidationTests
  {
    [Fact]
    public void Validate_RejectsUnconfirmedBlankProject()
    {
      Stage01Model model = ValidSiteModel();
      model.ConfirmBlankProject = false;

      ValidationResult result = Stage01Validator.Validate(model, new List<FieldDefinition>());

      Assert.False(result.IsValid);
      Assert.Contains(result.Messages, message => message.FieldKey == "HBR|Precheck|BlankProject");
    }

    [Fact]
    public void Validate_RejectsTrueNorthOutsideSupportedRange()
    {
      Stage01Model model = ValidSiteModel();
      model.SetValue(Stage01Keys.TrueNorthAngle, "181");

      ValidationResult result = Stage01Validator.Validate(model, new List<FieldDefinition>());

      Assert.False(result.IsValid);
      Assert.Contains(result.Messages, message => message.FieldKey == Stage01Keys.TrueNorthAngle);
    }

    [Fact]
    public void Validate_SiteModelRequiresThreePlanningTargets()
    {
      Stage01Model model = BaseModel(PlanningTargetRequirementPolicy.SiteModel);

      ValidationResult result = Stage01Validator.Validate(model, new List<FieldDefinition>());

      Assert.False(result.IsValid);
      Assert.Contains(result.Messages, message => message.FieldKey == PlanningTargetCatalog.Get(PlanningTargetCatalog.BuildingDensityCode).MvdFieldKey);
      Assert.Contains(result.Messages, message => message.FieldKey == PlanningTargetCatalog.Get(PlanningTargetCatalog.FloorAreaRatioCode).MvdFieldKey);
      Assert.Contains(result.Messages, message => message.FieldKey == PlanningTargetCatalog.Get(PlanningTargetCatalog.GreenRateCode).MvdFieldKey);
    }

    [Theory]
    [InlineData(PlanningTargetRequirementPolicy.AboveGroundModel)]
    [InlineData(PlanningTargetRequirementPolicy.UndergroundModel)]
    public void Validate_BuildingModelsDoNotRepeatProjectTargetInput(string modelFileType)
    {
      ValidationResult result = Stage01Validator.Validate(BaseModel(modelFileType), new List<FieldDefinition>());
      Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsCompleteEssentialInput()
    {
      ValidationResult result = Stage01Validator.Validate(ValidSiteModel(), new List<FieldDefinition>());
      Assert.True(result.IsValid);
    }

    private static Stage01Model ValidSiteModel()
    {
      Stage01Model model = BaseModel(PlanningTargetRequirementPolicy.SiteModel);
      AddTarget(model, PlanningTargetCatalog.BuildingDensityCode, PlanningTargetOperator.LessOrEqual, "30");
      AddTarget(model, PlanningTargetCatalog.FloorAreaRatioCode, PlanningTargetOperator.LessOrEqual, "2.00");
      AddTarget(model, PlanningTargetCatalog.GreenRateCode, PlanningTargetOperator.GreaterOrEqual, "35");
      return model;
    }

    private static Stage01Model BaseModel(string modelFileType)
    {
      var model = new Stage01Model { ConfirmBlankProject = true };
      model.SetValue(Stage01Keys.ProjectNumber, "P-001");
      model.SetValue(Stage01Keys.ProjectName, "测试项目");
      model.SetValue(Stage01Keys.SubitemName, "测试子项");
      model.SetValue(Stage01Keys.ModelFileType, modelFileType);
      model.SetValue(Stage01Keys.ModelScope, "报规模型");
      model.SetValue(Stage01Keys.BaseX, "123.45");
      model.SetValue(Stage01Keys.BaseY, "456.78");
      model.SetValue(Stage01Keys.BaseElevation, "20.5");
      model.SetValue(Stage01Keys.CoordinateSystem, "CGCS2000");
      model.SetValue(Stage01Keys.ElevationSystem, "1985国家高程基准");
      model.SetValue(Stage01Keys.TrueNorthAngle, "0");
      return model;
    }

    private static void AddTarget(Stage01Model model, string metricCode, PlanningTargetOperator @operator, string value)
    {
      PlanningTargetDefinition definition = PlanningTargetCatalog.Get(metricCode);
      Assert.True(PlanningTargetValue.TryCreate(
        metricCode,
        @operator,
        value,
        null,
        definition.Unit,
        "项目初始化",
        out PlanningTargetValue target,
        out string error), error);
      model.SetPlanningTarget(target);
    }
  }
}
