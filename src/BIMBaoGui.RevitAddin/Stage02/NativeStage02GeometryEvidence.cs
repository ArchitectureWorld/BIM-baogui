using System;
using System.Collections.Generic;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal sealed class NativeStage02BoundingBoxEvidence
  {
    internal bool Available { get; set; }
    internal double MinXFeet { get; set; }
    internal double MinYFeet { get; set; }
    internal double MinZFeet { get; set; }
    internal double MaxXFeet { get; set; }
    internal double MaxYFeet { get; set; }
    internal double MaxZFeet { get; set; }
  }

  internal enum NativeStage02GeometryCheckState
  {
    Passed,
    Failed,
    ManualReviewRequired,
    ManualReviewApproved
  }

  internal sealed class NativeStage02GeometryCheckEvidence
  {
    internal string CheckId { get; set; } = string.Empty;
    internal string RuleText { get; set; } = string.Empty;
    internal NativeStage02GeometryCheckState State { get; set; }
    internal string Code { get; set; } = string.Empty;
    internal string Basis { get; set; } = string.Empty;
    internal string ManualReviewRecordHash { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02GeometryEvidence
  {
    internal NativeStage02BoundingBoxEvidence BoundingBox { get; set; } =
      new NativeStage02BoundingBoxEvidence();
    internal string LocationKind { get; set; } = string.Empty;
    internal IReadOnlyList<double> LocationCoordinatesFeet { get; set; } =
      Array.Empty<double>();
    internal double? ApprovedProjectedAreaSquareMetres { get; set; }
    internal string ProjectedAreaSource { get; set; } = string.Empty;
    internal IReadOnlyList<IReadOnlyList<double>> PlanarLoopsMetres { get; set; } =
      Array.Empty<IReadOnlyList<double>>();
    internal IReadOnlyList<IReadOnlyList<double>> CurveChainsMetres { get; set; } =
      Array.Empty<IReadOnlyList<double>>();
    internal double ShortCurveToleranceMetres { get; set; }
    internal string TopologySource { get; set; } = string.Empty;
    internal string CaptureCode { get; set; } = string.Empty;
    internal string EvidenceHash { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02TaskGeometryEvaluation
  {
    internal string TaskId { get; set; } = string.Empty;
    internal string ElementUniqueId { get; set; } = string.Empty;
    internal IReadOnlyList<NativeStage02GeometryCheckEvidence> Checks { get; set; } =
      Array.Empty<NativeStage02GeometryCheckEvidence>();
    internal string EvaluationHash { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02ManualReviewRecord
  {
    internal string SchemaVersion { get; set; } =
      "HBR_NATIVE_GEOMETRY_REVIEW_V1";
    internal string CheckId { get; set; } = string.Empty;
    internal string RuleText { get; set; } = string.Empty;
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
    internal IReadOnlyList<string> ElementUniqueIds { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> ElementSnapshotHashes { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<string> GeometryEvidenceHashes { get; set; } =
      Array.Empty<string>();
    internal string Decision { get; set; } = string.Empty;
    internal string Reviewer { get; set; } = string.Empty;
    internal string Basis { get; set; } = string.Empty;
    internal string ReviewedUtc { get; set; } = string.Empty;
    internal string RecordHash { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02ManualReviewCommand
  {
    internal string CheckId { get; set; } = string.Empty;
    internal string Decision { get; set; } = string.Empty;
    internal string Reviewer { get; set; } = string.Empty;
    internal string Basis { get; set; } = string.Empty;

    internal NativeStage02ManualReviewCommand Clone()
    {
      return new NativeStage02ManualReviewCommand
      {
        CheckId = CheckId ?? string.Empty,
        Decision = Decision ?? string.Empty,
        Reviewer = Reviewer ?? string.Empty,
        Basis = Basis ?? string.Empty
      };
    }
  }

  internal sealed class NativeStage02RoleConfirmationDecision
  {
    internal bool Confirmed { get; set; }
    internal string Code { get; set; } = string.Empty;
    internal string ResolvedRoleId { get; set; } = string.Empty;
    internal string Source { get; set; } = string.Empty;
    internal NativeStage02RoleConfirmation Confirmation { get; set; }
  }

  internal sealed class NativeStage02GeometryEvaluationContext
  {
    internal NativeWorkflowIdentity Identity { get; set; }
    internal IReadOnlyList<NativeStage02ElementSnapshot> ConfirmedElements
    {
      get;
      set;
    } = Array.Empty<NativeStage02ElementSnapshot>();
    internal IReadOnlyList<NativeStage02ManualReviewRecord> ManualReviews
    {
      get;
      set;
    } = Array.Empty<NativeStage02ManualReviewRecord>();
    internal bool ScopeComplete { get; set; } = true;
  }
}
