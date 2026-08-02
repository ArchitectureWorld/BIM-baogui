using System;
using System.IO;
using BIMBaoGui.Stage01.Mvd;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class MvdIfcPathPolicyTests
  {
    [Fact]
    public void ResolveDestination_uses_sibling_MVD_name_by_default()
    {
      string directory = Path.Combine(Path.GetTempPath(), "BIMBaoGui-PathPolicy");
      string source = Path.Combine(directory, "model.ifc");

      string destination = MvdIfcPathPolicy.ResolveDestination(source, null);

      Assert.Equal(Path.Combine(directory, "model-MVD.ifc"), destination);
    }

    [Fact]
    public void ResolveDestination_rejects_case_insensitive_source_equality()
    {
      string source = Path.Combine(Path.GetTempPath(), "Model.ifc");
      string destination = Path.Combine(Path.GetTempPath(), "model.IFC");

      Assert.Throws<InvalidOperationException>(
        () => MvdIfcPathPolicy.ResolveDestination(source, destination));
    }

    [Theory]
    [InlineData("\u202A")]
    [InlineData("\u202B")]
    [InlineData("\u202D")]
    [InlineData("\u202E")]
    [InlineData("\u2066")]
    [InlineData("\u2067")]
    [InlineData("\u2068")]
    [InlineData("\uFEFF")]
    public void ResolveDestination_removes_clipboard_format_characters(
      string formatCharacter)
    {
      string directory = Path.Combine(Path.GetTempPath(), "BIMBaoGui-PathPolicy");
      string source = Path.Combine(directory, "model.ifc");

      string destination = MvdIfcPathPolicy.ResolveDestination(
        formatCharacter + source + "\u202C\u2069",
        null);

      Assert.Equal(Path.Combine(directory, "model-MVD.ifc"), destination);
    }

    [Theory]
    [InlineData("model.txt")]
    [InlineData("model.ifczip")]
    public void ResolveDestination_rejects_non_IFC_extensions(string name)
    {
      string path = Path.Combine(Path.GetTempPath(), name);

      Assert.Throws<InvalidDataException>(
        () => MvdIfcPathPolicy.ResolveDestination(path, null));
    }

    [Fact]
    public void ResolveDestination_rejects_existing_destination()
    {
      string directory = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui-PathPolicy-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      try
      {
        string source = Path.Combine(directory, "source.ifc");
        string destination = Path.Combine(directory, "target.ifc");
        File.WriteAllText(destination, "existing");

        Assert.Throws<IOException>(
          () => MvdIfcPathPolicy.ResolveDestination(source, destination));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }
  }
}
