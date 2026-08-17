using System;

namespace BIMBaoGui.RevitAddin.Workflow
{
  internal enum NativeWorkflowFreshnessState
  {
    Unknown,
    Current,
    SchemaMismatch,
    ResultHashMismatch,
    DocumentMismatch,
    ModelTypeMismatch,
    RulePackageMismatch,
    InputStale
  }

  internal sealed class NativeWorkflowFreshnessDecision
  {
    internal NativeWorkflowFreshnessState State { get; set; }
    internal string Code { get; set; } = string.Empty;
  }

  internal static class NativeWorkflowFreshnessPolicy
  {
    internal static NativeWorkflowFreshnessDecision Evaluate(
      NativeWorkflowResultEnvelope result,
      NativeWorkflowIdentity currentIdentity,
      string currentInputSnapshotHash)
    {
      if (result == null || currentIdentity == null)
        return Decision(NativeWorkflowFreshnessState.Unknown, "WORKFLOW_RESULT_UNKNOWN");

      if (!string.Equals(
        result.SchemaVersion,
        NativeWorkflowResultEnvelope.CurrentSchemaVersion,
        StringComparison.Ordinal))
      {
        return Decision(
          NativeWorkflowFreshnessState.SchemaMismatch,
          "WORKFLOW_SCHEMA_MISMATCH");
      }

      string recomputedHash;
      try
      {
        recomputedHash = NativeWorkflowResultCanonicalizer.ComputeResultHash(result);
      }
      catch (Exception)
      {
        return Decision(
          NativeWorkflowFreshnessState.ResultHashMismatch,
          "WORKFLOW_RESULT_HASH_MISMATCH");
      }
      if (!string.Equals(
        result.ResultHash,
        recomputedHash,
        StringComparison.Ordinal)
        || !string.Equals(
          result.CanonicalJson,
          NativeWorkflowResultCanonicalizer.SerializeCanonical(result),
          StringComparison.Ordinal))
      {
        return Decision(
          NativeWorkflowFreshnessState.ResultHashMismatch,
          "WORKFLOW_RESULT_HASH_MISMATCH");
      }

      if (!Same(
        result.Identity.DocumentFingerprint,
        currentIdentity.DocumentFingerprint))
      {
        return Decision(
          NativeWorkflowFreshnessState.DocumentMismatch,
          "WORKFLOW_DOCUMENT_MISMATCH");
      }
      if (!Same(result.Identity.ModelFileType, currentIdentity.ModelFileType))
      {
        return Decision(
          NativeWorkflowFreshnessState.ModelTypeMismatch,
          "WORKFLOW_MODEL_TYPE_MISMATCH");
      }
      if (!Same(result.Identity.RulePackageId, currentIdentity.RulePackageId)
        || !Same(
          result.Identity.RulePackageVersion,
          currentIdentity.RulePackageVersion)
        || !Same(
          result.Identity.RulePackageSha256,
          currentIdentity.RulePackageSha256))
      {
        return Decision(
          NativeWorkflowFreshnessState.RulePackageMismatch,
          "WORKFLOW_RULE_PACKAGE_MISMATCH");
      }
      if (!Same(result.InputSnapshotHash, currentInputSnapshotHash))
      {
        return Decision(
          NativeWorkflowFreshnessState.InputStale,
          "WORKFLOW_INPUT_STALE");
      }
      return Decision(NativeWorkflowFreshnessState.Current, "WORKFLOW_CURRENT");
    }

    private static bool Same(string left, string right)
    {
      return string.Equals(
        left ?? string.Empty,
        right ?? string.Empty,
        StringComparison.Ordinal);
    }

    private static NativeWorkflowFreshnessDecision Decision(
      NativeWorkflowFreshnessState state,
      string code)
    {
      return new NativeWorkflowFreshnessDecision { State = state, Code = code };
    }
  }
}
