using BIMBaoGui.Stage01.Context;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HBRLiveContextPolicyTests
  {
    [Fact]
    public void Validate_MatchingLiveIdentityHasNoBlockers()
    {
      Assert.Empty(Validate());
    }

    [Fact]
    public void Validate_DifferentPayloadHashIsBlocked()
    {
      Assert.Contains(
        HBRLiveContextPolicy.Validate(
          "file-guid",
          "ABC123",
          true,
          "file-guid",
          "OTHER",
          HBRContextVersions.FileContextSchema),
        message => message.Contains("载荷哈希"));
    }

    [Fact]
    public void Validate_DifferentFileGuidIsBlocked()
    {
      Assert.Contains(
        HBRLiveContextPolicy.Validate(
          "file-guid",
          "ABC123",
          true,
          "other-guid",
          "ABC123",
          HBRContextVersions.FileContextSchema),
        message => message.Contains("文件唯一 ID"));
    }

    [Fact]
    public void Validate_MissingLiveInitializationIsBlockedWithoutMismatchNoise()
    {
      var blockers = HBRLiveContextPolicy.Validate(
        "file-guid",
        "ABC123",
        false,
        string.Empty,
        string.Empty,
        string.Empty);

      Assert.Single(blockers);
      Assert.Contains("没有有效的 Stage01 初始化记录", blockers[0]);
    }

    [Fact]
    public void Validate_ShaAndGuidComparisonIsCaseInsensitive()
    {
      Assert.Empty(HBRLiveContextPolicy.Validate(
        "FILE-GUID",
        "ABCDEF",
        true,
        "file-guid",
        "abcdef",
        HBRContextVersions.FileContextSchema));
    }

    [Fact]
    public void Validate_DifferentWorkflowVersionIsBlocked()
    {
      Assert.Contains(
        HBRLiveContextPolicy.Validate(
          "file-guid",
          "ABC123",
          true,
          "file-guid",
          "ABC123",
          "0.8.0"),
        message => message.Contains("工作流版本"));
    }

    private static System.Collections.Generic.IReadOnlyList<string> Validate()
    {
      return HBRLiveContextPolicy.Validate(
        "file-guid",
        "ABC123",
        true,
        "file-guid",
        "ABC123",
        HBRContextVersions.FileContextSchema);
    }
  }
}
