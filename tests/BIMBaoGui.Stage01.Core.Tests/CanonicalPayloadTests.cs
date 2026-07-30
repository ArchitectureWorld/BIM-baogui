using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class CanonicalPayloadTests
  {
    [Fact]
    public void Build_IsDeterministicRegardlessOfInsertionOrder()
    {
      var first = new Stage01Model();
      first.SetValue("b", "2");
      first.SetValue("a", "1");
      first.SetCondition("z", true);
      first.SetCondition("a", false);

      var second = new Stage01Model();
      second.SetValue("a", "1");
      second.SetValue("b", "2");
      second.SetCondition("a", false);
      second.SetCondition("z", true);

      Assert.Equal(CanonicalPayload.Build(first), CanonicalPayload.Build(second));
      Assert.Equal(CanonicalPayload.Sha256(CanonicalPayload.Build(first)), CanonicalPayload.Sha256(CanonicalPayload.Build(second)));
    }

    [Fact]
    public void Build_DoesNotIncludeVolatileInitializationStatus()
    {
      var first = new Stage01Model();
      first.SetValue(Stage01Keys.ProjectNumber, "P-001");
      first.SetValue(Stage01Keys.InitializationStatus, "待提交");
      var second = first.Clone();
      second.SetValue(Stage01Keys.InitializationStatus, "初始化通过");

      Assert.Equal(CanonicalPayload.Build(first), CanonicalPayload.Build(second));
    }
  }
}
