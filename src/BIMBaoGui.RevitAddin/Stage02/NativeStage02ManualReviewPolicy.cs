using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02ManualReviewPolicy
  {
    private static readonly JavaScriptSerializer Serializer =
      new JavaScriptSerializer();

    internal static NativeStage02ManualReviewRecord Seal(
      NativeStage02ManualReviewRecord draft)
    {
      NativeStage02ManualReviewRecord record = Normalize(draft);
      record.RecordHash = Sha256(Canonical(record));
      return record;
    }

    internal static NativeStage02GeometryCheckEvidence VerifyCurrent(
      NativeStage02ManualReviewRecord record,
      string checkId,
      string ruleText,
      NativeWorkflowIdentity identity,
      IReadOnlyList<string> elementUniqueIds,
      IReadOnlyList<string> elementSnapshotHashes,
      IReadOnlyList<string> geometryEvidenceHashes)
    {
      if (record == null)
        return Result(
          checkId,
          ruleText,
          NativeStage02GeometryCheckState.ManualReviewRequired,
          "MANUAL_REVIEW_REQUIRED",
          string.Empty);
      if (identity == null) throw new ArgumentNullException(nameof(identity));

      NativeStage02ManualReviewRecord current;
      try
      {
        current = Normalize(new NativeStage02ManualReviewRecord
        {
          CheckId = checkId,
          RuleText = ruleText,
          DocumentFingerprint = identity.DocumentFingerprint,
          RulePackageSha256 = identity.RulePackageSha256,
          ElementUniqueIds = elementUniqueIds,
          ElementSnapshotHashes = elementSnapshotHashes,
          GeometryEvidenceHashes = geometryEvidenceHashes,
          Decision = record.Decision,
          Reviewer = record.Reviewer,
          Basis = record.Basis,
          ReviewedUtc = record.ReviewedUtc
        });
        NativeStage02ManualReviewRecord normalizedRecord = Normalize(record);
        bool hashValid = string.Equals(
          Clean(record.RecordHash).ToLowerInvariant(),
          Sha256(Canonical(normalizedRecord)),
          StringComparison.Ordinal);
        bool factsCurrent = EquivalentFacts(normalizedRecord, current);
        if (!hashValid || !factsCurrent)
          return Result(
            checkId,
            ruleText,
            NativeStage02GeometryCheckState.ManualReviewRequired,
            "MANUAL_REVIEW_STALE",
            string.Empty);
        if (string.Equals(
          normalizedRecord.Decision,
          "REJECTED",
          StringComparison.Ordinal))
          return Result(
            checkId,
            ruleText,
            NativeStage02GeometryCheckState.Failed,
            "MANUAL_REVIEW_REJECTED",
            normalizedRecord.RecordHash);
        return Result(
          checkId,
          ruleText,
          NativeStage02GeometryCheckState.ManualReviewApproved,
          "MANUAL_REVIEW_APPROVED_CURRENT",
          normalizedRecord.RecordHash);
      }
      catch
      {
        return Result(
          checkId,
          ruleText,
          NativeStage02GeometryCheckState.ManualReviewRequired,
          "MANUAL_REVIEW_STALE",
          string.Empty);
      }
    }

    internal static string Canonical(NativeStage02ManualReviewRecord record)
    {
      NativeStage02ManualReviewRecord value = Normalize(record);
      var builder = new StringBuilder(2048);
      builder.Append('{');
      Property(builder, "schemaVersion", value.SchemaVersion, false);
      Property(builder, "checkId", value.CheckId, true);
      Property(builder, "ruleText", value.RuleText, true);
      Property(builder, "documentFingerprint", value.DocumentFingerprint, true);
      Property(builder, "rulePackageSha256", value.RulePackageSha256, true);
      ArrayProperty(builder, "elementUniqueIds", value.ElementUniqueIds);
      ArrayProperty(builder, "elementSnapshotHashes", value.ElementSnapshotHashes);
      ArrayProperty(builder, "geometryEvidenceHashes", value.GeometryEvidenceHashes);
      Property(builder, "decision", value.Decision, true);
      Property(builder, "reviewer", value.Reviewer, true);
      Property(builder, "basis", value.Basis, true);
      Property(builder, "reviewedUtc", value.ReviewedUtc, true);
      builder.Append('}');
      return builder.ToString();
    }

    private static NativeStage02ManualReviewRecord Normalize(
      NativeStage02ManualReviewRecord input)
    {
      if (input == null) throw new ArgumentNullException(nameof(input));
      string checkId = Require(input.CheckId, "MANUAL_REVIEW_CHECK_ID_REQUIRED");
      string ruleText = Require(input.RuleText, "MANUAL_REVIEW_RULE_REQUIRED");
      string document = Require(
        input.DocumentFingerprint,
        "MANUAL_REVIEW_DOCUMENT_REQUIRED");
      string ruleHash = Require(
        input.RulePackageSha256,
        "MANUAL_REVIEW_RULE_HASH_REQUIRED").ToLowerInvariant();
      string decision = Require(
        input.Decision,
        "MANUAL_REVIEW_DECISION_REQUIRED").ToUpperInvariant();
      if (decision != "APPROVED" && decision != "REJECTED")
        throw new InvalidOperationException("MANUAL_REVIEW_DECISION_INVALID");
      string reviewer = Require(
        input.Reviewer,
        "MANUAL_REVIEW_REVIEWER_REQUIRED");
      string basis = Require(input.Basis, "MANUAL_REVIEW_BASIS_REQUIRED");
      string reviewedUtc = Require(
        input.ReviewedUtc,
        "MANUAL_REVIEW_REVIEWED_UTC_REQUIRED");
      string[] ids = CleanArray(input.ElementUniqueIds);
      string[] snapshots = CleanArray(input.ElementSnapshotHashes);
      string[] geometry = CleanArray(input.GeometryEvidenceHashes);
      if (ids.Length == 0 || ids.Length != snapshots.Length
        || ids.Length != geometry.Length)
        throw new InvalidOperationException("MANUAL_REVIEW_FACT_LIST_MISMATCH");
      if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        throw new InvalidOperationException("MANUAL_REVIEW_ELEMENT_DUPLICATE");
      var rows = ids.Select((id, index) => new
        {
          Id = id,
          Snapshot = snapshots[index],
          Geometry = geometry[index]
        })
        .OrderBy(value => value.Id, StringComparer.Ordinal)
        .ToArray();
      return new NativeStage02ManualReviewRecord
      {
        SchemaVersion = "HBR_NATIVE_GEOMETRY_REVIEW_V1",
        CheckId = checkId,
        RuleText = ruleText,
        DocumentFingerprint = document,
        RulePackageSha256 = ruleHash,
        ElementUniqueIds = rows.Select(value => value.Id).ToArray(),
        ElementSnapshotHashes = rows.Select(value => value.Snapshot).ToArray(),
        GeometryEvidenceHashes = rows.Select(value => value.Geometry).ToArray(),
        Decision = decision,
        Reviewer = reviewer,
        Basis = basis,
        ReviewedUtc = reviewedUtc,
        RecordHash = Clean(input.RecordHash).ToLowerInvariant()
      };
    }

    private static bool EquivalentFacts(
      NativeStage02ManualReviewRecord left,
      NativeStage02ManualReviewRecord right)
    {
      return left.CheckId == right.CheckId
        && left.RuleText == right.RuleText
        && left.DocumentFingerprint == right.DocumentFingerprint
        && left.RulePackageSha256 == right.RulePackageSha256
        && left.ElementUniqueIds.SequenceEqual(right.ElementUniqueIds)
        && left.ElementSnapshotHashes.SequenceEqual(right.ElementSnapshotHashes)
        && left.GeometryEvidenceHashes.SequenceEqual(right.GeometryEvidenceHashes);
    }

    private static NativeStage02GeometryCheckEvidence Result(
      string checkId,
      string ruleText,
      NativeStage02GeometryCheckState state,
      string code,
      string recordHash)
    {
      return new NativeStage02GeometryCheckEvidence
      {
        CheckId = Clean(checkId),
        RuleText = Clean(ruleText),
        State = state,
        Code = code,
        Basis = code,
        ManualReviewRecordHash = recordHash ?? string.Empty
      };
    }

    private static void ArrayProperty(
      StringBuilder builder,
      string name,
      IEnumerable<string> values)
    {
      builder.Append(',').Append(Q(name)).Append(":[");
      bool first = true;
      foreach (string value in values ?? Array.Empty<string>())
      {
        if (!first) builder.Append(',');
        first = false;
        builder.Append(Q(value));
      }
      builder.Append(']');
    }

    private static void Property(
      StringBuilder builder,
      string name,
      string value,
      bool comma)
    {
      if (comma) builder.Append(',');
      builder.Append(Q(name)).Append(':').Append(Q(value));
    }

    private static string Q(string value)
    {
      return Serializer.Serialize(value ?? string.Empty);
    }

    private static string Sha256(string value)
    {
      using (SHA256 sha = SHA256.Create())
      {
        return string.Concat(sha.ComputeHash(new UTF8Encoding(false).GetBytes(
          value ?? string.Empty)).Select(item => item.ToString(
            "x2", CultureInfo.InvariantCulture)));
      }
    }

    private static string[] CleanArray(IEnumerable<string> values)
    {
      return (values ?? Array.Empty<string>()).Select(value =>
        Require(value, "MANUAL_REVIEW_FACT_REQUIRED")).ToArray();
    }

    private static string Require(string value, string code)
    {
      string clean = Clean(value);
      if (clean.Length == 0) throw new InvalidOperationException(code);
      return clean;
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }
}
