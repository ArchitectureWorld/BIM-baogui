using System;
using System.Collections.Generic;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03RevitRequestPolicyTests
  {
    [Fact]
    public void ScanRequest_SnapshotsContextDocumentIdentity()
    {
      HBRFileContext context = BuildContext();

      var request = new Stage03RevitScanRequest(context);

      Assert.Same(context, request.Context);
      Assert.Equal("document-fingerprint", request.DocumentFingerprint);
      Assert.Equal("测试模型.rvt", request.DocumentTitle);
      Assert.Equal(context.RulePackageId, request.RulePackageId);
      Assert.Equal(context.RulePackageVersion, request.RulePackageVersion);
      Assert.Equal(context.RulePackageSha256, request.RulePackageSha256);
    }

    [Fact]
    public void ExportRequest_CarriesOnlyPureIdentityAndExactRawPath()
    {
      var request = new Stage03RevitExportRequest(
        "document-fingerprint",
        "测试模型.rvt",
        "hbr-package",
        "0.9.0",
        "ABCDEF",
        @"C:\exports\model-RAW.ifc");

      Assert.Equal("document-fingerprint", request.DocumentFingerprint);
      Assert.Equal("测试模型.rvt", request.DocumentTitle);
      Assert.Equal("hbr-package", request.RulePackageId);
      Assert.Equal("0.9.0", request.RulePackageVersion);
      Assert.Equal("ABCDEF", request.RulePackageSha256);
      Assert.Equal(@"C:\exports\model-RAW.ifc", request.RawIfcPath);
    }

    [Fact]
    public void Identity_AcceptsExactDocumentIdentity()
    {
      Stage03RevitRequestIdentityDecision decision =
        Stage03RevitRequestIdentityPolicy.Evaluate(
          "document-fingerprint",
          "测试模型.rvt",
          "document-fingerprint",
          "测试模型.rvt");

      Assert.True(decision.Success, decision.Message);
    }

    [Theory]
    [InlineData("other-fingerprint", "测试模型.rvt")]
    [InlineData("document-fingerprint", "其他模型.rvt")]
    [InlineData("", "测试模型.rvt")]
    [InlineData("document-fingerprint", "")]
    public void Identity_RejectsMissingOrChangedDocumentIdentity(
      string actualFingerprint,
      string actualTitle)
    {
      Stage03RevitRequestIdentityDecision decision =
        Stage03RevitRequestIdentityPolicy.Evaluate(
          "document-fingerprint",
          "测试模型.rvt",
          actualFingerprint,
          actualTitle);

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Message);
    }

    [Fact]
    public void RulePackageIdentity_AcceptsExactIdentityIgnoringShaCase()
    {
      Stage03RevitRequestIdentityDecision decision =
        Stage03RevitRequestRulePackagePolicy.Evaluate(
          "hbr-package",
          "0.9.0",
          "abcdef",
          "hbr-package",
          "0.9.0",
          "ABCDEF");

      Assert.True(decision.Success, decision.Message);
    }

    [Theory]
    [InlineData("other-package", "0.9.0", "ABCDEF")]
    [InlineData("hbr-package", "9.9.9", "ABCDEF")]
    [InlineData("hbr-package", "0.9.0", "BAD")]
    public void RulePackageIdentity_RejectsChangedIdentity(
      string actualId,
      string actualVersion,
      string actualSha256)
    {
      Stage03RevitRequestIdentityDecision decision =
        Stage03RevitRequestRulePackagePolicy.Evaluate(
          "hbr-package",
          "0.9.0",
          "ABCDEF",
          actualId,
          actualVersion,
          actualSha256);

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Message);
    }

    private static HBRFileContext BuildContext()
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      var provisional = new HBRFileContext(
        HBRContextVersions.FileContextSchema,
        "0.9.0",
        "file-guid",
        "document-fingerprint",
        "测试模型.rvt",
        "P-001",
        "测试项目",
        "S-001",
        "测试子项",
        "SITE_MODEL",
        "测试范围",
        null,
        new Dictionary<string, PlanningTargetValue>(StringComparer.Ordinal),
        new Dictionary<string, bool>(StringComparer.Ordinal),
        Array.Empty<string>(),
        Array.Empty<string>(),
        true,
        false,
        string.Empty,
        package.PackageId,
        package.PackageVersion,
        package.RulePackageSha256,
        "payload-hash",
        string.Empty);
      return provisional.WithHash(
        HBRFileContextCanonicalizer.ComputeHash(provisional));
    }
  }
}
