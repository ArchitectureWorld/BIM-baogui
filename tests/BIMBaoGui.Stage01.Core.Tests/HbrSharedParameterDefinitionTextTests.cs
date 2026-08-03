using System;
using System.Linq;
using System.IO;
using System.Text;
using BIMBaoGui.Stage01.Revit.Parameters;
using BIMBaoGui.Stage01.Rules;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrSharedParameterDefinitionTextTests
  {
    [Fact]
    public void ProjectsEveryProductionPropertyExactlyOnceAsVisibleEditable()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;

      string text = HbrSharedParameterDefinitionText.Create(
        database.Package.Properties);
      string[] parameterLines = text.Split(new[] { "\r\n" },
        StringSplitOptions.RemoveEmptyEntries)
        .Where(line => line.StartsWith("PARAM\t", StringComparison.Ordinal))
        .ToArray();

      Assert.Equal(359, parameterLines.Length);
      Assert.Equal(
        359,
        parameterLines
          .Select(line => line.Split('\t')[1])
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .Count());
      Assert.All(parameterLines, line =>
      {
        string[] columns = line.Split('\t');
        Assert.Equal("1", columns[6]);
        Assert.Equal("1", columns[8]);
        Assert.Equal("0", columns[9]);
      });
    }

    [Fact]
    public void ProjectionIsDeterministicAndUsesCanonicalNamesAndTypes()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      string first = HbrSharedParameterDefinitionText.Create(
        database.Package.Properties.Reverse());
      string second = HbrSharedParameterDefinitionText.Create(
        database.Package.Properties);

      Assert.Equal(first, second);
      foreach (HbrRuleProperty property in database.Package.Properties)
      {
        string expected = "PARAM\t"
          + property.Revit.ParameterGuid.ToString("D").ToLowerInvariant()
          + "\t"
          + property.Revit.ParameterName
          + "\t"
          + property.Revit.ParameterType.ToUpperInvariant();
        Assert.Contains(expected, first, StringComparison.Ordinal);
      }
    }

    [Fact]
    public void RevitFileBytesUseUtf16LittleEndianBomForChineseNames()
    {
      byte[] bytes = HbrSharedParameterDefinitionText.CreateRevitBytes(
        HbrRuleDatabase.Current.Package.Properties);

      Assert.True(bytes.Length > 2);
      Assert.Equal(0xFF, bytes[0]);
      Assert.Equal(0xFE, bytes[1]);
      string text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
      Assert.Contains("HBR｜", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesActualTemporaryRevitFileAsUtf16LittleEndian()
    {
      string path = Path.Combine(
        Path.GetTempPath(),
        "HBR_shared_parameter_test_" + Guid.NewGuid().ToString("N") + ".txt");
      try
      {
        HbrSharedParameterDefinitionText.WriteRevitFile(
          path,
          HbrRuleDatabase.Current.Package.Properties);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 2);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);
        Assert.Contains(
          "HBR｜",
          Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2),
          StringComparison.Ordinal);
      }
      finally
      {
        if (File.Exists(path)) File.Delete(path);
      }
    }
  }
}
