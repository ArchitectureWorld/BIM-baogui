using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BIMBaoGui.Stage01.Mvd;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class MvdIfcRealFileAcceptanceTests
  {
    [Fact]
    public void Normalize_real_IFC_when_acceptance_paths_are_provided()
    {
      string source = Environment.GetEnvironmentVariable(
        "BIMBAOGUI_MVD_SOURCE_IFC");
      string destination = Environment.GetEnvironmentVariable(
        "BIMBAOGUI_MVD_DESTINATION_IFC");
      if (string.IsNullOrWhiteSpace(source)
        && string.IsNullOrWhiteSpace(destination))
        return;

      Assert.False(string.IsNullOrWhiteSpace(source));
      Assert.False(string.IsNullOrWhiteSpace(destination));
      Assert.True(File.Exists(source), "真实验收源 IFC 不存在。");
      Assert.False(File.Exists(destination), "真实验收目标 IFC 必须是新文件。");
      string sourceHashBefore = ComputeSha256(source);

      MvdIfcFileResult result = new MvdIfcFileService().Execute(
        source,
        destination);

      Assert.True(result.Success, string.Join(" | ", result.Messages));
      Assert.Equal(sourceHashBefore, ComputeSha256(source));
      Assert.Equal(sourceHashBefore, result.SourceSha256);
      Assert.True(File.Exists(destination));
      Assert.True(new FileInfo(destination).Length > 0);
    }

    private static string ComputeSha256(string path)
    {
      using (FileStream stream = File.OpenRead(path))
      using (SHA256 algorithm = SHA256.Create())
        return string.Concat(algorithm.ComputeHash(stream)
          .Select(value => value.ToString("X2")));
    }
  }
}
