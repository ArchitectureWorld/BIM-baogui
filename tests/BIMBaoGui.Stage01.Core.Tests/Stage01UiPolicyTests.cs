using System;
using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01UiPolicyTests
  {
    [Fact]
    public void BuildDirectoryGroups_ShowsEveryUserFacingGroupInStableOrder()
    {
      string[] source =
      {
        "00_当前Revit文件",
        "01_文件与项目身份",
        "01_文件与阶段",
        "02_坐标与高程",
        "03_行政区划",
        "04_地籍信息",
        "05_报建联系人",
        "06_参建组织",
        "07_登记信息",
        "08_规划控制指标",
        "09_提交与回读"
      };

      string[] actual = Stage01UiPolicy.BuildDirectoryGroups(source);

      Assert.Equal(new[]
      {
        "01_文件与项目身份",
        "02_坐标与高程",
        "03_行政区划",
        "04_地籍信息",
        "05_报建联系人",
        "06_参建组织",
        "07_登记信息",
        "08_规划控制指标",
        "10_项目条件",
        "11_提交与校验"
      }, actual);
    }

    [Theory]
    [InlineData("文件与项目身份", true, "文件与项目身份 *")]
    [InlineData("项目条件", false, "项目条件")]
    public void DecorateRequiredLabel_AppendsStarOnlyToRequiredItems(string label, bool required, string expected)
    {
      Assert.Equal(expected, Stage01UiPolicy.DecorateRequiredLabel(label, required));
    }

    [Theory]
    [InlineData(-5, 20, 8, 0)]
    [InlineData(0, 20, 8, 0)]
    [InlineData(7, 20, 8, 7)]
    [InlineData(99, 20, 8, 12)]
    [InlineData(6, 5, 8, 0)]
    public void ClampScrollOffset_StaysInsideScrollableRange(int requested, int itemCount, int visibleCount, int expected)
    {
      Assert.Equal(expected, Stage01UiPolicy.ClampScrollOffset(requested, itemCount, visibleCount));
    }

    [Theory]
    [InlineData(5, 120, 20, 8, 2)]
    [InlineData(5, -120, 20, 8, 8)]
    [InlineData(0, 120, 20, 8, 0)]
    [InlineData(12, -120, 20, 8, 12)]
    public void ScrollByWheel_UsesRowsAndClampsAtBothEnds(int current, int delta, int itemCount, int visibleCount, int expected)
    {
      Assert.Equal(expected, Stage01UiPolicy.ScrollByWheel(current, delta, itemCount, visibleCount));
    }
  }
}
