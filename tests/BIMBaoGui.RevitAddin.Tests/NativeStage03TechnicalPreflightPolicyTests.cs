using System;
using System.IO;
using System.Linq;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage03;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage03TechnicalPreflightPolicyTests
  {
    [Fact]
    public void Output_probe_normalizes_absolute_path_and_leaves_no_probe_file()
    {
      string root = Path.Combine(Path.GetTempPath(),
        "hbr-stage03-preflight-" + Guid.NewGuid().ToString("N"));
      try
      {
        Directory.CreateDirectory(root);

        NativeStage03TechnicalPreflightEvidence result =
          NativeStage03TechnicalPreflightService.Probe(
            root, true, true, true, true);

        Assert.Equal(Path.GetFullPath(root), result.NormalizedOutputDirectory);
        Assert.True(result.OutputDirectoryWritable);
        Assert.Empty(result.FatalCodes);
        Assert.Matches("^[0-9a-f]{64}$", result.ProbeHash);
        Assert.Empty(Directory.GetFiles(root));
      }
      finally
      {
        if (Directory.Exists(root)) Directory.Delete(root, true);
      }
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative-output")]
    public void Empty_or_relative_output_is_technical_fatal(string path)
    {
      NativeStage03TechnicalPreflightEvidence result =
        NativeStage03TechnicalPreflightService.Probe(
          path, true, true, true, true);

      Assert.Contains("INVALID_OUTPUT_DIRECTORY", result.FatalCodes);
      Assert.False(result.OutputDirectoryWritable);
    }

    [Fact]
    public void Dependency_failures_are_exact_sorted_technical_codes()
    {
      string root = Path.GetTempPath();

      NativeStage03TechnicalPreflightEvidence result =
        NativeStage03TechnicalPreflightService.Probe(
          root, false, false, false, false);

      Assert.Equal(new[]
      {
        "DOCUMENT_UNAVAILABLE",
        "IFC_EXPORTER_UNAVAILABLE",
        "REPORT_WRITER_UNAVAILABLE",
        "TRANSLATOR_DEPENDENCY_UNAVAILABLE"
      }, result.FatalCodes);
    }

    [Fact]
    public void Scan_request_clone_keeps_output_directory_without_model_profile_input()
    {
      var request = new NativeStage03ScanRequest
      {
        Mode = NativeStage03Mode.ForcedTest,
        ForceReason = "test",
        OutputDirectory = "C:\\output"
      };

      NativeStage03ScanRequest clone = request.Clone();

      Assert.Equal(request.OutputDirectory, clone.OutputDirectory);
      Assert.Equal(request.Mode, clone.Mode);
      Assert.Equal(request.ForceReason, clone.ForceReason);
    }

    [Fact]
    public void Scan_hash_binds_runtime_preflight_manifest_readbacks_checks_and_results()
    {
      NativeStage03ScanResult baseline = Scan();
      string hash = NativeStage03Canonicalizer.ComputeHash(baseline);

      NativeStage03ScanResult changed = Scan();
      changed.PluginRuntime.AddinDllSha256 = new string('8', 64);
      Assert.NotEqual(hash, NativeStage03Canonicalizer.ComputeHash(changed));
      changed = Scan();
      changed.NormalizedOutputDirectory = "C:\\other";
      Assert.NotEqual(hash, NativeStage03Canonicalizer.ComputeHash(changed));
      changed = Scan();
      changed.PreflightHash = new string('7', 64);
      Assert.NotEqual(hash, NativeStage03Canonicalizer.ComputeHash(changed));
      changed = Scan();
      changed.Stage02ACurrentInputSnapshotHash = new string('6', 64);
      Assert.NotEqual(hash, NativeStage03Canonicalizer.ComputeHash(changed));
      changed = Scan();
      changed.OfficialAcceptanceManifest.Sha256 = new string('5', 64);
      Assert.NotEqual(hash, NativeStage03Canonicalizer.ComputeHash(changed));
      changed = Scan();
      changed.OfficialAcceptanceRevitReadbacks[0].Values[0].CanonicalValue = "2";
      Assert.NotEqual(hash, NativeStage03Canonicalizer.ComputeHash(changed));
      changed = Scan();
      changed.Checklist[0].RuleText = "changed-rule";
      Assert.NotEqual(hash, NativeStage03Canonicalizer.ComputeHash(changed));
      changed = Scan();
      changed.Stage02BWorkflowResult.ResultHash = new string('4', 64);
      Assert.NotEqual(hash, NativeStage03Canonicalizer.ComputeHash(changed));
    }

    [Fact]
    public void Scan_hash_excludes_messages_timestamps_and_collection_order()
    {
      NativeStage03ScanResult left = Scan();
      NativeStage03ScanResult right = Scan();
      right.Status = "different display text";
      right.Messages = new[] { "different message" };
      right.Stage01WorkflowResult.UpdatedUtc = "2099-01-01T00:00:00Z";
      right.Checklist[0].IssueMessage = "different issue prose";
      NativeOfficialAcceptanceOwnerReadback[] reversed = right
        .OfficialAcceptanceRevitReadbacks[0].Values.Reverse().ToArray();
      right.OfficialAcceptanceRevitReadbacks[0].Values = reversed;

      Assert.Equal(NativeStage03Canonicalizer.ComputeHash(left),
        NativeStage03Canonicalizer.ComputeHash(right));
    }

    private static NativeStage03ScanResult Scan()
    {
      var manifest = new NativeOfficialAcceptanceManifest
      {
        Sha256 = new string('1', 64),
        Properties = new[]
        {
          new NativeOfficialAcceptanceManifestEntry
          {
            PropertyId = "property-a",
            Identity = "IfcProject|Pset_A|A",
            DeclaredIfcType = "IfcReal",
            CanonicalUnit = "m2",
            ParameterGuid = "11111111-1111-1111-1111-111111111111",
            BindingScope = "INSTANCE",
            SourceStage = NativeReportingSourceStage.Stage01
          }
        }
      };
      return new NativeStage03ScanResult
      {
        Mode = NativeStage03Mode.Strict,
        ForceReason = string.Empty,
        Status = "display",
        RulePackageId = "HBR-WUHAN-PLANNING",
        RulePackageVersion = "1.0.0",
        RulePackageSha256 = new string('a', 64),
        DocumentFingerprint = "document",
        DocumentPath = "C:\\model.rvt",
        ModelFileType = "总平模型",
        RevitVersion = "2020",
        NormalizedOutputDirectory = "C:\\output",
        PreflightHash = new string('2', 64),
        Stage02ACurrentInputSnapshotHash = new string('3', 64),
        PluginRuntime = new NativePluginRuntimeIdentity
        {
          ProductVersion = "0.4.3",
          AssemblyVersion = "0.4.3.0",
          InformationalVersion = "0.4.3.sha."
            + new string('b', 40),
          CommitSha = new string('b', 40),
          AddinDllPath = "C:\\BIMBaoGui.RevitAddin.dll",
          AddinDllSha256 = new string('c', 64)
        },
        Stage01WorkflowResult = Workflow(new string('d', 64)),
        Stage02AWorkflowResult = Workflow(new string('e', 64)),
        Stage02BWorkflowResult = Workflow(new string('f', 64)),
        OfficialAcceptanceManifest = manifest,
        OfficialAcceptanceRevitReadbacks = new[]
        {
          new NativeOfficialAcceptancePropertyReadback
          {
            PropertyId = "property-a",
            SourceStage = NativeReportingSourceStage.Stage01,
            SourceResultHash = new string('d', 64),
            Values = new[]
            {
              new NativeOfficialAcceptanceOwnerReadback
              {
                RevitUniqueId = "revit-b",
                ExpectedIfcGlobalId = "global-b",
                CanonicalValue = "1"
              },
              new NativeOfficialAcceptanceOwnerReadback
              {
                RevitUniqueId = "revit-a",
                ExpectedIfcGlobalId = "global-a",
                CanonicalValue = "1"
              }
            }
          }
        },
        Checklist = new[]
        {
          new NativeStage03ChecklistItem
          {
            CheckId = "check-a",
            SourceStage = NativeReportingSourceStage.Stage01,
            CheckKind = NativeReportingCheckKind.Stage01Field,
            Status = NativeStage03ChecklistStatus.Passed,
            FieldKey = "field-a",
            RuleText = "rule-a",
            Elements = Array.Empty<NativeIssueElementReference>()
          }
        },
        Messages = new[] { "display message" }
      };
    }

    private static NativeWorkflowResultEnvelope Workflow(string hash)
    {
      return new NativeWorkflowResultEnvelope
      {
        ResultHash = hash,
        UpdatedUtc = "2026-08-14T00:00:00Z"
      };
    }
  }
}
