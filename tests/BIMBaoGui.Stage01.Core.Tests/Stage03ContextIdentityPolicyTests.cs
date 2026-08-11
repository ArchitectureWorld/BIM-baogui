using System;
using System.Collections.Generic;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03ContextIdentityPolicyTests
  {
    [Fact]
    public void Evaluate_AcceptsVerifiedContextWhenOfficialProtocolIsIncompatible()
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      HBRFileContext context = BuildContext(
        package,
        initializationPassed: true,
        officialProtocolCompatible: false);

      Stage03ContextIdentityDecision decision =
        Stage03ContextIdentityPolicy.Evaluate(
          context,
          package.PackageId,
          package.PackageVersion,
          package.RulePackageSha256,
          "document-fingerprint",
          "测试模型.rvt");

      Assert.True(context.IsReady);
      Assert.True(decision.Success, string.Join(" ", decision.Messages));
      Assert.Empty(decision.Messages);
    }

    [Fact]
    public void Evaluate_RejectsContextWithoutSuccessfulInitialization()
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      HBRFileContext context = BuildContext(
        package,
        initializationPassed: false,
        officialProtocolCompatible: false);

      Stage03ContextIdentityDecision decision = Evaluate(context, package);

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Messages);
    }

    [Fact]
    public void Evaluate_RejectsWrongSchema()
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      HBRFileContext context = BuildContext(
        package,
        initializationPassed: true,
        officialProtocolCompatible: false,
        schemaVersion: "0.8.0");

      Stage03ContextIdentityDecision decision = Evaluate(context, package);

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Messages);
    }

    [Fact]
    public void Evaluate_RejectsCorruptContextHash()
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      HBRFileContext context = BuildContext(
        package,
        initializationPassed: true,
        officialProtocolCompatible: false,
        corruptHash: true);

      Stage03ContextIdentityDecision decision = Evaluate(context, package);

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Messages);
    }

    [Theory]
    [InlineData("other-package", null, null)]
    [InlineData(null, "9.9.9", null)]
    [InlineData(null, null, "BAD-SHA256")]
    public void Evaluate_RejectsRulePackageIdentityMismatch(
      string expectedId,
      string expectedVersion,
      string expectedSha256)
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      HBRFileContext context = BuildContext(
        package,
        initializationPassed: true,
        officialProtocolCompatible: false);

      Stage03ContextIdentityDecision decision =
        Stage03ContextIdentityPolicy.Evaluate(
          context,
          expectedId ?? package.PackageId,
          expectedVersion ?? package.PackageVersion,
          expectedSha256 ?? package.RulePackageSha256,
          "document-fingerprint",
          "测试模型.rvt");

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Messages);
    }

    [Theory]
    [InlineData("other-fingerprint", "测试模型.rvt")]
    [InlineData("document-fingerprint", "另一个模型.rvt")]
    public void Evaluate_RejectsDocumentIdentityMismatch(
      string documentFingerprint,
      string documentTitle)
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      HBRFileContext context = BuildContext(
        package,
        initializationPassed: true,
        officialProtocolCompatible: false);

      Stage03ContextIdentityDecision decision =
        Stage03ContextIdentityPolicy.Evaluate(
          context,
          package.PackageId,
          package.PackageVersion,
          package.RulePackageSha256,
          documentFingerprint,
          documentTitle);

      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Messages);
    }

    [Fact]
    public void Evaluate_RejectsStructurallyInvalidContext()
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      HBRFileContext context = BuildContext(
        package,
        initializationPassed: true,
        officialProtocolCompatible: false,
        fileGuid: string.Empty);

      Stage03ContextIdentityDecision decision = Evaluate(context, package);

      Assert.False(context.IsValid);
      Assert.False(decision.Success);
      Assert.NotEmpty(decision.Messages);
    }

    private static Stage03ContextIdentityDecision Evaluate(
      HBRFileContext context,
      HbrRulePackage package)
    {
      return Stage03ContextIdentityPolicy.Evaluate(
        context,
        package.PackageId,
        package.PackageVersion,
        package.RulePackageSha256,
        "document-fingerprint",
        "测试模型.rvt");
    }

    private static HBRFileContext BuildContext(
      HbrRulePackage package,
      bool initializationPassed,
      bool officialProtocolCompatible,
      string schemaVersion = null,
      bool corruptHash = false,
      string fileGuid = "file-guid")
    {
      var provisional = new HBRFileContext(
        schemaVersion ?? HBRContextVersions.FileContextSchema,
        "0.9.0",
        fileGuid,
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
        initializationPassed,
        officialProtocolCompatible,
        string.Empty,
        package.PackageId,
        package.PackageVersion,
        package.RulePackageSha256,
        "payload-hash",
        string.Empty);
      return provisional.WithHash(
        corruptHash
          ? "CORRUPT-HASH"
          : HBRFileContextCanonicalizer.ComputeHash(provisional));
    }
  }
}
