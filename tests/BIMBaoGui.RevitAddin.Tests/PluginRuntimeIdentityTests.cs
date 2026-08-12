using BIMBaoGui.RevitAddin.Runtime;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class PluginRuntimeIdentityTests
  {
    [Fact]
    public void CreateNormalizesVersionAndExposesBuildCommitAndPath()
    {
      PluginRuntimeIdentity identity = PluginRuntimeIdentity.Create(
        "0.4.1+build.250.sha.0123456789abcdef",
        "250",
        "0123456789abcdef",
        @"C:\BIMBaoGui\BIMBaoGui.RevitAddin.dll");

      Assert.Equal("0.4.1", identity.ProductVersion);
      Assert.Equal("250", identity.BuildNumber);
      Assert.Equal("0123456789abcdef", identity.CommitSha);
      Assert.Equal("01234567", identity.ShortCommitSha);
      Assert.EndsWith(
        "BIMBaoGui.RevitAddin.dll",
        identity.AssemblyPath,
        System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalBuildUsesExplicitFallbacks()
    {
      PluginRuntimeIdentity identity = PluginRuntimeIdentity.Create(
        "0.4.1",
        "",
        "",
        "");

      Assert.Equal("local", identity.BuildNumber);
      Assert.Equal("unknown", identity.CommitSha);
      Assert.Equal("unknown", identity.ShortCommitSha);
      Assert.Equal("运行时未提供程序集路径", identity.AssemblyPath);
    }
  }
}