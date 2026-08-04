using System;
using BIMBaoGui.Stage01.Context;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class Stage03RevitScanRequest
  {
    internal Stage03RevitScanRequest(HBRFileContext context)
    {
      Context = context ?? throw new ArgumentNullException(nameof(context));
      DocumentFingerprint = context.RevitDocumentFingerprint;
      DocumentTitle = context.RevitDocumentTitle;
      RulePackageId = context.RulePackageId;
      RulePackageVersion = context.RulePackageVersion;
      RulePackageSha256 = context.RulePackageSha256;
    }

    internal HBRFileContext Context { get; }
    internal string DocumentFingerprint { get; }
    internal string DocumentTitle { get; }
    internal string RulePackageId { get; }
    internal string RulePackageVersion { get; }
    internal string RulePackageSha256 { get; }
  }

  internal sealed class Stage03RevitExportRequest
  {
    internal Stage03RevitExportRequest(
      string documentFingerprint,
      string documentTitle,
      string rulePackageId,
      string rulePackageVersion,
      string rulePackageSha256,
      string rawIfcPath)
    {
      DocumentFingerprint = documentFingerprint ?? string.Empty;
      DocumentTitle = documentTitle ?? string.Empty;
      RulePackageId = rulePackageId ?? string.Empty;
      RulePackageVersion = rulePackageVersion ?? string.Empty;
      RulePackageSha256 = rulePackageSha256 ?? string.Empty;
      RawIfcPath = rawIfcPath ?? string.Empty;
    }

    internal string DocumentFingerprint { get; }
    internal string DocumentTitle { get; }
    internal string RulePackageId { get; }
    internal string RulePackageVersion { get; }
    internal string RulePackageSha256 { get; }
    internal string RawIfcPath { get; }
  }

  internal sealed class Stage03RevitRequestIdentityDecision
  {
    internal Stage03RevitRequestIdentityDecision(
      bool success,
      string message)
    {
      Success = success;
      Message = message ?? string.Empty;
    }

    internal bool Success { get; }
    internal string Message { get; }
  }

  internal static class Stage03RevitRequestIdentityPolicy
  {
    internal static Stage03RevitRequestIdentityDecision Evaluate(
      string expectedFingerprint,
      string expectedTitle,
      string actualFingerprint,
      string actualTitle)
    {
      if (string.IsNullOrWhiteSpace(expectedFingerprint)
        || string.IsNullOrWhiteSpace(expectedTitle))
      {
        return Failed("Stage03 Revit 请求缺少文档指纹或标题。");
      }
      if (string.IsNullOrWhiteSpace(actualFingerprint)
        || string.IsNullOrWhiteSpace(actualTitle))
      {
        return Failed("Revit callback 当前没有稳定活动文档身份。");
      }
      if (!string.Equals(
          expectedFingerprint,
          actualFingerprint,
          StringComparison.Ordinal)
        || !string.Equals(
          expectedTitle,
          actualTitle,
          StringComparison.Ordinal))
      {
        return Failed("Revit callback 活动文档与 Stage03 请求身份不一致。");
      }
      return new Stage03RevitRequestIdentityDecision(true, string.Empty);
    }

    private static Stage03RevitRequestIdentityDecision Failed(string message)
    {
      return new Stage03RevitRequestIdentityDecision(false, message);
    }
  }

  internal static class Stage03RevitRequestRulePackagePolicy
  {
    internal static Stage03RevitRequestIdentityDecision Evaluate(
      string expectedId,
      string expectedVersion,
      string expectedSha256,
      string actualId,
      string actualVersion,
      string actualSha256)
    {
      if (string.IsNullOrWhiteSpace(expectedId)
        || string.IsNullOrWhiteSpace(expectedVersion)
        || string.IsNullOrWhiteSpace(expectedSha256))
      {
        return Failed("Stage03 Revit 请求缺少规则包身份。");
      }
      if (!string.Equals(expectedId, actualId, StringComparison.Ordinal)
        || !string.Equals(
          expectedVersion,
          actualVersion,
          StringComparison.Ordinal)
        || !string.Equals(
          expectedSha256,
          actualSha256,
          StringComparison.OrdinalIgnoreCase))
      {
        return Failed("Stage03 Revit 请求规则包身份与当前数据库不一致。");
      }
      return new Stage03RevitRequestIdentityDecision(true, string.Empty);
    }

    private static Stage03RevitRequestIdentityDecision Failed(string message)
    {
      return new Stage03RevitRequestIdentityDecision(false, message);
    }
  }
}
