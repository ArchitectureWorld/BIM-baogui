using System;
using System.Collections.Generic;
using BIMBaoGui.Stage01.Context;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class Stage03ContextIdentityDecision
  {
    internal Stage03ContextIdentityDecision(
      bool success,
      IReadOnlyList<string> messages)
    {
      Success = success;
      Messages = messages ?? Array.Empty<string>();
    }

    internal bool Success { get; }
    internal IReadOnlyList<string> Messages { get; }
  }

  internal static class Stage03ContextIdentityPolicy
  {
    internal static Stage03ContextIdentityDecision Evaluate(
      HBRFileContext context,
      string rulePackageId,
      string rulePackageVersion,
      string rulePackageSha256,
      string documentFingerprint,
      string documentTitle)
    {
      if (context == null)
      {
        return new Stage03ContextIdentityDecision(
          false,
          new[] { "缺少 HBRFileContext。" });
      }
      var messages = new List<string>();
      if (!context.IsValid)
        messages.Add("HBRFileContext 缺少稳定身份字段。");
      if (!context.InitializationPassed)
        messages.Add("HBRFileContext 尚未通过初始化。");
      if (!string.Equals(
        context.SchemaVersion,
        HBRContextVersions.FileContextSchema,
        StringComparison.Ordinal))
      {
        messages.Add("HBRFileContext schemaVersion 不受 Stage03 支持。");
      }
      try
      {
        if (!string.Equals(
          context.FileContextHash,
          HBRFileContextCanonicalizer.ComputeHash(context),
          StringComparison.OrdinalIgnoreCase))
        {
          messages.Add("HBRFileContext 哈希校验失败。");
        }
      }
      catch (Exception)
      {
        messages.Add("HBRFileContext 无法执行哈希校验。");
      }
      if (!string.Equals(
          context.RulePackageId,
          rulePackageId ?? string.Empty,
          StringComparison.Ordinal)
        || !string.Equals(
          context.RulePackageVersion,
          rulePackageVersion ?? string.Empty,
          StringComparison.Ordinal)
        || !string.Equals(
          context.RulePackageSha256,
          rulePackageSha256 ?? string.Empty,
          StringComparison.OrdinalIgnoreCase))
      {
        messages.Add("HBRFileContext 的规则包身份与 Stage03 规则包不一致。");
      }
      if (!string.Equals(
          context.RevitDocumentFingerprint,
          documentFingerprint ?? string.Empty,
          StringComparison.Ordinal)
        || !string.Equals(
          context.RevitDocumentTitle,
          documentTitle ?? string.Empty,
          StringComparison.Ordinal))
      {
        messages.Add("HBRFileContext 的 Revit 文档身份与活动文档不一致。");
      }
      return new Stage03ContextIdentityDecision(
        messages.Count == 0,
        messages.ToArray());
    }
  }
}
