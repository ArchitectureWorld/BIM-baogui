using System;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03ComponentStatePolicyTests
  {
    [Fact]
    public void Strict_and_force_mapping_are_exact_and_force_requires_reason()
    {
      Assert.Equal(
        Stage03GateMode.Strict,
        Stage03ComponentStatePolicy.ResolveMode(true));
      Assert.Equal(
        Stage03GateMode.Force,
        Stage03ComponentStatePolicy.ResolveMode(false));

      var state = new Stage03ComponentStatePolicy();
      Stage03ComponentInputSignature force = Signature(
        mode: Stage03GateMode.Force,
        forceReason: " \t ");
      state.UpdateSignature(force);

      Assert.False(state.TryBegin(
        force,
        out Stage03ComponentRunToken token,
        out string error));
      Assert.Null(token);
      Assert.Contains("强制原因", error);

      Stage03ComponentInputSignature strict = Signature(
        mode: Stage03GateMode.Strict,
        forceReason: " \t ");
      state.UpdateSignature(strict);
      Assert.True(state.TryBegin(strict, out token, out error));
      Assert.NotNull(token);
      Assert.Empty(error);
    }

    [Fact]
    public void Execute_is_recognized_only_on_false_to_true_edge()
    {
      var state = new Stage03ComponentStatePolicy();

      Assert.False(state.ObserveExecution(false));
      Assert.True(state.ObserveExecution(true));
      Assert.False(state.ObserveExecution(true));
      Assert.False(state.ObserveExecution(false));
      Assert.True(state.ObserveExecution(true));
    }

    [Theory]
    [InlineData("context")]
    [InlineData("output")]
    [InlineData("mode")]
    [InlineData("reason")]
    [InlineData("path")]
    [InlineData("fingerprint")]
    public void Every_signature_input_change_invalidates_previous_generation(
      string changedField)
    {
      var state = new Stage03ComponentStatePolicy();
      Stage03ComponentInputSignature original = Signature();
      Assert.True(state.UpdateSignature(original));
      long originalGeneration = state.Generation;
      Assert.False(state.UpdateSignature(Signature()));
      Assert.Equal(originalGeneration, state.Generation);

      Assert.True(state.UpdateSignature(ChangedSignature(changedField)));
      Assert.True(state.Generation > originalGeneration);
    }

    [Fact]
    public void Original_force_reason_whitespace_change_is_signature_change()
    {
      var state = new Stage03ComponentStatePolicy();
      Stage03ComponentInputSignature first = Signature(
        mode: Stage03GateMode.Force,
        forceReason: "reason");
      Stage03ComponentInputSignature whitespaceVariant = Signature(
        mode: Stage03GateMode.Force,
        forceReason: " reason ");

      Assert.True(state.UpdateSignature(first));
      Assert.True(state.UpdateSignature(whitespaceVariant));
      Assert.NotEqual(first, whitespaceVariant);
    }

    [Fact]
    public void Generation_and_signature_reject_late_completion_after_aba()
    {
      var state = new Stage03ComponentStatePolicy();
      Stage03ComponentInputSignature signatureA = Signature();
      Stage03ComponentInputSignature signatureB = Signature(
        outputDirectory: @"C:\output-b");
      state.UpdateSignature(signatureA);
      Assert.True(state.TryBegin(
        signatureA,
        out Stage03ComponentRunToken oldA,
        out string firstError));
      Assert.Empty(firstError);

      state.UpdateSignature(signatureB);
      state.UpdateSignature(Signature());
      Assert.False(state.TryPublish(oldA));

      Assert.True(state.TryBegin(
        Signature(),
        out Stage03ComponentRunToken currentA,
        out string secondError));
      Assert.Empty(secondError);
      Assert.True(currentA.Generation > oldA.Generation);
      Assert.False(state.TryPublish(oldA));
      Assert.True(state.TryPublish(currentA));
    }

    private static Stage03ComponentInputSignature ChangedSignature(
      string changedField)
    {
      switch (changedField)
      {
        case "context": return Signature(contextHash: "context-b");
        case "output": return Signature(outputDirectory: @"C:\output-b");
        case "mode": return Signature(mode: Stage03GateMode.Force);
        case "reason": return Signature(forceReason: "different");
        case "path": return Signature(documentPath: @"C:\models\b.rvt");
        case "fingerprint": return Signature(fingerprint: "fingerprint-b");
        default: throw new ArgumentOutOfRangeException(nameof(changedField));
      }
    }

    private static Stage03ComponentInputSignature Signature(
      string contextHash = "context-a",
      string outputDirectory = @"C:\output-a",
      Stage03GateMode mode = Stage03GateMode.Strict,
      string forceReason = "",
      string documentPath = @"C:\models\a.rvt",
      string fingerprint = "fingerprint-a")
    {
      return new Stage03ComponentInputSignature(
        contextHash,
        outputDirectory,
        mode,
        forceReason,
        documentPath,
        fingerprint);
    }
  }
}
