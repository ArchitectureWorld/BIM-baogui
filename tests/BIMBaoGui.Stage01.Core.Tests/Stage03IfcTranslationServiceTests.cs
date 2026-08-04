using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BIMBaoGui.Stage01.Mvd;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03IfcTranslationServiceTests
  {
    private const string OwnerGlobalId = "3t3TDZl_D9NOIWB0BSjzJI";
    private static readonly UTF8Encoding Utf8NoBom =
      new UTF8Encoding(false, true);

    [Fact]
    public async Task TranslateAsync_returns_before_blocking_candidate_hook_and_runs_off_ui_context()
    {
      using (var fixture = TranslationFixture.Create())
      using (var hookEntered = new ManualResetEventSlim(false))
      using (var releaseHook = new ManualResetEventSlim(false))
      using (var callReturned = new ManualResetEventSlim(false))
      {
        int callingThreadId = 0;
        int hookThreadId = 0;
        Exception callFailure = null;
        Task<Stage03WorkflowTranslationResult> translationTask = null;
        var service = new Stage03IfcTranslationService(candidatePath =>
        {
          hookThreadId = Thread.CurrentThread.ManagedThreadId;
          hookEntered.Set();
          releaseHook.Wait();
        });
        var callingThread = new Thread(() =>
        {
          SynchronizationContext.SetSynchronizationContext(
            new SingleThreadSynchronizationContext());
          callingThreadId = Thread.CurrentThread.ManagedThreadId;
          try
          {
            translationTask = service.TranslateAsync(Request(
              fixture,
              new[] { ScanField() },
              new[] { EnrichmentValue() }));
          }
          catch (Exception exception)
          {
            callFailure = exception;
          }
          finally
          {
            callReturned.Set();
          }
        })
        {
          IsBackground = true,
          Name = "Stage03 translator UI-context probe"
        };

        try
        {
          callingThread.Start();
          Assert.True(
            hookEntered.Wait(TimeSpan.FromSeconds(5)),
            "candidateWritten hook did not start within the timeout.");
          Assert.True(
            callReturned.Wait(TimeSpan.FromSeconds(1)),
            "TranslateAsync did not return before the blocking hook released.");
          Assert.Null(callFailure);
          Assert.NotNull(translationTask);
          Assert.NotEqual(callingThreadId, hookThreadId);

          releaseHook.Set();
          Task completed = await Task.WhenAny(
            translationTask,
            Task.Delay(TimeSpan.FromSeconds(5)));
          Assert.Same(translationTask, completed);
          Stage03WorkflowTranslationResult result = await translationTask;
          Assert.True(result.Success);
        }
        finally
        {
          releaseHook.Set();
          callingThread.Join(TimeSpan.FromSeconds(5));
        }
      }
    }

    [Fact]
    public async Task Missing_property_set_is_enriched_re_read_and_published_without_mutating_raw()
    {
      using (var fixture = TranslationFixture.Create())
      {
        byte[] rawBefore = File.ReadAllBytes(fixture.RawPath);
        string rawHashBefore = Sha256(fixture.RawPath);
        HbrIfcEnrichmentValue value = EnrichmentValue();

        Stage03WorkflowTranslationResult result =
          await new Stage03IfcTranslationService().TranslateAsync(
            Request(fixture, new[] { ScanField() }, new[] { value }));

        Assert.True(result.Success);
        Assert.Empty(result.TechnicalFatalCodes);
        Assert.True(File.Exists(fixture.FinalPath));
        Assert.Equal(rawBefore, File.ReadAllBytes(fixture.RawPath));
        Assert.Equal(rawHashBefore, Sha256(fixture.RawPath));
        Assert.Equal(new FileInfo(fixture.FinalPath).Length, result.FinalIfcLength);
        Assert.Equal(Sha256(fixture.FinalPath), result.FinalIfcSha256);
        Assert.True(result.RawInspection.Success);
        Assert.True(result.FinalInspection.Success);

        HbrIfcBatchInspectionResult diskInspection =
          new HbrIfcFieldInspector().InspectMany(
            IfcStepDocument.Parse(File.ReadAllText(
              fixture.FinalPath,
              Utf8NoBom)),
            new[] { value });
        Assert.True(diskInspection.Success);

        Stage03FieldResult field = Assert.Single(result.Fields);
        Assert.Equal("HBR.TEST", field.PropertyId);
        Assert.Equal("PROJECT", field.Role);
        Assert.Equal("owner-uid", field.OwnerUniqueId);
        Assert.Equal("scan-value", field.RevitNormalizedValue);
        Assert.Equal("#7", field.RawIfcOwner);
        Assert.Equal("Pset_申报信息属性集", field.RawIfcPropertySet);
        Assert.Equal("申报名称", field.RawIfcProperty);
        Assert.Equal("IFCLABEL", field.RawIfcType);
        Assert.Equal("湖北报规", field.RawIfcValue);
        Assert.Equal(Stage03FieldStatus.Pass, field.RawIfcStatus);
        Assert.Equal("#7", field.FinalIfcOwner);
        Assert.Equal("Pset_申报信息属性集", field.FinalIfcPropertySet);
        Assert.Equal("申报名称", field.FinalIfcProperty);
        Assert.Equal("IFCLABEL", field.FinalIfcType);
        Assert.Equal("湖北报规", field.FinalIfcValue);
        Assert.Equal(Stage03FieldStatus.Pass, field.FinalIfcStatus);
      }
    }

    [Fact]
    public async Task Existing_final_target_is_never_overwritten()
    {
      using (var fixture = TranslationFixture.Create())
      {
        byte[] occupied = Utf8NoBom.GetBytes("occupied-final");
        File.WriteAllBytes(fixture.FinalPath, occupied);
        byte[] rawBefore = File.ReadAllBytes(fixture.RawPath);

        Stage03WorkflowTranslationResult result =
          await new Stage03IfcTranslationService().TranslateAsync(
            Request(
              fixture,
              new[] { ScanField() },
              new[] { EnrichmentValue() }));

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.Equal(occupied, File.ReadAllBytes(fixture.FinalPath));
        Assert.Equal(rawBefore, File.ReadAllBytes(fixture.RawPath));
        Assert.Empty(result.FinalIfcSha256);
        Assert.Equal(0, result.FinalIfcLength);
      }
    }

    [Fact]
    public async Task Malformed_utf8_raw_fails_closed_without_final_claim()
    {
      using (var fixture = TranslationFixture.Create())
      {
        byte[] malformed = { 0xff, 0xfe, 0xfd };
        File.WriteAllBytes(fixture.RawPath, malformed);

        Stage03WorkflowTranslationResult result =
          await new Stage03IfcTranslationService().TranslateAsync(
            Request(
              fixture,
              new[] { ScanField() },
              new[] { EnrichmentValue() }));

        Assert.False(result.Success);
        Assert.False(File.Exists(fixture.FinalPath));
        Assert.Equal(malformed, File.ReadAllBytes(fixture.RawPath));
        Assert.Empty(result.FinalIfcPath);
        Assert.Empty(result.FinalIfcSha256);
        Assert.Equal(0, result.FinalIfcLength);
        Assert.Contains(result.Diagnostics, diagnostic =>
          diagnostic.Code == Stage03TechnicalFatalCodes.InvalidIfc
          && diagnostic.Severity == "ERROR");
        Assert.IsType<DecoderFallbackException>(
          result.FailureException);
      }
    }

    [Fact]
    public async Task Candidate_re_read_failure_retains_temp_and_never_claims_final()
    {
      using (var fixture = TranslationFixture.Create())
      {
        byte[] rawBefore = File.ReadAllBytes(fixture.RawPath);
        var service = new Stage03IfcTranslationService(candidatePath =>
          File.WriteAllText(candidatePath, "not-an-ifc", Utf8NoBom));

        Stage03WorkflowTranslationResult result = await service.TranslateAsync(
          Request(
            fixture,
            new[] { ScanField() },
            new[] { EnrichmentValue() }));

        Assert.False(result.Success);
        Assert.False(File.Exists(fixture.FinalPath));
        Assert.Equal(rawBefore, File.ReadAllBytes(fixture.RawPath));
        Assert.Empty(result.FinalIfcPath);
        Assert.Empty(result.FinalIfcSha256);
        Assert.Equal(0, result.FinalIfcLength);
        string[] partialArtifacts = Directory.GetFiles(fixture.DirectoryPath)
          .Where(path => !string.Equals(
            path,
            fixture.RawPath,
            StringComparison.OrdinalIgnoreCase))
          .ToArray();
        Assert.Single(partialArtifacts);
        Assert.Equal("not-an-ifc", File.ReadAllText(
          partialArtifacts[0],
          Utf8NoBom));
      }
    }

    [Fact]
    public async Task Inactive_duplicate_enrichment_identity_is_preserved_without_fabricated_evidence()
    {
      using (var fixture = TranslationFixture.Create())
      {
        Stage03FieldResult first = InactiveField(17);
        Stage03FieldResult second = InactiveField(18);

        Stage03WorkflowTranslationResult result =
          await new Stage03IfcTranslationService().TranslateAsync(
            Request(
              fixture,
              new[] { second, first },
              Array.Empty<HbrIfcEnrichmentValue>()));

        Assert.True(result.Success);
        Assert.Equal(2, result.Fields.Count);
        Assert.All(result.Fields, field =>
        {
          Assert.Equal(Stage03FieldStatus.NotApplicable, field.Status);
          Assert.Equal(
            Stage03FieldStatus.NotEvaluated,
            field.RawIfcStatus);
          Assert.Equal(
            Stage03FieldStatus.NotEvaluated,
            field.FinalIfcStatus);
          Assert.Empty(field.RawIfcOwner);
          Assert.Empty(field.FinalIfcOwner);
        });
        Assert.Empty(result.RawInspection.Fields);
        Assert.Empty(result.FinalInspection.Fields);
      }
    }

    [Fact]
    public async Task Empty_enrichment_batch_publishes_valid_re_read_copy()
    {
      using (var fixture = TranslationFixture.Create())
      {
        byte[] rawBefore = File.ReadAllBytes(fixture.RawPath);

        Stage03WorkflowTranslationResult result =
          await new Stage03IfcTranslationService().TranslateAsync(
            Request(
              fixture,
              Array.Empty<Stage03FieldResult>(),
              Array.Empty<HbrIfcEnrichmentValue>()));

        Assert.True(result.Success);
        Assert.True(File.Exists(fixture.FinalPath));
        Assert.Equal(rawBefore, File.ReadAllBytes(fixture.RawPath));
        Assert.True(result.RawInspection.Success);
        Assert.Empty(result.RawInspection.Fields);
        Assert.True(result.FinalInspection.Success);
        Assert.Empty(result.FinalInspection.Fields);
        Assert.Empty(result.Fields);
        Assert.Equal(Sha256(fixture.FinalPath), result.FinalIfcSha256);
        Assert.NotEmpty(result.FinalIfcPath);
      }
    }

    private static Stage03WorkflowTranslationRequest Request(
      TranslationFixture fixture,
      Stage03FieldResult[] fields,
      HbrIfcEnrichmentValue[] values)
    {
      return new Stage03WorkflowTranslationRequest(
        fixture.RawPath,
        fixture.FinalPath,
        fields,
        values);
    }

    private static Stage03FieldResult ScanField()
    {
      return new Stage03FieldResult
      {
        PropertyId = "HBR.TEST",
        ContractKind = "OFFICIAL",
        Requirement = "REQUIRED",
        Applicability = "ACTIVE",
        Entity = "IfcProject",
        PropertySet = "Pset_申报信息属性集",
        IfcProperty = "申报名称",
        Role = "PROJECT",
        ElementId = 17,
        OwnerUniqueId = "owner-uid",
        ParameterGuid = "11111111-1111-1111-1111-111111111111",
        ParameterName = "申报名称",
        ParameterScope = "INSTANCE",
        CarrierStatus = Stage03FieldStatus.Pass,
        ParameterStatus = Stage03FieldStatus.Pass,
        RevitStatus = Stage03FieldStatus.Pass,
        RevitRawValue = "scan-value",
        RevitNormalizedValue = "scan-value",
        RevitValueSource = "GUID_INSTANCE:owner-uid",
        RawIfcStatus = Stage03FieldStatus.NotEvaluated,
        FinalIfcStatus = Stage03FieldStatus.NotEvaluated,
        Status = Stage03FieldStatus.Pass,
        Active = true,
        IsBusinessBlocker = false,
        Messages = new[] { "scan-evidence" }
      };
    }

    private static HbrIfcEnrichmentValue EnrichmentValue()
    {
      return new HbrIfcEnrichmentValue
      {
        OwnerEntityType = "IfcProject",
        OwnerGlobalId = OwnerGlobalId,
        OwnerStrategy = HbrIfcOwnerStrategies.GlobalId,
        PropertySetName = "Pset_申报信息属性集",
        PropertyName = "申报名称",
        DeclaredIfcType = "IFCLABEL",
        CanonicalValue = "湖北报规",
        PropertyIdentity = "HBR.TEST|PROJECT|owner-uid",
        SemanticKey = "IfcProject|Pset_申报信息属性集|申报名称"
      };
    }

    private static Stage03FieldResult InactiveField(int elementId)
    {
      Stage03FieldResult field = ScanField();
      field.ElementId = elementId;
      field.Active = false;
      field.Status = Stage03FieldStatus.NotApplicable;
      field.RevitStatus = Stage03FieldStatus.NotEvaluated;
      field.RevitNormalizedValue = string.Empty;
      field.RawIfcStatus = Stage03FieldStatus.NotEvaluated;
      field.FinalIfcStatus = Stage03FieldStatus.NotEvaluated;
      return field;
    }

    private static string CreateIfc()
    {
      return "ISO-10303-21;\r\n"
        + "HEADER;\r\n"
        + "FILE_SCHEMA(('IFC4'));\r\n"
        + "ENDSEC;\r\n"
        + "DATA;\r\n"
        + "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "ENDSEC;\r\n"
        + "END-ISO-10303-21;\r\n";
    }

    private static string Sha256(string path)
    {
      using (SHA256 algorithm = SHA256.Create())
      using (FileStream stream = File.OpenRead(path))
      {
        return BitConverter.ToString(algorithm.ComputeHash(stream))
          .Replace("-", string.Empty)
          .ToLowerInvariant();
      }
    }

    private sealed class TranslationFixture : IDisposable
    {
      private TranslationFixture(string directoryPath)
      {
        DirectoryPath = directoryPath;
        RawPath = Path.Combine(directoryPath, "model.raw.ifc");
        FinalPath = Path.Combine(directoryPath, "model.hifc-mvd.ifc");
        File.WriteAllText(RawPath, CreateIfc(), Utf8NoBom);
      }

      internal string DirectoryPath { get; }
      internal string RawPath { get; }
      internal string FinalPath { get; }

      internal static TranslationFixture Create()
      {
        string directory = Path.Combine(
          Path.GetTempPath(),
          "BIMBaoGui-Stage03-Translator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new TranslationFixture(directory);
      }

      public void Dispose()
      {
        if (Directory.Exists(DirectoryPath))
          Directory.Delete(DirectoryPath, true);
      }
    }

    private sealed class SingleThreadSynchronizationContext
      : SynchronizationContext
    {
    }
  }
}
