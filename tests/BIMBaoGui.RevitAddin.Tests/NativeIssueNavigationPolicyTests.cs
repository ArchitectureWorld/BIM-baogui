using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Issues;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeIssueNavigationPolicyTests
  {
    [Fact]
    public void Missing_element_issue_cannot_request_fake_revit_location()
    {
      NativeIssueNavigationDecision decision = NativeIssueNavigationPolicy.Evaluate(
        new NativeIssueNavigationRequest
        {
          Action = NativeIssueNavigationAction.Zoom,
          DocumentFingerprint = "doc",
          Elements = Array.Empty<NativeIssueElementReference>()
        }, "doc");

      Assert.False(decision.Allowed);
      Assert.Equal("ISSUE_ELEMENT_MISSING", decision.Code);
    }

    [Fact]
    public void Restore_view_is_the_only_revit_action_that_allows_no_elements()
    {
      NativeIssueNavigationDecision restore = NativeIssueNavigationPolicy.Evaluate(
        new NativeIssueNavigationRequest
        {
          Action = NativeIssueNavigationAction.RestoreView,
          DocumentFingerprint = "doc",
          Elements = Array.Empty<NativeIssueElementReference>()
        }, "doc");
      NativeIssueNavigationDecision select = NativeIssueNavigationPolicy.Evaluate(
        new NativeIssueNavigationRequest
        {
          Action = NativeIssueNavigationAction.Select,
          DocumentFingerprint = "doc",
          Elements = Array.Empty<NativeIssueElementReference>()
        }, "doc");

      Assert.True(restore.Allowed);
      Assert.Empty(restore.ResolvedElements);
      Assert.False(select.Allowed);
      Assert.Equal("ISSUE_ELEMENT_MISSING", select.Code);
    }

    [Fact]
    public void Cross_document_and_non_revit_routes_fail_closed()
    {
      NativeIssueNavigationDecision crossDocument =
        NativeIssueNavigationPolicy.Evaluate(Request(
          NativeIssueNavigationAction.Zoom), "other-doc");
      NativeIssueNavigationDecision sourceRoute =
        NativeIssueNavigationPolicy.Evaluate(Request(
          NativeIssueNavigationAction.OpenStage02A), "doc");

      Assert.False(crossDocument.Allowed);
      Assert.Equal("ISSUE_DOCUMENT_MISMATCH", crossDocument.Code);
      Assert.False(sourceRoute.Allowed);
      Assert.Equal("ISSUE_ACTION_UNSUPPORTED", sourceRoute.Code);
    }

    [Fact]
    public void Empty_unique_id_fails_closed()
    {
      NativeIssueNavigationRequest invalid = Request(
        NativeIssueNavigationAction.Isolate);
      invalid.Elements = new[]
      {
        new NativeIssueElementReference { UniqueId = " ", ElementId = 7 }
      };

      Assert.Equal(
        "ISSUE_ELEMENT_INVALID",
        NativeIssueNavigationPolicy.Evaluate(invalid, "doc").Code);
    }

    [Fact]
    public void Non_positive_element_id_fails_closed()
    {
      NativeIssueNavigationRequest invalid = Request(
        NativeIssueNavigationAction.Isolate);
      invalid.Elements = new[]
      {
        new NativeIssueElementReference { UniqueId = "u-1", ElementId = 0 }
      };

      Assert.Equal(
        "ISSUE_ELEMENT_INVALID",
        NativeIssueNavigationPolicy.Evaluate(invalid, "doc").Code);
    }

    [Fact]
    public void Duplicate_unique_id_fails_closed()
    {
      NativeIssueNavigationRequest duplicate = Request(
        NativeIssueNavigationAction.Zoom);
      duplicate.Elements = new[]
      {
        new NativeIssueElementReference { UniqueId = "u-1", ElementId = 7 },
        new NativeIssueElementReference { UniqueId = "u-1", ElementId = 7 }
      };

      Assert.Equal(
        "ISSUE_ELEMENT_DUPLICATE",
        NativeIssueNavigationPolicy.Evaluate(duplicate, "doc").Code);
    }

    [Fact]
    public void Request_clone_deep_copies_complete_element_references()
    {
      NativeIssueNavigationRequest original = Request(
        NativeIssueNavigationAction.Select);

      NativeIssueNavigationRequest clone = original.Clone();
      original.Elements[0].ElementName = "changed";

      Assert.NotSame(original.Elements[0], clone.Elements[0]);
      Assert.Equal("element", clone.Elements[0].ElementName);
      Assert.Equal("category", clone.Elements[0].CategoryName);
      Assert.Equal(7, clone.Elements[0].ElementId);
      Assert.Equal("u-1", clone.Elements[0].UniqueId);
    }

    [Fact]
    public void Issue_hub_replaces_one_source_and_sorts_a_document_snapshot()
    {
      var hub = new NativeIssueHub();
      hub.ResetForDocument("doc");
      hub.Replace("STAGE02A", new[]
      {
        Issue("b", NativeIssueSeverity.Warning, "CHECK-B"),
        Issue("a", NativeIssueSeverity.Blocker, "CHECK-A")
      });
      hub.Replace("STAGE01", new[]
      {
        Issue("c", NativeIssueSeverity.Blocker, "CHECK-C", "STAGE01")
      });
      hub.Replace("STAGE02A", new[]
      {
        Issue("d", NativeIssueSeverity.Blocker, "CHECK-D")
      });

      Assert.Equal(
        new[] { "c", "d" },
        hub.Snapshot().Select(value => value.IssueId));

      hub.ResetForDocument("other-doc");
      Assert.Empty(hub.Snapshot());
    }

    [Fact]
    public void Issue_hub_rejects_foreign_document_issues()
    {
      var hub = new NativeIssueHub();
      hub.ResetForDocument("doc");

      Assert.Throws<ArgumentException>(() => hub.Replace("STAGE02A", new[]
      {
        Issue("foreign", NativeIssueSeverity.Blocker, "CHECK", "STAGE02A",
          "other-doc")
      }));
      Assert.Empty(hub.Snapshot());
    }

    private static NativeIssueNavigationRequest Request(
      NativeIssueNavigationAction action)
    {
      return new NativeIssueNavigationRequest
      {
        IssueId = "issue",
        Action = action,
        DocumentFingerprint = "doc",
        Elements = new[]
        {
          new NativeIssueElementReference
          {
            UniqueId = "u-1",
            ElementId = 7,
            ElementName = "element",
            CategoryName = "category"
          }
        }
      };
    }

    private static NativeIssueRecord Issue(
      string issueId,
      NativeIssueSeverity severity,
      string checkId,
      string source = "STAGE02A",
      string document = "doc")
    {
      return new NativeIssueRecord
      {
        IssueId = issueId,
        DocumentFingerprint = document,
        Severity = severity,
        SourceFeature = source,
        CheckId = checkId,
        Code = "CODE",
        Missing = "missing",
        Impact = "impact",
        Remediation = "remediation",
        Route = NativeIssueNavigationAction.OpenStage02A
      };
    }
  }
}
