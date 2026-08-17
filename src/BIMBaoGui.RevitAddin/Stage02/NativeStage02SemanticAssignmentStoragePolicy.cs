using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal enum NativeStage02SemanticAssignmentStorageState
  {
    NoRecord,
    Current,
    NeedsReconfirmation,
    Corrupt,
    UnsupportedFuture
  }

  internal sealed class NativeStage02SemanticAssignmentStorageSnapshot
  {
    internal string SchemaVersion { get; set; } = string.Empty;
    internal string RulePackageId { get; set; } = string.Empty;
    internal string RulePackageVersion { get; set; } = string.Empty;
    internal string CanonicalJson { get; set; } = string.Empty;
    internal string PayloadSha256 { get; set; } = string.Empty;
    internal string UpdatedUtc { get; set; } = string.Empty;
    internal NativeStage02SemanticAssignmentPayload Payload { get; set; }
  }

  internal sealed class NativeStage02SemanticAssignmentStorageDecision
  {
    internal NativeStage02SemanticAssignmentStorageState State { get; set; }
    internal string Message { get; set; } = string.Empty;
    internal NativeStage02SemanticAssignmentPayload Payload { get; set; }
    internal IReadOnlyList<string> StaleElementUniqueIds { get; set; } =
      Array.Empty<string>();
  }

  internal static class NativeStage02SemanticAssignmentStoragePolicy
  {
    internal static NativeStage02SemanticAssignmentStorageDecision Evaluate(
      NativeStage02SemanticAssignmentStorageSnapshot snapshot,
      IEnumerable<string> existingElementUniqueIds)
    {
      if (snapshot == null
        || (string.IsNullOrWhiteSpace(snapshot.CanonicalJson)
          && snapshot.Payload == null))
      {
        return new NativeStage02SemanticAssignmentStorageDecision
        {
          State = NativeStage02SemanticAssignmentStorageState.NoRecord,
          Payload = EmptyPayload(snapshot)
        };
      }

      if (CompareVersion(
        Clean(snapshot.SchemaVersion),
        NativeStage02SemanticAssignmentSchema.Version) > 0)
      {
        return new NativeStage02SemanticAssignmentStorageDecision
        {
          State = NativeStage02SemanticAssignmentStorageState.UnsupportedFuture,
          Message = "Stage02 Assignment Schema 高于当前插件支持版本。"
        };
      }

      if (string.Equals(
        Clean(snapshot.SchemaVersion),
        NativeStage02SemanticAssignmentSchema.LegacyVersion,
        StringComparison.Ordinal))
      {
        if (snapshot.Payload == null)
          return Corrupt("Stage02 legacy Assignment Payload 无法解析。");
        string legacyHash = NativeStage02SemanticAssignmentCanonicalizer.Sha256(
          snapshot.CanonicalJson ?? string.Empty);
        if (!string.Equals(
          legacyHash,
          Clean(snapshot.PayloadSha256).ToLowerInvariant(),
          StringComparison.Ordinal))
          return Corrupt("Stage02 legacy Assignment SHA-256 校验失败。");
        NativeStage02SemanticAssignmentPayload legacy;
        try
        {
          legacy = NativeStage02SemanticAssignmentCanonicalizer.Normalize(
            snapshot.Payload);
        }
        catch (Exception exception)
        {
          return Corrupt(exception.Message);
        }
        return new NativeStage02SemanticAssignmentStorageDecision
        {
          State = NativeStage02SemanticAssignmentStorageState.NeedsReconfirmation,
          Message = "Stage02 1.0.0 角色记录已保留，必须按当前构件和规则重新确认。",
          Payload = legacy,
          StaleElementUniqueIds = StaleIds(
            legacy,
            existingElementUniqueIds)
        };
      }

      if (!string.Equals(
        Clean(snapshot.SchemaVersion),
        NativeStage02SemanticAssignmentSchema.Version,
        StringComparison.Ordinal))
      {
        return Corrupt("Stage02 Assignment Schema 版本无效。" );
      }

      if (snapshot.Payload == null)
        return Corrupt("Stage02 Assignment Payload 无法解析。" );

      NativeStage02SemanticAssignmentPayload normalized;
      string canonical;
      try
      {
        normalized = NativeStage02SemanticAssignmentCanonicalizer.Normalize(
          snapshot.Payload);
        canonical = NativeStage02SemanticAssignmentCanonicalizer.SerializeCanonical(
          normalized);
      }
      catch (Exception exception)
      {
        return Corrupt(exception.Message);
      }

      if (!string.Equals(
        canonical,
        snapshot.CanonicalJson ?? string.Empty,
        StringComparison.Ordinal))
      {
        return Corrupt("Stage02 Assignment canonical JSON 与解析内容不一致。" );
      }

      string actualHash = NativeStage02SemanticAssignmentCanonicalizer.Sha256(
        canonical);
      if (!string.Equals(
        actualHash,
        Clean(snapshot.PayloadSha256).ToLowerInvariant(),
        StringComparison.Ordinal))
      {
        return Corrupt("Stage02 Assignment SHA-256 校验失败。" );
      }

      return new NativeStage02SemanticAssignmentStorageDecision
      {
        State = NativeStage02SemanticAssignmentStorageState.Current,
        Payload = normalized,
        StaleElementUniqueIds = StaleIds(normalized, existingElementUniqueIds)
      };
    }

    internal static NativeStage02SemanticAssignmentStorageSnapshot CreateSnapshot(
      NativeStage02SemanticAssignmentPayload payload,
      string updatedUtc)
    {
      NativeStage02SemanticAssignmentPayload normalized =
        NativeStage02SemanticAssignmentCanonicalizer.Normalize(payload);
      string canonical = NativeStage02SemanticAssignmentCanonicalizer
        .SerializeCanonical(normalized);
      return new NativeStage02SemanticAssignmentStorageSnapshot
      {
        SchemaVersion = normalized.SchemaVersion,
        RulePackageId = normalized.RulePackageId,
        RulePackageVersion = normalized.RulePackageVersion,
        CanonicalJson = canonical,
        PayloadSha256 = NativeStage02SemanticAssignmentCanonicalizer.Sha256(
          canonical),
        UpdatedUtc = Clean(updatedUtc),
        Payload = normalized
      };
    }

    private static NativeStage02SemanticAssignmentStorageDecision Corrupt(
      string message)
    {
      return new NativeStage02SemanticAssignmentStorageDecision
      {
        State = NativeStage02SemanticAssignmentStorageState.Corrupt,
        Message = message ?? string.Empty
      };
    }

    private static NativeStage02SemanticAssignmentPayload EmptyPayload(
      NativeStage02SemanticAssignmentStorageSnapshot snapshot)
    {
      return new NativeStage02SemanticAssignmentPayload
      {
        SchemaVersion = NativeStage02SemanticAssignmentSchema.Version,
        RulePackageId = snapshot?.RulePackageId ?? string.Empty,
        RulePackageVersion = snapshot?.RulePackageVersion ?? string.Empty,
        Assignments = Array.Empty<NativeStage02SemanticAssignmentRecord>()
      };
    }

    private static int CompareVersion(string left, string right)
    {
      Version leftVersion;
      Version rightVersion;
      if (!Version.TryParse(left, out leftVersion)) return -1;
      if (!Version.TryParse(right, out rightVersion)) return 0;
      return leftVersion.CompareTo(rightVersion);
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }

    private static IReadOnlyList<string> StaleIds(
      NativeStage02SemanticAssignmentPayload payload,
      IEnumerable<string> existingElementUniqueIds)
    {
      var existing = new HashSet<string>(
        (existingElementUniqueIds ?? Array.Empty<string>())
          .Select(Clean)
          .Where(value => value.Length > 0),
        StringComparer.Ordinal);
      return new ReadOnlyCollection<string>((payload?.Assignments
          ?? Array.Empty<NativeStage02SemanticAssignmentRecord>())
        .Select(value => value.ElementUniqueId)
        .Where(value => !existing.Contains(value))
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray());
    }
  }
}
