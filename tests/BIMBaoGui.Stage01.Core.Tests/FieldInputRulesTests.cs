using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class FieldInputRulesTests
  {
    [Fact]
    public void BuildPlaceholder_ShowsTypeAndConcreteExample()
    {
      var definition = new FieldDefinition
      {
        Key = Stage01Keys.BaseX,
        Label = "基点坐标 X",
        Kind = FieldKind.Number,
        Essential = true
      };

      string placeholder = FieldInputRules.BuildPlaceholder(definition);

      Assert.Contains("数值", placeholder);
      Assert.Contains("示例", placeholder);
      Assert.Contains("38561234.123", placeholder);
    }

    [Fact]
    public void Validate_RejectsEmptyRequiredValue()
    {
      var definition = new FieldDefinition { Label = "项目编号", Kind = FieldKind.Text };

      string error = FieldInputRules.Validate(definition, string.Empty, true);

      Assert.Equal("该项为必填项。", error);
    }

    [Theory]
    [InlineData("abc", "应填写数值，例如 123.45。")]
    [InlineData("12.5", null)]
    public void Validate_NumberExplainsExpectedFormat(string value, string expected)
    {
      var definition = new FieldDefinition { Label = "容积率", Kind = FieldKind.Number };

      Assert.Equal(expected, FieldInputRules.Validate(definition, value, false));
    }

    [Theory]
    [InlineData("43000", "邮政编码应为 6 位数字，例如 430000。")]
    [InlineData("430000", null)]
    public void Validate_PostalCodeUsesFieldSpecificRule(string value, string expected)
    {
      var definition = new FieldDefinition { Label = "邮政编码", Kind = FieldKind.Text };

      Assert.Equal(expected, FieldInputRules.Validate(definition, value, false));
    }

    [Theory]
    [InlineData("181", "真北角度必须位于 -180° 到 180°。")]
    [InlineData("-45.5", null)]
    public void Validate_TrueNorthUsesSupportedRange(string value, string expected)
    {
      var definition = new FieldDefinition
      {
        Key = Stage01Keys.TrueNorthAngle,
        Label = "真北角度",
        Kind = FieldKind.Number
      };

      Assert.Equal(expected, FieldInputRules.Validate(definition, value, true));
    }
  }
}
