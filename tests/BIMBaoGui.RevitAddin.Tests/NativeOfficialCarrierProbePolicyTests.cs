using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BIMBaoGui.RevitAddin.Acceptance;
using BIMBaoGui.RevitAddin.Rules;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeOfficialCarrierProbePolicyTests
  {
    [Fact]
    public void Production_and_source_documents_are_rejected_before_authorization()
    {
      using (var sandbox = new ProbeSandbox())
      {
        Assert.Equal("PROBE_ACTIVE_DOCUMENT_MISMATCH",
          sandbox.Authorize(sandbox.ProductionPath).ErrorCode);
        Assert.Equal("PROBE_SOURCE_DOCUMENT_FORBIDDEN",
          sandbox.Authorize(sandbox.SourcePath).ErrorCode);
      }
    }

    [Fact]
    public void Escaped_or_reparse_context_path_is_rejected()
    {
      using (var sandbox = new ProbeSandbox())
      {
        NativeOfficialCarrierProbeContext escaped = sandbox.Context();
        escaped.ProbeCopyPath = Path.Combine(
          Path.GetDirectoryName(sandbox.Root),
          "escaped__HIFC_CARRIER_PROBE__.rvt");
        Assert.Equal("PROBE_PATH_ESCAPE",
          sandbox.Authorize(sandbox.ProbePath, escaped).ErrorCode);

        Assert.Equal("PROBE_REPARSE_POINT_FORBIDDEN",
          sandbox.Authorize(
            sandbox.ProbePath,
            sandbox.Context(),
            path => string.Equals(
              Path.GetFullPath(path),
              Path.GetFullPath(sandbox.ProbePath),
              StringComparison.OrdinalIgnoreCase)).ErrorCode);
      }
    }

    [Theory]
    [InlineData("source-sha")]
    [InlineData("commit")]
    [InlineData("rule")]
    public void Sha_commit_and_rule_mismatch_are_rejected(string mismatch)
    {
      using (var sandbox = new ProbeSandbox())
      {
        NativeOfficialCarrierProbeContext context = sandbox.Context();
        string activeSha = sandbox.SourceSha;
        string installedCommit = sandbox.Commit;
        string installedRule = sandbox.RuleSha;
        if (mismatch == "source-sha") activeSha = new string('f', 64);
        if (mismatch == "commit") installedCommit = new string('e', 40);
        if (mismatch == "rule") installedRule = new string('d', 64);

        string previous = Environment.GetEnvironmentVariable(
          NativeOfficialCarrierProbePolicy.ContextEnvironmentVariable);
        NativeOfficialCarrierProbeAuthorization result;
        try
        {
          Environment.SetEnvironmentVariable(
            NativeOfficialCarrierProbePolicy.ContextEnvironmentVariable,
            sandbox.ContextPath);
          result = NativeOfficialCarrierProbePolicy.Authorize(
              sandbox.ContextPath,
              sandbox.ProbePath,
              activeSha,
              context,
              installedCommit,
              installedRule,
              _ => false);
        }
        finally
        {
          Environment.SetEnvironmentVariable(
            NativeOfficialCarrierProbePolicy.ContextEnvironmentVariable,
            previous);
        }

        Assert.False(result.Authorized);
        Assert.Contains(mismatch == "source-sha" ? "SHA" :
          mismatch.ToUpperInvariant(), result.ErrorCode);
      }
    }

    [Fact]
    public void Duplicate_candidate_is_rejected_before_authorization()
    {
      using (var sandbox = new ProbeSandbox())
      {
        NativeOfficialCarrierProbeContext context = sandbox.Context();
        context.Candidates = new[]
        {
          context.Candidates[0], context.Candidates[0]
        };

        Assert.Equal("PROBE_CANDIDATE_DUPLICATE",
          sandbox.Authorize(sandbox.ProbePath, context).ErrorCode);
      }
    }

    [Fact]
    public void Project_information_candidate_requires_the_fixed_token()
    {
      using (var sandbox = new ProbeSandbox())
      {
        NativeOfficialCarrierProbeContext context = sandbox.Context();
        context.Candidates[0].UniqueId = "uid-project";

        Assert.Equal("PROBE_PROJECT_INFORMATION_CANDIDATE_INVALID",
          sandbox.Authorize(sandbox.ProbePath, context).ErrorCode);
      }
    }

    [Fact]
    public void Same_source_name_ambiguity_and_guid_mismatch_are_rejected()
    {
      using (var sandbox = new ProbeSandbox())
      {
        IReadOnlyList<NativeOfficialCarrierProbeSentinel> sentinels =
          NativeOfficialCarrierProbePolicy.BuildSentinels(
            sandbox.Context(),
            NativeStage02BMetricCatalog.Current.MetricsFor("总平模型"));
        NativeOfficialCarrierProbeSentinel first = sentinels[0];

        Assert.Equal("OFFICIAL_SOURCE_NAME_AMBIGUOUS",
          NativeOfficialCarrierProbePolicy.ValidateExistingSourceParameters(
            sentinels,
            new[]
            {
              Existing(first), Existing(first)
            }).ErrorCode);
        NativeOfficialCarrierProbeExistingParameter mismatched = Existing(first);
        mismatched.ParameterGuid = Guid.NewGuid();
        Assert.Equal("OFFICIAL_SOURCE_NAME_CONTRACT_MISMATCH",
          NativeOfficialCarrierProbePolicy.ValidateExistingSourceParameters(
            sentinels, new[] { mismatched }).ErrorCode);
      }
    }

    [Fact]
    public void Real_and_integer_sentinels_are_unique_and_round_trip()
    {
      using (var sandbox = new ProbeSandbox())
      {
        IReadOnlyList<NativeOfficialCarrierProbeSentinel> sentinels =
          NativeOfficialCarrierProbePolicy.BuildSentinels(
            sandbox.Context(),
            NativeStage02BMetricCatalog.Current.MetricsFor("总平模型"));

        Assert.Equal(6, sentinels.Count);
        Assert.Equal(6, sentinels.Select(value => value.CanonicalValue)
          .Distinct(StringComparer.Ordinal).Count());
        foreach (NativeOfficialCarrierProbeSentinel sentinel in sentinels)
        {
          if (sentinel.DeclaredIfcType == "IfcInteger")
            Assert.Equal(sentinel.CanonicalValue,
              long.Parse(sentinel.CanonicalValue).ToString());
          else
            Assert.Equal(sentinel.CanonicalValue,
              double.Parse(sentinel.CanonicalValue,
                System.Globalization.CultureInfo.InvariantCulture)
                .ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
        }
      }
    }

    private static NativeOfficialCarrierProbeExistingParameter Existing(
      NativeOfficialCarrierProbeSentinel sentinel)
    {
      return new NativeOfficialCarrierProbeExistingParameter
      {
        ExactSourceName = sentinel.ExactSourceName,
        ParameterGuid = sentinel.ParameterGuid,
        StorageType = sentinel.StorageType
      };
    }

    private sealed class ProbeSandbox : IDisposable
    {
      internal ProbeSandbox()
      {
        Root = Path.Combine(Path.GetTempPath(), "BIMBaoGui-Probe-Tests",
          Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        SourcePath = Path.Combine(Path.GetDirectoryName(Root),
          Guid.NewGuid().ToString("N") + ".rvt");
        ProbePath = Path.Combine(Root,
          "copy__HIFC_CARRIER_PROBE__.rvt");
        ProductionPath = Path.Combine(Root, "production.rvt");
        ContextPath = Path.Combine(Root, "context.json");
        File.WriteAllText(SourcePath, "golden");
        File.Copy(SourcePath, ProbePath);
        File.WriteAllText(ProductionPath, "production");
        File.WriteAllText(ContextPath, "{}");
        SourceSha = Sha256(SourcePath);
        Commit = new string('a', 40);
        RuleSha = new string('b', 64);
      }

      internal string Root { get; }
      internal string SourcePath { get; }
      internal string ProbePath { get; }
      internal string ProductionPath { get; }
      internal string ContextPath { get; }
      internal string SourceSha { get; }
      internal string Commit { get; }
      internal string RuleSha { get; }

      internal NativeOfficialCarrierProbeContext Context()
      {
        return new NativeOfficialCarrierProbeContext
        {
          SourceGoldenRvtPath = SourcePath,
          SourceGoldenRvtSha256 = SourceSha,
          ProbeCopyPath = ProbePath,
          ProbeCopyPreSeedSha256 = SourceSha,
          AcceptanceRoot = Root,
          AcceptanceRunId = "run",
          Nonce = "00112233445566778899aabbccddeeff",
          CommitSha = Commit,
          RulePackageSha256 = RuleSha,
          Metrics = NativeStage02BMetricCatalog.Current.MetricsFor("总平模型")
            .Select(value => new NativeOfficialCarrierProbeMetric
            {
              PropertyId = value.PropertyId,
              Sequence = value.Sequence,
              IfcEntity = value.Property.IfcEntity,
              IfcPropertySet = value.Property.IfcPropertySet,
              IfcProperty = value.Property.IfcProperty,
              ExactOfficialSourceName = value.Property.IfcProperty,
              DeclaredIfcType = value.Property.DeclaredIfcType,
              CanonicalUnit = value.Property.CanonicalUnit,
              StorageType = value.Property.StorageType
            }).ToArray(),
          Candidates = new[]
          {
            new NativeOfficialCarrierProbeCandidate
            {
              PropertyId = "ca21e324-046b-5bfd-84c8-0d3470082303",
              UniqueId = NativeOfficialCarrierProbeCandidate.ProjectInformationToken,
              CategoryBuiltInId = "OST_ProjectInformation",
              ElementClass = "Autodesk.Revit.DB.ProjectInfo"
            },
            new NativeOfficialCarrierProbeCandidate
            {
              PropertyId = "93e51676-237e-56a8-8f28-2da845422e2e",
              UniqueId = "uid-site",
              CategoryBuiltInId = "OST_BuildingPad",
              ElementClass = "Autodesk.Revit.DB.Architecture.BuildingPad"
            },
            new NativeOfficialCarrierProbeCandidate
            {
              PropertyId = "201a00ac-3672-5ded-83d2-ed96f81bfabf",
              UniqueId = "uid-site",
              CategoryBuiltInId = "OST_BuildingPad",
              ElementClass = "Autodesk.Revit.DB.Architecture.BuildingPad"
            },
            new NativeOfficialCarrierProbeCandidate
            {
              PropertyId = "f630ad47-b006-5127-badd-b1660cf996c3",
              UniqueId = "uid-site",
              CategoryBuiltInId = "OST_BuildingPad",
              ElementClass = "Autodesk.Revit.DB.Architecture.BuildingPad"
            },
            new NativeOfficialCarrierProbeCandidate
            {
              PropertyId = "c62cfd5f-2a50-5230-9c5d-4037c39061bf",
              UniqueId = "uid-zone",
              CategoryBuiltInId = "OST_Areas",
              ElementClass = "Autodesk.Revit.DB.SpatialElement"
            },
            new NativeOfficialCarrierProbeCandidate
            {
              PropertyId = "84df74c2-a7e5-5a98-a5e0-4458e49a3973",
              UniqueId = "uid-zone",
              CategoryBuiltInId = "OST_Areas",
              ElementClass = "Autodesk.Revit.DB.SpatialElement"
            }
          }
        };
      }

      internal NativeOfficialCarrierProbeAuthorization Authorize(
        string activePath,
        NativeOfficialCarrierProbeContext context = null,
        Func<string, bool> reparse = null)
      {
        string previous = Environment.GetEnvironmentVariable(
          "BIMBAOGUI_ACCEPTANCE_PROBE_CONTEXT");
        try
        {
          Environment.SetEnvironmentVariable(
            "BIMBAOGUI_ACCEPTANCE_PROBE_CONTEXT", ContextPath);
          return NativeOfficialCarrierProbePolicy.Authorize(
            ContextPath,
            activePath,
            SourceSha,
            context ?? Context(),
            Commit,
            RuleSha,
            reparse ?? (_ => false));
        }
        finally
        {
          Environment.SetEnvironmentVariable(
            "BIMBAOGUI_ACCEPTANCE_PROBE_CONTEXT", previous);
        }
      }

      public void Dispose()
      {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        catch { }
        try { if (File.Exists(SourcePath)) File.Delete(SourcePath); }
        catch { }
      }

      private static string Sha256(string path)
      {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 hash = SHA256.Create())
          return string.Concat(hash.ComputeHash(stream)
            .Select(value => value.ToString("x2")));
      }
    }
  }
}
