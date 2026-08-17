using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeWorkflowResultCanonicalizerTests
  {
    [Fact]
    public void CanonicalResultIsOrderIndependentAndHashBound()
    {
      NativeWorkflowIdentity identity = TestIdentity("TOTAL_PLAN");
      NativeWorkflowResultEnvelope a = Build(
        identity,
        new[] { Item("B"), Item("A") });
      NativeWorkflowResultEnvelope b = Build(
        identity,
        new[] { Item("A"), Item("B") });

      Assert.Equal(a.CanonicalJson, b.CanonicalJson);
      Assert.Equal(a.ResultHash, b.ResultHash);
      Assert.Matches("^[0-9a-f]{64}$", a.ResultHash);
      Assert.Equal(new[] { "A", "B" }, a.Items.Select(value => value.Identity));
      Assert.All(a.Items, value => Assert.Matches(
        "^[0-9a-f]{64}$",
        value.StableHash));
    }

    [Fact]
    public void InputSnapshotAndItemValueBothBindTheResultHash()
    {
      NativeWorkflowIdentity identity = TestIdentity("TOTAL_PLAN");
      NativeWorkflowResultEnvelope baseline = Build(identity, new[] { Item("A") });
      NativeWorkflowItemEvidence changedItem = Item("A");
      changedItem.CurrentValue = "changed";
      NativeWorkflowResultEnvelope changedValue = Build(identity, new[] { changedItem });
      NativeWorkflowResultEnvelope changedInput = NativeWorkflowResultCanonicalizer.Build(
        "run-1",
        "TEST",
        "TEST_FUNCTION",
        identity,
        new string('d', 64),
        new[] { Item("A") },
        "2026-08-14T00:00:00.0000000Z");

      Assert.NotEqual(baseline.ResultHash, changedValue.ResultHash);
      Assert.NotEqual(baseline.ResultHash, changedInput.ResultHash);
    }

    [Fact]
    public void DuplicateOrEmptyItemIdentityIsRejected()
    {
      NativeWorkflowIdentity identity = TestIdentity("TOTAL_PLAN");
      Assert.Throws<ArgumentException>(() => Build(
        identity,
        new[] { Item("A"), Item("A") }));
      Assert.Throws<ArgumentException>(() => Build(
        identity,
        new[] { Item(string.Empty) }));
    }

    internal static NativeWorkflowIdentity TestIdentity(string document)
    {
      return new NativeWorkflowIdentity
      {
        DocumentFingerprint = document,
        ModelFileType = "总平模型",
        RulePackageId = "HBR-WUHAN-PLANNING",
        RulePackageVersion = "1.0.0",
        RulePackageSha256 = new string('a', 64)
      };
    }

    internal static NativeWorkflowItemEvidence Item(string identity)
    {
      return new NativeWorkflowItemEvidence
      {
        Identity = identity,
        CurrentValue = identity + "-value",
        Source = "TEST",
        WriteSucceeded = true,
        ReadbackSucceeded = true,
        InputHash = new string('c', 64),
        UpdatedUtc = "2026-08-14T00:00:00.0000000Z"
      };
    }

    internal static NativeWorkflowResultEnvelope Build(
      NativeWorkflowIdentity identity,
      IEnumerable<NativeWorkflowItemEvidence> items)
    {
      return NativeWorkflowResultCanonicalizer.Build(
        "run-1",
        "TEST",
        "TEST_FUNCTION",
        identity,
        new string('b', 64),
        items,
        "2026-08-14T00:00:00.0000000Z");
    }
  }
}
