using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BIMBaoGui.McpContracts;

namespace BIMBaoGui.RevitAddin.McpBridge
{
  internal sealed class McpNamedPipeServer : IDisposable
  {
    private readonly object _syncRoot = new object();
    private readonly string _pipeName;
    private readonly string _sessionToken;
    private readonly McpBridgeCommandRouter _router;
    private readonly CancellationTokenSource _stop =
      new CancellationTokenSource();
    private Task _listenerTask;
    private NamedPipeServerStream _activeServer;

    internal McpNamedPipeServer(
      string pipeName,
      string sessionToken,
      McpBridgeCommandRouter router)
    {
      if (string.IsNullOrWhiteSpace(pipeName))
        throw new ArgumentException("Pipe name 不能为空。", nameof(pipeName));
      if (string.IsNullOrWhiteSpace(sessionToken))
        throw new ArgumentException("Session token 不能为空。", nameof(sessionToken));
      _pipeName = pipeName;
      _sessionToken = sessionToken;
      _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    internal void Start()
    {
      lock (_syncRoot)
      {
        if (_listenerTask != null) return;
        _listenerTask = Task.Factory.StartNew(
          ListenLoop,
          CancellationToken.None,
          TaskCreationOptions.LongRunning,
          TaskScheduler.Default);
      }
    }

    internal void Stop()
    {
      _stop.Cancel();
      lock (_syncRoot)
      {
        try { _activeServer?.Dispose(); }
        catch { }
      }
      try { _listenerTask?.Wait(TimeSpan.FromSeconds(3)); }
      catch { }
    }

    public void Dispose()
    {
      Stop();
      _stop.Dispose();
    }

    private void ListenLoop()
    {
      while (!_stop.IsCancellationRequested)
      {
        NamedPipeServerStream server = null;
        try
        {
          server = CreateServer();
          lock (_syncRoot) _activeServer = server;
          server.WaitForConnection();
          HandleConnection(server);
        }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
        {
          return;
        }
        catch (IOException) when (_stop.IsCancellationRequested)
        {
          return;
        }
        catch
        {
          if (_stop.IsCancellationRequested) return;
          Thread.Sleep(100);
        }
        finally
        {
          lock (_syncRoot)
          {
            if (ReferenceEquals(_activeServer, server)) _activeServer = null;
          }
          try { server?.Dispose(); }
          catch { }
        }
      }
    }

    private void HandleConnection(NamedPipeServerStream server)
    {
      BridgeRequest request = null;
      BridgeResponse response;
      try
      {
        string requestJson = BridgeFrameCodec.ReadJsonAsync(
          server,
          BridgeProtocol.MaxRequestBytes,
          _stop.Token).GetAwaiter().GetResult();
        request = McpBridgeJson.DeserializeRequest(requestJson);
        ValidateRequest(request);
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(
          _stop.Token))
        {
          timeout.CancelAfter(request.TimeoutMs);
          response = _router.RouteAsync(request, timeout.Token)
            .GetAwaiter().GetResult();
        }
      }
      catch (BridgeProtocolException exception)
      {
        response = BridgeResponse.Failure(
          request?.RequestId ?? string.Empty,
          exception.ErrorCode,
          exception.Message);
      }
      catch (McpCommandException exception)
      {
        response = BridgeResponse.Failure(
          request?.RequestId ?? string.Empty,
          exception.ErrorCode,
          exception.Message,
          exception.Status);
      }
      catch (OperationCanceledException)
      {
        response = BridgeResponse.Failure(
          request?.RequestId ?? string.Empty,
          BridgeErrorCodes.Timeout,
          "Bridge 请求超时。" );
      }
      catch (Exception exception)
      {
        response = BridgeResponse.Failure(
          request?.RequestId ?? string.Empty,
          BridgeErrorCodes.TechnicalFatal,
          "Bridge 请求处理失败：" + exception.Message);
      }
      if (!server.IsConnected) return;
      BridgeFrameCodec.WriteJsonAsync(
        server,
        McpBridgeJson.SerializeResponse(response),
        BridgeProtocol.MaxResponseBytes,
        _stop.Token).GetAwaiter().GetResult();
    }

    private void ValidateRequest(BridgeRequest request)
    {
      if (request == null)
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          "Bridge 请求为空。" );
      }
      if (!string.Equals(
        request.ProtocolVersion,
        BridgeProtocol.Version,
        StringComparison.Ordinal))
      {
        throw new McpCommandException(
          BridgeErrorCodes.ProtocolMismatch,
          "Bridge protocol version 不一致。" );
      }
      if (string.IsNullOrWhiteSpace(request.RequestId)
        || !Guid.TryParse(request.RequestId, out _))
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          "Bridge request_id 必须为 UUID。" );
      }
      if (!FixedTimeEquals(request.SessionToken, _sessionToken))
      {
        throw new McpCommandException(
          BridgeErrorCodes.AuthenticationFailed,
          "Bridge session token 无效。" );
      }
      if (string.IsNullOrWhiteSpace(request.Method))
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          "Bridge method 不能为空。" );
      }
      if (request.TimeoutMs < BridgeProtocol.MinimumTimeoutMs
        || request.TimeoutMs > BridgeProtocol.MaximumTimeoutMs)
      {
        throw new McpCommandException(
          BridgeErrorCodes.InvalidArgument,
          "Bridge timeout_ms 超出允许范围。" );
      }
    }

    private static NamedPipeServerStream CreateServer()
    {
      WindowsIdentity identity = WindowsIdentity.GetCurrent();
      SecurityIdentifier sid = identity.User
        ?? throw new InvalidOperationException("无法读取当前 Windows 用户 SID。" );
      var security = new PipeSecurity();
      security.SetAccessRuleProtection(true, false);
      security.AddAccessRule(new PipeAccessRule(
        sid,
        PipeAccessRights.FullControl,
        AccessControlType.Allow));
      return new NamedPipeServerStream(
        McpBridgeHost.PipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous,
        65536,
        65536,
        security);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
      byte[] leftBytes = System.Text.Encoding.UTF8.GetBytes(left ?? string.Empty);
      byte[] rightBytes = System.Text.Encoding.UTF8.GetBytes(right ?? string.Empty);
      int difference = leftBytes.Length ^ rightBytes.Length;
      int count = Math.Max(leftBytes.Length, rightBytes.Length);
      for (int index = 0; index < count; index++)
      {
        byte l = index < leftBytes.Length ? leftBytes[index] : (byte)0;
        byte r = index < rightBytes.Length ? rightBytes[index] : (byte)0;
        difference |= l ^ r;
      }
      return difference == 0;
    }
  }
}
