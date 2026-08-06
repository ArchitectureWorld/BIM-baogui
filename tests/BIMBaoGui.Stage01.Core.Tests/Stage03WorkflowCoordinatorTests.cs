using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Diagnostics;
using BIMBaoGui.Stage01.Mvd;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03WorkflowCoordinatorTests
  {
    [Fact]
    public async Task Translation_failure_keeps_raw_and_writes_failure_report()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        translatorFails: true))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Force,
          "验证转译失败路径");

        Assert.False(result.Success);
        Assert.EndsWith("-RAW.ifc", result.RawIfcPath);
        Assert.True(File.Exists(result.RawIfcPath));
        Assert.EndsWith("-HIFC-MVD.ifc", result.FinalIfcPath);
        Assert.False(File.Exists(result.FinalIfcPath));
        Assert.True(File.Exists(result.FailureReportPath));
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Strict_blocked_writes_only_fields_report()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { BlockingField() }))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.True(result.Success);
        Assert.False(result.AllowExport);
        Assert.False(result.Forced);
        Assert.Equal(1, fixture.ScanCalls);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Equal(0, fixture.TranslateCalls);
        Assert.Equal(1, fixture.FieldReportCalls);
        Assert.Equal(0, fixture.FailureReportCalls);
        Assert.True(File.Exists(result.FieldReportPath));
        Assert.False(File.Exists(result.RawIfcPath));
        Assert.False(File.Exists(result.FinalIfcPath));
        Assert.Empty(result.RawIfcSha256);
        Assert.Empty(result.FinalIfcSha256);
      }
    }

    [Fact]
    public async Task Valid_force_permits_business_blocker_without_fabricated_ifc_evidence()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { BlockingField() },
        enrichmentValues: Array.Empty<HbrIfcEnrichmentValue>()))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Force,
          "经授权放行业务缺失参数");

        Assert.True(result.Success);
        Assert.True(result.AllowExport);
        Assert.True(result.Forced);
        Assert.True(File.Exists(result.RawIfcPath));
        Assert.True(File.Exists(result.FinalIfcPath));
        Assert.True(File.Exists(result.FieldReportPath));
        Assert.Equal(1, fixture.ExportCalls);
        Assert.Equal(1, fixture.TranslateCalls);
        Assert.Equal(1, fixture.FieldReportCalls);
        Assert.Equal(0, fixture.FailureReportCalls);

        Stage03FieldResult field = Assert.Single(result.Fields);
        Assert.True(field.Active);
        Assert.True(field.IsBusinessBlocker);
        Assert.Equal(Stage03FieldStatus.MissingParameter, field.Status);
        Assert.Equal(
          Stage03FieldStatus.MissingParameter,
          field.ParameterStatus);
        Assert.NotEqual(Stage03FieldStatus.Pass, field.Status);
        Assert.Equal(Stage03FieldStatus.NotEvaluated, field.RawIfcStatus);
        Assert.Equal(Stage03FieldStatus.NotEvaluated, field.FinalIfcStatus);
        Assert.Empty(field.RawIfcOwner);
        Assert.Empty(field.RawIfcPropertySet);
        Assert.Empty(field.RawIfcProperty);
        Assert.Empty(field.RawIfcType);
        Assert.Empty(field.RawIfcValue);
        Assert.Empty(field.FinalIfcOwner);
        Assert.Empty(field.FinalIfcPropertySet);
        Assert.Empty(field.FinalIfcProperty);
        Assert.Empty(field.FinalIfcType);
        Assert.Empty(field.FinalIfcValue);
      }
    }

    [Fact]
    public async Task Strict_blocked_report_writer_cannot_publish_ifc_artifacts()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { BlockingField() },
        invalidServiceResult:
          InvalidServiceResult.StrictFieldReportCreatesIfcArtifacts))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.OutputExists,
          result.TechnicalFatalCodes);
        Assert.True(File.Exists(result.RawIfcPath));
        Assert.True(File.Exists(result.FinalIfcPath));
        Assert.Equal(1, fixture.FieldReportCalls);
        Assert.Equal(1, fixture.FailureReportCalls);
      }
    }

    [Fact]
    public async Task Successful_run_publishes_three_files_and_verified_hashes()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create())
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.True(result.Success);
        Assert.True(result.AllowExport);
        Assert.Equal(1, fixture.ScanCalls);
        Assert.Equal(1, fixture.ExportCalls);
        Assert.Equal(1, fixture.TranslateCalls);
        Assert.Equal(1, fixture.FieldReportCalls);
        Assert.Equal(0, fixture.FailureReportCalls);
        Assert.True(File.Exists(result.RawIfcPath));
        Assert.True(File.Exists(result.FinalIfcPath));
        Assert.True(File.Exists(result.FieldReportPath));
        Assert.Equal(Sha256(result.RawIfcPath), result.RawIfcSha256);
        Assert.Equal(Sha256(result.FinalIfcPath), result.FinalIfcSha256);
        Assert.Equal(Sha256(result.FieldReportPath), result.FieldReportSha256);
        Assert.Equal(result.RawIfcSha256,
          fixture.LastFieldReportContext.RawIfcSha256);
        Assert.Equal(result.FinalIfcSha256,
          fixture.LastFieldReportContext.FinalIfcSha256);
        Assert.Equal(result.RunId, fixture.LastFieldReportContext.RunId);
        Assert.Equal(result.RunId,
          fixture.LastFieldReportContext.OutputPaths.RunId);

        fixture.SourceFields[0].PropertyId = "MUTATED";
        fixture.SourceFields.Clear();
        Assert.Single(result.Fields);
        Assert.Equal("HBR.TEST", result.Fields[0].PropertyId);
        var mutableView = Assert.IsAssignableFrom<IList<Stage03FieldResult>>(
          result.Fields);
        Assert.Throws<NotSupportedException>(() =>
          mutableView.Add(PassingField()));
      }
    }

    [Fact]
    public async Task Force_without_reason_is_blocked_before_export()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { BlockingField() }))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Force,
          "   ");

        Assert.False(result.Success);
        Assert.False(result.AllowExport);
        Assert.False(result.Forced);
        Assert.Equal(1, fixture.ScanCalls);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Equal(0, fixture.TranslateCalls);
        Assert.Equal(1, fixture.FieldReportCalls);
        Assert.Equal(0, fixture.FailureReportCalls);
        Assert.True(File.Exists(result.FieldReportPath));
        Assert.Empty(result.FailureReportPath);
        Assert.Equal(result.RunId, fixture.LastFieldReportContext.RunId);
        Assert.Single(result.Fields);
        Assert.Contains(result.Messages, message =>
          message.Contains("Force") && message.Contains("原因"));
      }
    }

    [Fact]
    public async Task Force_without_reason_with_clean_scan_publishes_typed_blocker()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create())
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Force,
          " \t\r\n ");

        Assert.False(result.Success);
        Assert.False(result.AllowExport);
        Assert.False(result.Forced);
        Assert.Equal(1, fixture.ScanCalls);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Equal(0, fixture.TranslateCalls);
        Assert.Equal(1, fixture.FieldReportCalls);
        Assert.Equal(0, fixture.FailureReportCalls);
        Assert.All(result.Fields, field =>
        {
          Assert.Equal(Stage03FieldStatus.Pass, field.Status);
          Assert.Equal(Stage03FieldStatus.Pass, field.CarrierStatus);
          Assert.Equal(Stage03FieldStatus.Pass, field.ParameterStatus);
          Assert.Equal(Stage03FieldStatus.Pass, field.RevitStatus);
        });
        Assert.Empty(result.TechnicalFatalCodes);
        Assert.DoesNotContain(
          result.Diagnostics,
          Stage03BlockingDiagnosticPolicy.IsBlocking);
        Stage03BusinessBlocker blocker = Assert.Single(
          result.GateDecision.BusinessBlockers);
        Assert.Equal("FORCE_REASON_REQUIRED", blocker.StatusCode);
        Assert.Equal(
          "FORCE_REASON_REQUIRED",
          Assert.Single(fixture.LastFieldReportContext
            .GateDecision.BusinessBlockers).StatusCode);

        string encoded = Assert.Single(
          Stage03FieldDetailFormatter.FormatAllBlockers(
            result.GateDecision,
            result.TechnicalFatalCodes,
            result.Diagnostics));
        var record = Assert.IsType<Dictionary<string, object>>(
          new JavaScriptSerializer().DeserializeObject(encoded));
        Assert.Equal("业务阻断", record["kind"]);
        Assert.Equal("FORCE_REASON_REQUIRED", record["status"]);
      }
    }

    [Fact]
    public async Task Scan_port_cannot_change_the_private_gate_snapshot()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { BlockingField() },
        mutateScanRequest: true))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.True(result.Success);
        Assert.False(result.AllowExport);
        Assert.False(result.Forced);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Equal(0, fixture.TranslateCalls);
      }
    }

    [Fact]
    public async Task Translation_port_cannot_mutate_the_identity_baseline()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        mutateTranslationRequest: true))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.True(File.Exists(result.FailureReportPath));
      }
    }

    [Fact]
    public async Task Throwing_clock_is_reported_through_the_failure_boundary()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        clockFails: true))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Equal(0, fixture.ScanCalls);
        Assert.Equal(1, fixture.FailureReportCalls);
        Assert.True(File.Exists(result.FailureReportPath));
      }
    }

    [Theory]
    [InlineData(InvalidServiceResult.FieldReportMutatesRaw)]
    [InlineData(InvalidServiceResult.FieldReportMutatesFinal)]
    public async Task Field_report_port_cannot_change_verified_ifc_artifacts(
      InvalidServiceResult invalidResult)
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult: invalidResult))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.True(File.Exists(result.FailureReportPath));
        if (invalidResult == InvalidServiceResult.FieldReportMutatesRaw)
        {
          Assert.Empty(result.RawIfcSha256);
          Assert.Equal(
            Sha256(result.FinalIfcPath),
            result.FinalIfcSha256);
        }
        else
        {
          Assert.Equal(Sha256(result.RawIfcPath), result.RawIfcSha256);
          Assert.Empty(result.FinalIfcSha256);
        }
      }
    }

    [Fact]
    public async Task Field_report_port_invalidates_both_ifc_hashes_without_deleting_artifacts()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult: InvalidServiceResult.FieldReportMutatesBothIfc))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.Empty(result.RawIfcSha256);
        Assert.Empty(result.FinalIfcSha256);
        Assert.True(File.Exists(result.RawIfcPath));
        Assert.True(File.Exists(result.FinalIfcPath));
      }
    }

    [Fact]
    public async Task Field_report_port_mutation_fails_closed_without_polluting_result()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult:
          InvalidServiceResult.FieldReportMutatesWorkflowSnapshot))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.ReportFailed,
          result.TechnicalFatalCodes);
        Assert.Equal("HBR.TEST", Assert.Single(result.Fields).PropertyId);
        Assert.Equal("fixture carrier", Assert.Single(result.Carriers).Name);
        Assert.Equal("INFO", Assert.Single(result.Diagnostics).Severity);
      }
    }

    [Fact]
    public async Task Force_cannot_bypass_technical_fatal()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        technicalFatalCodes: new[]
        {
          Stage03TechnicalFatalCodes.UnsupportedRevit
        }))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Force,
          "仅放行业务缺陷");

        Assert.False(result.Success);
        Assert.False(result.AllowExport);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Equal(0, fixture.TranslateCalls);
        Assert.Equal(1, fixture.FieldReportCalls);
        Assert.True(File.Exists(result.FieldReportPath));
        Assert.Equal(1, fixture.FailureReportCalls);
        Assert.True(File.Exists(result.FailureReportPath));
        Assert.Equal(result.RunId, fixture.LastFailureReportContext.RunId);
        var failureReport = Assert.IsType<Dictionary<string, object>>(
          new JavaScriptSerializer().DeserializeObject(
            File.ReadAllText(result.FailureReportPath)));
        Assert.Equal(result.RunId, failureReport["runId"]);
        Assert.Contains(
          Stage03TechnicalFatalCodes.UnsupportedRevit,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Failure_report_writer_failure_does_not_invent_report_path()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        translatorFails: true,
        failureReportFails: true))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Force,
          "验证失败报告二次失败");

        Assert.False(result.Success);
        Assert.Empty(result.FailureReportPath);
        Assert.Contains(
          Stage03TechnicalFatalCodes.ReportFailed,
          result.TechnicalFatalCodes);
        Assert.Contains(result.Messages, message =>
          message.Contains(Stage03TechnicalFatalCodes.ReportFailed));
      }
    }

    [Theory]
    [InlineData(InvalidServiceResult.ExportWrongPath)]
    [InlineData(InvalidServiceResult.ExportWrongLength)]
    [InlineData(InvalidServiceResult.ExportWrongHash)]
    [InlineData(InvalidServiceResult.ExportMissingFile)]
    [InlineData(InvalidServiceResult.TranslationWrongPath)]
    [InlineData(InvalidServiceResult.TranslationWrongLength)]
    [InlineData(InvalidServiceResult.TranslationWrongHash)]
    [InlineData(InvalidServiceResult.TranslationMissingFile)]
    public async Task Invalid_service_result_fails_closed(
      InvalidServiceResult invalidResult)
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult: invalidResult))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.True(File.Exists(result.FailureReportPath));
        Assert.Contains(
          IsExportFailure(invalidResult)
            ? Stage03TechnicalFatalCodes.ExportFailed
            : Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Translation_field_identity_segments_cannot_collide()
    {
      Stage03FieldResult source = PassingField();
      source.PropertyId = "A|B";
      source.Role = "C";
      source.OwnerUniqueId = "D";
      source.ElementId = 1;
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { source },
        translationIdentityCollision: true))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
      }
    }

    [Theory]
    [InlineData(InvalidScanEnrichment.Missing)]
    [InlineData(InvalidScanEnrichment.Orphan)]
    [InlineData(InvalidScanEnrichment.MismatchedValue)]
    public async Task Scan_enrichment_must_exactly_match_exportable_fields(
      InvalidScanEnrichment invalidEnrichment)
    {
      Stage03FieldResult field = EnrichedPassingField();
      HbrIfcEnrichmentValue expected = EnrichmentValue(field);
      IReadOnlyList<HbrIfcEnrichmentValue> values;
      switch (invalidEnrichment)
      {
        case InvalidScanEnrichment.Missing:
          values = Array.Empty<HbrIfcEnrichmentValue>();
          break;
        case InvalidScanEnrichment.Orphan:
          values = new[]
          {
            expected,
            new HbrIfcEnrichmentValue
            {
              OwnerEntityType = "IfcProject",
              OwnerStrategy = HbrIfcOwnerStrategies.SingleEntityByType,
              PropertySetName = "HBR",
              PropertyName = "Orphan",
              DeclaredIfcType = "IFCLABEL",
              CanonicalValue = "orphan",
              PropertyIdentity = "ORPHAN|PROJECT|OWNER-1",
              SemanticKey = "orphan-semantic"
            }
          };
          break;
        case InvalidScanEnrichment.MismatchedValue:
          expected.CanonicalValue = "wrong";
          values = new[] { expected };
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(invalidEnrichment));
      }
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { field },
        enrichmentValues: values))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Equal(0, fixture.TranslateCalls);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidFieldStatus,
          result.TechnicalFatalCodes);
      }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Scan_cannot_claim_ifc_status_before_translation(
      bool rawStatusWasEvaluated)
    {
      Stage03FieldResult field = EnrichedPassingField();
      if (rawStatusWasEvaluated)
        field.RawIfcStatus = Stage03FieldStatus.Pass;
      else
        field.FinalIfcStatus = Stage03FieldStatus.Pass;
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { field },
        enrichmentValues: new[] { EnrichmentValue(field) }))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidFieldStatus,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Translation_cannot_rewrite_scan_owned_field_values()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult:
          InvalidServiceResult.TranslationChangesRevitValue))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.True(File.Exists(result.RawIfcPath));
      }
    }

    [Theory]
    [InlineData(InvalidServiceResult.TranslationMissingFieldDetails)]
    [InlineData(InvalidServiceResult.TranslationRawNotEvaluated)]
    [InlineData(InvalidServiceResult.TranslationFinalNotEvaluated)]
    public async Task Translation_requires_explicit_matching_ifc_field_evidence(
      InvalidServiceResult invalidResult)
    {
      Stage03FieldResult field = EnrichedPassingField();
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { field },
        enrichmentValues: new[] { EnrichmentValue(field) },
        invalidServiceResult: invalidResult))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.True(File.Exists(result.RawIfcPath));
      }
    }

    [Fact]
    public async Task Translation_machine_fatal_cannot_be_reported_as_success()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult: InvalidServiceResult.TranslationTechnicalFatal))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Force,
          "技术错误不可放行");

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.Equal(1, fixture.FailureReportCalls);
      }
    }

    [Fact]
    public async Task Null_translation_result_fails_closed_and_preserves_raw()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult: InvalidServiceResult.TranslationNullResult))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.True(File.Exists(result.RawIfcPath));
        Assert.False(File.Exists(result.FinalIfcPath));
        Assert.Empty(result.FinalIfcSha256);
        Assert.Equal(1, fixture.FailureReportCalls);
        Assert.True(File.Exists(result.FailureReportPath));
      }
    }

    [Fact]
    public async Task Non_throwing_translation_failure_preserves_root_cause_and_diagnostic()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult:
          InvalidServiceResult.TranslationFailureDtoWithRootCause))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.Contains(result.Diagnostics, diagnostic =>
          diagnostic.Code == "TRANSLATOR_SENTINEL_DIAGNOSTIC"
          && diagnostic.Stage == "translate-ifc-sentinel"
          && diagnostic.Message == "translator sentinel diagnostic");
        Assert.True(File.Exists(result.RawIfcPath));
        Assert.False(File.Exists(result.FinalIfcPath));
        Assert.Empty(result.FinalIfcSha256);
        Assert.True(File.Exists(result.FailureReportPath));

        Assert.NotNull(fixture.LastFailureReportContext);
        Exception reported = fixture.LastFailureReportContext.Exception;
        Assert.Same(fixture.TranslationFailureException, reported);
        Assert.IsType<SentinelTranslationException>(reported);
        Assert.Equal(SentinelTranslationException.ExpectedHResult,
          reported.HResult);
        Assert.IsType<InvalidDataException>(reported.InnerException);
        Assert.Equal(
          "translator sentinel inner cause",
          reported.InnerException.Message);
      }
    }

    [Fact]
    public async Task Translation_raw_batch_failure_cannot_be_reported_as_success()
    {
      Stage03FieldResult field = EnrichedPassingField();
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { field },
        enrichmentValues: new[] { EnrichmentValue(field) },
        invalidServiceResult:
          InvalidServiceResult.TranslationRawInspectionFailed))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.Equal(1, fixture.FailureReportCalls);
      }
    }

    [Fact]
    public async Task Translation_raw_inspection_evidence_must_match_workflow_fields()
    {
      Stage03FieldResult field = EnrichedPassingField();
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { field },
        enrichmentValues: new[] { EnrichmentValue(field) },
        invalidServiceResult:
          InvalidServiceResult.TranslationRawInspectionEvidenceMismatch))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.Equal(1, fixture.FailureReportCalls);
      }
    }

    [Fact]
    public async Task Translation_fatal_diagnostic_cannot_be_reported_as_success()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult:
          InvalidServiceResult.TranslationFatalDiagnostic))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Force,
          "致命诊断不可放行");

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
        Assert.Equal(1, fixture.FailureReportCalls);
      }
    }

    [Theory]
    [InlineData(InvalidServiceResult.TranslationMissingFinalInspection)]
    [InlineData(InvalidServiceResult.TranslationDuplicateFinalInspection)]
    public async Task Final_inspection_identity_must_exactly_cover_enrichment(
      InvalidServiceResult invalidResult)
    {
      Stage03FieldResult field = EnrichedPassingField();
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { field },
        enrichmentValues: new[] { EnrichmentValue(field) },
        invalidServiceResult: invalidResult))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Active_pass_field_without_translation_evidence_fails_closed()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { PassingField() },
        enrichmentValues: Array.Empty<HbrIfcEnrichmentValue>()))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Inactive_not_applicable_field_can_omit_translation_evidence()
    {
      Stage03FieldResult field = PassingField();
      field.Active = false;
      field.Status = Stage03FieldStatus.NotApplicable;
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        fields: new[] { field },
        enrichmentValues: Array.Empty<HbrIfcEnrichmentValue>()))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.True(result.Success);
      }
    }

    [Theory]
    [InlineData(InvalidServiceResult.TranslationFinalOwnerIdNotPositive)]
    [InlineData(InvalidServiceResult.TranslationFinalPropertyIdNotPositive)]
    [InlineData(InvalidServiceResult.TranslationFinalPropertySetIdNotPositive)]
    [InlineData(InvalidServiceResult.TranslationFinalRelationshipIdNotPositive)]
    public async Task Final_inspection_entity_ids_must_be_positive(
      InvalidServiceResult invalidResult)
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult: invalidResult))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Final_inspection_owner_id_must_match_final_field_owner()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult:
          InvalidServiceResult.TranslationFinalOwnerMismatch))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Final_inspection_typed_token_must_match_canonical_value()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult:
          InvalidServiceResult.TranslationFinalTypedTokenMismatch))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.InvalidIfc,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Scan_identity_mismatch_fails_before_export()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        scanIdentityMismatch: true))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Force,
          "身份错误不可放行");

        Assert.False(result.Success);
        Assert.Equal(1, fixture.ScanCalls);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Contains(
          Stage03TechnicalFatalCodes.WrongDocument,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Live_scan_document_path_must_match_the_report_path()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        scanDocumentPathMismatch: true))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Equal(1, fixture.ScanCalls);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Contains(
          Stage03TechnicalFatalCodes.WrongDocument,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Missing_rvt_path_fails_before_host_scan()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create())
      {
        fixture.DeleteDocumentFile();

        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Equal(0, fixture.ScanCalls);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Contains(
          Stage03TechnicalFatalCodes.WrongDocument,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Output_stem_must_match_the_live_rvt_file_name()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create())
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty,
          rvtStem: "WrongModel");

        Assert.False(result.Success);
        Assert.Equal(1, fixture.ScanCalls);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Contains(
          Stage03TechnicalFatalCodes.WrongDocument,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Tampered_context_hash_fails_before_scan()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        tamperedContextHash: true))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Equal(0, fixture.ScanCalls);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Contains(
          Stage03TechnicalFatalCodes.WrongDocument,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public async Task Invalid_run_id_fails_before_scan()
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create())
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty,
          "../escape");

        Assert.False(result.Success);
        Assert.Equal(0, fixture.ScanCalls);
        Assert.Equal(0, fixture.ExportCalls);
        Assert.Contains(
          "INVALID_OUTPUT_PATH",
          result.TechnicalFatalCodes);
      }
    }

    [Theory]
    [InlineData(InvalidServiceResult.ExportCreatesFinalCollision)]
    [InlineData(InvalidServiceResult.TranslationCreatesReportCollision)]
    public async Task Concurrent_official_target_occupancy_is_output_exists(
      InvalidServiceResult invalidResult)
    {
      using (CoordinatorFixture fixture = CoordinatorFixture.Create(
        invalidServiceResult: invalidResult))
      {
        Stage03RunResult result = await fixture.RunAsync(
          Stage03GateMode.Strict,
          string.Empty);

        Assert.False(result.Success);
        Assert.Contains(
          Stage03TechnicalFatalCodes.OutputExists,
          result.TechnicalFatalCodes);
      }
    }

    [Fact]
    public void Constructor_rejects_every_missing_service_delegate()
    {
      Assert.Throws<ArgumentNullException>(() =>
        new Stage03WorkflowCoordinator(null));

      Action<Stage03WorkflowServices>[] removeDelegate =
      {
        services => services.ScanAsync = null,
        services => services.ExportRawAsync = null,
        services => services.TranslateAsync = null,
        services => services.WriteFieldReport = null,
        services => services.WriteFailureReport = null,
        services => services.UtcNow = null
      };
      foreach (Action<Stage03WorkflowServices> remove in removeDelegate)
      {
        Stage03WorkflowServices services = ValidServiceShell();
        remove(services);
        Assert.Throws<ArgumentNullException>(() =>
          new Stage03WorkflowCoordinator(services));
      }
    }

    private static bool IsExportFailure(InvalidServiceResult value)
    {
      return value == InvalidServiceResult.ExportWrongPath
        || value == InvalidServiceResult.ExportWrongLength
        || value == InvalidServiceResult.ExportWrongHash
        || value == InvalidServiceResult.ExportMissingFile;
    }

    private static Stage03WorkflowServices ValidServiceShell()
    {
      return new Stage03WorkflowServices
      {
        ScanAsync = _ => Task.FromResult(new Stage03WorkflowScanResult()),
        ExportRawAsync = _ => Task.FromResult(
          new Stage03WorkflowRawExportResult()),
        TranslateAsync = _ => Task.FromResult(
          new Stage03WorkflowTranslationResult()),
        WriteFieldReport = _ => null,
        WriteFailureReport = _ => null,
        UtcNow = () => DateTimeOffset.UtcNow
      };
    }

    private static Stage03FieldResult PassingField()
    {
      return new Stage03FieldResult
      {
        PropertyId = "HBR.TEST",
        Entity = "IfcProject",
        PropertySet = "HBR",
        IfcProperty = "Test",
        Active = true,
        Status = Stage03FieldStatus.Pass,
        CarrierStatus = Stage03FieldStatus.Pass,
        ParameterStatus = Stage03FieldStatus.Pass,
        RevitStatus = Stage03FieldStatus.Pass,
        RawIfcStatus = Stage03FieldStatus.NotEvaluated,
        FinalIfcStatus = Stage03FieldStatus.NotEvaluated,
        Messages = Array.Empty<string>()
      };
    }

    private static Stage03FieldResult BlockingField()
    {
      Stage03FieldResult field = PassingField();
      field.Status = Stage03FieldStatus.MissingParameter;
      field.ParameterStatus = Stage03FieldStatus.MissingParameter;
      field.IsBusinessBlocker = true;
      field.Messages = new[] { "缺少测试参数。" };
      return field;
    }

    private static Stage03FieldResult EnrichedPassingField()
    {
      Stage03FieldResult field = PassingField();
      field.Requirement = "REQUIRED";
      field.Role = "PROJECT";
      field.ElementId = 1;
      field.OwnerUniqueId = "OWNER-1";
      field.RevitRawValue = "expected";
      field.RevitNormalizedValue = "expected";
      field.RevitValueSource = "INSTANCE";
      return field;
    }

    private static HbrIfcEnrichmentValue EnrichmentValue(
      Stage03FieldResult field)
    {
      return new HbrIfcEnrichmentValue
      {
        OwnerEntityType = field.Entity,
        OwnerStrategy = HbrIfcOwnerStrategies.SingleEntityByType,
        PropertySetName = field.PropertySet,
        PropertyName = field.IfcProperty,
        DeclaredIfcType = "IFCLABEL",
        CanonicalValue = field.RevitNormalizedValue,
        PropertyIdentity = field.PropertyId + "|" + field.Role + "|"
          + field.OwnerUniqueId,
        SemanticKey = "semantic|" + field.OwnerUniqueId
      };
    }

    private static string Sha256(string path)
    {
      using (SHA256 algorithm = SHA256.Create())
      using (FileStream stream = File.OpenRead(path))
      {
        return string.Concat(
          algorithm.ComputeHash(stream)
            .Select(value => value.ToString("x2")));
      }
    }

    public enum InvalidServiceResult
    {
      None,
      ExportWrongPath,
      ExportWrongLength,
      ExportWrongHash,
      ExportMissingFile,
      ExportCreatesFinalCollision,
      TranslationWrongPath,
      TranslationWrongLength,
      TranslationWrongHash,
      TranslationMissingFile,
      TranslationChangesRevitValue,
      TranslationRawNotEvaluated,
      TranslationFinalNotEvaluated,
      TranslationTechnicalFatal,
      TranslationNullResult,
      TranslationFailureDtoWithRootCause,
      TranslationMissingFieldDetails,
      TranslationMissingFinalInspection,
      TranslationDuplicateFinalInspection,
      TranslationFinalOwnerIdNotPositive,
      TranslationFinalPropertyIdNotPositive,
      TranslationFinalPropertySetIdNotPositive,
      TranslationFinalRelationshipIdNotPositive,
      TranslationFinalOwnerMismatch,
      TranslationFinalTypedTokenMismatch,
      TranslationRawInspectionFailed,
      TranslationRawInspectionEvidenceMismatch,
      TranslationFatalDiagnostic,
      TranslationCreatesReportCollision,
      FieldReportMutatesRaw,
      FieldReportMutatesFinal,
      FieldReportMutatesBothIfc,
      FieldReportMutatesWorkflowSnapshot,
      StrictFieldReportCreatesIfcArtifacts
    }

    public enum InvalidScanEnrichment
    {
      Missing,
      Orphan,
      MismatchedValue
    }

    private sealed class CoordinatorFixture : IDisposable
    {
      private readonly string _directory;
      private readonly string _documentPath;
      private readonly HBRFileContext _context;
      private readonly Stage03WorkflowCoordinator _coordinator;
      private readonly InvalidServiceResult _invalidServiceResult;
      private readonly bool _translatorFails;
      private readonly bool _failureReportFails;
      private readonly bool _scanIdentityMismatch;
      private readonly bool _mutateScanRequest;
      private readonly bool _mutateTranslationRequest;
      private readonly bool _clockFails;
      private readonly bool _scanDocumentPathMismatch;
      private readonly bool _translationIdentityCollision;
      private readonly IReadOnlyList<string> _technicalFatalCodes;
      private readonly IReadOnlyList<HbrIfcEnrichmentValue> _enrichmentValues;

      private CoordinatorFixture(
        string directory,
        string documentPath,
        HBRFileContext context,
        IEnumerable<Stage03FieldResult> fields,
        IEnumerable<HbrIfcEnrichmentValue> enrichmentValues,
        IEnumerable<string> technicalFatalCodes,
        InvalidServiceResult invalidServiceResult,
        bool translatorFails,
        bool failureReportFails,
        bool scanIdentityMismatch,
        bool mutateScanRequest,
        bool mutateTranslationRequest,
        bool clockFails,
        bool scanDocumentPathMismatch,
        bool translationIdentityCollision)
      {
        _directory = directory;
        _documentPath = documentPath;
        _context = context;
        _invalidServiceResult = invalidServiceResult;
        _translatorFails = translatorFails;
        _failureReportFails = failureReportFails;
        _scanIdentityMismatch = scanIdentityMismatch;
        _mutateScanRequest = mutateScanRequest;
        _mutateTranslationRequest = mutateTranslationRequest;
        _clockFails = clockFails;
        _scanDocumentPathMismatch = scanDocumentPathMismatch;
        _translationIdentityCollision = translationIdentityCollision;
        TranslationFailureException = new SentinelTranslationException(
          "translator sentinel root cause",
          new InvalidDataException("translator sentinel inner cause"));
        _technicalFatalCodes = (technicalFatalCodes ?? Array.Empty<string>())
          .ToArray();
        if (fields == null)
        {
          Stage03FieldResult defaultField = EnrichedPassingField();
          SourceFields = new List<Stage03FieldResult> { defaultField };
          _enrichmentValues = (enrichmentValues
            ?? new[] { EnrichmentValue(defaultField) }).ToArray();
        }
        else
        {
          SourceFields = fields.ToList();
          _enrichmentValues = (enrichmentValues
            ?? Array.Empty<HbrIfcEnrichmentValue>()).ToArray();
        }
        var services = new Stage03WorkflowServices
        {
          ScanAsync = ScanAsync,
          ExportRawAsync = ExportRawAsync,
          TranslateAsync = TranslateAsync,
          WriteFieldReport = WriteFieldReport,
          WriteFailureReport = WriteFailureReport,
          UtcNow = UtcNow
        };
        _coordinator = new Stage03WorkflowCoordinator(services);
      }

      internal List<Stage03FieldResult> SourceFields { get; }
      internal int ScanCalls { get; private set; }
      internal int ExportCalls { get; private set; }
      internal int TranslateCalls { get; private set; }
      internal int FieldReportCalls { get; private set; }
      internal int FailureReportCalls { get; private set; }
      internal Stage03FieldReportContext LastFieldReportContext
      {
        get;
        private set;
      }
      internal Stage03FailureReportContext LastFailureReportContext
      {
        get;
        private set;
      }
      internal Exception TranslationFailureException { get; }

      internal void DeleteDocumentFile()
      {
        File.Delete(_documentPath);
      }

      internal static CoordinatorFixture Create(
        IEnumerable<Stage03FieldResult> fields = null,
        IEnumerable<HbrIfcEnrichmentValue> enrichmentValues = null,
        IEnumerable<string> technicalFatalCodes = null,
        InvalidServiceResult invalidServiceResult = InvalidServiceResult.None,
        bool translatorFails = false,
        bool failureReportFails = false,
        bool scanIdentityMismatch = false,
        bool tamperedContextHash = false,
        bool mutateScanRequest = false,
        bool mutateTranslationRequest = false,
        bool clockFails = false,
        bool scanDocumentPathMismatch = false,
        bool translationIdentityCollision = false)
      {
        string directory = Path.Combine(
          Path.GetTempPath(),
          "hbr-stage03-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string documentPath = Path.Combine(directory, "Model.rvt");
        File.WriteAllText(documentPath, "fixture rvt");
        if (scanDocumentPathMismatch)
          File.WriteAllText(Path.Combine(directory, "Other.rvt"), "other rvt");
        return new CoordinatorFixture(
          directory,
          documentPath,
          CreateContext(!tamperedContextHash, documentPath),
          fields,
          enrichmentValues,
          technicalFatalCodes,
          invalidServiceResult,
          translatorFails,
          failureReportFails,
          scanIdentityMismatch,
          mutateScanRequest,
          mutateTranslationRequest,
          clockFails,
          scanDocumentPathMismatch,
          translationIdentityCollision);
      }

      internal Task<Stage03RunResult> RunAsync(
        Stage03GateMode mode,
        string forceReason,
        string runId = "20260804T120000Z-fixture",
        string rvtStem = "Model")
      {
        return _coordinator.RunAsync(new Stage03WorkflowRequest
        {
          Context = _context,
          OutputDirectory = _directory,
          RvtStem = rvtStem,
          RunId = runId,
          DocumentPath = _documentPath,
          PluginVersion = "0.9.0",
          Mode = mode,
          ForceReason = forceReason
        });
      }

      public void Dispose()
      {
        if (Directory.Exists(_directory))
          Directory.Delete(_directory, true);
      }

      private Task<Stage03WorkflowScanResult> ScanAsync(
        Stage03WorkflowRequest request)
      {
        ScanCalls++;
        if (_mutateScanRequest)
        {
          request.Mode = Stage03GateMode.Force;
          request.ForceReason = "扫描端口不应控制门禁。";
        }
        return Task.FromResult(new Stage03WorkflowScanResult
        {
          FileGuid = _context.FileGuid,
          DocumentFingerprint = _context.RevitDocumentFingerprint,
          DocumentTitle = _context.RevitDocumentTitle,
          DocumentPath = _scanDocumentPathMismatch
            ? Path.Combine(_directory, "Other.rvt")
            : _documentPath,
          RevitVersion = "2020",
          RulePackageId = _context.RulePackageId,
          RulePackageVersion = _context.RulePackageVersion,
          RulePackageSha256 = _scanIdentityMismatch
            ? new string('d', 64)
            : _context.RulePackageSha256,
          Carriers = _invalidServiceResult ==
              InvalidServiceResult.FieldReportMutatesWorkflowSnapshot
            ? new[]
            {
              new Stage03CarrierResult
              {
                Entity = "IfcProject",
                Role = "PROJECT",
                ElementId = 1,
                UniqueId = "CARRIER-1",
                Category = "Project Information",
                Name = "fixture carrier",
                Status = Stage03FieldStatus.Pass,
                Active = true,
                Messages = Array.Empty<string>()
              }
            }
            : Array.Empty<Stage03CarrierResult>(),
          Fields = SourceFields,
          EnrichmentValues = _enrichmentValues,
          TechnicalFatalCodes = _technicalFatalCodes,
          Diagnostics = _invalidServiceResult ==
              InvalidServiceResult.FieldReportMutatesWorkflowSnapshot
            ? new[]
            {
              new Stage03Diagnostic
              {
                Code = "FIXTURE_INFO",
                Stage = "scan",
                Severity = "INFO",
                Message = "fixture diagnostic"
              }
            }
            : Array.Empty<Stage03Diagnostic>()
        });
      }

      private Task<Stage03WorkflowRawExportResult> ExportRawAsync(
        Stage03WorkflowExportRequest request)
      {
        ExportCalls++;
        if (_invalidServiceResult != InvalidServiceResult.ExportMissingFile)
          File.WriteAllText(request.RawIfcPath, MinimalIfc4());
        if (_invalidServiceResult ==
          InvalidServiceResult.ExportCreatesFinalCollision)
        {
          File.WriteAllText(
            request.RawIfcPath.Substring(
              0,
              request.RawIfcPath.Length - "-RAW.ifc".Length)
              + "-HIFC-MVD.ifc",
            "occupied");
        }
        long actualLength = File.Exists(request.RawIfcPath)
          ? new FileInfo(request.RawIfcPath).Length
          : 0L;
        string actualHash = File.Exists(request.RawIfcPath)
          ? Sha256(request.RawIfcPath)
          : new string('0', 64);
        return Task.FromResult(new Stage03WorkflowRawExportResult
        {
          RawIfcPath = _invalidServiceResult ==
              InvalidServiceResult.ExportWrongPath
            ? Path.Combine(_directory, "wrong-RAW.ifc")
            : request.RawIfcPath,
          RawIfcLength = _invalidServiceResult ==
              InvalidServiceResult.ExportWrongLength
            ? actualLength + 1L
            : actualLength,
          RawIfcSha256 = _invalidServiceResult ==
              InvalidServiceResult.ExportWrongHash
            ? new string('f', 64)
            : actualHash
        });
      }

      private Task<Stage03WorkflowTranslationResult> TranslateAsync(
        Stage03WorkflowTranslationRequest request)
      {
        TranslateCalls++;
        if (_translatorFails)
        {
          return Task.FromException<Stage03WorkflowTranslationResult>(
            new InvalidDataException("fixture translation failure"));
        }
        if (_invalidServiceResult ==
          InvalidServiceResult.TranslationNullResult)
        {
          return Task.FromResult<Stage03WorkflowTranslationResult>(null);
        }
        if (_invalidServiceResult ==
          InvalidServiceResult.TranslationFailureDtoWithRootCause)
        {
          var failure = new Stage03WorkflowTranslationResult
          {
            Success = false,
            TechnicalFatalCodes = new[]
            {
              Stage03TechnicalFatalCodes.InvalidIfc
            },
            FinalIfcPath = string.Empty,
            FinalIfcLength = 0L,
            FinalIfcSha256 = string.Empty,
            Fields = request.Fields,
            Diagnostics = new[]
            {
              new Stage03Diagnostic
              {
                Code = "TRANSLATOR_SENTINEL_DIAGNOSTIC",
                Stage = "translate-ifc-sentinel",
                Severity = "ERROR",
                Message = "translator sentinel diagnostic"
              }
            }
          };
          failure.FailureException = TranslationFailureException;
          return Task.FromResult(failure);
        }
        if (_invalidServiceResult !=
          InvalidServiceResult.TranslationMissingFile)
        {
          File.WriteAllText(request.FinalIfcPath, MinimalIfc4());
        }
        if (_invalidServiceResult ==
          InvalidServiceResult.TranslationCreatesReportCollision)
        {
          File.WriteAllText(
            request.FinalIfcPath.Substring(
              0,
              request.FinalIfcPath.Length - "-HIFC-MVD.ifc".Length)
              + "-fields.json",
            "occupied");
        }
        if (_mutateTranslationRequest && request.Fields.Count > 0)
          request.Fields[0].PropertyId = "MUTATED-BY-TRANSLATOR";
        long actualLength = File.Exists(request.FinalIfcPath)
          ? new FileInfo(request.FinalIfcPath).Length
          : 0L;
        string actualHash = File.Exists(request.FinalIfcPath)
          ? Sha256(request.FinalIfcPath)
          : new string('0', 64);
        Stage03FieldResult[] translated = request.Fields
          .Select(TranslatedField)
          .ToArray();
        if (_invalidServiceResult !=
          InvalidServiceResult.TranslationMissingFieldDetails)
        {
          PopulateIfcEvidence(translated, request.EnrichmentValues);
        }
        if (_translationIdentityCollision && translated.Length > 0)
        {
          translated[0].PropertyId = "A";
          translated[0].Role = "B|C";
        }
        if (_invalidServiceResult ==
            InvalidServiceResult.TranslationChangesRevitValue
          && translated.Length > 0)
        {
          translated[0].RevitNormalizedValue = "rewritten";
        }
        if (_invalidServiceResult ==
            InvalidServiceResult.TranslationRawNotEvaluated
          && translated.Length > 0)
        {
          translated[0].RawIfcStatus = Stage03FieldStatus.NotEvaluated;
        }
        if (_invalidServiceResult ==
            InvalidServiceResult.TranslationFinalNotEvaluated
          && translated.Length > 0)
        {
          translated[0].FinalIfcStatus = Stage03FieldStatus.NotEvaluated;
        }
        HbrIfcBatchInspectionResult rawInspection = SuccessfulInspection(
          request.EnrichmentValues);
        HbrIfcBatchInspectionResult finalInspection = SuccessfulInspection(
          request.EnrichmentValues);
        if (_invalidServiceResult ==
          InvalidServiceResult.TranslationRawInspectionFailed)
        {
          rawInspection = FailedInspection(request.EnrichmentValues[0]);
        }
        if (_invalidServiceResult ==
          InvalidServiceResult.TranslationRawInspectionEvidenceMismatch)
        {
          rawInspection = MismatchedInspection(request.EnrichmentValues[0]);
        }
        if (_invalidServiceResult ==
          InvalidServiceResult.TranslationMissingFinalInspection)
        {
          finalInspection = EmptySuccessfulInspection();
        }
        if (_invalidServiceResult ==
            InvalidServiceResult.TranslationDuplicateFinalInspection
          && request.EnrichmentValues.Count > 0)
        {
          HbrIfcFieldInspectionResult item = SuccessfulInspectionField(
            request.EnrichmentValues[0]);
          finalInspection = new HbrIfcBatchInspectionResult(
            true,
            string.Empty,
            "fixture duplicate inspection",
            new[] { item, item });
        }
        if (IsInvalidFinalInspection(_invalidServiceResult))
        {
          HbrIfcEnrichmentValue value = request.EnrichmentValues[0];
          finalInspection = FinalInspection(
            value,
            _invalidServiceResult ==
                InvalidServiceResult.TranslationFinalOwnerIdNotPositive
              ? 0
              : _invalidServiceResult ==
                  InvalidServiceResult.TranslationFinalOwnerMismatch
                ? 2
                : 1,
            _invalidServiceResult ==
                InvalidServiceResult.TranslationFinalPropertyIdNotPositive
              ? 0
              : 2,
            _invalidServiceResult ==
                InvalidServiceResult.TranslationFinalPropertySetIdNotPositive
              ? 0
              : 3,
            _invalidServiceResult ==
                InvalidServiceResult.TranslationFinalRelationshipIdNotPositive
              ? 0
              : 4,
            _invalidServiceResult ==
                InvalidServiceResult.TranslationFinalTypedTokenMismatch
              ? "IFCLABEL('wrong')"
              : TypedToken(value));
        }
        return Task.FromResult(new Stage03WorkflowTranslationResult
        {
          Success = _invalidServiceResult !=
            InvalidServiceResult.TranslationTechnicalFatal,
          TechnicalFatalCodes = _invalidServiceResult ==
              InvalidServiceResult.TranslationTechnicalFatal
            ? new[] { "TRANSLATOR_FATAL" }
            : Array.Empty<string>(),
          RawInspection = rawInspection,
          FinalInspection = finalInspection,
          FinalIfcPath = _invalidServiceResult ==
              InvalidServiceResult.TranslationWrongPath
            ? Path.Combine(_directory, "wrong-HIFC-MVD.ifc")
            : request.FinalIfcPath,
          FinalIfcLength = _invalidServiceResult ==
              InvalidServiceResult.TranslationWrongLength
            ? actualLength + 1L
            : actualLength,
          FinalIfcSha256 = _invalidServiceResult ==
              InvalidServiceResult.TranslationWrongHash
            ? new string('f', 64)
            : actualHash,
          Fields = translated,
          Diagnostics = _invalidServiceResult ==
              InvalidServiceResult.TranslationFatalDiagnostic
            ? new[]
            {
              new Stage03Diagnostic
              {
                Code = "TRANSLATOR_FATAL",
                Stage = "translate-ifc",
                Severity = "FATAL",
                Message = "fixture fatal diagnostic"
              }
            }
            : Array.Empty<Stage03Diagnostic>()
        });
      }

      private static HbrIfcBatchInspectionResult FailedInspection(
        HbrIfcEnrichmentValue value)
      {
        var field = new HbrIfcFieldInspectionResult(
          value.PropertyIdentity,
          false,
          "RAW_INSPECTION_FAILED",
          "fixture raw inspection failure");
        return new HbrIfcBatchInspectionResult(
          false,
          "RAW_INSPECTION_FAILED",
          "fixture raw inspection failure",
          new[] { field });
      }

      private static HbrIfcBatchInspectionResult MismatchedInspection(
        HbrIfcEnrichmentValue value)
      {
        var field = new HbrIfcFieldInspectionResult(
          value.PropertyIdentity,
          true,
          string.Empty,
          "fixture mismatched raw evidence",
          ownerId: null,
          propertyId: 2,
          propertySetId: 3,
          relationshipId: 4,
          actualIfcType: "IFCTEXT",
          typedToken: string.Empty);
        return new HbrIfcBatchInspectionResult(
          true,
          string.Empty,
          "fixture mismatched raw evidence",
          new[] { field });
      }

      private static HbrIfcBatchInspectionResult EmptySuccessfulInspection()
      {
        return new HbrIfcBatchInspectionResult(
          true,
          string.Empty,
          "fixture inspection",
          Array.Empty<HbrIfcFieldInspectionResult>());
      }

      private static HbrIfcBatchInspectionResult SuccessfulInspection(
        IReadOnlyList<HbrIfcEnrichmentValue> values)
      {
        HbrIfcFieldInspectionResult[] fields = values
          .Select(SuccessfulInspectionField)
          .ToArray();
        return new HbrIfcBatchInspectionResult(
          true,
          string.Empty,
          "fixture inspection",
          fields);
      }

      private static bool IsInvalidFinalInspection(
        InvalidServiceResult value)
      {
        return value == InvalidServiceResult.TranslationFinalOwnerIdNotPositive
          || value ==
            InvalidServiceResult.TranslationFinalPropertyIdNotPositive
          || value ==
            InvalidServiceResult.TranslationFinalPropertySetIdNotPositive
          || value ==
            InvalidServiceResult.TranslationFinalRelationshipIdNotPositive
          || value == InvalidServiceResult.TranslationFinalOwnerMismatch
          || value == InvalidServiceResult.TranslationFinalTypedTokenMismatch;
      }

      private static HbrIfcBatchInspectionResult FinalInspection(
        HbrIfcEnrichmentValue value,
        int ownerId,
        int propertyId,
        int propertySetId,
        int relationshipId,
        string typedToken)
      {
        var field = new HbrIfcFieldInspectionResult(
          value.PropertyIdentity,
          true,
          string.Empty,
          "fixture final inspection",
          ownerId,
          propertyId,
          propertySetId,
          relationshipId,
          value.DeclaredIfcType,
          typedToken);
        return new HbrIfcBatchInspectionResult(
          true,
          string.Empty,
          "fixture final inspection",
          new[] { field });
      }

      private static HbrIfcFieldInspectionResult SuccessfulInspectionField(
        HbrIfcEnrichmentValue value)
      {
        return new HbrIfcFieldInspectionResult(
          value.PropertyIdentity,
          true,
          string.Empty,
          "fixture field inspection",
          ownerId: 1,
          propertyId: 2,
          propertySetId: 3,
          relationshipId: 4,
          actualIfcType: value.DeclaredIfcType,
          typedToken: TypedToken(value));
      }

      private static string TypedToken(HbrIfcEnrichmentValue value)
      {
        HbrIfcCanonicalValueDecision decision =
          HbrIfcCanonicalValuePolicy.Validate(
            value.DeclaredIfcType,
            value.CanonicalValue);
        string inner = decision.RequiresStringEncoding
          ? IfcStepSyntax.EncodeString(decision.NormalizedValue)
          : decision.NormalizedValue;
        return IfcStepSyntax.FormatTypedValue(
          value.DeclaredIfcType,
          inner);
      }

      private static void PopulateIfcEvidence(
        IEnumerable<Stage03FieldResult> fields,
        IEnumerable<HbrIfcEnrichmentValue> values)
      {
        var byIdentity = fields.ToDictionary(
          field => field.PropertyId + "|" + field.Role + "|"
            + field.OwnerUniqueId,
          StringComparer.Ordinal);
        foreach (HbrIfcEnrichmentValue value in values)
        {
          Stage03FieldResult field = byIdentity[value.PropertyIdentity];
          field.RawIfcOwner = "#1";
          field.RawIfcPropertySet = value.PropertySetName;
          field.RawIfcProperty = value.PropertyName;
          field.RawIfcType = value.DeclaredIfcType;
          field.RawIfcValue = value.CanonicalValue;
          field.RawIfcStatus = Stage03FieldStatus.Pass;
          field.FinalIfcOwner = "#1";
          field.FinalIfcPropertySet = value.PropertySetName;
          field.FinalIfcProperty = value.PropertyName;
          field.FinalIfcType = value.DeclaredIfcType;
          field.FinalIfcValue = value.CanonicalValue;
          field.FinalIfcStatus = Stage03FieldStatus.Pass;
        }
      }

      private Stage03FieldReportWriteResult WriteFieldReport(
        Stage03FieldReportContext context)
      {
        FieldReportCalls++;
        LastFieldReportContext = context;
        File.WriteAllText(context.OutputPaths.FieldReport, "{\"fixture\":true}");
        if (_invalidServiceResult == InvalidServiceResult.FieldReportMutatesRaw)
          File.AppendAllText(context.OutputPaths.RawIfc, "MUTATED");
        if (_invalidServiceResult == InvalidServiceResult.FieldReportMutatesFinal)
          File.AppendAllText(context.OutputPaths.FinalIfc, "MUTATED");
        if (_invalidServiceResult == InvalidServiceResult.FieldReportMutatesBothIfc)
        {
          File.AppendAllText(context.OutputPaths.RawIfc, "MUTATED");
          File.AppendAllText(context.OutputPaths.FinalIfc, "MUTATED");
        }
        if (_invalidServiceResult ==
          InvalidServiceResult.FieldReportMutatesWorkflowSnapshot)
        {
          context.Fields[0].PropertyId = "MUTATED-BY-REPORT-WRITER";
          context.Carriers[0].Name = "mutated carrier";
          context.Diagnostics[0].Severity = "FATAL";
        }
        if (_invalidServiceResult ==
          InvalidServiceResult.StrictFieldReportCreatesIfcArtifacts)
        {
          File.WriteAllText(context.OutputPaths.RawIfc, MinimalIfc4());
          File.WriteAllText(context.OutputPaths.FinalIfc, MinimalIfc4());
        }
        return new Stage03FieldReportWriteResult(
          context.OutputPaths.FieldReport,
          new string('e', 64),
          Sha256(context.OutputPaths.FieldReport));
      }

      private Stage03FailureReportWriteResult WriteFailureReport(
        Stage03FailureReportContext context)
      {
        FailureReportCalls++;
        LastFailureReportContext = context;
        string reportPath = Path.Combine(
          _directory,
          "BIMBaoGui.Stage03.failure-fixture.json");
        if (_failureReportFails)
        {
          return new Stage03FailureReportWriteResult
          {
            Success = false,
            ReportPath = reportPath,
            ErrorCode = Stage03TechnicalFatalCodes.ReportFailed,
            OriginalExceptionSummary = context.Exception.Message,
            ReportWriteErrorSummary = "fixture report failure"
          };
        }
        File.WriteAllText(
          reportPath,
          new JavaScriptSerializer().Serialize(
            new Dictionary<string, object>
            {
              ["runId"] = context.RunId
            }));
        return new Stage03FailureReportWriteResult
        {
          Success = true,
          ReportPath = reportPath,
          OriginalExceptionSummary = context.Exception.Message
        };
      }

      private DateTimeOffset UtcNow()
      {
        if (_clockFails)
          throw new InvalidOperationException("fixture clock failure");
        return new DateTimeOffset(
          2026,
          8,
          4,
          12,
          0,
          0,
          TimeSpan.Zero);
      }

      private static HBRFileContext CreateContext(
        bool validHash,
        string documentPath)
      {
        var provisional = new HBRFileContext(
          HBRContextVersions.FileContextSchema,
          "0.9.0",
          "11111111-1111-1111-1111-111111111111",
          HBRDocumentFingerprint.Compute(documentPath, "Model", "2020"),
          "Model",
          "P-001",
          "测试项目",
          "S-001",
          "测试子项",
          "ARCHITECTURE",
          "WHOLE_MODEL",
          null,
          new Dictionary<string, PlanningTargetValue>(),
          new Dictionary<string, bool>(),
          Array.Empty<string>(),
          Array.Empty<string>(),
          true,
          true,
          "1.0.0",
          "hbr-rules",
          "1.0.0",
          new string('b', 64),
          new string('c', 64),
          string.Empty);
        return provisional.WithHash(
          validHash
            ? HBRFileContextCanonicalizer.ComputeHash(provisional)
            : new string('0', 64));
      }

      private static Stage03FieldResult TranslatedField(
        Stage03FieldResult source)
      {
        return new Stage03FieldResult
        {
          PropertyId = source.PropertyId,
          ContractKind = source.ContractKind,
          Requirement = source.Requirement,
          Applicability = source.Applicability,
          Entity = source.Entity,
          PropertySet = source.PropertySet,
          IfcProperty = source.IfcProperty,
          Role = source.Role,
          ElementId = source.ElementId,
          OwnerUniqueId = source.OwnerUniqueId,
          ParameterGuid = source.ParameterGuid,
          ParameterName = source.ParameterName,
          ParameterScope = source.ParameterScope,
          CarrierStatus = source.CarrierStatus,
          ParameterStatus = source.ParameterStatus,
          RevitStatus = source.RevitStatus,
          RevitRawValue = source.RevitRawValue,
          RevitNormalizedValue = source.RevitNormalizedValue,
          RevitValueSource = source.RevitValueSource,
          RawIfcStatus = Stage03FieldStatus.NotEvaluated,
          FinalIfcStatus = Stage03FieldStatus.NotEvaluated,
          Status = source.Status,
          Active = source.Active,
          IsBusinessBlocker = source.IsBusinessBlocker,
          Messages = source.Messages == null
            ? Array.Empty<string>()
            : source.Messages.ToArray()
        };
      }

      private static string MinimalIfc4()
      {
        return "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\n"
          + "DATA;\nENDSEC;\nEND-ISO-10303-21;\n";
      }
    }

    private sealed class SentinelTranslationException : IOException
    {
      internal const int ExpectedHResult = unchecked((int)0x81234567);

      internal SentinelTranslationException(
        string message,
        Exception innerException)
        : base(message, innerException)
      {
        HResult = ExpectedHResult;
      }
    }
  }
}
