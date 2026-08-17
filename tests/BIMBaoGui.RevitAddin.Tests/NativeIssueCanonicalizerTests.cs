using System;
using BIMBaoGui.RevitAddin.Issues;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeIssueCanonicalizerTests
  {
    [Fact]
    public void IssueIdUsesFieldIdentityButNotMessageText()
    {
      NativeIssueRecord first = Issue(
        "MISSING_FIELD",
        "旧文案",
        "HBR|FileIdentity|FileGuid");
      NativeIssueRecord second = Issue(
        "MISSING_FIELD",
        "新文案",
        "HBR|FileIdentity|FileGuid");
      NativeIssueRecord other = Issue(
        "MISSING_FIELD",
        "旧文案",
        "HBR|FileIdentity|ModelFileType");

      Assert.Equal(
        NativeIssueCanonicalizer.ComputeId(first),
        NativeIssueCanonicalizer.ComputeId(second));
      Assert.NotEqual(
        NativeIssueCanonicalizer.ComputeId(first),
        NativeIssueCanonicalizer.ComputeId(other));
      Assert.Matches("^[0-9a-f]{64}$", NativeIssueCanonicalizer.ComputeId(first));
    }

    [Fact]
    public void IssueIdSortsUniqueIdsAndRejectsMissingStableIdentity()
    {
      NativeIssueRecord first = Issue("CODE", "text", "field");
      first.Elements = new[]
      {
        new NativeIssueElementReference { UniqueId = "B", ElementId = 2 },
        new NativeIssueElementReference { UniqueId = "A", ElementId = 1 }
      };
      NativeIssueRecord second = Issue("CODE", "different", "field");
      second.Elements = new[]
      {
        new NativeIssueElementReference { UniqueId = "A", ElementId = 999 },
        new NativeIssueElementReference { UniqueId = "B", ElementId = 888 }
      };
      Assert.Equal(
        NativeIssueCanonicalizer.ComputeId(first),
        NativeIssueCanonicalizer.ComputeId(second));

      first.DocumentFingerprint = string.Empty;
      Assert.Throws<ArgumentException>(() => NativeIssueCanonicalizer.ComputeId(first));
      first.DocumentFingerprint = "TOTAL_PLAN";
      first.Elements = new[]
      {
        new NativeIssueElementReference { UniqueId = string.Empty, ElementId = 1 }
      };
      Assert.Throws<ArgumentException>(() => NativeIssueCanonicalizer.ComputeId(first));
    }

    private static NativeIssueRecord Issue(
      string code,
      string message,
      string fieldKey)
    {
      return new NativeIssueRecord
      {
        DocumentFingerprint = "TOTAL_PLAN",
        Severity = NativeIssueSeverity.Blocker,
        SourceFeature = "STAGE01",
        CheckId = "STAGE01.FIELD",
        Code = code,
        Missing = message,
        FieldKey = fieldKey,
        Route = NativeIssueNavigationAction.OpenStage01
      };
    }
  }
}
