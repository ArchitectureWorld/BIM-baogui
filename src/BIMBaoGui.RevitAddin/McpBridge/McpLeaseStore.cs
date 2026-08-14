using System;
using System.Collections.Generic;
using BIMBaoGui.McpContracts;

namespace BIMBaoGui.RevitAddin.McpBridge
{
  internal interface IMcpClock
  {
    DateTimeOffset UtcNow { get; }
  }

  internal sealed class SystemMcpClock : IMcpClock
  {
    internal static readonly SystemMcpClock Instance = new SystemMcpClock();

    private SystemMcpClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
  }

  internal sealed class McpLeaseException : Exception
  {
    internal McpLeaseException(string errorCode, string message)
      : base(message)
    {
      ErrorCode = errorCode ?? BridgeErrorCodes.LeaseNotFound;
    }

    internal string ErrorCode { get; }
  }

  internal sealed class McpLeaseStore<T> where T : class
  {
    private readonly object _syncRoot = new object();
    private readonly Dictionary<string, LeaseEntry> _leases =
      new Dictionary<string, LeaseEntry>(StringComparer.Ordinal);
    private readonly IMcpClock _clock;
    private readonly TimeSpan _lifetime;

    internal McpLeaseStore(IMcpClock clock, TimeSpan lifetime)
    {
      _clock = clock ?? throw new ArgumentNullException(nameof(clock));
      if (lifetime <= TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(nameof(lifetime));
      _lifetime = lifetime;
    }

    internal void Create(string key, T value)
    {
      string normalizedKey = NormalizeKey(key);
      if (ReferenceEquals(value, null))
        throw new ArgumentNullException(nameof(value));

      lock (_syncRoot)
      {
        PurgeExpiredExcept(null);
        _leases[normalizedKey] = new LeaseEntry
        {
          Value = value,
          ExpiresUtc = _clock.UtcNow.Add(_lifetime)
        };
      }
    }

    internal T Get(string key)
    {
      string normalizedKey = NormalizeKey(key);
      lock (_syncRoot)
      {
        LeaseEntry entry = FindValidEntry(normalizedKey);
        return entry.Value;
      }
    }

    internal T Consume(string key)
    {
      string normalizedKey = NormalizeKey(key);
      lock (_syncRoot)
      {
        LeaseEntry entry = FindValidEntry(normalizedKey);
        _leases.Remove(normalizedKey);
        return entry.Value;
      }
    }

    internal void Clear()
    {
      lock (_syncRoot)
        _leases.Clear();
    }

    private LeaseEntry FindValidEntry(string key)
    {
      if (!_leases.TryGetValue(key, out LeaseEntry entry))
      {
        PurgeExpiredExcept(null);
        throw new McpLeaseException(
          BridgeErrorCodes.LeaseNotFound,
          "找不到对应的 MCP 确认租约，请重新执行只读预览或校验。" );
      }
      if (entry.ExpiresUtc <= _clock.UtcNow)
      {
        _leases.Remove(key);
        PurgeExpiredExcept(null);
        throw new McpLeaseException(
          BridgeErrorCodes.LeaseExpired,
          "MCP 确认租约已过期，请重新执行只读预览或校验。" );
      }
      PurgeExpiredExcept(key);
      return entry;
    }

    private void PurgeExpiredExcept(string preservedKey)
    {
      DateTimeOffset now = _clock.UtcNow;
      var expired = new List<string>();
      foreach (KeyValuePair<string, LeaseEntry> pair in _leases)
      {
        if (!string.Equals(pair.Key, preservedKey, StringComparison.Ordinal)
          && pair.Value.ExpiresUtc <= now)
          expired.Add(pair.Key);
      }
      foreach (string key in expired) _leases.Remove(key);
    }

    private static string NormalizeKey(string key)
    {
      if (string.IsNullOrWhiteSpace(key))
        throw new ArgumentException("MCP 租约键不能为空。", nameof(key));
      return key.Trim();
    }

    private sealed class LeaseEntry
    {
      internal T Value { get; set; }
      internal DateTimeOffset ExpiresUtc { get; set; }
    }
  }
}
