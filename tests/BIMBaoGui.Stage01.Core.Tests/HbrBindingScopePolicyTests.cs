using System;
using BIMBaoGui.Stage01.Revit.Parameters;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrBindingScopePolicyTests
  {
    [Fact]
    public void Type_and_instance_scopes_are_explicit()
    {
      Assert.True(HbrBindingScopePolicy.RequiresTypeBinding("TYPE"));
      Assert.False(HbrBindingScopePolicy.RequiresTypeBinding("INSTANCE"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("type")]
    [InlineData("UNKNOWN")]
    public void Unknown_scope_is_rejected_instead_of_defaulting_to_instance(
      string scope)
    {
      InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
        () => HbrBindingScopePolicy.RequiresTypeBinding(scope));

      Assert.Contains("bindingScope", exception.Message);
    }
  }
}
