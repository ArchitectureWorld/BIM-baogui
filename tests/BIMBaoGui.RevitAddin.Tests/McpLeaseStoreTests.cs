using System;
using BIMBaoGui.McpContracts;
using BIMBaoGui.RevitAddin.McpBridge;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class McpLeaseStoreTests
  {
    [Fact]
    public void LeaseCanBeReadThenConsumedExactlyOnce()
    {
      var clock = new FakeMcpClock(
        new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
      var store = new McpLeaseStore<string>(
        clock,
        TimeSpan.FromMinutes(30));

      store.Create("hash-1", "payload");

      Assert.Equal("payload", store.Get("hash-1"));
      Assert.Equal("payload", store.Consume("hash-1"));
      McpLeaseException exception = Assert.Throws<McpLeaseException>(() =>
        store.Consume("hash-1"));
      Assert.Equal(BridgeErrorCodes.LeaseNotFound, exception.ErrorCode);
    }

    [Fact]
    public void ExpiredLeaseFailsClosedAndIsRemoved()
    {
      var clock = new FakeMcpClock(
        new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
      var store = new McpLeaseStore<string>(
        clock,
        TimeSpan.FromMinutes(30));
      store.Create("hash-expired", "payload");
      clock.Advance(TimeSpan.FromMinutes(31));

      McpLeaseException expired = Assert.Throws<McpLeaseException>(() =>
        store.Get("hash-expired"));
      Assert.Equal(BridgeErrorCodes.LeaseExpired, expired.ErrorCode);

      McpLeaseException missing = Assert.Throws<McpLeaseException>(() =>
        store.Get("hash-expired"));
      Assert.Equal(BridgeErrorCodes.LeaseNotFound, missing.ErrorCode);
    }

    [Fact]
    public void DuplicateKeyReplacesOnlyAfterExplicitCreate()
    {
      var clock = new FakeMcpClock(DateTimeOffset.UtcNow);
      var store = new McpLeaseStore<string>(clock, TimeSpan.FromMinutes(30));

      store.Create("same-hash", "first");
      store.Create("same-hash", "second");

      Assert.Equal("second", store.Consume("same-hash"));
    }

    [Fact]
    public void EmptyKeysAndNullValuesAreRejected()
    {
      var store = new McpLeaseStore<string>(
        new FakeMcpClock(DateTimeOffset.UtcNow),
        TimeSpan.FromMinutes(30));

      Assert.Throws<ArgumentException>(() => store.Create(" ", "value"));
      Assert.Throws<ArgumentNullException>(() => store.Create("hash", null));
    }

    private sealed class FakeMcpClock : IMcpClock
    {
      internal FakeMcpClock(DateTimeOffset utcNow)
      {
        UtcNow = utcNow;
      }

      public DateTimeOffset UtcNow { get; private set; }

      internal void Advance(TimeSpan duration)
      {
        UtcNow = UtcNow.Add(duration);
      }
    }
  }
}
