using System;
using System.Collections.Generic;

namespace BIMBaoGui.RevitAddin.Acceptance
{
  internal sealed class NativeOfficialCarrierProbeMetric
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal int Sequence { get; set; }
    internal string IfcEntity { get; set; } = string.Empty;
    internal string IfcPropertySet { get; set; } = string.Empty;
    internal string IfcProperty { get; set; } = string.Empty;
    internal string ExactOfficialSourceName { get; set; } = string.Empty;
    internal string DeclaredIfcType { get; set; } = string.Empty;
    internal string CanonicalUnit { get; set; } = string.Empty;
    internal string StorageType { get; set; } = string.Empty;
  }

  internal sealed class NativeOfficialCarrierProbeCandidate
  {
    internal const string ProjectInformationToken = "PROJECT_INFORMATION";

    internal string PropertyId { get; set; } = string.Empty;
    internal string UniqueId { get; set; } = string.Empty;
    internal string CategoryBuiltInId { get; set; } = string.Empty;
    internal string ElementClass { get; set; } = string.Empty;
  }

  internal sealed class NativeOfficialCarrierProbeContext
  {
    internal string SchemaVersion { get; set; } =
      "HBR_OFFICIAL_CARRIER_PROBE_V1";
    internal string SourceGoldenRvtPath { get; set; } = string.Empty;
    internal string SourceGoldenRvtSha256 { get; set; } = string.Empty;
    internal string ProbeCopyPath { get; set; } = string.Empty;
    internal string ProbeCopyPreSeedSha256 { get; set; } = string.Empty;
    internal string AcceptanceRoot { get; set; } = string.Empty;
    internal string AcceptanceRunId { get; set; } = string.Empty;
    internal string Nonce { get; set; } = string.Empty;
    internal string CommitSha { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
    internal IReadOnlyList<NativeOfficialCarrierProbeMetric> Metrics
    {
      get;
      set;
    } = Array.Empty<NativeOfficialCarrierProbeMetric>();
    internal IReadOnlyList<NativeOfficialCarrierProbeCandidate> Candidates
    {
      get;
      set;
    } = Array.Empty<NativeOfficialCarrierProbeCandidate>();
  }

  internal sealed class NativeOfficialCarrierProbeAuthorization
  {
    internal bool Authorized { get; set; }
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal sealed class NativeOfficialCarrierProbeSentinel
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal int Sequence { get; set; }
    internal int CandidateIndex { get; set; }
    internal string CandidateUniqueId { get; set; } = string.Empty;
    internal string CategoryBuiltInId { get; set; } = string.Empty;
    internal string ElementClass { get; set; } = string.Empty;
    internal string IfcEntity { get; set; } = string.Empty;
    internal string IfcPropertySet { get; set; } = string.Empty;
    internal string IfcProperty { get; set; } = string.Empty;
    internal string ExactSourceName { get; set; } = string.Empty;
    internal string DeclaredIfcType { get; set; } = string.Empty;
    internal string CanonicalUnit { get; set; } = string.Empty;
    internal string StorageType { get; set; } = string.Empty;
    internal Guid ParameterGuid { get; set; }
    internal string BindingScope { get; set; } = "INSTANCE";
    internal string CanonicalValue { get; set; } = string.Empty;
  }

  internal sealed class NativeOfficialCarrierProbeExistingParameter
  {
    internal string ExactSourceName { get; set; } = string.Empty;
    internal Guid ParameterGuid { get; set; }
    internal string StorageType { get; set; } = string.Empty;
  }

  internal sealed class NativeOfficialCarrierProbeSeedItem
  {
    internal string PropertyId { get; set; } = string.Empty;
    internal string IfcEntity { get; set; } = string.Empty;
    internal string IfcPropertySet { get; set; } = string.Empty;
    internal string IfcProperty { get; set; } = string.Empty;
    internal string ExactSourceName { get; set; } = string.Empty;
    internal string DeclaredIfcType { get; set; } = string.Empty;
    internal string CanonicalUnit { get; set; } = string.Empty;
    internal string CandidateUniqueId { get; set; } = string.Empty;
    internal string CategoryBuiltInId { get; set; } = string.Empty;
    internal string ElementClass { get; set; } = string.Empty;
    internal string ParameterGuid { get; set; } = string.Empty;
    internal string Sentinel { get; set; } = string.Empty;
    internal string Readback { get; set; } = string.Empty;
  }

  internal sealed class NativeOfficialCarrierProbeSeedManifest
  {
    internal string SchemaVersion { get; set; } =
      "HBR_OFFICIAL_CARRIER_PROBE_SEED_V1";
    internal string ContextPath { get; set; } = string.Empty;
    internal string ContextSha256 { get; set; } = string.Empty;
    internal string SourceGoldenRvtSha256 { get; set; } = string.Empty;
    internal string ProbeRvtPath { get; set; } = string.Empty;
    internal string ProbeRvtSha256 { get; set; } = string.Empty;
    internal string CommitSha { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
    internal IReadOnlyList<NativeOfficialCarrierProbeSeedItem> Items
    {
      get;
      set;
    } = Array.Empty<NativeOfficialCarrierProbeSeedItem>();
  }
}
