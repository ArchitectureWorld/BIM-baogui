using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Issues;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02IssueCompiler
  {
    internal static IReadOnlyList<NativeIssueRecord> Compile(
      NativeStage02Preview preview)
    {
      if (preview == null) throw new ArgumentNullException(nameof(preview));
      var issues = new List<NativeIssueRecord>();
      foreach (NativeStage02ElementPlan element in preview.Elements
        ?? Array.Empty<NativeStage02ElementPlan>())
      {
        if (element?.Element == null) continue;
        if (element.RoleConfirmation != null
          && !element.RoleConfirmation.Confirmed)
        {
          issues.Add(Create(
            preview.DocumentFingerprint,
            element,
            "STAGE02A.ROLE_CONFIRMATION",
            element.RoleConfirmation.Code,
            "语义角色尚未获得当前构件/规则快照的显式确认。"));
        }
        foreach (NativeStage02GeometryCheckEvidence check in
          element.TaskGeometry?.Checks
            ?? Array.Empty<NativeStage02GeometryCheckEvidence>())
        {
          if (check == null
            || check.State == NativeStage02GeometryCheckState.Passed
            || check.State == NativeStage02GeometryCheckState.ManualReviewApproved)
            continue;
          issues.Add(Create(
            preview.DocumentFingerprint,
            element,
            check.CheckId,
            check.Code,
            check.RuleText));
        }
      }
      return issues
        .OrderBy(value => value.IssueId, StringComparer.Ordinal)
        .ToArray();
    }

    private static NativeIssueRecord Create(
      string documentFingerprint,
      NativeStage02ElementPlan element,
      string checkId,
      string code,
      string missing)
    {
      var issue = new NativeIssueRecord
      {
        DocumentFingerprint = documentFingerprint ?? string.Empty,
        Severity = NativeIssueSeverity.Blocker,
        SourceFeature = "STAGE02A",
        CheckId = checkId ?? string.Empty,
        Code = code ?? string.Empty,
        Missing = missing ?? string.Empty,
        Impact = "Stage03 保持红色，不能把当前构件视为已完成 02A 准备。",
        Remediation = "返回 02A，确认候选并修复几何证据或完成当前快照复核。",
        RoleId = element.EffectiveRoleId ?? string.Empty,
        Elements = new[]
        {
          new NativeIssueElementReference
          {
            ElementId = element.Element.ElementId,
            UniqueId = element.Element.UniqueId ?? string.Empty,
            ElementName = element.Element.ElementName ?? string.Empty,
            CategoryName = element.Element.CategoryName ?? string.Empty
          }
        },
        Route = NativeIssueNavigationAction.OpenStage02A
      };
      issue.IssueId = NativeIssueCanonicalizer.ComputeId(issue);
      return issue;
    }
  }
}
