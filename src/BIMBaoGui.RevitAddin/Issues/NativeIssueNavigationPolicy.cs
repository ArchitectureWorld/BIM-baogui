using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Issues
{
  internal sealed class NativeIssueNavigationRequest
  {
    internal string IssueId { get; set; } = string.Empty;
    internal NativeIssueNavigationAction Action { get; set; }
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal IReadOnlyList<NativeIssueElementReference> Elements { get; set; } =
      Array.Empty<NativeIssueElementReference>();

    internal NativeIssueNavigationRequest Clone()
    {
      return new NativeIssueNavigationRequest
      {
        IssueId = IssueId ?? string.Empty,
        Action = Action,
        DocumentFingerprint = DocumentFingerprint ?? string.Empty,
        Elements = new ReadOnlyCollection<NativeIssueElementReference>(
          (Elements ?? Array.Empty<NativeIssueElementReference>())
            .Select(CloneElement)
            .ToArray())
      };
    }

    internal static NativeIssueElementReference CloneElement(
      NativeIssueElementReference value)
    {
      return new NativeIssueElementReference
      {
        ElementId = value?.ElementId ?? 0,
        UniqueId = value?.UniqueId ?? string.Empty,
        ElementName = value?.ElementName ?? string.Empty,
        CategoryName = value?.CategoryName ?? string.Empty
      };
    }
  }

  internal sealed class NativeIssueNavigationDecision
  {
    internal bool Allowed { get; set; }
    internal string Code { get; set; } = string.Empty;
    internal IReadOnlyList<NativeIssueElementReference> ResolvedElements
    {
      get;
      set;
    } = Array.Empty<NativeIssueElementReference>();
  }

  internal sealed class NativeIssueNavigationResult
  {
    internal bool Succeeded { get; set; }
    internal string Code { get; set; } = string.Empty;
    internal NativeIssueNavigationAction Action { get; set; }
    internal IReadOnlyList<int> AffectedElementIds { get; set; } =
      Array.Empty<int>();
  }

  internal static class NativeIssueNavigationPolicy
  {
    internal static NativeIssueNavigationDecision Evaluate(
      NativeIssueNavigationRequest request,
      string currentDocumentFingerprint)
    {
      if (request == null)
        return Deny("ISSUE_REQUEST_INVALID");
      string requestedDocument = Clean(request.DocumentFingerprint);
      string currentDocument = Clean(currentDocumentFingerprint);
      if (requestedDocument.Length == 0 || currentDocument.Length == 0)
        return Deny("ISSUE_DOCUMENT_MISSING");
      if (!string.Equals(
        requestedDocument,
        currentDocument,
        StringComparison.Ordinal))
        return Deny("ISSUE_DOCUMENT_MISMATCH");
      if (!IsRevitAction(request.Action))
        return Deny("ISSUE_ACTION_UNSUPPORTED");

      NativeIssueElementReference[] elements = (request.Elements
          ?? Array.Empty<NativeIssueElementReference>())
        .ToArray();
      if (elements.Length == 0)
      {
        return request.Action == NativeIssueNavigationAction.RestoreView
          ? Allow(Array.Empty<NativeIssueElementReference>())
          : Deny("ISSUE_ELEMENT_MISSING");
      }
      if (elements.Any(value => value == null
        || value.ElementId <= 0
        || Clean(value.UniqueId).Length == 0))
        return Deny("ISSUE_ELEMENT_INVALID");
      if (elements
        .GroupBy(value => Clean(value.UniqueId), StringComparer.Ordinal)
        .Any(group => group.Count() > 1))
        return Deny("ISSUE_ELEMENT_DUPLICATE");

      return Allow(elements
        .OrderBy(value => Clean(value.UniqueId), StringComparer.Ordinal)
        .Select(value => new NativeIssueElementReference
        {
          ElementId = value.ElementId,
          UniqueId = Clean(value.UniqueId),
          ElementName = value.ElementName ?? string.Empty,
          CategoryName = value.CategoryName ?? string.Empty
        })
        .ToArray());
    }

    private static bool IsRevitAction(NativeIssueNavigationAction action)
    {
      return action == NativeIssueNavigationAction.Select
        || action == NativeIssueNavigationAction.Zoom
        || action == NativeIssueNavigationAction.Isolate
        || action == NativeIssueNavigationAction.RestoreView;
    }

    private static NativeIssueNavigationDecision Allow(
      IEnumerable<NativeIssueElementReference> elements)
    {
      return new NativeIssueNavigationDecision
      {
        Allowed = true,
        Code = "OK",
        ResolvedElements = new ReadOnlyCollection<NativeIssueElementReference>(
          (elements ?? Array.Empty<NativeIssueElementReference>())
            .Select(NativeIssueNavigationRequest.CloneElement)
            .ToArray())
      };
    }

    private static NativeIssueNavigationDecision Deny(string code)
    {
      return new NativeIssueNavigationDecision
      {
        Allowed = false,
        Code = code ?? string.Empty,
        ResolvedElements = Array.Empty<NativeIssueElementReference>()
      };
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }
  }
}
