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
      Stage01Model model = ValidModel();
      model.ConfirmBlankProject = false;

      ValidationResult result = Stage01Validator.Validate(model, new List<FieldDefinition>());

      Assert.False(result.IsValid);
      Assert.Contains(result.Messages, message => message.FieldKey == "HBR|Precheck|BlankProject");
    }

    [Fact]
    public void Validate_RejectsTrueNorthOutsideSupportedRange()
    {
      Stage01Model model = ValidModel();
      model.SetValue(Stage01Keys.TrueNorthAngle, "181");

      ValidationResult result = Stage01Validator.Validate(model, new List<FieldDefinition>());

      Assert.False(result.IsValid);
      Assert.Contains(result.Messages, message => message.FieldKey == Stage01Keys.TrueNorthAngle);
    }

    [Fact]
    public void Validate_AcceptsCompleteEssentialInput()
    {
      ValidationResult result = Stage01Validator.Validate(ValidModel(), new List<FieldDefinition>());
      Assert.True(result.IsValid);
    }

    private static Stage01Model ValidModel()
    {
      var model = new Stage01Model { ConfirmBlankProject = true };
      model.SetValue(Stage01Keys.ProjectNumber, "P-001");
      model.SetValue(Stage01Keys.ProjectName, "测试项目");
      model.SetValue(Stage01Keys.SubitemName, "总平");
      model.SetValue(Stage01Keys.ModelFileType, "总平模型");
      model.SetValue(Stage01Keys.ModelScope, "项目总平面报规模型");
      model.SetValue(Stage01Keys.BaseX, "123.45");
      model.SetValue(Stage01Keys.BaseY, "456.78");
      model.SetValue(Stage01Keys.BaseElevation, "20.5");
      model.SetValue(Stage01Keys.CoordinateSystem, "CGCS2000");
      model.SetValue(Stage01Keys.ElevationSystem, "1985国家高程基准");
      model.SetValue(Stage01Keys.TrueNorthAngle, "0");
      return model;
    }
  }
}
