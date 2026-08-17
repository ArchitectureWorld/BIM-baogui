using System;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal static class NativeStage02BStoragePolicy
  {
    internal static NativeStage02BStorageSnapshot Merge(
      NativeStage02BStorageSnapshot current,
      NativeStage02BMetricRecord attempted)
    {
      if (attempted == null) throw new ArgumentNullException(nameof(attempted));
      NativeStage02BMetricRecord[] retained = (current?.Records ??
        Array.Empty<NativeStage02BMetricRecord>())
        .Where(value => value != null && !string.Equals(value.PropertyId,
          attempted.PropertyId, StringComparison.Ordinal))
        .ToArray();
      return NativeStage02BCanonicalizer.SealSnapshot(retained.Concat(
        new[] { attempted }));
    }
  }
}
