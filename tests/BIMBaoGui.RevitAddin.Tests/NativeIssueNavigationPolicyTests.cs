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
    public void Clone_keeps_null_reference_invalid_for_dispatch_call_chain()
    {
      NativeIssueNavigationRequest request = Request(
        NativeIssueNavigationAction.Zoom);
      request.Elements = new NativeIssueElementReference[]
      {
        request.Elements[0],
        null
      };

      NativeIssueNavigationRequest clone = request.Clone();
      NativeIssueNavigationDecision decision =
        NativeIssueNavigationPolicy.Evaluate(clone, "doc");

      Assert.Equal(2, clone.Elements.Count);
      Assert.False(decision.Allowed);
      Assert.Equal("ISSUE_ELEMENT_INVALID", decision.Code);
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

    [Fact]
    public void Document_snapshot_transition_clears_old_issues_before_new_preview()
    {
      var hub = new NativeIssueHub();
      hub.ResetForDocument("doc-a");
      hub.Replace("STAGE02A", new[]
      {
        Issue("a", NativeIssueSeverity.Blocker, "CHECK", document: "doc-a")
      });

      hub.ResetForDocument(new CurrentDocumentSnapshot
      {
        HasDocument = true,
        DocumentFingerprint = "doc-b"
      });

      Assert.Equal("doc-b", hub.DocumentFingerprint);
      Assert.Empty(hub.Snapshot());
    }

    [Fact]
    public void Visible_stage02_lifecycle_clears_old_issues_on_active_document_boundary()
    {
      var hub = new NativeIssueHub();
      hub.ResetForDocument("doc-a");
      hub.Replace("STAGE02A", new[]
      {
        Issue("a", NativeIssueSeverity.Blocker, "CHECK", document: "doc-a")
      });
      var source = new FakeDocumentBoundarySource();
      int refreshCount = 0;
      var lifecycle = new NativeIssueHubLifecycle(
        hub,
        source,
        () => refreshCount++);

      lifecycle.Activate();
      source.Raise(new CurrentDocumentSnapshot
      {
        HasDocument = true,
        DocumentFingerprint = "doc-b"
      });

      Assert.Equal(1, source.SubscriberCount);
      Assert.Equal("doc-b", hub.DocumentFingerprint);
      Assert.Empty(hub.Snapshot());
      Assert.Equal(1, refreshCount);

      lifecycle.Deactivate();
      source.Raise(new CurrentDocumentSnapshot
      {
        HasDocument = true,
        DocumentFingerprint = "doc-c"
      });
      Assert.Equal(0, source.SubscriberCount);
      Assert.Equal("doc-b", hub.DocumentFingerprint);
    }

    [Fact]
    public void Snapshot_failure_clears_and_refreshes_before_reporting_error()
    {
      var hub = new NativeIssueHub();
      hub.ResetForDocument("doc-a");
      hub.Replace("STAGE02A", new[]
      {
        Issue("a", NativeIssueSeverity.Blocker, "CHECK", document: "doc-a")
      });
      var order = new System.Collections.Generic.List<string>();
      var lifecycle = new NativeIssueHubLifecycle(
        hub,
        new FakeDocumentBoundarySource(),
        () => order.Add("refresh"));
      var failure = new InvalidOperationException("snapshot failed");

      lifecycle.ApplySnapshotFailure(
        failure,
        error => order.Add("failure:" + error.Message));

      Assert.Equal(string.Empty, hub.DocumentFingerprint);
      Assert.Empty(hub.Snapshot());
      Assert.Equal(
        new[] { "refresh", "failure:snapshot failed" },
        order);
    }

    [Fact]
    public void Synchronous_snapshot_request_throw_uses_fail_closed_wrapper()
    {
      var hub = new NativeIssueHub();
      hub.ResetForDocument("doc-a");
      hub.Replace("STAGE02A", new[]
      {
        Issue("a", NativeIssueSeverity.Blocker, "CHECK", document: "doc-a")
      });
      var order = new System.Collections.Generic.List<string>();
      var lifecycle = new NativeIssueHubLifecycle(
        hub,
        new FakeDocumentBoundarySource(),
        () => order.Add("refresh"));

      NativeIssueSnapshotRequest.Execute(
        (completed, failed) => throw new InvalidOperationException("raise failed"),
        lifecycle,
        snapshot => order.Add("completed"),
        error => order.Add("failure:" + error.Message));

      Assert.Equal(string.Empty, hub.DocumentFingerprint);
      Assert.Empty(hub.Snapshot());
      Assert.Equal(
        new[] { "refresh", "failure:raise failed" },
        order);
    }

    [Fact]
    public void External_event_observation_failure_fails_every_queued_request()
    {
      var queue = new System.Collections.Concurrent.ConcurrentQueue<int>();
      queue.Enqueue(1);
      queue.Enqueue(2);
      var observationFailure = new InvalidOperationException("observe failed");
      var callbackFailure = new InvalidOperationException("callback failed");
      var executed = new System.Collections.Generic.List<int>();
      var failed = new System.Collections.Generic.List<int>();
      var receivedFailures = new System.Collections.Generic.List<Exception>();
      var diagnostics = new System.Collections.Generic.List<Exception>();

      Exception escaped = Record.Exception(() =>
        RevitExternalEventExecutionBoundary.Execute(
          queue,
          new object(),
          application => throw observationFailure,
          (request, application) => executed.Add(request),
          (request, exception) =>
          {
            failed.Add(request);
            receivedFailures.Add(exception);
            if (request == 1) throw callbackFailure;
          },
          exception => diagnostics.Add(exception)));

      Assert.Null(escaped);
      Assert.Empty(executed);
      Assert.Equal(new[] { 1, 2 }, failed);
      Assert.Equal(
        new[] { observationFailure, observationFailure },
        receivedFailures);
      Assert.Equal(new[] { callbackFailure }, diagnostics);
      Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void External_event_failure_callback_cannot_block_later_requests()
    {
      var queue = new System.Collections.Concurrent.ConcurrentQueue<int>();
      queue.Enqueue(1);
      queue.Enqueue(2);
      var requestFailure = new InvalidOperationException("request failed");
      var callbackFailure = new InvalidOperationException("callback failed");
      var diagnostics = new System.Collections.Generic.List<Exception>();
      int observationCount = 0;
      int failureCallbackCount = 0;
      int successCount = 0;

      Exception escaped = Record.Exception(() =>
        RevitExternalEventExecutionBoundary.Execute(
          queue,
          new object(),
          application => observationCount++,
          (request, application) =>
          {
            if (request == 1) throw requestFailure;
            successCount++;
          },
          (request, exception) =>
          {
            Assert.Equal(1, request);
            Assert.Same(requestFailure, exception);
            failureCallbackCount++;
            throw callbackFailure;
          },
          exception => diagnostics.Add(exception)));

      Assert.Null(escaped);
      Assert.Equal(1, observationCount);
      Assert.Equal(1, failureCallbackCount);
      Assert.Equal(1, successCount);
      Assert.Equal(new[] { callbackFailure }, diagnostics);
      Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void Boundary_registry_refcounts_real_source_attachment()
    {
      var transitions = new System.Collections.Generic.List<string>();
      var source = new object();
      var registry = new NativeDocumentBoundarySubscriptionRegistry(
        value => transitions.Add("attach"),
        value => transitions.Add("detach"));
      Action<CurrentDocumentSnapshot> first = snapshot => { };
      Action<CurrentDocumentSnapshot> second = snapshot => { };

      registry.SetSource(source);
      registry.Add(first);
      registry.Add(second);
      Assert.True(registry.IsAttached);
      Assert.Equal(2, registry.SubscriberCount);
      Assert.Equal(new[] { "attach" }, transitions);

      registry.Remove(first);
      Assert.True(registry.IsAttached);
      Assert.Equal(1, registry.SubscriberCount);
      Assert.Equal(new[] { "attach" }, transitions);

      registry.Remove(second);
      Assert.False(registry.IsAttached);
      Assert.Equal(0, registry.SubscriberCount);
      Assert.Equal(new[] { "attach", "detach" }, transitions);

      registry.Add(first);
      Assert.True(registry.IsAttached);
      Assert.Equal(1, registry.SubscriberCount);
      Assert.Equal(new[] { "attach", "detach", "attach" }, transitions);

      registry.Clear();
      Assert.False(registry.IsAttached);
      Assert.Equal(0, registry.SubscriberCount);
      Assert.Equal(
        new[] { "attach", "detach", "attach", "detach" },
        transitions);
    }

    [Fact]
    public void Boundary_registry_compensates_partial_attach_and_retries_same_source()
    {
      var transitions = new System.Collections.Generic.List<string>();
      var source = new object();
      var failure = new InvalidOperationException("partial attach failed");
      var received = new System.Collections.Generic.List<CurrentDocumentSnapshot>();
      int attachAttempts = 0;
      bool physicallyAttached = false;
      var registry = new NativeDocumentBoundarySubscriptionRegistry(
        value =>
        {
          attachAttempts++;
          physicallyAttached = true;
          transitions.Add("attach:" + attachAttempts);
          if (attachAttempts == 1) throw failure;
        },
        value =>
        {
          physicallyAttached = false;
          transitions.Add("detach");
        });
      Action<CurrentDocumentSnapshot> subscriber = snapshot =>
        received.Add(snapshot);

      registry.SetSource(source);
      Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
        registry.Add(subscriber)));

      Assert.False(physicallyAttached);
      Assert.False(registry.IsAttached);
      Assert.Same(source, registry.CurrentSource);
      Assert.Equal(1, registry.SubscriberCount);
      Assert.Equal(new[] { "attach:1", "detach" }, transitions);

      registry.SetSource(source);
      registry.SetSource(source);
      var snapshot = new CurrentDocumentSnapshot { DocumentTitle = "retry" };
      registry.Publish(snapshot);

      Assert.True(physicallyAttached);
      Assert.True(registry.IsAttached);
      Assert.Equal(new[] { "attach:1", "detach", "attach:2" }, transitions);
      Assert.Equal(new[] { snapshot }, received);

      registry.Clear();
      Assert.False(physicallyAttached);
      Assert.False(registry.IsAttached);
      Assert.Null(registry.CurrentSource);
      Assert.Equal(0, registry.SubscriberCount);
      Assert.Equal(
        new[] { "attach:1", "detach", "attach:2", "detach" },
        transitions);
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

    private sealed class FakeDocumentBoundarySource
      : INativeDocumentBoundarySource
    {
      internal int SubscriberCount =>
        DocumentBoundaryChanged?.GetInvocationList().Length ?? 0;

      public event Action<CurrentDocumentSnapshot> DocumentBoundaryChanged;

      internal void Raise(CurrentDocumentSnapshot snapshot)
      {
        DocumentBoundaryChanged?.Invoke(snapshot);
      }
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
