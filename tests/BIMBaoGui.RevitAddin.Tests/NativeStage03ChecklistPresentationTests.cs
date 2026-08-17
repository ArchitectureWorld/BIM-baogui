using BIMBaoGui.RevitAddin.Stage03;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage03ChecklistPresentationTests
  {
    [Theory]
    [InlineData(0, "#FFE5E7EB")]
    [InlineData(1, "#FFDCFCE7")]
    [InlineData(2, "#FFFEE2E2")]
    [InlineData(3, "#FFFEF3C7")]
    public void Checklist_status_has_stable_background(
      int status,
      string expected)
    {
      Assert.Equal(expected, NativeStage03ChecklistPresentation.Background(
        (NativeStage03ChecklistStatus)status));
    }

    [Theory]
    [InlineData(0, "未检查")]
    [InlineData(1, "通过")]
    [InlineData(2, "失败")]
    [InlineData(3, "警告")]
    public void Checklist_status_has_stable_text(
      int status,
      string expected)
    {
      Assert.Equal(expected, NativeStage03ChecklistPresentation.StatusText(
        (NativeStage03ChecklistStatus)status));
    }
  }
}
