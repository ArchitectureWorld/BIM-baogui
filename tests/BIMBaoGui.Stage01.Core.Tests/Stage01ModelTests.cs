using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01ModelTests
  {
    [Fact]
    public void Clone_IsIndependentFromOriginal()
    {
      var original = new Stage01Model();
      original.SetValue("field", "before");
      original.SetCondition("condition", true);
      original.SetOrganizationValue("organization", "before");

      Stage01Model clone = original.Clone();
      clone.SetValue("field", "after");
      clone.SetCondition("condition", false);
      clone.SetOrganizationValue("organization", "after");

      Assert.Equal("before", original.GetValue("field"));
      Assert.True(original.GetCondition("condition"));
      Assert.Equal("before", original.GetOrganizationValue("organization"));
    }
  }
}
