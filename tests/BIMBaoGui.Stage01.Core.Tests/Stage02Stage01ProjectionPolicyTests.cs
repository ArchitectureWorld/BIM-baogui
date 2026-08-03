using System;
using System.Collections.Generic;
using BIMBaoGui.Stage01.Stage02;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage02Stage01ProjectionPolicyTests
  {
    [Fact]
    public void Non_organization_role_uses_root_values()
    {
      var root = Values("root-key", "root-value");
      var organizations = new[] { Values("org-key", "org-value") };

      Stage02Stage01ProjectionResult result =
        Stage02Stage01ProjectionPolicy.Resolve(
          "PROJECT",
          string.Empty,
          root,
          organizations);

      Assert.True(result.Success);
      Assert.Equal("root-value", result.Values["root-key"]);
      Assert.Equal(string.Empty, result.RecordIdentity);
      Assert.Empty(result.Blockers);
    }

    [Fact]
    public void Single_organization_is_selected_deterministically()
    {
      var organizations = new[] { Values("org-key", "org-value") };

      Stage02Stage01ProjectionResult result =
        Stage02Stage01ProjectionPolicy.Resolve(
          "ORGANIZATION",
          string.Empty,
          Values("root-key", "root-value"),
          organizations);

      Assert.True(result.Success);
      Assert.Equal("org-value", result.Values["org-key"]);
      Assert.Equal(
        "STAGE01_ORGANIZATION_INDEX:0",
        result.RecordIdentity);
      Assert.Empty(result.Blockers);
    }

    [Fact]
    public void Multiple_organizations_without_identity_are_blocked()
    {
      var organizations = new[]
      {
        Values("org-key", "first"),
        Values("org-key", "second")
      };

      Stage02Stage01ProjectionResult result =
        Stage02Stage01ProjectionPolicy.Resolve(
          "ORGANIZATION",
          string.Empty,
          Values("root-key", "root-value"),
          organizations);

      Assert.False(result.Success);
      Stage02Blocker blocker = Assert.Single(result.Blockers);
      Assert.Equal("AMBIGUOUS_STAGE01_ORGANIZATION", blocker.Code);
      Assert.Empty(result.Values);
    }

    [Fact]
    public void Explicit_organization_identity_selects_exact_record()
    {
      var organizations = new[]
      {
        Values("org-key", "first"),
        Values("org-key", "second")
      };

      Stage02Stage01ProjectionResult result =
        Stage02Stage01ProjectionPolicy.Resolve(
          "ORGANIZATION",
          "STAGE01_ORGANIZATION_INDEX:1",
          Values("root-key", "root-value"),
          organizations);

      Assert.True(result.Success);
      Assert.Equal("second", result.Values["org-key"]);
      Assert.Equal(
        "STAGE01_ORGANIZATION_INDEX:1",
        result.RecordIdentity);
    }

    private static Dictionary<string, string> Values(
      string key,
      string value)
    {
      return new Dictionary<string, string>(StringComparer.Ordinal)
      {
        [key] = value
      };
    }
  }
}
