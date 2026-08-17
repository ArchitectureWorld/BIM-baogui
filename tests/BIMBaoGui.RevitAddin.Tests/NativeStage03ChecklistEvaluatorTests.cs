using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Stage02B;
using BIMBaoGui.RevitAddin.Stage03;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage03ChecklistEvaluatorTests
  {
    private const string ElementUniqueId = "element-1";
    private const string RoleId = "SITE_TOTAL_LAND";
    private const string TaskId = "SITE.TOTAL_LAND";
    private const string PropertyId =
      "b970d6b1-92c9-51d2-8fac-187808a07801";

    [Fact]
    public void Scan_not_executed_is_the_only_not_checked_state()
    {
      NativeStage03SourceEvidenceBundle evidence = CurrentBundle();
      evidence.ScanExecuted = false;

      NativeStage03ChecklistItem item = Assert.Single(
        NativeStage03ChecklistEvaluator.Evaluate(
          new[] { RoleDefinition() }, evidence));

      Assert.Equal(NativeStage03ChecklistStatus.NotChecked, item.Status);
      Assert.Empty(item.IssueCode);
    }

    [Fact]
    public void Missing_required_element_and_required_data_are_stable_red_codes()
    {
      NativeStage03SourceEvidenceBundle evidence = CurrentBundle();
      evidence.Stage01.Model.SetValue(NativeStage01Keys.ProjectName, string.Empty);

      NativeStage03ChecklistItem[] items = NativeStage03ChecklistEvaluator
        .Evaluate(new[]
        {
          RoleDefinition(),
          new NativeReportingCheckDefinition
          {
            CheckId = "STAGE01.PROJECT_NAME",
            SourceStage = NativeReportingSourceStage.Stage01,
            CheckKind = NativeReportingCheckKind.Stage01Field,
            FieldKey = NativeStage01Keys.ProjectName
          }
        }, evidence).ToArray();

      Assert.Equal("MISSING_REQUIRED_ELEMENT", items[0].IssueCode);
      Assert.Equal(NativeStage03ChecklistStatus.Failed, items[0].Status);
      Assert.Equal("MISSING_REQUIRED_DATA", items[1].IssueCode);
      Assert.Equal(NativeStage03ChecklistStatus.Failed, items[1].Status);
    }

    [Fact]
    public void Low_confidence_unconfirmed_candidate_is_warning_not_blocker()
    {
      NativeStage03SourceEvidenceBundle evidence = CurrentBundle(
        new NativeStage02ElementPlan
        {
          Element = Element(),
          RoleMatchStatus = NativeStage02RoleMatchStatus.NameNotMatched,
          Candidates = new[]
          {
            new NativeStage02SemanticCandidate
            {
              RoleId = RoleId,
              Confidence = "LOW"
            }
          },
          RoleConfirmation = new NativeStage02RoleConfirmationDecision
          {
            Confirmed = false,
            Code = "ROLE_CONFIRMATION_REQUIRED",
            ResolvedRoleId = RoleId
          }
        });

      NativeStage03ChecklistItem item = Assert.Single(
        NativeStage03ChecklistEvaluator.Evaluate(
          new[] { RoleDefinition() }, evidence));

      Assert.Equal(NativeStage03ChecklistStatus.Warning, item.Status);
      Assert.Equal("LOW_CONFIDENCE_CANDIDATE", item.IssueCode);
    }

    [Fact]
    public void Role_or_bounding_box_alone_cannot_make_geometry_green()
    {
      NativeStage02ElementPlan plan = ConfirmedElement();
      plan.Element.Geometry = new NativeStage02GeometryEvidence
      {
        BoundingBox = new NativeStage02BoundingBoxEvidence
        {
          Available = true,
          MinXFeet = 0,
          MinYFeet = 0,
          MaxXFeet = 10,
          MaxYFeet = 10
        },
        EvidenceHash = new string('6', 64)
      };
      plan.TaskGeometry = null;
      NativeStage03SourceEvidenceBundle evidence = CurrentBundle(plan);
      evidence.Stage02AResult = Result(evidence.CurrentIdentity,
        evidence.Stage02ACurrentInputSnapshotHash,
        RoleItem(true));

      NativeStage03ChecklistItem item = Assert.Single(
        NativeStage03ChecklistEvaluator.Evaluate(
          new[] { GeometryDefinition() }, evidence));

      Assert.Equal(NativeStage03ChecklistStatus.Failed, item.Status);
      Assert.Equal("GEOMETRY_CAPTURE_UNSUPPORTED", item.IssueCode);
    }

    [Fact]
    public void Current_write_readback_and_rule_evidence_pass_internally()
    {
      NativeReportingCheckDefinition definition = GeometryDefinition();
      NativeStage02ElementPlan plan = ConfirmedElement(definition,
        NativeStage02GeometryCheckState.Passed,
        "GEOMETRY_CHECK_PASSED");
      NativeStage03SourceEvidenceBundle evidence = CurrentBundle(plan);
      evidence.Stage02AResult = Result(evidence.CurrentIdentity,
        evidence.Stage02ACurrentInputSnapshotHash,
        RoleItem(true), GeometryItem(definition, true));

      NativeStage03ChecklistItem item = Assert.Single(
        NativeStage03ChecklistEvaluator.Evaluate(
          new[] { definition }, evidence));

      Assert.Equal(NativeStage03ChecklistStatus.Passed, item.Status);
      Assert.True(item.InternalValidationPassed);
      Assert.True(item.OfficialAcceptancePassed);
      Assert.Empty(item.IssueCode);
    }

    [Fact]
    public void Internal_pass_with_pending_official_carrier_is_yellow_and_not_official()
    {
      NativeReportingCheckDefinition definition = GeometryDefinition();
      definition.OfficialCarrierStatus =
        NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt;
      NativeStage02ElementPlan plan = ConfirmedElement(definition,
        NativeStage02GeometryCheckState.Passed,
        "GEOMETRY_CHECK_PASSED");
      NativeStage03SourceEvidenceBundle evidence = CurrentBundle(plan);
      evidence.Stage02AResult = Result(evidence.CurrentIdentity,
        evidence.Stage02ACurrentInputSnapshotHash,
        RoleItem(true), GeometryItem(definition, true));

      NativeStage03ChecklistItem item = Assert.Single(
        NativeStage03ChecklistEvaluator.Evaluate(
          new[] { definition }, evidence));

      Assert.Equal(NativeStage03ChecklistStatus.Warning, item.Status);
      Assert.Equal("INTERNAL_PASS_OFFICIAL_PENDING", item.IssueCode);
      Assert.True(item.InternalValidationPassed);
      Assert.False(item.OfficialAcceptancePassed);
    }

    [Fact]
    public void Failed_latest_stage02b_attempt_invalidates_old_success()
    {
      NativeStage03SourceEvidenceBundle evidence = CurrentBundle();
      evidence.Stage02B.Records = new[]
      {
        MetricRecord("run-new", "run-old", "FAILED", "SUCCEEDED", "25")
      };
      var definition = new NativeReportingCheckDefinition
      {
        CheckId = "STAGE02B.METRIC." + PropertyId,
        SourceStage = NativeReportingSourceStage.Stage02B,
        CheckKind = NativeReportingCheckKind.Stage02BMetric,
        PropertyId = PropertyId
      };

      NativeStage03ChecklistItem item = Assert.Single(
        NativeStage03ChecklistEvaluator.Evaluate(
          new[] { definition }, evidence));

      Assert.Equal(NativeStage03ChecklistStatus.Failed, item.Status);
      Assert.Equal("READBACK_FAILED", item.IssueCode);
    }

    [Fact]
    public void Pending_stage02b_official_carrier_remains_a_business_blocker()
    {
      NativeStage03SourceEvidenceBundle evidence = CurrentBundle();
      evidence.Stage02B.Records = new[]
      {
        MetricRecord(
          "run-current",
          "run-current",
          "SUCCEEDED",
          "SUCCEEDED",
          "25",
          NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt)
      };
      var definition = new NativeReportingCheckDefinition
      {
        CheckId = "STAGE02B.METRIC." + PropertyId,
        SourceStage = NativeReportingSourceStage.Stage02B,
        CheckKind = NativeReportingCheckKind.Stage02BMetric,
        PropertyId = PropertyId,
        OfficialCarrierStatus =
          NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt
      };

      NativeStage03ChecklistItem item = Assert.Single(
        NativeStage03ChecklistEvaluator.Evaluate(
          new[] { definition }, evidence));

      Assert.Equal(NativeStage03ChecklistStatus.Failed, item.Status);
      Assert.Equal("OFFICIAL_CARRIER_PENDING_GOLDEN_RVT", item.IssueCode);
      Assert.False(item.OfficialAcceptancePassed);
    }

    [Theory]
    [InlineData("<=", "30", "25", "Passed", "")]
    [InlineData("<=", "20", "25", "Failed",
      "TARGET_COMPARISON_FAILED")]
    [InlineData(">=", "20", "25", "Passed", "")]
    public void Target_comparison_uses_explicit_property_mapping_and_operator(
      string comparisonOperator,
      string target,
      string actual,
      string expectedStatus,
      string expectedCode)
    {
      const string targetKey = "planning.building_density";
      NativeStage03SourceEvidenceBundle evidence = CurrentBundle();
      evidence.Stage01.Model.PlanningTargets[targetKey] =
        new NativePlanningTargetValue(
          comparisonOperator, target, string.Empty, "%", "TEST", string.Empty);
      evidence.Stage02B.Records = new[]
      {
        MetricRecord("run-current", "run-current", "SUCCEEDED", "SUCCEEDED",
          actual)
      };
      var definition = new NativeReportingCheckDefinition
      {
        CheckId = "STAGE03.TARGET.TEST",
        SourceStage = NativeReportingSourceStage.CrossStage,
        CheckKind = NativeReportingCheckKind.TargetComparison,
        PropertyId = PropertyId,
        TargetKey = targetKey
      };

      NativeStage03ChecklistItem item = Assert.Single(
        NativeStage03ChecklistEvaluator.Evaluate(
          new[] { definition }, evidence));

      Assert.Equal(expectedStatus, item.Status.ToString());
      Assert.Equal(expectedCode, item.IssueCode);
    }

    [Fact]
    public void Noncurrent_workflow_result_is_red_with_freshness_code()
    {
      NativeStage03SourceEvidenceBundle evidence = CurrentBundle();
      evidence.Stage02AResult.ResultHash = new string('0', 64);

      NativeStage03ChecklistItem item = Assert.Single(
        NativeStage03ChecklistEvaluator.Evaluate(
          new[] { RoleDefinition() }, evidence));

      Assert.Equal(NativeStage03ChecklistStatus.Failed, item.Status);
      Assert.Equal("WORKFLOW_RESULT_HASH_MISMATCH", item.IssueCode);
    }

    [Fact]
    public void Skeleton_geometry_rejects_old_stage02a_success_after_live_input_changes()
    {
      NativeReportingCheckDefinition definition = NativeReportingRuleCatalog
        .Current.GetChecks("总平模型")
        .First(value => value.TaskId == "SITE.SKELETON"
          && value.CheckKind == NativeReportingCheckKind.Geometry);
      NativeStage02ElementPlan plan = ConfirmedElement(definition,
        NativeStage02GeometryCheckState.Passed,
        "GEOMETRY_CHECK_PASSED");
      NativeStage03SourceEvidenceBundle evidence = CurrentBundle(plan);
      evidence.Stage02AResult = Result(
        evidence.CurrentIdentity,
        new string('f', 64),
        GeometryItem(definition, true));

      NativeStage03ChecklistItem item = Assert.Single(
        NativeStage03ChecklistEvaluator.Evaluate(
          new[] { definition }, evidence));

      Assert.Equal(NativeStage03ChecklistStatus.Failed, item.Status);
      Assert.Equal("WORKFLOW_INPUT_STALE", item.IssueCode);
    }

    private static NativeReportingCheckDefinition RoleDefinition()
    {
      return new NativeReportingCheckDefinition
      {
        CheckId = "STAGE02A.ROLE." + RoleId,
        SourceStage = NativeReportingSourceStage.Stage02A,
        CheckKind = NativeReportingCheckKind.SemanticRole,
        TaskId = TaskId,
        RoleId = RoleId
      };
    }

    private static NativeReportingCheckDefinition GeometryDefinition()
    {
      return new NativeReportingCheckDefinition
      {
        CheckId = "STAGE02A.GEOMETRY.TEST",
        SourceStage = NativeReportingSourceStage.Stage02A,
        CheckKind = NativeReportingCheckKind.Geometry,
        TaskId = TaskId,
        RoleId = RoleId,
        RuleText = "边界闭合",
        OfficialCarrierStatus = NativeOfficialCarrierEvidenceStatus.Verified
      };
    }

    private static NativeStage03SourceEvidenceBundle CurrentBundle(
      params NativeStage02ElementPlan[] plans)
    {
      NativeWorkflowIdentity identity = Identity();
      const string stage01Input =
        "1111111111111111111111111111111111111111111111111111111111111111";
      const string stage02AInput =
        "2222222222222222222222222222222222222222222222222222222222222222";
      const string stage02BInput =
        "3333333333333333333333333333333333333333333333333333333333333333";
      var model = new NativeStage01Model();
      model.SetValue(NativeStage01Keys.ModelFileType, "总平模型");
      model.SetValue(NativeStage01Keys.ProjectName, "项目");
      return new NativeStage03SourceEvidenceBundle
      {
        ScanExecuted = true,
        CurrentIdentity = identity,
        Stage01CurrentInputSnapshotHash = stage01Input,
        Stage02ACurrentInputSnapshotHash = stage02AInput,
        Stage02BCurrentInputSnapshotHash = stage02BInput,
        Stage01 = new NativeStage01ReadResult
        {
          Success = true,
          Model = model
        },
        Stage01Result = Result(identity, stage01Input,
          WorkflowItem(NativeStage01Keys.ProjectName, true, "项目")),
        Stage02A = new NativeStage02Preview
        {
          ScopeMode = NativeStage02ScopeMode.FullModel,
          DocumentFingerprint = identity.DocumentFingerprint,
          ModelProfile = identity.ModelFileType,
          Elements = plans ?? Array.Empty<NativeStage02ElementPlan>()
        },
        Stage02AResult = Result(identity, stage02AInput,
          WorkflowItem("SCOPE_COMPLETE", true, "true")),
        Stage02B = new NativeStage02BReadResult
        {
          Identity = identity,
          Records = Array.Empty<NativeStage02BMetricRecord>()
        },
        Stage02BResult = Result(identity, stage02BInput,
          WorkflowItem("STAGE02B_CURRENT", true, "true")),
        TechnicalPreflight = new NativeStage03TechnicalPreflightEvidence
        {
          DocumentReady = true,
          OutputDirectoryWritable = true,
          RevitIfcExporterAvailable = true,
          TranslatorDependenciesAvailable = true,
          ReportWriterAvailable = true
        }
      };
    }

    private static NativeWorkflowIdentity Identity()
    {
      return new NativeWorkflowIdentity
      {
        DocumentFingerprint = "document",
        ModelFileType = "总平模型",
        RulePackageId = "HBR-WUHAN-PLANNING",
        RulePackageVersion = "1.0.0",
        RulePackageSha256 = new string('a', 64)
      };
    }

    private static NativeWorkflowResultEnvelope Result(
      NativeWorkflowIdentity identity,
      string inputHash,
      params NativeWorkflowItemEvidence[] items)
    {
      return NativeWorkflowResultCanonicalizer.Build(
        "run",
        "TEST",
        "TEST",
        identity,
        inputHash,
        items,
        "2026-08-14T00:00:00.0000000Z");
    }

    private static NativeWorkflowItemEvidence WorkflowItem(
      string identity,
      bool succeeded,
      string currentValue)
    {
      return new NativeWorkflowItemEvidence
      {
        Identity = identity,
        CurrentValue = currentValue,
        Source = "TEST",
        WriteSucceeded = succeeded,
        ReadbackSucceeded = succeeded,
        InputHash = new string('9', 64),
        UpdatedUtc = "2026-08-14T00:00:00.0000000Z",
        ErrorCode = succeeded ? string.Empty : "FAILED"
      };
    }

    private static NativeWorkflowItemEvidence RoleItem(bool succeeded)
    {
      return WorkflowItem(ElementUniqueId + "|ROLE_CONFIRMATION",
        succeeded, RoleId);
    }

    private static NativeWorkflowItemEvidence GeometryItem(
      NativeReportingCheckDefinition definition,
      bool succeeded)
    {
      return WorkflowItem(ElementUniqueId + "|" + definition.CheckId,
        succeeded, "State=Passed");
    }

    private static NativeStage02ElementSnapshot Element()
    {
      return new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "document",
        UniqueId = ElementUniqueId,
        ElementId = 1,
        AssignedRoleId = RoleId,
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          EvidenceHash = new string('6', 64)
        }
      };
    }

    private static NativeStage02ElementPlan ConfirmedElement(
      NativeReportingCheckDefinition definition = null,
      NativeStage02GeometryCheckState state = NativeStage02GeometryCheckState.Passed,
      string code = "GEOMETRY_CHECK_PASSED")
    {
      return new NativeStage02ElementPlan
      {
        Element = Element(),
        RoleMatchStatus = NativeStage02RoleMatchStatus.Matched,
        RoleId = RoleId,
        EffectiveRoleId = RoleId,
        ElementSnapshotHash = new string('7', 64),
        RoleConfirmation = new NativeStage02RoleConfirmationDecision
        {
          Confirmed = true,
          Code = "ROLE_CONFIRMED",
          ResolvedRoleId = RoleId
        },
        TaskGeometry = definition == null ? null :
          new NativeStage02TaskGeometryEvaluation
          {
            TaskId = definition.TaskId,
            ElementUniqueId = ElementUniqueId,
            Checks = new[]
            {
              new NativeStage02GeometryCheckEvidence
              {
                CheckId = definition.CheckId,
                RuleText = definition.RuleText,
                State = state,
                Code = code
              }
            }
          }
      };
    }

    private static NativeStage02BMetricRecord MetricRecord(
      string attemptRunId,
      string successRunId,
      string writeStatus,
      string readbackStatus,
      string value,
      NativeOfficialCarrierEvidenceStatus officialCarrierStatus =
        NativeOfficialCarrierEvidenceStatus.Verified)
    {
      return NativeStage02BCanonicalizer.SealRecord(
        new NativeStage02BMetricRecord
        {
          PropertyId = PropertyId,
          Identity = "IfcSite|Pset_场地信息属性集|建筑密度",
          RequestedCanonicalValue = value,
          LastSuccessfulCanonicalValue = value,
          LastAttemptRunId = attemptRunId,
          LastSuccessfulRunId = successRunId,
          WriteStatus = writeStatus,
          ReadbackStatus = readbackStatus,
          OfficialCarrierStatus = officialCarrierStatus,
          IdentityContext = Identity()
        });
    }
  }
}
