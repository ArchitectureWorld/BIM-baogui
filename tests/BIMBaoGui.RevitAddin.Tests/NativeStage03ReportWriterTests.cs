using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using BIMBaoGui.HifcCore;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage03;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage03ReportWriterTests : IDisposable
  {
    private readonly string _root;

    public NativeStage03ReportWriterTests()
    {
      _root = Path.Combine(Path.GetTempPath(),
        "BIMBaoGui.Stage03ReportWriterTests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Scan_evidence_preserves_complete_confirmed_identity_and_red_items()
    {
      NativeStage03ScanResult scan = Scan(NativeStage03Mode.ForcedTest,
        NativeStage03ChecklistStatus.Failed);

      string path = NativeStage03ReportWriter.WriteScanEvidence(scan);

      Assert.Equal(Path.Combine(_root,
        scan.ScanHash + "-stage03-scan-evidence.json"), path);
      Dictionary<string, object> json = ReadObject(path);
      AssertSharedIdentityProjection(scan, json, "STAGE03_SCAN", true, false,
        NativeStage03ChecklistStatus.Failed);
      Assert.Equal("STAGE03_SCAN", json["report_kind"]);
      Assert.True((bool)json["is_test_export"]);
      Assert.False((bool)json["counts_as_normal_export_pass"]);
      Assert.Equal("PENDING", json["official_acceptance_status"]);
      Assert.Equal(Path.GetFullPath(Path.Combine(_root, "current.rvt")),
        json["document_path"]);
      Assert.Equal(_root, json["normalized_output_directory"]);
      Assert.Equal(scan.ScanHash, json["scan_hash"]);
      Assert.Equal(new string('c', 64), json["preflight_hash"]);

      Dictionary<string, object> counts = Object(json, "checklist_counts");
      Assert.Equal(0, counts["passed"]);
      Assert.Equal(1, counts["failed"]);
      Assert.Equal(0, counts["warning"]);
      Assert.Equal(0, counts["not_checked"]);
      Dictionary<string, object> workflows = Object(json, "workflow_results");
      AssertWorkflow(Object(workflows, "stage01"), "RUN-01", '1', '4');
      AssertWorkflow(Object(workflows, "stage02a"), "RUN-02A", '2', '5');
      AssertWorkflow(Object(workflows, "stage02b"), "RUN-02B", '3', '6');

      Dictionary<string, object> runtime = Object(json, "plugin_runtime");
      Assert.Equal(scan.PluginRuntime.ProductVersion,
        runtime["product_version"]);
      Assert.Equal(scan.PluginRuntime.AssemblyVersion,
        runtime["assembly_version"]);
      Assert.Equal(scan.PluginRuntime.InformationalVersion,
        runtime["informational_version"]);
      Assert.Equal(scan.PluginRuntime.CommitSha, runtime["commit_sha"]);
      Assert.Equal(scan.PluginRuntime.AddinDllPath,
        runtime["addin_dll_path"]);
      Assert.Equal(scan.PluginRuntime.AddinDllSha256,
        runtime["addin_dll_sha256"]);

      Dictionary<string, object> manifest = Object(json,
        "official_acceptance_manifest");
      Assert.Equal("1.0.0", manifest["schema_version"]);
      Assert.Equal(ManifestSha(scan.OfficialAcceptanceManifest.Properties),
        manifest["sha256"]);
      object[] properties = ObjectArray(manifest, "properties");
      Assert.Equal(2, properties.Length);
      Assert.Equal("11111111-1111-1111-1111-111111111111",
        ((Dictionary<string, object>)properties[0])["property_id"]);
      Assert.Equal("IfcProject|Pset_Project|ProjectName",
        ((Dictionary<string, object>)properties[0])["identity"]);
      Assert.Equal("STAGE01",
        ((Dictionary<string, object>)properties[0])["source_stage"]);
      Assert.Equal("22222222-2222-2222-2222-222222222222",
        ((Dictionary<string, object>)properties[1])["property_id"]);

      object[] readbacks = ObjectArray(json,
        "official_acceptance_revit_readbacks");
      Assert.Equal(2, readbacks.Length);
      Dictionary<string, object> firstReadback =
        (Dictionary<string, object>)readbacks[0];
      Assert.Equal("11111111-1111-1111-1111-111111111111",
        firstReadback["property_id"]);
      Assert.Equal(new string('1', 64),
        firstReadback["source_result_hash"]);
      object[] ownerValues = ObjectArray(firstReadback, "values");
      Assert.Equal(2, ownerValues.Length);
      Assert.Equal("owner-b",
        ((Dictionary<string, object>)ownerValues[0])["revit_unique_id"]);
      Assert.Equal("owner-a",
        ((Dictionary<string, object>)ownerValues[1])["revit_unique_id"]);

      object[] checklist = ObjectArray(json, "checklist");
      Dictionary<string, object> item =
        Assert.IsType<Dictionary<string, object>>(Assert.Single(checklist));
      Assert.Equal("CHECK-RED", item["check_id"]);
      Assert.Equal("STAGE02B", item["source_stage"]);
      Assert.Equal("Failed", item["status"]);
      Assert.Equal("MISSING_DATA", item["issue_code"]);
      Assert.Equal("PendingGoldenRvt", item["official_carrier_status"]);
      object[] elements = ObjectArray(item, "elements");
      Assert.Equal("element-a",
        ((Dictionary<string, object>)elements[0])["element_unique_id"]);
      Assert.Equal("element-b",
        ((Dictionary<string, object>)elements[1])["element_unique_id"]);
    }

    [Fact]
    public void Scan_evidence_reuses_identical_bytes_but_never_overwrites_a_collision()
    {
      NativeStage03ScanResult scan = Scan(NativeStage03Mode.ForcedTest,
        NativeStage03ChecklistStatus.Failed);
      string path = NativeStage03ReportWriter.WriteScanEvidence(scan);
      byte[] original = File.ReadAllBytes(path);

      Assert.Equal(path, NativeStage03ReportWriter.WriteScanEvidence(scan));
      Assert.Equal(original, File.ReadAllBytes(path));
      File.WriteAllText(path, "{\"different\":true}");

      NativeStage03ReportException collision = Assert.Throws<
        NativeStage03ReportException>(() =>
          NativeStage03ReportWriter.WriteScanEvidence(scan));
      Assert.Equal(NativeStage03Codes.ScanEvidenceCollision, collision.Code);
      Assert.Equal("{\"different\":true}", File.ReadAllText(path));
    }

    [Fact]
    public void Scan_evidence_rejects_unsaved_documents_and_forged_runtime_identity()
    {
      NativeStage03ScanResult unsaved = Scan(NativeStage03Mode.Strict,
        NativeStage03ChecklistStatus.Passed);
      unsaved.DocumentPath = string.Empty;
      NativeStage03ReportException unsavedFailure = Assert.Throws<
        NativeStage03ReportException>(() =>
          NativeStage03ReportWriter.WriteScanEvidence(unsaved));
      Assert.Equal(NativeStage03Codes.UnsavedDocument, unsavedFailure.Code);

      NativeStage03ScanResult forged = Scan(NativeStage03Mode.Strict,
        NativeStage03ChecklistStatus.Passed);
      forged.PluginRuntime.InformationalVersion =
        "0.4.3+build.123.sha." + new string('f', 40);
      NativeStage03ReportException runtimeFailure = Assert.Throws<
        NativeStage03ReportException>(() =>
          NativeStage03ReportWriter.WriteScanEvidence(forged));
      Assert.Equal(NativeStage03Codes.RuntimeArtifactChanged,
        runtimeFailure.Code);
    }

    [Fact]
    public void Golden_zero_red_strict_success_writes_a_scan_bound_validation()
    {
      NativeStage03ScanResult scan = Scan(NativeStage03Mode.Strict,
        NativeStage03ChecklistStatus.Passed);
      scan.AllowExport = true;
      scan.ExportFields = new[] { new HifcFieldRequest() };
      scan.OfficialAcceptanceManifest.Sha256 = ManifestSha(
        scan.OfficialAcceptanceManifest.Properties);
      scan.ScanHash = NativeStage03Canonicalizer.ComputeHash(scan);
      NativeStage03RunPaths paths = Paths(scan);
      NativeStage03RawIfcArtifact raw = Raw(paths);
      HifcTranslationResult translation = Translation(paths);

      NativeStage03ReportWriter.WriteSuccess(
        paths, scan, raw, translation,
        System.Array.Empty<NativeStage03FieldEvidence>());

      string validationPath = Path.Combine(paths.RunDirectory,
        scan.ScanHash + "-validation.json");
      Assert.Equal(validationPath, paths.ValidationReportPath);
      Dictionary<string, object> validation = ReadObject(validationPath);
      AssertSharedIdentityProjection(scan, validation, "VALIDATION", false,
        true, NativeStage03ChecklistStatus.Passed);
      Assert.Equal("VALIDATION", validation["report_kind"]);
      Assert.Equal("STRICT", validation["execution_mode"]);
      Assert.True((bool)validation["export_succeeded"]);
      Assert.False((bool)validation["is_test_export"]);
      Assert.True((bool)validation["counts_as_normal_export_pass"]);
      Assert.Equal(scan.ScanHash, validation["scan_hash"]);
      Assert.Empty(ObjectArray(validation, "blockers"));
      Assert.Equal(scan.OfficialAcceptanceManifest.Sha256,
        Object(validation, "official_acceptance_manifest")["sha256"]);
      Assert.Equal(2, ObjectArray(validation,
        "official_acceptance_revit_readbacks").Length);

      Dictionary<string, object> fields = ReadObject(paths.FieldsReportPath);
      Assert.Single(ObjectArray(fields, "checklist"));
    }

    [Fact]
    public void Strict_red_checklist_never_creates_a_success_validation()
    {
      NativeStage03ScanResult scan = Scan(NativeStage03Mode.Strict,
        NativeStage03ChecklistStatus.Failed);
      scan.OfficialAcceptanceManifest.Sha256 = ManifestSha(
        scan.OfficialAcceptanceManifest.Properties);
      scan.ScanHash = NativeStage03Canonicalizer.ComputeHash(scan);
      NativeStage03RunPaths paths = Paths(scan);

      Assert.Throws<NativeStage03ReportException>(() =>
        NativeStage03ReportWriter.WriteSuccess(paths, scan, Raw(paths),
          Translation(paths),
          System.Array.Empty<NativeStage03FieldEvidence>()));
      Assert.False(File.Exists(Path.Combine(paths.RunDirectory,
        scan.ScanHash + "-validation.json")));
    }

    [Fact]
    public void Strict_success_rejects_a_red_checklist_with_forged_zero_counts()
    {
      NativeStage03ScanResult scan = Scan(NativeStage03Mode.Strict,
        NativeStage03ChecklistStatus.Failed);
      scan.AllowExport = true;
      scan.FailedCount = 0;
      scan.ExportFields = new[] { new HifcFieldRequest() };
      scan.OfficialAcceptanceManifest.Sha256 = ManifestSha(
        scan.OfficialAcceptanceManifest.Properties);
      scan.ScanHash = NativeStage03Canonicalizer.ComputeHash(scan);
      NativeStage03RunPaths paths = Paths(scan);

      NativeStage03ReportException failure = Assert.Throws<
        NativeStage03ReportException>(() =>
          NativeStage03ReportWriter.WriteSuccess(paths, scan, Raw(paths),
            Translation(paths), Array.Empty<NativeStage03FieldEvidence>()));

      Assert.Equal(NativeStage03Codes.FieldNotReady, failure.Code);
      Assert.False(File.Exists(Path.Combine(paths.RunDirectory,
        scan.ScanHash + "-validation.json")));
    }

    [Fact]
    public void Strict_success_rejects_zero_export_fields_even_when_counts_are_green()
    {
      NativeStage03ScanResult scan = Scan(NativeStage03Mode.Strict,
        NativeStage03ChecklistStatus.Passed);
      scan.AllowExport = true;
      scan.OfficialAcceptanceManifest.Sha256 = ManifestSha(
        scan.OfficialAcceptanceManifest.Properties);
      scan.ScanHash = NativeStage03Canonicalizer.ComputeHash(scan);
      NativeStage03RunPaths paths = Paths(scan);

      NativeStage03ReportException failure = Assert.Throws<
        NativeStage03ReportException>(() =>
          NativeStage03ReportWriter.WriteSuccess(paths, scan, Raw(paths),
            Translation(paths), Array.Empty<NativeStage03FieldEvidence>()));

      Assert.Equal(NativeStage03Codes.FieldNotReady, failure.Code);
      Assert.False(File.Exists(Path.Combine(paths.RunDirectory,
        scan.ScanHash + "-validation.json")));
    }

    [Fact]
    public void Scan_evidence_collision_closes_the_export_gate_without_overwrite()
    {
      NativeStage03ScanResult scan = Scan(NativeStage03Mode.ForcedTest,
        NativeStage03ChecklistStatus.Failed);
      string evidencePath = Path.Combine(_root,
        scan.ScanHash + "-stage03-scan-evidence.json");
      File.WriteAllText(evidencePath, "protected collision bytes");

      NativeStage03Scanner.FinalizeScanEvidence(scan);

      Assert.False(scan.AllowExport);
      Assert.False(scan.Forced);
      Assert.False(scan.Success);
      Assert.Contains(NativeStage03Codes.ScanEvidenceCollision,
        scan.TechnicalFatalCodes);
      Assert.Contains(NativeStage03Codes.ReportWriterUnavailable,
        scan.TechnicalFatalCodes);
      Assert.Equal(scan.ScanHash,
        NativeStage03Canonicalizer.ComputeHash(scan));
      Assert.Equal("protected collision bytes", File.ReadAllText(evidencePath));
    }

    [Fact]
    public void Failure_report_keeps_the_same_scan_manifest_and_readbacks()
    {
      NativeStage03ScanResult scan = Scan(NativeStage03Mode.ForcedTest,
        NativeStage03ChecklistStatus.Failed);
      NativeStage03RunPaths paths = Paths(scan);

      NativeStage03ReportWriter.WriteFailure(paths, scan,
        "TRANSLATOR_DEPENDENCY_UNAVAILABLE", "translator missing");

      Dictionary<string, object> failure = ReadObject(paths.FailureReportPath);
      AssertSharedIdentityProjection(scan, failure, "FAILURE", true, false,
        NativeStage03ChecklistStatus.Failed);
      Assert.Equal("FAILURE", failure["report_kind"]);
      Assert.True((bool)failure["is_test_export"]);
      Assert.False((bool)failure["counts_as_normal_export_pass"]);
      Assert.Equal(scan.OfficialAcceptanceManifest.Sha256,
        Object(failure, "official_acceptance_manifest")["sha256"]);
      Assert.Equal(2, ObjectArray(failure,
        "official_acceptance_revit_readbacks").Length);
      Assert.Equal(scan.ScanHash, failure["scan_hash"]);
      Assert.Equal("TRANSLATOR_DEPENDENCY_UNAVAILABLE",
        failure["error_code"]);
    }

    [Fact]
    public void Validation_rejects_readbacks_changed_after_the_confirmed_scan()
    {
      NativeStage03ScanResult scan = Scan(NativeStage03Mode.Strict,
        NativeStage03ChecklistStatus.Passed);
      scan.AllowExport = true;
      scan.OfficialAcceptanceManifest.Sha256 = ManifestSha(
        scan.OfficialAcceptanceManifest.Properties);
      scan.ScanHash = NativeStage03Canonicalizer.ComputeHash(scan);
      scan.OfficialAcceptanceRevitReadbacks[0].Values[0].CanonicalValue =
        "tampered after scan";
      NativeStage03RunPaths paths = Paths(scan);

      NativeStage03ReportException failure = Assert.Throws<
        NativeStage03ReportException>(() =>
          NativeStage03ReportWriter.WriteSuccess(paths, scan, Raw(paths),
            Translation(paths),
            System.Array.Empty<NativeStage03FieldEvidence>()));
      Assert.Equal(NativeStage03Codes.ScanExpired, failure.Code);
      Assert.False(File.Exists(Path.Combine(paths.RunDirectory,
        scan.ScanHash + "-validation.json")));
    }

    [Fact]
    public void Scan_evidence_fails_closed_for_each_confirmed_identity_mutation()
    {
      NativeStage03ScanResult manifest = Scan(NativeStage03Mode.ForcedTest,
        NativeStage03ChecklistStatus.Failed);
      manifest.OfficialAcceptanceManifest.Properties[0].Identity =
        "IfcBuilding|Pset_Building|Tampered";
      Assert.Equal(NativeStage03Codes.ReportWriterUnavailable,
        Assert.Throws<NativeStage03ReportException>(() =>
          NativeStage03ReportWriter.WriteScanEvidence(manifest)).Code);

      NativeStage03ScanResult readback = Scan(NativeStage03Mode.ForcedTest,
        NativeStage03ChecklistStatus.Failed);
      readback.OfficialAcceptanceRevitReadbacks[0].Values[0].CanonicalValue =
        "tampered";
      Assert.Equal(NativeStage03Codes.ScanExpired,
        Assert.Throws<NativeStage03ReportException>(() =>
          NativeStage03ReportWriter.WriteScanEvidence(readback)).Code);

      NativeStage03ScanResult runtime = Scan(NativeStage03Mode.ForcedTest,
        NativeStage03ChecklistStatus.Failed);
      runtime.PluginRuntime.ProductVersion = "tampered";
      Assert.Equal(NativeStage03Codes.RuntimeArtifactChanged,
        Assert.Throws<NativeStage03ReportException>(() =>
          NativeStage03ReportWriter.WriteScanEvidence(runtime)).Code);

      NativeStage03ScanResult dll = Scan(NativeStage03Mode.ForcedTest,
        NativeStage03ChecklistStatus.Failed);
      dll.PluginRuntime.AddinDllSha256 = new string('f', 64);
      Assert.Equal(NativeStage03Codes.RuntimeArtifactChanged,
        Assert.Throws<NativeStage03ReportException>(() =>
          NativeStage03ReportWriter.WriteScanEvidence(dll)).Code);
    }

    [Fact]
    public void Execution_keeps_specific_report_identity_failure_codes()
    {
      var changed = new NativeStage03ReportException(
        NativeStage03Codes.RuntimeArtifactChanged, "dll changed");

      Assert.Equal(NativeStage03Codes.RuntimeArtifactChanged,
        NativeStage03WorkflowService.ExecutionFailureCode(changed));
      Assert.Equal("STAGE03_EXECUTION_FAILED",
        NativeStage03WorkflowService.ExecutionFailureCode(
          new IOException("disk failed")));
    }

    public void Dispose()
    {
      if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private NativeStage03ScanResult Scan(
      NativeStage03Mode mode,
      NativeStage03ChecklistStatus status)
    {
      var result = new NativeStage03ScanResult
      {
        Success = true,
        Mode = mode,
        ForceReason = mode == NativeStage03Mode.ForcedTest
          ? "开发测试缺项导出"
          : string.Empty,
        AllowExport = mode == NativeStage03Mode.ForcedTest,
        Forced = mode == NativeStage03Mode.ForcedTest,
        ScanHash = string.Empty,
        RulePackageId = "HBR-WUHAN-PLANNING",
        RulePackageVersion = "1.0.0",
        RulePackageSha256 = new string('a', 64),
        DocumentFingerprint = "DOC-FINGERPRINT",
        DocumentPath = Path.GetFullPath(Path.Combine(_root, "current.rvt")),
        RevitVersion = "2020",
        NormalizedOutputDirectory = _root,
        PreflightHash = new string('c', 64),
        Stage01WorkflowResult = Workflow("RUN-01", '1', '4'),
        Stage02AWorkflowResult = Workflow("RUN-02A", '2', '5'),
        Stage02BWorkflowResult = Workflow("RUN-02B", '3', '6'),
        PluginRuntime = RuntimeIdentity(),
        OfficialAcceptanceManifest = new NativeOfficialAcceptanceManifest
        {
          SchemaVersion = "1.0.0",
          Sha256 = string.Empty,
          Properties = new[]
          {
            ManifestEntry("22222222-2222-2222-2222-222222222222",
              "IfcBuilding|Pset_Building|Height", "IfcReal", "m",
              NativeReportingSourceStage.Stage02B),
            ManifestEntry("11111111-1111-1111-1111-111111111111",
              "IfcProject|Pset_Project|ProjectName", "IfcLabel", "",
              NativeReportingSourceStage.Stage01)
          }
        },
        OfficialAcceptanceRevitReadbacks = new[]
        {
          Readback("22222222-2222-2222-2222-222222222222",
            NativeReportingSourceStage.Stage02B, '3',
            Owner("owner-c", "gid-c", "42.5")),
          Readback("11111111-1111-1111-1111-111111111111",
            NativeReportingSourceStage.Stage01, '1',
            Owner("owner-b", "gid-a", "Project B"),
            Owner("owner-a", "gid-b", "Project A"))
        },
        Checklist = new[]
        {
          new NativeStage03ChecklistItem
          {
            CheckId = "CHECK-RED",
            CheckKind = NativeReportingCheckKind.PropertyConsistency,
            DisplayName = "官方载体证据",
            SourceStage = NativeReportingSourceStage.Stage02B,
            ApplicableBasis = "RULE-001",
            CurrentValue = "",
            Unit = "m",
            Status = status,
            IssueCode = "MISSING_DATA",
            RemediationTarget = "02B",
            FieldKey = "building_height",
            PropertyId = "22222222-2222-2222-2222-222222222222",
            RoleId = "BUILDING",
            RuleText = "建筑高度必须有官方载体证据",
            TargetKey = "height_limit",
            ElementId = 20,
            ElementUniqueId = "element-b",
            Elements = new[]
            {
              new NativeIssueElementReference
              {
                ElementId = 20,
                UniqueId = "element-b",
                ElementName = "B",
                CategoryName = "Mass"
              },
              new NativeIssueElementReference
              {
                ElementId = 10,
                UniqueId = "element-a",
                ElementName = "A",
                CategoryName = "Mass"
              }
            },
            OfficialCarrierStatus =
              NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt,
            OfficialProjectionCarrierId = "carrier-02b",
            OfficialCarrierProbeRef = "probe-02b",
            OfficialEvidenceRef = "evidence-02b"
          }
        },
        PassedCount = status == NativeStage03ChecklistStatus.Passed ? 1 : 0,
        FailedCount = status == NativeStage03ChecklistStatus.Failed ? 1 : 0,
        WarningCount = status == NativeStage03ChecklistStatus.Warning ? 1 : 0,
        NotCheckedCount = status == NativeStage03ChecklistStatus.NotChecked
          ? 1 : 0,
        TechnicalFatalCodes = System.Array.Empty<string>(),
        BusinessBlockers = status == NativeStage03ChecklistStatus.Failed
          ? new[] { "MISSING_DATA" }
          : System.Array.Empty<string>()
      };
      result.OfficialAcceptanceManifest.Sha256 = ManifestSha(
        result.OfficialAcceptanceManifest.Properties);
      result.ScanHash = NativeStage03Canonicalizer.ComputeHash(result);
      return result;
    }

    private static NativeWorkflowResultEnvelope Workflow(
      string runId, char result, char input)
    {
      return new NativeWorkflowResultEnvelope
      {
        RunId = runId,
        ResultHash = new string(result, 64),
        InputSnapshotHash = new string(input, 64)
      };
    }

    private static NativeOfficialAcceptanceManifestEntry ManifestEntry(
      string propertyId,
      string identity,
      string declaredType,
      string unit,
      NativeReportingSourceStage stage)
    {
      return new NativeOfficialAcceptanceManifestEntry
      {
        PropertyId = propertyId,
        Identity = identity,
        DeclaredIfcType = declaredType,
        CanonicalUnit = unit,
        ParameterGuid = propertyId,
        BindingScope = "INSTANCE",
        SourceStage = stage
      };
    }

    private static NativeOfficialAcceptancePropertyReadback Readback(
      string propertyId,
      NativeReportingSourceStage stage,
      char resultHash,
      params NativeOfficialAcceptanceOwnerReadback[] values)
    {
      return new NativeOfficialAcceptancePropertyReadback
      {
        PropertyId = propertyId,
        SourceStage = stage,
        SourceResultHash = new string(resultHash, 64),
        Values = values
      };
    }

    private static NativeOfficialAcceptanceOwnerReadback Owner(
      string uniqueId, string globalId, string value)
    {
      return new NativeOfficialAcceptanceOwnerReadback
      {
        RevitUniqueId = uniqueId,
        ExpectedIfcGlobalId = globalId,
        CanonicalValue = value
      };
    }

    private static NativePluginRuntimeIdentity RuntimeIdentity()
    {
      Assembly assembly = typeof(NativeStage03ReportWriter).Assembly;
      string path = Path.GetFullPath(assembly.Location);
      string informational = assembly.GetCustomAttribute<
        AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty;
      Match commit = Regex.Match(informational,
        @"(?:^|\.)sha\.([0-9a-fA-F]{40})(?:$|\.)");
      using (SHA256 algorithm = SHA256.Create())
      using (FileStream stream = File.OpenRead(path))
      {
        return new NativePluginRuntimeIdentity
        {
          ProductVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion
            ?? string.Empty,
          AssemblyVersion = assembly.GetName().Version?.ToString()
            ?? string.Empty,
          InformationalVersion = informational,
          CommitSha = commit.Success
            ? commit.Groups[1].Value.ToLowerInvariant()
            : string.Empty,
          AddinDllPath = path,
          AddinDllSha256 = string.Concat(algorithm.ComputeHash(stream)
            .Select(value => value.ToString("x2")))
        };
      }
    }

    private NativeStage03RunPaths Paths(NativeStage03ScanResult scan)
    {
      NativeStage03RunPaths paths = NativeStage03OutputPathPolicy.Create(
        _root,
        scan.DocumentPath,
        "REPORT-RUN",
        DateTimeOffset.Parse("2026-08-17T12:00:00+08:00"),
        scan.Mode);
      Directory.CreateDirectory(paths.RunDirectory);
      return paths;
    }

    private static NativeStage03RawIfcArtifact Raw(
      NativeStage03RunPaths paths)
    {
      return new NativeStage03RawIfcArtifact
      {
        Path = paths.RawIfcPath,
        Length = 10,
        Sha256 = new string('7', 64)
      };
    }

    private static HifcTranslationResult Translation(
      NativeStage03RunPaths paths)
    {
      return new HifcTranslationResult
      {
        Success = true,
        InternalStatus = HifcCoreStatus.InternalValidated,
        IfcFluxStatus = HifcCoreStatus.IfcFluxManualPending,
        FinalIfcPath = paths.FinalIfcPath,
        FinalIfcLength = 20,
        FinalIfcSha256 = new string('8', 64),
        Fields = System.Array.Empty<HifcFieldEvidence>()
      };
    }

    private static string ManifestSha(
      IEnumerable<NativeOfficialAcceptanceManifestEntry> properties)
    {
      string canonical = "BIMBAOGUI_OFFICIAL_ACCEPTANCE_MANIFEST|1.0.0\n"
        + string.Join(string.Empty, properties
          .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
          .Select(value => value.PropertyId + '\u001f'
            + value.Identity + '\u001f'
            + value.DeclaredIfcType + '\u001f'
            + value.CanonicalUnit + '\u001f'
            + Guid.Parse(value.ParameterGuid).ToString("D").ToLowerInvariant()
            + '\u001f' + value.BindingScope + '\u001f'
            + Stage(value.SourceStage) + "\n"));
      using (SHA256 algorithm = SHA256.Create())
      {
        return string.Concat(algorithm.ComputeHash(
          new System.Text.UTF8Encoding(false).GetBytes(canonical))
          .Select(value => value.ToString("x2")));
      }
    }

    private static string Stage(NativeReportingSourceStage stage)
    {
      switch (stage)
      {
        case NativeReportingSourceStage.Stage01: return "STAGE01";
        case NativeReportingSourceStage.Stage02A: return "STAGE02A";
        case NativeReportingSourceStage.Stage02B: return "STAGE02B";
        default: throw new InvalidOperationException();
      }
    }

    private static Dictionary<string, object> ReadObject(string path)
    {
      return (Dictionary<string, object>)new JavaScriptSerializer()
        .DeserializeObject(File.ReadAllText(path));
    }

    private static Dictionary<string, object> Object(
      IDictionary<string, object> parent, string key)
    {
      return Assert.IsType<Dictionary<string, object>>(parent[key]);
    }

    private static object[] ObjectArray(IDictionary<string, object> parent,
      string key)
    {
      return Assert.IsType<object[]>(parent[key]);
    }

    private void AssertSharedIdentityProjection(
      NativeStage03ScanResult scan,
      IDictionary<string, object> actual,
      string reportKind,
      bool isTestExport,
      bool countsAsNormalExportPass,
      NativeStage03ChecklistStatus checklistStatus)
    {
      var expected = new Dictionary<string, object>(StringComparer.Ordinal)
      {
        ["report_kind"] = reportKind,
        ["is_test_export"] = isTestExport,
        ["counts_as_normal_export_pass"] = countsAsNormalExportPass,
        ["official_acceptance_status"] = "PENDING",
        ["checklist_counts"] = new Dictionary<string, object>
        {
          ["passed"] = checklistStatus == NativeStage03ChecklistStatus.Passed
            ? 1 : 0,
          ["failed"] = checklistStatus == NativeStage03ChecklistStatus.Failed
            ? 1 : 0,
          ["warning"] = 0,
          ["not_checked"] = 0
        },
        ["workflow_results"] = new Dictionary<string, object>
        {
          ["stage01"] = ExpectedWorkflow("RUN-01", '1', '4'),
          ["stage02a"] = ExpectedWorkflow("RUN-02A", '2', '5'),
          ["stage02b"] = ExpectedWorkflow("RUN-02B", '3', '6')
        },
        ["rule_package"] = new Dictionary<string, object>
        {
          ["id"] = "HBR-WUHAN-PLANNING",
          ["version"] = "1.0.0",
          ["sha256"] = new string('a', 64)
        },
        ["document_fingerprint"] = "DOC-FINGERPRINT",
        ["document_path"] = Path.GetFullPath(Path.Combine(_root,
          "current.rvt")),
        ["plugin_runtime"] = ExpectedRuntime(RuntimeIdentity()),
        ["official_acceptance_manifest"] = ExpectedManifest(),
        ["official_acceptance_revit_readbacks"] = ExpectedReadbacks(),
        ["checklist"] = ExpectedChecklist(checklistStatus),
        ["revit_version"] = "2020",
        ["scan_hash"] = scan.ScanHash,
        ["normalized_output_directory"] = _root,
        ["preflight_hash"] = new string('c', 64),
        ["technical_fatals"] = Array.Empty<object>()
      };
      var projected = new Dictionary<string, object>(StringComparer.Ordinal);
      foreach (string key in expected.Keys) projected[key] = actual[key];
      AssertJsonGraph(expected, projected, "identity");
    }

    private static Dictionary<string, object> ExpectedWorkflow(
      string runId, char result, char input)
    {
      return new Dictionary<string, object>
      {
        ["run_id"] = runId,
        ["result_hash"] = new string(result, 64),
        ["input_snapshot_hash"] = new string(input, 64)
      };
    }

    private static Dictionary<string, object> ExpectedRuntime(
      NativePluginRuntimeIdentity runtime)
    {
      return new Dictionary<string, object>
      {
        ["product_version"] = runtime.ProductVersion,
        ["assembly_version"] = runtime.AssemblyVersion,
        ["informational_version"] = runtime.InformationalVersion,
        ["commit_sha"] = runtime.CommitSha,
        ["addin_dll_path"] = runtime.AddinDllPath,
        ["addin_dll_sha256"] = runtime.AddinDllSha256
      };
    }

    private static Dictionary<string, object> ExpectedManifest()
    {
      return new Dictionary<string, object>
      {
        ["schema_version"] = "1.0.0",
        ["sha256"] = ManifestSha(new[]
        {
          ManifestEntry("22222222-2222-2222-2222-222222222222",
            "IfcBuilding|Pset_Building|Height", "IfcReal", "m",
            NativeReportingSourceStage.Stage02B),
          ManifestEntry("11111111-1111-1111-1111-111111111111",
            "IfcProject|Pset_Project|ProjectName", "IfcLabel", "",
            NativeReportingSourceStage.Stage01)
        }),
        ["properties"] = new object[]
        {
          new Dictionary<string, object>
          {
            ["property_id"] = "11111111-1111-1111-1111-111111111111",
            ["identity"] = "IfcProject|Pset_Project|ProjectName",
            ["declared_ifc_type"] = "IfcLabel",
            ["canonical_unit"] = "",
            ["parameter_guid"] = "11111111-1111-1111-1111-111111111111",
            ["binding_scope"] = "INSTANCE",
            ["source_stage"] = "STAGE01"
          },
          new Dictionary<string, object>
          {
            ["property_id"] = "22222222-2222-2222-2222-222222222222",
            ["identity"] = "IfcBuilding|Pset_Building|Height",
            ["declared_ifc_type"] = "IfcReal",
            ["canonical_unit"] = "m",
            ["parameter_guid"] = "22222222-2222-2222-2222-222222222222",
            ["binding_scope"] = "INSTANCE",
            ["source_stage"] = "STAGE02B"
          }
        }
      };
    }

    private static object[] ExpectedReadbacks()
    {
      return new object[]
      {
        new Dictionary<string, object>
        {
          ["property_id"] = "11111111-1111-1111-1111-111111111111",
          ["source_stage"] = "STAGE01",
          ["source_result_hash"] = new string('1', 64),
          ["values"] = new object[]
          {
            ExpectedOwner("owner-b", "gid-a", "Project B"),
            ExpectedOwner("owner-a", "gid-b", "Project A")
          }
        },
        new Dictionary<string, object>
        {
          ["property_id"] = "22222222-2222-2222-2222-222222222222",
          ["source_stage"] = "STAGE02B",
          ["source_result_hash"] = new string('3', 64),
          ["values"] = new object[]
          {
            ExpectedOwner("owner-c", "gid-c", "42.5")
          }
        }
      };
    }

    private static Dictionary<string, object> ExpectedOwner(
      string uniqueId, string globalId, string value)
    {
      return new Dictionary<string, object>
      {
        ["revit_unique_id"] = uniqueId,
        ["expected_ifc_global_id"] = globalId,
        ["canonical_value"] = value
      };
    }

    private static object[] ExpectedChecklist(
      NativeStage03ChecklistStatus status)
    {
      return new object[]
      {
        new Dictionary<string, object>
        {
          ["check_id"] = "CHECK-RED",
          ["check_kind"] = "PropertyConsistency",
          ["display_name"] = "官方载体证据",
          ["source_stage"] = "STAGE02B",
          ["applicable_basis"] = "RULE-001",
          ["current_value"] = "",
          ["unit"] = "m",
          ["status"] = status.ToString(),
          ["issue_code"] = "MISSING_DATA",
          ["remediation_target"] = "02B",
          ["field_key"] = "building_height",
          ["property_id"] = "22222222-2222-2222-2222-222222222222",
          ["role_id"] = "BUILDING",
          ["rule_text"] = "建筑高度必须有官方载体证据",
          ["target_key"] = "height_limit",
          ["element_id"] = 20,
          ["element_unique_id"] = "element-b",
          ["elements"] = new object[]
          {
            new Dictionary<string, object>
            {
              ["element_id"] = 10,
              ["element_unique_id"] = "element-a",
              ["element_name"] = "A",
              ["category_name"] = "Mass"
            },
            new Dictionary<string, object>
            {
              ["element_id"] = 20,
              ["element_unique_id"] = "element-b",
              ["element_name"] = "B",
              ["category_name"] = "Mass"
            }
          },
          ["official_carrier_status"] = "PendingGoldenRvt",
          ["official_projection_carrier_id"] = "carrier-02b",
          ["official_carrier_probe_ref"] = "probe-02b",
          ["official_evidence_ref"] = "evidence-02b"
        }
      };
    }

    private static void AssertJsonGraph(object expected, object actual,
      string path)
    {
      var expectedObject = expected as IDictionary<string, object>;
      var actualObject = actual as IDictionary<string, object>;
      if (expectedObject != null || actualObject != null)
      {
        Assert.NotNull(expectedObject);
        Assert.NotNull(actualObject);
        Assert.Equal(expectedObject.Keys.OrderBy(value => value,
          StringComparer.Ordinal), actualObject.Keys.OrderBy(value => value,
          StringComparer.Ordinal));
        foreach (string key in expectedObject.Keys)
          AssertJsonGraph(expectedObject[key], actualObject[key],
            path + "." + key);
        return;
      }
      object[] expectedArray = expected as object[];
      object[] actualArray = actual as object[];
      if (expectedArray != null || actualArray != null)
      {
        Assert.NotNull(expectedArray);
        Assert.NotNull(actualArray);
        Assert.Equal(expectedArray.Length, actualArray.Length);
        for (int index = 0; index < expectedArray.Length; index++)
          AssertJsonGraph(expectedArray[index], actualArray[index],
            path + "[" + index + "]");
        return;
      }
      Assert.True(object.Equals(expected, actual),
        path + " expected=" + expected + " actual=" + actual);
    }

    private static void AssertWorkflow(
      IDictionary<string, object> value,
      string runId,
      char result,
      char input)
    {
      Assert.Equal(runId, value["run_id"]);
      Assert.Equal(new string(result, 64), value["result_hash"]);
      Assert.Equal(new string(input, 64), value["input_snapshot_hash"]);
    }
  }
}
