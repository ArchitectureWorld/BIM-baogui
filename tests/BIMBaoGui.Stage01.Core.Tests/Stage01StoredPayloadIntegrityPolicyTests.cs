using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01StoredPayloadIntegrityPolicyTests
  {
    [Fact]
    public void Canonical_payload_with_matching_hash_is_verified()
    {
      Stage01Model model = Fixture();
      string payload = CanonicalPayload.Build(model);

      Stage01StoredPayloadIntegrityDecision decision =
        Stage01StoredPayloadIntegrityPolicy.Evaluate(
          payload,
          CanonicalPayload.Sha256(payload));

      Assert.True(decision.Success);
      Assert.Equal(payload, decision.CanonicalPayload);
      Assert.Equal(string.Empty, decision.ErrorCode);
    }

    [Fact]
    public void Canonical_but_tampered_payload_rejects_stale_hash()
    {
      Stage01Model model = Fixture();
      string original = CanonicalPayload.Build(model);
      string tampered = original.Replace("safe-value", "evil-value");

      Stage01StoredPayloadIntegrityDecision decision =
        Stage01StoredPayloadIntegrityPolicy.Evaluate(
          tampered,
          CanonicalPayload.Sha256(original));

      Assert.False(decision.Success);
      Assert.Equal("PAYLOAD_HASH_MISMATCH", decision.ErrorCode);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"values\":[]}")]
    public void Malformed_or_noncanonical_payload_is_corrupt(string payload)
    {
      Stage01StoredPayloadIntegrityDecision decision =
        Stage01StoredPayloadIntegrityPolicy.Evaluate(payload, new string('0', 64));

      Assert.False(decision.Success);
      Assert.Equal("CORRUPT_STAGE01_STORAGE", decision.ErrorCode);
    }

    private static Stage01Model Fixture()
    {
      var model = new Stage01Model();
      model.SetValue("test-key", "safe-value");
      return model;
    }
  }
}
