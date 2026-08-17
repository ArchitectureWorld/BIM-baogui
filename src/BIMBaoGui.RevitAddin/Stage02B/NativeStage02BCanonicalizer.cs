using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal static class NativeStage02BCanonicalizer
  {
    internal static NativeStage02BMetricRecord SealRecord(
      NativeStage02BMetricRecord record)
    {
      if (record == null) throw new ArgumentNullException(nameof(record));
      NativeStage02BMetricRecord sealedRecord = Clone(record);
      sealedRecord.ResultHash = NativeWorkflowIdentityFactory.Sha256(
        SerializeRecord(sealedRecord));
      return sealedRecord;
    }

    internal static bool VerifyRecord(NativeStage02BMetricRecord record)
    {
      if (record == null || string.IsNullOrWhiteSpace(record.ResultHash)) return false;
      return string.Equals(record.ResultHash,
        NativeWorkflowIdentityFactory.Sha256(SerializeRecord(record)),
        StringComparison.Ordinal);
    }

    internal static NativeStage02BStorageSnapshot SealSnapshot(
      IEnumerable<NativeStage02BMetricRecord> records)
    {
      NativeStage02BMetricRecord[] normalized = (records ??
        Array.Empty<NativeStage02BMetricRecord>())
        .Where(value => value != null)
        .Select(SealRecord)
        .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
        .ToArray();
      string duplicate = normalized.GroupBy(value => value.PropertyId,
        StringComparer.Ordinal).Where(group => group.Count() > 1)
        .Select(group => group.Key).FirstOrDefault();
      if (duplicate != null)
        throw new ArgumentException("Stage02B snapshot contains duplicate propertyId: "
          + duplicate, nameof(records));
      var snapshot = new NativeStage02BStorageSnapshot
      {
        Records = new ReadOnlyCollection<NativeStage02BMetricRecord>(normalized)
      };
      snapshot.CanonicalJson = SerializeSnapshot(snapshot);
      snapshot.SnapshotHash = NativeWorkflowIdentityFactory.Sha256(
        snapshot.CanonicalJson);
      return snapshot;
    }

    internal static string SerializeRecord(NativeStage02BMetricRecord record)
    {
      if (record == null) throw new ArgumentNullException(nameof(record));
      var builder = new StringBuilder();
      builder.Append('{');
      Append(builder, "propertyId", record.PropertyId); builder.Append(',');
      Append(builder, "identity", record.Identity); builder.Append(',');
      Append(builder, "unit", record.Unit); builder.Append(',');
      Append(builder, "source", record.Source); builder.Append(',');
      Append(builder, "requestedCanonicalValue", record.RequestedCanonicalValue); builder.Append(',');
      Append(builder, "lastSuccessfulCanonicalValue", record.LastSuccessfulCanonicalValue); builder.Append(',');
      Append(builder, "lastAttemptRunId", record.LastAttemptRunId); builder.Append(',');
      Append(builder, "lastSuccessfulRunId", record.LastSuccessfulRunId); builder.Append(',');
      Append(builder, "writeStatus", record.WriteStatus); builder.Append(',');
      Append(builder, "readbackStatus", record.ReadbackStatus); builder.Append(',');
      Append(builder, "projectionStatus", record.ProjectionStatus); builder.Append(',');
      Append(builder, "officialCarrierStatus", record.OfficialCarrierStatus.ToString()); builder.Append(',');
      Append(builder, "officialProjectionCarrierId", record.OfficialProjectionCarrierId); builder.Append(',');
      Append(builder, "officialCarrierProbeRef", record.OfficialCarrierProbeRef); builder.Append(',');
      Append(builder, "officialEvidenceRef", record.OfficialEvidenceRef); builder.Append(',');
      AppendIdentity(builder, record.IdentityContext); builder.Append(',');
      Append(builder, "updatedUtc", record.UpdatedUtc); builder.Append(',');
      Append(builder, "errorCode", record.ErrorCode);
      builder.Append('}');
      return builder.ToString();
    }

    private static string SerializeSnapshot(NativeStage02BStorageSnapshot snapshot)
    {
      var builder = new StringBuilder();
      builder.Append('{');
      Append(builder, "schemaVersion", snapshot.SchemaVersion); builder.Append(",\"records\":[");
      for (int index = 0; index < snapshot.Records.Count; index++)
      {
        if (index > 0) builder.Append(',');
        NativeStage02BMetricRecord record = snapshot.Records[index];
        builder.Append('{');
        builder.Append("\"record\":");
        builder.Append(SerializeRecord(record));
        builder.Append(',');
        Append(builder, "resultHash", record.ResultHash);
        builder.Append('}');
      }
      builder.Append("]}");
      return builder.ToString();
    }

    private static NativeStage02BMetricRecord Clone(NativeStage02BMetricRecord value)
    {
      return new NativeStage02BMetricRecord
      {
        PropertyId = Clean(value.PropertyId), Identity = Clean(value.Identity),
        Unit = Clean(value.Unit), Source = Clean(value.Source),
        RequestedCanonicalValue = Clean(value.RequestedCanonicalValue),
        LastSuccessfulCanonicalValue = Clean(value.LastSuccessfulCanonicalValue),
        LastAttemptRunId = Clean(value.LastAttemptRunId),
        LastSuccessfulRunId = Clean(value.LastSuccessfulRunId),
        WriteStatus = Clean(value.WriteStatus), ReadbackStatus = Clean(value.ReadbackStatus),
        ProjectionStatus = Clean(value.ProjectionStatus),
        OfficialCarrierStatus = value.OfficialCarrierStatus,
        OfficialProjectionCarrierId = Clean(value.OfficialProjectionCarrierId),
        OfficialCarrierProbeRef = Clean(value.OfficialCarrierProbeRef),
        OfficialEvidenceRef = Clean(value.OfficialEvidenceRef),
        IdentityContext = CloneIdentity(value.IdentityContext),
        UpdatedUtc = Clean(value.UpdatedUtc), ErrorCode = Clean(value.ErrorCode)
      };
    }

    private static NativeWorkflowIdentity CloneIdentity(NativeWorkflowIdentity value)
    {
      if (value == null) return null;
      return new NativeWorkflowIdentity
      {
        DocumentFingerprint = Clean(value.DocumentFingerprint),
        ModelFileType = Clean(value.ModelFileType),
        RulePackageId = Clean(value.RulePackageId),
        RulePackageVersion = Clean(value.RulePackageVersion),
        RulePackageSha256 = Clean(value.RulePackageSha256)
      };
    }

    private static void AppendIdentity(StringBuilder builder,
      NativeWorkflowIdentity identity)
    {
      builder.Append("\"identityContext\":{");
      Append(builder, "documentFingerprint", identity?.DocumentFingerprint); builder.Append(',');
      Append(builder, "modelFileType", identity?.ModelFileType); builder.Append(',');
      Append(builder, "rulePackageId", identity?.RulePackageId); builder.Append(',');
      Append(builder, "rulePackageVersion", identity?.RulePackageVersion); builder.Append(',');
      Append(builder, "rulePackageSha256", identity?.RulePackageSha256);
      builder.Append('}');
    }

    private static void Append(StringBuilder builder, string name, string value)
    {
      builder.Append('"').Append(Escape(name)).Append("\":\"")
        .Append(Escape(value ?? string.Empty)).Append('"');
    }

    private static string Clean(string value) => (value ?? string.Empty).Trim();

    private static string Escape(string value)
    {
      return NativeWorkflowResultCanonicalizer.Escape(value ?? string.Empty);
    }
  }
}
