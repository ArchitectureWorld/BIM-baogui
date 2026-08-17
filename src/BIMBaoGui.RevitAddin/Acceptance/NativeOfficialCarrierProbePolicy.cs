using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Acceptance
{
  internal static class NativeOfficialCarrierProbePolicy
  {
    internal const string ContextEnvironmentVariable =
      "BIMBAOGUI_ACCEPTANCE_PROBE_CONTEXT";
    internal const string ProbeFileMarker = "__HIFC_CARRIER_PROBE__";

    internal static NativeOfficialCarrierProbeAuthorization Authorize(
      string contextPath,
      string activeDocumentPath,
      string activeDocumentPreSeedSha256,
      NativeOfficialCarrierProbeContext context)
    {
      return Authorize(
        contextPath,
        activeDocumentPath,
        activeDocumentPreSeedSha256,
        context,
        InstalledCommitSha(),
        NativeStage02RuleCatalog.Current.Identity.RulePackageSha256,
        IsReparsePoint);
    }

    internal static NativeOfficialCarrierProbeAuthorization Authorize(
      string contextPath,
      string activeDocumentPath,
      string activeDocumentPreSeedSha256,
      NativeOfficialCarrierProbeContext context,
      string installedCommitSha,
      string installedRulePackageSha256,
      Func<string, bool> isReparsePoint)
    {
      if (context == null) return Reject("PROBE_CONTEXT_INVALID");
      string environmentPath = Environment.GetEnvironmentVariable(
        ContextEnvironmentVariable) ?? string.Empty;
      string normalizedContext;
      string normalizedEnvironment;
      string acceptanceRoot;
      string probePath;
      string sourcePath;
      string activePath;
      try
      {
        normalizedContext = NormalizeFilePath(contextPath);
        normalizedEnvironment = NormalizeFilePath(environmentPath);
        acceptanceRoot = NormalizeDirectoryPath(context.AcceptanceRoot);
        probePath = NormalizeFilePath(context.ProbeCopyPath);
        sourcePath = NormalizeFilePath(context.SourceGoldenRvtPath);
        activePath = NormalizeFilePath(activeDocumentPath);
      }
      catch
      {
        return Reject("PROBE_PATH_INVALID");
      }
      if (!SamePath(normalizedContext, normalizedEnvironment))
        return Reject("PROBE_CONTEXT_ENVIRONMENT_MISMATCH");
      if (!File.Exists(normalizedContext))
        return Reject("PROBE_CONTEXT_NOT_FOUND");
      if (!IsWithinRoot(acceptanceRoot, normalizedContext)
        || !IsWithinRoot(acceptanceRoot, probePath))
        return Reject("PROBE_PATH_ESCAPE");
      if (HasReparsePoint(normalizedContext, isReparsePoint)
        || HasReparsePoint(probePath, isReparsePoint)
        || HasReparsePoint(sourcePath, isReparsePoint))
        return Reject("PROBE_REPARSE_POINT_FORBIDDEN");
      if (SamePath(sourcePath, probePath))
        return Reject("PROBE_SOURCE_COPY_COLLISION");
      if (SamePath(activePath, sourcePath))
        return Reject("PROBE_SOURCE_DOCUMENT_FORBIDDEN");
      if (!SamePath(activePath, probePath))
        return Reject("PROBE_ACTIVE_DOCUMENT_MISMATCH");
      if (Path.GetFileName(probePath).IndexOf(
          ProbeFileMarker, StringComparison.Ordinal) < 0)
        return Reject("PROBE_FILENAME_MARKER_MISSING");
      if (!File.Exists(sourcePath) || !File.Exists(probePath))
        return Reject("PROBE_RVT_NOT_FOUND");

      string sourceSha;
      string probeSha;
      try
      {
        sourceSha = ComputeSha256(sourcePath);
        probeSha = ComputeSha256(probePath);
      }
      catch
      {
        return Reject("PROBE_SHA_READ_FAILED");
      }
      if (!IsSha256(context.SourceGoldenRvtSha256)
        || !IsSha256(context.ProbeCopyPreSeedSha256)
        || !IsSha256(activeDocumentPreSeedSha256)
        || !string.Equals(sourceSha, context.SourceGoldenRvtSha256,
          StringComparison.Ordinal)
        || !string.Equals(probeSha, context.ProbeCopyPreSeedSha256,
          StringComparison.Ordinal)
        || !string.Equals(activeDocumentPreSeedSha256,
          context.ProbeCopyPreSeedSha256, StringComparison.Ordinal)
        || !string.Equals(context.SourceGoldenRvtSha256,
          context.ProbeCopyPreSeedSha256, StringComparison.Ordinal))
        return Reject("PROBE_PRESEED_SHA_MISMATCH");

      if (!IsCommitSha(context.CommitSha)
        || !IsCommitSha(installedCommitSha)
        || !string.Equals(context.CommitSha, installedCommitSha,
          StringComparison.OrdinalIgnoreCase))
        return Reject("PROBE_COMMIT_IDENTITY_MISMATCH");
      if (!IsSha256(context.RulePackageSha256)
        || !IsSha256(installedRulePackageSha256)
        || !string.Equals(context.RulePackageSha256,
          installedRulePackageSha256, StringComparison.Ordinal))
        return Reject("PROBE_RULE_IDENTITY_MISMATCH");
      if (!string.Equals(context.SchemaVersion,
        "HBR_OFFICIAL_CARRIER_PROBE_V1", StringComparison.Ordinal)
        || string.IsNullOrWhiteSpace(context.AcceptanceRunId)
        || string.IsNullOrWhiteSpace(context.Nonce))
        return Reject("PROBE_CONTEXT_INVALID");

      NativeOfficialCarrierProbeMetric[] metrics = (context.Metrics
          ?? Array.Empty<NativeOfficialCarrierProbeMetric>())
        .Where(value => value != null).ToArray();
      if (metrics.Length != 6
        || metrics.Any(value => !Guid.TryParse(value.PropertyId, out _)
          || string.IsNullOrWhiteSpace(value.ExactOfficialSourceName)
          || (value.DeclaredIfcType != "IfcReal"
            && value.DeclaredIfcType != "IfcInteger"))
        || metrics.GroupBy(value => value.PropertyId, StringComparer.Ordinal)
          .Any(group => group.Count() > 1))
        return Reject("PROBE_METRIC_CONTEXT_INVALID");
      NativeOfficialCarrierProbeCandidate[] candidates = (context.Candidates
          ?? Array.Empty<NativeOfficialCarrierProbeCandidate>())
        .Where(value => value != null).ToArray();
      if (candidates.Length == 0
        || candidates.Any(value => string.IsNullOrWhiteSpace(value.PropertyId)
          || string.IsNullOrWhiteSpace(value.UniqueId)
          || string.IsNullOrWhiteSpace(value.CategoryBuiltInId)
          || string.IsNullOrWhiteSpace(value.ElementClass)))
        return Reject("PROBE_CANDIDATE_INVALID");
      if (candidates.GroupBy(value =>
          (value.PropertyId ?? string.Empty) + "\n"
            + (value.UniqueId ?? string.Empty), StringComparer.Ordinal)
        .Any(group => group.Count() > 1))
        return Reject("PROBE_CANDIDATE_DUPLICATE");
      var metricIds = new HashSet<string>(
        metrics.Select(value => value.PropertyId), StringComparer.Ordinal);
      if (candidates.Any(value => !metricIds.Contains(value.PropertyId))
        || metrics.Any(metric => !candidates.Any(candidate => string.Equals(
          candidate.PropertyId, metric.PropertyId, StringComparison.Ordinal))))
        return Reject("PROBE_CANDIDATE_SET_MISMATCH");
      foreach (NativeOfficialCarrierProbeMetric metric in metrics)
      {
        NativeOfficialCarrierProbeCandidate[] metricCandidates = candidates
          .Where(value => string.Equals(
            value.PropertyId,
            metric.PropertyId,
            StringComparison.Ordinal)).ToArray();
        bool projectInformation = string.Equals(
          metric.IfcEntity,
          "IfcProject",
          StringComparison.Ordinal);
        if (projectInformation
          && (metricCandidates.Length != 1
            || !string.Equals(
              metricCandidates[0].UniqueId,
              NativeOfficialCarrierProbeCandidate.ProjectInformationToken,
              StringComparison.Ordinal)
            || !string.Equals(
              metricCandidates[0].CategoryBuiltInId,
              "OST_ProjectInformation",
              StringComparison.Ordinal)
            || !string.Equals(
              metricCandidates[0].ElementClass,
              "Autodesk.Revit.DB.ProjectInfo",
              StringComparison.Ordinal)))
          return Reject("PROBE_PROJECT_INFORMATION_CANDIDATE_INVALID");
        if (!projectInformation && metricCandidates.Any(value => string.Equals(
          value.UniqueId,
          NativeOfficialCarrierProbeCandidate.ProjectInformationToken,
          StringComparison.Ordinal)))
          return Reject("PROBE_CANDIDATE_INVALID");
      }
      return new NativeOfficialCarrierProbeAuthorization
      {
        Authorized = true
      };
    }

    internal static IReadOnlyList<NativeOfficialCarrierProbeSentinel>
      BuildSentinels(
        NativeOfficialCarrierProbeContext context,
        IReadOnlyList<NativeStage02BMetricDefinition> metrics)
    {
      if (context == null) throw new ArgumentNullException(nameof(context));
      NativeStage02BMetricDefinition[] catalog = (metrics
          ?? Array.Empty<NativeStage02BMetricDefinition>())
        .Where(value => value != null)
        .OrderBy(value => value.Sequence)
        .ToArray();
      NativeOfficialCarrierProbeMetric[] contextMetrics = (context.Metrics
          ?? Array.Empty<NativeOfficialCarrierProbeMetric>())
        .Where(value => value != null).ToArray();
      if (catalog.Length != 6 || contextMetrics.Length != 6)
        throw new InvalidDataException("PROBE_METRIC_CONTEXT_INVALID");
      var contextById = contextMetrics.ToDictionary(
        value => value.PropertyId, value => value, StringComparer.Ordinal);
      ushort nonceSha16 = NonceSha16(context.Nonce);
      var result = new List<NativeOfficialCarrierProbeSentinel>();
      foreach (NativeStage02BMetricDefinition metric in catalog)
      {
        if (!contextById.TryGetValue(metric.PropertyId,
          out NativeOfficialCarrierProbeMetric definition)
          || definition.Sequence != metric.Sequence
          || !string.Equals(definition.IfcEntity,
            metric.Property.IfcEntity, StringComparison.Ordinal)
          || !string.Equals(definition.IfcPropertySet,
            metric.Property.IfcPropertySet, StringComparison.Ordinal)
          || !string.Equals(definition.IfcProperty,
            metric.Property.IfcProperty, StringComparison.Ordinal)
          || !string.Equals(definition.DeclaredIfcType,
            metric.Property.DeclaredIfcType, StringComparison.Ordinal)
          || !string.Equals(definition.CanonicalUnit ?? string.Empty,
            metric.Property.CanonicalUnit ?? string.Empty,
            StringComparison.Ordinal)
          || !string.Equals(definition.StorageType,
            metric.Property.StorageType, StringComparison.Ordinal)
          || string.IsNullOrWhiteSpace(definition.ExactOfficialSourceName))
          throw new InvalidDataException("PROBE_METRIC_CONTEXT_MISMATCH");

        NativeOfficialCarrierProbeCandidate[] candidates =
          (context.Candidates ?? Array.Empty<NativeOfficialCarrierProbeCandidate>())
          .Where(value => value != null && string.Equals(
            value.PropertyId, metric.PropertyId, StringComparison.Ordinal))
          .OrderBy(value => value.UniqueId, StringComparer.Ordinal)
          .ToArray();
        if (candidates.Length == 0)
          throw new InvalidDataException("PROBE_CANDIDATE_SET_MISMATCH");
        for (int index = 0; index < candidates.Length; index++)
        {
          string sentinel;
          if (string.Equals(definition.DeclaredIfcType,
            "IfcInteger", StringComparison.Ordinal))
          {
            sentinel = (700000000L + metric.Sequence * 10000L + index)
              .ToString(CultureInfo.InvariantCulture);
          }
          else if (string.Equals(definition.DeclaredIfcType,
            "IfcReal", StringComparison.Ordinal))
          {
            double value = 700000d + metric.Sequence * 1000d + index
              + nonceSha16 / 1000000000d;
            sentinel = value.ToString("G17", CultureInfo.InvariantCulture);
          }
          else
          {
            throw new InvalidDataException("PROBE_SENTINEL_TYPE_UNSUPPORTED");
          }
          result.Add(new NativeOfficialCarrierProbeSentinel
          {
            PropertyId = metric.PropertyId,
            Sequence = metric.Sequence,
            CandidateIndex = index,
            CandidateUniqueId = candidates[index].UniqueId,
            CategoryBuiltInId = candidates[index].CategoryBuiltInId,
            ElementClass = candidates[index].ElementClass,
            IfcEntity = definition.IfcEntity,
            IfcPropertySet = definition.IfcPropertySet,
            IfcProperty = definition.IfcProperty,
            ExactSourceName = definition.ExactOfficialSourceName,
            DeclaredIfcType = definition.DeclaredIfcType,
            CanonicalUnit = definition.CanonicalUnit ?? string.Empty,
            StorageType = definition.StorageType,
            ParameterGuid = metric.Property.ParameterGuid,
            BindingScope = "INSTANCE",
            CanonicalValue = sentinel
          });
        }
      }
      if (result.GroupBy(value => value.CanonicalValue,
        StringComparer.Ordinal).Any(group => group.Count() > 1))
        throw new InvalidDataException("PROBE_SENTINEL_DUPLICATE");
      return new ReadOnlyCollection<NativeOfficialCarrierProbeSentinel>(
        result);
    }

    internal static NativeOfficialCarrierProbeAuthorization
      ValidateExistingSourceParameters(
        IReadOnlyList<NativeOfficialCarrierProbeSentinel> sentinels,
        IReadOnlyList<NativeOfficialCarrierProbeExistingParameter> existing)
    {
      NativeOfficialCarrierProbeSentinel[] definitions = (sentinels
          ?? Array.Empty<NativeOfficialCarrierProbeSentinel>())
        .Where(value => value != null)
        .GroupBy(value => value.PropertyId, StringComparer.Ordinal)
        .Select(group => group.First()).ToArray();
      NativeOfficialCarrierProbeExistingParameter[] parameters = (existing
          ?? Array.Empty<NativeOfficialCarrierProbeExistingParameter>())
        .Where(value => value != null).ToArray();
      if (definitions.GroupBy(value => value.ExactSourceName,
        StringComparer.Ordinal).Any(group => group.Count() > 1))
        return Reject("OFFICIAL_SOURCE_NAME_AMBIGUOUS");
      foreach (NativeOfficialCarrierProbeSentinel definition in definitions)
      {
        NativeOfficialCarrierProbeExistingParameter[] matches = parameters
          .Where(value => string.Equals(value.ExactSourceName,
            definition.ExactSourceName, StringComparison.Ordinal)).ToArray();
        if (matches.Length > 1)
          return Reject("OFFICIAL_SOURCE_NAME_AMBIGUOUS");
        if (matches.Length == 1
          && (matches[0].ParameterGuid != definition.ParameterGuid
            || !string.Equals(matches[0].StorageType,
              definition.StorageType, StringComparison.Ordinal)))
          return Reject("OFFICIAL_SOURCE_NAME_CONTRACT_MISMATCH");
      }
      return new NativeOfficialCarrierProbeAuthorization
      {
        Authorized = true
      };
    }

    internal static string InstalledCommitSha()
    {
      AssemblyMetadataAttribute metadata = typeof(App).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(value => string.Equals(
          value.Key, "HBR.CommitSha", StringComparison.Ordinal));
      return metadata?.Value ?? string.Empty;
    }

    internal static string ComputeSha256(string path)
    {
      using (FileStream stream = File.OpenRead(path))
      using (SHA256 algorithm = SHA256.Create())
        return string.Concat(algorithm.ComputeHash(stream)
          .Select(value => value.ToString("x2")));
    }

    private static ushort NonceSha16(string nonce)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(nonce ?? string.Empty);
        byte[] digest = algorithm.ComputeHash(bytes);
        return (ushort)((digest[0] << 8) | digest[1]);
      }
    }

    private static bool IsReparsePoint(string path)
    {
      if (!File.Exists(path) && !Directory.Exists(path)) return false;
      return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool HasReparsePoint(
      string path,
      Func<string, bool> isReparsePoint)
    {
      if (isReparsePoint == null) return true;
      string current = Path.GetFullPath(path);
      while (!string.IsNullOrWhiteSpace(current))
      {
        if (isReparsePoint(current)) return true;
        current = Path.GetDirectoryName(current);
      }
      return false;
    }

    private static string NormalizeFilePath(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new ArgumentException("Path is required.", nameof(value));
      return Path.GetFullPath(value.Trim())
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeDirectoryPath(string value)
    {
      return NormalizeFilePath(value);
    }

    private static bool SamePath(string left, string right)
    {
      return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithinRoot(string root, string path)
    {
      if (SamePath(root, path)) return false;
      string prefix = root + Path.DirectorySeparatorChar;
      return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSha256(string value)
    {
      return IsHex(value, 64);
    }

    private static bool IsCommitSha(string value)
    {
      return IsHex(value, 40) || IsHex(value, 64);
    }

    private static bool IsHex(string value, int length)
    {
      string normalized = (value ?? string.Empty).Trim();
      return normalized.Length == length && normalized.All(character =>
        (character >= '0' && character <= '9')
        || (character >= 'a' && character <= 'f')
        || (character >= 'A' && character <= 'F'));
    }

    private static NativeOfficialCarrierProbeAuthorization Reject(string code)
    {
      return new NativeOfficialCarrierProbeAuthorization
      {
        ErrorCode = code ?? string.Empty,
        Message = code ?? string.Empty
      };
    }
  }
}
