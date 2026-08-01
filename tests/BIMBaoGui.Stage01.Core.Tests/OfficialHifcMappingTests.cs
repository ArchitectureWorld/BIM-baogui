using BIMBaoGui.Stage01.Hifc;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class OfficialHifcMappingTests
  {
    [Fact]
    public void IsTypeBinding_UsesTheTrimmedBindingScope()
    {
      var mapping = new OfficialHifcMapping { BindingScope = " TYPE " };

      Assert.True(mapping.IsTypeBinding);
    }
  }
}
