using System;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeWorkflowIdentityTests
  {
    [Fact]
    public void DocumentFingerprintUsesTheFrozenFivePartIdentityOrder()
    {
      string actual = NativeWorkflowIdentityFactory.ComputeDocumentFingerprint(
        @"C:\models\total.rvt",
        "总平验收",
        "2020",
        "11111111-1111-1111-1111-111111111111",
        new string('b', 64));

      Assert.Equal(
        "208c5061480f3ee7061e679011d221ee16af0eefda5e978b1b94c3aa44b05d7e",
        actual);
      Assert.Matches("^[0-9a-f]{64}$", actual);
    }

    [Theory]
    [InlineData("", "title", "2020", "file", "hash")]
    [InlineData("path", "", "2020", "file", "hash")]
    [InlineData("path", "title", "", "file", "hash")]
    [InlineData("path", "title", "2020", "", "hash")]
    [InlineData("path", "title", "2020", "file", "")]
    public void DocumentFingerprintRejectsIncompleteIdentityInputs(
      string path,
      string title,
      string version,
      string fileGuid,
      string payloadHash)
    {
      Assert.Throws<ArgumentException>(() =>
        NativeWorkflowIdentityFactory.ComputeDocumentFingerprint(
          path,
          title,
          version,
          fileGuid,
          payloadHash));
    }

  }
}
