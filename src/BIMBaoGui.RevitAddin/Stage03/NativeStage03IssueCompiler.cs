using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal static class NativeStage03IssueCompiler
  {
    internal static NativeIssueRecord Compile(NativeStage03ChecklistItem item)
    {
      if (item == null) throw new ArgumentNullException(nameof(item));
      IReadOnlyList<NativeIssueElementReference> elements = Elements(item);
      return new NativeIssueRecord
      {
        IssueId = "STAGE03:" + Clean(item.CheckId),
        SourceFeature = "STAGE03",
        CheckId = Clean(item.CheckId),
        Code = Clean(item.IssueCode),
        Severity = item.Status == NativeStage03ChecklistStatus.Failed
          ? NativeIssueSeverity.Blocker : NativeIssueSeverity.Warning,
        Missing = Missing(item),
        Impact = Impact(item),
        Remediation = Remediation(item),
        FieldKey = Clean(item.FieldKey),
        PropertyId = Clean(item.PropertyId),
        RoleId = Clean(item.RoleId),
        Elements = elements,
        Route = Route(item, elements)
      };
    }

    private static NativeIssueNavigationAction Route(
      NativeStage03ChecklistItem item,
      IReadOnlyList<NativeIssueElementReference> elements)
    {
      switch (item.SourceStage)
      {
        case NativeReportingSourceStage.Stage01:
          return NativeIssueNavigationAction.OpenStage01;
        case NativeReportingSourceStage.Stage02B:
          return NativeIssueNavigationAction.OpenStage02B;
        case NativeReportingSourceStage.Stage02A:
          return elements.Count > 0
            ? NativeIssueNavigationAction.Select
            : NativeIssueNavigationAction.OpenStage02A;
        default:
          return NativeIssueNavigationAction.StayStage03;
      }
    }

    private static IReadOnlyList<NativeIssueElementReference> Elements(
      NativeStage03ChecklistItem item)
    {
      NativeIssueElementReference[] supplied = (item.Elements
          ?? Array.Empty<NativeIssueElementReference>())
        .Where(value => value != null)
        .Select(NativeIssueNavigationRequest.CloneElement)
        .ToArray();
      if (supplied.Length > 0)
        return new ReadOnlyCollection<NativeIssueElementReference>(supplied);
      if (!item.ElementId.HasValue
        && string.IsNullOrWhiteSpace(item.ElementUniqueId))
        return Array.Empty<NativeIssueElementReference>();
      return new ReadOnlyCollection<NativeIssueElementReference>(new[]
      {
        new NativeIssueElementReference
        {
          ElementId = item.ElementId ?? 0,
          UniqueId = Clean(item.ElementUniqueId)
        }
      });
    }

    private static string Missing(NativeStage03ChecklistItem item)
    {
      string message = Clean(item.IssueMessage);
      return message.Length > 0 ? message : "检查项未满足：" + Clean(item.DisplayName);
    }

    private static string Impact(NativeStage03ChecklistItem item)
    {
      return item.Status == NativeStage03ChecklistStatus.Failed
        ? "该问题会阻断 H-IFC 导出或验收。"
        : "该问题需要复查，避免依赖证据过期。";
    }

    private static string Remediation(NativeStage03ChecklistItem item)
    {
      string target = Clean(item.RemediationTarget);
      return target.Length > 0
        ? target
        : "重新读取完整清单后复查该项。";
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }
}
