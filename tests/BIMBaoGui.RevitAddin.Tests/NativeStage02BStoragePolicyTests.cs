using System.Linq;
using BIMBaoGui.RevitAddin.Stage02B;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02BStoragePolicyTests
  {
    [Fact]
    public void MergeReplacesOnlyAttemptedPropertyAndRetainsOtherMetrics()
    {
      NativeStage02BStorageSnapshot current = NativeStage02BCanonicalizer.SealSnapshot(
        new[]
        {
          NativeStage02BCanonicalizerTests.Record("a"),
          NativeStage02BCanonicalizerTests.Record("b")
        });
      NativeStage02BMetricRecord attempted = NativeStage02BCanonicalizer.SealRecord(
        NativeStage02BCanonicalizerTests.Record("a"));
      attempted.RequestedCanonicalValue = "2.3";
      attempted = NativeStage02BCanonicalizer.SealRecord(attempted);

      NativeStage02BStorageSnapshot merged = NativeStage02BStoragePolicy.Merge(
        current, attempted);

      Assert.Equal(2, merged.Records.Count);
      Assert.Equal("2.3", merged.Records.Single(value => value.PropertyId == "a")
        .RequestedCanonicalValue);
      Assert.Equal("1.2", merged.Records.Single(value => value.PropertyId == "b")
        .RequestedCanonicalValue);
      Assert.NotEqual(current.SnapshotHash, merged.SnapshotHash);
    }
  }
}
