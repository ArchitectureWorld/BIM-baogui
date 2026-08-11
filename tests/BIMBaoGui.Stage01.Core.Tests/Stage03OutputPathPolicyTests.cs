using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03OutputPathPolicyTests
  {
    [Fact]
    public void Create_builds_three_direct_children_with_one_stable_run_id()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "武汉站模型",
          "20260803-102030-456");

        Assert.Equal(Path.GetFullPath(directory), paths.OutputDirectory);
        Assert.Equal("20260803-102030-456", paths.RunId);
        Assert.Equal(
          "武汉站模型-20260803-102030-456-RAW.ifc",
          Path.GetFileName(paths.RawIfc));
        Assert.Equal(
          "武汉站模型-20260803-102030-456-HIFC-MVD.ifc",
          Path.GetFileName(paths.FinalIfc));
        Assert.Equal(
          "武汉站模型-20260803-102030-456-fields.json",
          Path.GetFileName(paths.FieldReport));
        Assert.All(
          new[] { paths.RawIfc, paths.FinalIfc, paths.FieldReport },
          path => Assert.Equal(
            Path.GetFullPath(directory),
            Path.GetDirectoryName(path)));
        Stage03OutputPathPolicy.ValidateUnused(paths);
        Assert.Empty(Directory.GetFiles(directory));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ValidateUnused_rejects_when_any_formal_target_already_exists(
      int occupiedIndex)
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-001");
        string[] targets =
          { paths.RawIfc, paths.FinalIfc, paths.FieldReport };
        File.WriteAllText(targets[occupiedIndex], "do-not-overwrite");

        IOException error = Assert.Throws<IOException>(() =>
          Stage03OutputPathPolicy.ValidateUnused(paths));

        Assert.Contains(Path.GetFileName(targets[occupiedIndex]), error.Message);
        Assert.Equal(
          "do-not-overwrite",
          File.ReadAllText(targets[occupiedIndex]));
        Assert.Single(Directory.GetFiles(directory));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Theory]
    [MemberData(nameof(InvalidStemAndRunIds))]
    public void Create_rejects_invalid_stem_or_run_id(
      string stem,
      string runId)
    {
      string directory = NewDirectory();
      try
      {
        Assert.ThrowsAny<ArgumentException>(() =>
          Stage03OutputPathPolicy.Create(directory, stem, runId));
        Assert.Empty(Directory.GetFiles(directory));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Create_rejects_empty_or_nonexistent_output_directory()
    {
      Assert.ThrowsAny<ArgumentException>(() =>
        Stage03OutputPathPolicy.Create(null, "model", "run-001"));
      Assert.ThrowsAny<ArgumentException>(() =>
        Stage03OutputPathPolicy.Create(" ", "model", "run-001"));

      string missing = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui-Stage03-missing-" + Guid.NewGuid().ToString("N"));
      Assert.False(Directory.Exists(missing));
      Assert.Throws<DirectoryNotFoundException>(() =>
        Stage03OutputPathPolicy.Create(missing, "model", "run-001"));
      Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Create_rejects_surrounding_directory_whitespace_instead_of_redirecting()
    {
      string directory = NewDirectory();
      try
      {
        Assert.Throws<ArgumentException>(() =>
          Stage03OutputPathPolicy.Create(
            directory + " ",
            "model",
            "run-001"));
        Assert.Empty(Directory.GetFiles(directory));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Theory]
    [InlineData("credential-marker")]
    [InlineData("run-secret-check")]
    [InlineData("run-token-check")]
    [InlineData("run-password-check")]
    [InlineData("run-sk-project-value")]
    public void Create_rejects_run_ids_that_failure_reports_must_redact(
      string runId)
    {
      string directory = NewDirectory();
      try
      {
        Assert.Throws<ArgumentException>(() =>
          Stage03OutputPathPolicy.Create(directory, "model", runId));
        Assert.Empty(Directory.GetFiles(directory));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Create_accepts_128_safe_characters_and_rejects_129()
    {
      string directory = NewDirectory();
      try
      {
        string maximum = new string('R', 128);
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "m",
          maximum);

        Assert.Equal(maximum, paths.RunId);
        Assert.Throws<ArgumentException>(() =>
          Stage03OutputPathPolicy.Create(
            directory,
            "m",
            new string('R', 129)));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    public static IEnumerable<object[]> InvalidStemAndRunIds()
    {
      yield return new object[] { null, "run-001" };
      yield return new object[] { string.Empty, "run-001" };
      yield return new object[] { " ", "run-001" };
      yield return new object[] { ".", "run-001" };
      yield return new object[] { "..", "run-001" };
      yield return new object[] { @"folder\model", "run-001" };
      yield return new object[] { "folder/model", "run-001" };
      yield return new object[] { Path.GetPathRoot(Path.GetTempPath()), "run-001" };
      yield return new object[] { "model", null };
      yield return new object[] { "model", string.Empty };
      yield return new object[] { "model", " " };
      yield return new object[] { "model", "." };
      yield return new object[] { "model", ".." };
      yield return new object[] { "model", @"..\escape" };
      yield return new object[] { "model", "../escape" };
      yield return new object[] { "model", @"folder\run" };
      yield return new object[] { "model", "folder/run" };
      yield return new object[] { "model", "run:001" };
      yield return new object[] { "model", "run_001" };
      yield return new object[] { "model", " run-001" };
      yield return new object[] { "model", "run-001 " };
    }

    private static string NewDirectory()
    {
      string directory = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui-Stage03OutputPathTests-"
        + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      return directory;
    }
  }
}
