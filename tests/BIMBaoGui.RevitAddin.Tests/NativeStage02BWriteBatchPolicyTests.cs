using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Stage02B;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02BWriteBatchPolicyTests
  {
    [Fact]
    public void One_metric_failure_preserves_other_successes()
    {
      NativeStage02BWriteBatchDecision decision =
        NativeStage02BWriteBatchPolicy.Merge(new[]
        {
          new NativeStage02BMetricOutcome
          {
            PropertyId = "A", Succeeded = true
          },
          new NativeStage02BMetricOutcome
          {
            PropertyId = "B", Succeeded = false,
            ErrorCode = "READBACK_FAILED"
          },
          new NativeStage02BMetricOutcome
          {
            PropertyId = "C", Succeeded = true
          }
        });

      Assert.Equal(new[] { "A", "C" }, decision.SuccessfulPropertyIds);
      Assert.Equal(new[] { "B" }, decision.FailedPropertyIds);
      Assert.True(decision.PartialSuccess);
    }

    [Fact]
    public void Retry_request_contains_only_latest_failed_metrics()
    {
      var last = new NativeStage02BWriteResult
      {
        RunId = "run-1",
        FailedPropertyIds = new[] { "B", "D" }
      };
      NativeStage02BMetricInput[] inputs =
      {
        new NativeStage02BMetricInput { PropertyId = "A", RawValue = "1" },
        new NativeStage02BMetricInput { PropertyId = "B", RawValue = "2" },
        new NativeStage02BMetricInput { PropertyId = "D", RawValue = "4" }
      };

      NativeStage02BWriteRequest retry = NativeStage02BWriteBatchPolicy
        .BuildRetry(last, inputs);

      Assert.Equal(
        new[] { "B", "D" },
        retry.Metrics.Select(value => value.PropertyId));
    }

    [Fact]
    public void Retry_follows_metric_catalog_order_not_failure_order()
    {
      var last = new NativeStage02BWriteResult
      {
        RunId = "run-1",
        FailedPropertyIds = new[]
        {
          "84df74c2-a7e5-5a98-a5e0-4458e49a3973",
          "93e51676-237e-56a8-8f28-2da845422e2e"
        }
      };
      NativeStage02BMetricInput[] inputs =
      {
        new NativeStage02BMetricInput
        {
          PropertyId = "84df74c2-a7e5-5a98-a5e0-4458e49a3973",
          RawValue = "84"
        },
        new NativeStage02BMetricInput
        {
          PropertyId = "93e51676-237e-56a8-8f28-2da845422e2e",
          RawValue = "93"
        }
      };

      NativeStage02BWriteRequest retry = NativeStage02BWriteBatchPolicy
        .BuildRetry(last, inputs);

      Assert.Equal(
        new[]
        {
          "93e51676-237e-56a8-8f28-2da845422e2e",
          "84df74c2-a7e5-5a98-a5e0-4458e49a3973"
        },
        retry.Metrics.Select(value => value.PropertyId));
      Assert.NotEqual(last.RunId, retry.RunId);
      Assert.Equal(retry.Metrics.Select(value => value.PropertyId),
        retry.PropertyIdsToRetry);
    }

    [Fact]
    public void Retry_rejects_a_failed_metric_without_current_input()
    {
      var last = new NativeStage02BWriteResult
      {
        FailedPropertyIds = new[]
        {
          "93e51676-237e-56a8-8f28-2da845422e2e"
        }
      };

      InvalidOperationException error = Assert.Throws<InvalidOperationException>(
        () => NativeStage02BWriteBatchPolicy.BuildRetry(
          last,
          new[]
          {
            new NativeStage02BMetricInput
            {
              PropertyId = "ca21e324-046b-5bfd-84c8-0d3470082303",
              RawValue = "1"
            }
          }));

      Assert.Equal("RETRY_INPUT_MISSING", error.Message);
    }
  }
}
