using System;
using System.IO;
using System.Linq;
using BIMBaoGui.HifcCore;
using Xunit;

namespace BIMBaoGui.HifcCore.Tests
{
  public sealed class HifcCoreServiceTests
  {
    [Fact]
    public void Translate_preserves_raw_and_publishes_exactly_validated_ifc4()
    {
      using (var sandbox = new TemporaryDirectory())
      {
        string raw = sandbox.CopyFixture();
        string rawHash = HifcCoreService.ComputeSha256(raw);
        string final = Path.Combine(sandbox.Path, "model_HIFC.ifc");
        string quarantine = Path.Combine(sandbox.Path, "quarantine");
        HifcFieldRequest field = CreateField();

        HifcTranslationResult result = HifcCoreService.Translate(
          new HifcTranslationRequest
          {
            RawIfcPath = raw,
            FinalIfcPath = final,
            QuarantineDirectory = quarantine,
            Fields = new[] { field }
          });

        Assert.True(result.Success, result.ErrorCode + ": " + result.Message);
        Assert.Equal(HifcCoreStatus.InternalValidated, result.InternalStatus);
        Assert.Equal(
          HifcCoreStatus.IfcFluxManualPending,
          result.IfcFluxStatus);
        Assert.Equal("IFC4", result.Schema, ignoreCase: true);
        Assert.True(File.Exists(final));
        Assert.Equal(rawHash, HifcCoreService.ComputeSha256(raw));
        Assert.Equal(rawHash, result.RawIfcSha256);
        Assert.False(string.IsNullOrWhiteSpace(result.FinalIfcSha256));
        HifcFieldEvidence evidence = Assert.Single(result.Fields);
        Assert.True(evidence.Success, evidence.ErrorCode + ": " + evidence.Message);
        Assert.True(evidence.OwnerId.HasValue);
        Assert.True(evidence.PropertySetId.HasValue);
        Assert.True(evidence.PropertyId.HasValue);
        Assert.True(evidence.RelationshipId.HasValue);

        HifcValidationResult validation = HifcCoreService.ValidateFile(
          final,
          new[] { field });
        Assert.True(validation.Success, validation.Message);
        Assert.Equal(result.FinalIfcSha256, validation.IfcSha256);
        Assert.Single(validation.Fields);
      }
    }

    [Fact]
    public void Translate_rejects_duplicate_property_identity_before_mutation()
    {
      using (var sandbox = new TemporaryDirectory())
      {
        string raw = sandbox.CopyFixture();
        string final = Path.Combine(sandbox.Path, "duplicate.ifc");
        HifcFieldRequest first = CreateField();
        HifcFieldRequest second = CreateField();
        second.PropertyName = "另一个属性";

        HifcTranslationResult result = HifcCoreService.Translate(
          new HifcTranslationRequest
          {
            RawIfcPath = raw,
            FinalIfcPath = final,
            QuarantineDirectory = Path.Combine(sandbox.Path, "quarantine"),
            Fields = new[] { first, second }
          });

        Assert.False(result.Success);
        Assert.Equal(
          HifcCoreErrorCodes.ExactValidationFailed,
          result.ErrorCode);
        Assert.False(File.Exists(final));
      }
    }

    [Fact]
    public void ValidateFile_rejects_unsupported_schema()
    {
      using (var sandbox = new TemporaryDirectory())
      {
        string source = sandbox.CopyFixture();
        string path = Path.Combine(sandbox.Path, "ifc2x3.ifc");
        string text = File.ReadAllText(source)
          .Replace("FILE_SCHEMA(('IFC4'));", "FILE_SCHEMA(('IFC2X3'));");
        File.WriteAllText(
          path,
          text,
          new System.Text.UTF8Encoding(false));

        HifcValidationResult result = HifcCoreService.ValidateFile(
          path,
          new[] { CreateField() });

        Assert.False(result.Success);
        Assert.Equal(HifcCoreErrorCodes.SchemaUnsupported, result.ErrorCode);
      }
    }

    private static HifcFieldRequest CreateField()
    {
      return new HifcFieldRequest
      {
        PropertyIdentity = "TEST.PROJECT.TEXT|PROJECT|project-information",
        SemanticKey = "BIMBaoGui|Stage03|Project|Text",
        OwnerEntityType = "IfcProject",
        OwnerStrategy = HifcOwnerStrategies.SingleEntityByType,
        PropertySetName = "Pset_BIMBaoGui_Stage03_Test",
        PropertyName = "插件内部测试字段",
        DeclaredIfcType = "IfcLabel",
        CanonicalValue = "Stage03",
        CanonicalUnit = string.Empty
      };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
      internal TemporaryDirectory()
      {
        Path = System.IO.Path.Combine(
          System.IO.Path.GetTempPath(),
          "BIMBaoGui.HifcCore.Tests",
          Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
      }

      internal string Path { get; }

      internal string CopyFixture()
      {
        string source = System.IO.Path.Combine(
          AppContext.BaseDirectory,
          "Fixtures",
          "HBR_HIFC_全映射结构验证_v1.0.ifc");
        string target = System.IO.Path.Combine(Path, "model_RAW.ifc");
        File.Copy(source, target, false);
        return target;
      }

      public void Dispose()
      {
        try
        {
          if (Directory.Exists(Path))
            Directory.Delete(Path, true);
        }
        catch
        {
        }
      }
    }
  }
}
