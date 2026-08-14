using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BIMBaoGui.McpContracts;
using Xunit;

namespace BIMBaoGui.McpContracts.Tests
{
  public sealed class BridgeFrameCodecTests
  {
    [Fact]
    public void Stage02_preview_command_carries_typed_semantic_assignment_inputs()
    {
      var command = new Stage02PreviewCommand
      {
        Scope = "current_selection",
        IdentificationMode = "manual",
        BulkRoleId = "SITE_GREEN_OBJECT",
        RoleOverrides = new[]
        {
          new Stage02RoleOverrideCommand
          {
            ElementUniqueId = "A",
            RoleId = "SITE_GREEN_OBJECT"
          }
        }
      };

      Assert.Equal("manual", command.IdentificationMode);
      Assert.Equal("SITE_GREEN_OBJECT", command.BulkRoleId);
      Stage02RoleOverrideCommand roleOverride = Assert.Single(
        command.RoleOverrides);
      Assert.Equal("A", roleOverride.ElementUniqueId);
      Assert.Equal("SITE_GREEN_OBJECT", roleOverride.RoleId);
      Assert.Equal(13, McpToolNames.Approved.Count);
      Assert.Equal(13, McpToolNames.Approved.Distinct().Count());
    }

    [Fact]
    public async Task RoundTripUsesFourByteLittleEndianLengthAndUtf8Json()
    {
      const string json = "{\"value\":\"中文\"}";
      using var stream = new MemoryStream();

      await BridgeFrameCodec.WriteJsonAsync(
        stream,
        json,
        BridgeProtocol.MaxRequestBytes,
        CancellationToken.None);

      byte[] bytes = stream.ToArray();
      int expectedLength = Encoding.UTF8.GetByteCount(json);
      Assert.Equal(expectedLength, bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24);

      stream.Position = 0;
      string result = await BridgeFrameCodec.ReadJsonAsync(
        stream,
        BridgeProtocol.MaxRequestBytes,
        CancellationToken.None);
      Assert.Equal(json, result);
      Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task OversizedFrameIsRejectedBeforePayloadAllocation()
    {
      using var stream = new MemoryStream();
      int length = BridgeProtocol.MaxRequestBytes + 1;
      stream.WriteByte((byte)(length & 0xff));
      stream.WriteByte((byte)((length >> 8) & 0xff));
      stream.WriteByte((byte)((length >> 16) & 0xff));
      stream.WriteByte((byte)((length >> 24) & 0xff));
      stream.Position = 0;

      BridgeProtocolException exception = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
        BridgeFrameCodec.ReadJsonAsync(
          stream,
          BridgeProtocol.MaxRequestBytes,
          CancellationToken.None));
      Assert.Equal(BridgeErrorCodes.MessageTooLarge, exception.ErrorCode);
    }

    [Fact]
    public async Task TruncatedHeaderAndPayloadFailClosed()
    {
      using var header = new MemoryStream(new byte[] { 1, 0, 0 });
      await Assert.ThrowsAsync<EndOfStreamException>(() =>
        BridgeFrameCodec.ReadJsonAsync(
          header,
          BridgeProtocol.MaxRequestBytes,
          CancellationToken.None));

      using var payload = new MemoryStream(new byte[] { 5, 0, 0, 0, (byte)'{', (byte)'}' });
      await Assert.ThrowsAsync<EndOfStreamException>(() =>
        BridgeFrameCodec.ReadJsonAsync(
          payload,
          BridgeProtocol.MaxRequestBytes,
          CancellationToken.None));
    }

    [Fact]
    public async Task WriteRejectsPayloadOverDeclaredLimit()
    {
      using var stream = new MemoryStream();
      string json = new string('x', 32);
      BridgeProtocolException exception = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
        BridgeFrameCodec.WriteJsonAsync(stream, json, 16, CancellationToken.None));
      Assert.Equal(BridgeErrorCodes.MessageTooLarge, exception.ErrorCode);
      Assert.Equal(0, stream.Length);
    }
  }
}
