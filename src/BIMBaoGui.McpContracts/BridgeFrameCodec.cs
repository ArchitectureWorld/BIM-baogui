using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BIMBaoGui.McpContracts
{
  public static class BridgeFrameCodec
  {
    private static readonly UTF8Encoding StrictUtf8 =
      new UTF8Encoding(false, true);

    public static async Task WriteJsonAsync(
      Stream stream,
      string json,
      int maximumBytes,
      CancellationToken cancellationToken)
    {
      if (stream == null) throw new ArgumentNullException(nameof(stream));
      if (!stream.CanWrite)
        throw new ArgumentException("The stream is not writable.", nameof(stream));
      if (maximumBytes <= 0)
        throw new ArgumentOutOfRangeException(nameof(maximumBytes));

      byte[] payload;
      try
      {
        payload = StrictUtf8.GetBytes(json ?? string.Empty);
      }
      catch (EncoderFallbackException exception)
      {
        throw new BridgeProtocolException(
          BridgeErrorCodes.InvalidUtf8,
          "The bridge JSON payload is not valid UTF-8 text.",
          exception);
      }

      ValidateLength(payload.Length, maximumBytes);
      byte[] header =
      {
        (byte)(payload.Length & 0xff),
        (byte)((payload.Length >> 8) & 0xff),
        (byte)((payload.Length >> 16) & 0xff),
        (byte)((payload.Length >> 24) & 0xff)
      };

      await stream.WriteAsync(
        header,
        0,
        header.Length,
        cancellationToken).ConfigureAwait(false);
      await stream.WriteAsync(
        payload,
        0,
        payload.Length,
        cancellationToken).ConfigureAwait(false);
      await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> ReadJsonAsync(
      Stream stream,
      int maximumBytes,
      CancellationToken cancellationToken)
    {
      if (stream == null) throw new ArgumentNullException(nameof(stream));
      if (!stream.CanRead)
        throw new ArgumentException("The stream is not readable.", nameof(stream));
      if (maximumBytes <= 0)
        throw new ArgumentOutOfRangeException(nameof(maximumBytes));

      var header = new byte[BridgeProtocol.HeaderBytes];
      await ReadExactlyAsync(
        stream,
        header,
        cancellationToken).ConfigureAwait(false);
      int length = header[0]
        | header[1] << 8
        | header[2] << 16
        | header[3] << 24;
      ValidateLength(length, maximumBytes);

      var payload = new byte[length];
      await ReadExactlyAsync(
        stream,
        payload,
        cancellationToken).ConfigureAwait(false);
      try
      {
        return StrictUtf8.GetString(payload);
      }
      catch (DecoderFallbackException exception)
      {
        throw new BridgeProtocolException(
          BridgeErrorCodes.InvalidUtf8,
          "The bridge frame contains invalid UTF-8 bytes.",
          exception);
      }
    }

    private static void ValidateLength(int length, int maximumBytes)
    {
      if (length <= 0)
      {
        throw new BridgeProtocolException(
          BridgeErrorCodes.InvalidFrame,
          "The bridge frame payload length must be positive.");
      }
      if (length > maximumBytes)
      {
        throw new BridgeProtocolException(
          BridgeErrorCodes.MessageTooLarge,
          "The bridge frame exceeds the configured message limit.");
      }
    }

    private static async Task ReadExactlyAsync(
      Stream stream,
      byte[] buffer,
      CancellationToken cancellationToken)
    {
      int offset = 0;
      while (offset < buffer.Length)
      {
        int read = await stream.ReadAsync(
          buffer,
          offset,
          buffer.Length - offset,
          cancellationToken).ConfigureAwait(false);
        if (read == 0)
          throw new EndOfStreamException("The bridge frame ended unexpectedly.");
        offset += read;
      }
    }
  }
}
