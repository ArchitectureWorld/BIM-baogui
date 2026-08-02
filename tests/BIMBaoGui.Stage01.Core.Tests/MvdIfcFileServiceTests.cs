using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BIMBaoGui.Stage01.Mvd;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class MvdIfcFileServiceTests
  {
    private const string ValidFixture =
      "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n"
      + "#11=IFCPROJECT('project-guid',$,'P',$,$,$,$,(#12),#13);\n"
      + "#23=IFCPROPERTYSINGLEVALUE('基点坐标X',$,IFCREAL(3373266.866),$);\n"
      + "#24=IFCPROPERTYSET('formal-guid',$,'申报信息属性集',$,(#23));\n"
      + "#25=IFCRELDEFINESBYPROPERTIES('formal-rel',$,$,$,(#11),#24);\n"
      + "ENDSEC;\nEND-ISO-10303-21;\n";

    [Fact]
    public void Execute_never_changes_source_and_creates_new_MVD_file()
    {
      string directory = CreateTemporaryDirectory();
      try
      {
        string source = Path.Combine(directory, "source.ifc");
        string destination = Path.Combine(directory, "source-MVD.ifc");
        File.WriteAllText(source, ValidFixture);
        string beforeHash = ComputeSha256(source);

        MvdIfcFileResult result = new MvdIfcFileService().Execute(
          source,
          destination);

        Assert.True(result.Success);
        Assert.Equal(beforeHash, ComputeSha256(source));
        Assert.Equal(beforeHash, result.SourceSha256);
        Assert.True(File.Exists(destination));
        Assert.Contains("Pset_", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        Assert.Empty(Directory.GetFiles(directory, "*.backup"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Execute_rejects_destination_equal_to_source()
    {
      string directory = CreateTemporaryDirectory();
      try
      {
        string source = Path.Combine(directory, "source.ifc");
        File.WriteAllText(source, ValidFixture);

        Assert.Throws<InvalidOperationException>(
          () => new MvdIfcFileService().Execute(source, source));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Execute_rejects_existing_destination_without_overwrite_or_backup()
    {
      string directory = CreateTemporaryDirectory();
      try
      {
        string source = Path.Combine(directory, "source.ifc");
        string destination = Path.Combine(directory, "target.ifc");
        File.WriteAllText(source, ValidFixture);
        File.WriteAllText(destination, "existing");

        Assert.Throws<IOException>(
          () => new MvdIfcFileService().Execute(source, destination));
        Assert.Equal("existing", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        Assert.Empty(Directory.GetFiles(directory, "*.backup"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Execute_removes_temporary_file_when_validation_fails()
    {
      string directory = CreateTemporaryDirectory();
      try
      {
        string source = Path.Combine(directory, "invalid.ifc");
        string destination = Path.Combine(directory, "invalid-MVD.ifc");
        File.WriteAllText(
          source,
          "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\n"
          + "DATA;\n#1=IFCPROJECT('g',$,'P',$,$,$,$,(#2),#3);\n"
          + "ENDSEC;\nEND-ISO-10303-21;\n");

        Assert.Throws<InvalidDataException>(
          () => new MvdIfcFileService().Execute(source, destination));
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    private static string CreateTemporaryDirectory()
    {
      string path = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui-MvdIfcFileServiceTests-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(path);
      return path;
    }

    private static string ComputeSha256(string path)
    {
      using (FileStream stream = File.OpenRead(path))
      using (SHA256 algorithm = SHA256.Create())
        return string.Concat(algorithm.ComputeHash(stream).Select(value => value.ToString("X2")));
    }
  }
}
