using System;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class Stage03ComponentInputSignature
    : IEquatable<Stage03ComponentInputSignature>
  {
    internal Stage03ComponentInputSignature(
      string fileContextHash,
      string outputDirectory,
      Stage03GateMode mode,
      string originalForceReason,
      string liveDocumentPath,
      string liveDocumentFingerprint)
    {
      FileContextHash = fileContextHash ?? string.Empty;
      OutputDirectory = outputDirectory ?? string.Empty;
      Mode = mode;
      OriginalForceReason = originalForceReason ?? string.Empty;
      LiveDocumentPath = liveDocumentPath ?? string.Empty;
      LiveDocumentFingerprint = liveDocumentFingerprint ?? string.Empty;
    }

    internal string FileContextHash { get; }
    internal string OutputDirectory { get; }
    internal Stage03GateMode Mode { get; }
    internal string OriginalForceReason { get; }
    internal string LiveDocumentPath { get; }
    internal string LiveDocumentFingerprint { get; }

    public bool Equals(Stage03ComponentInputSignature other)
    {
      return other != null
        && string.Equals(
          FileContextHash,
          other.FileContextHash,
          StringComparison.Ordinal)
        && string.Equals(
          OutputDirectory,
          other.OutputDirectory,
          StringComparison.Ordinal)
        && Mode == other.Mode
        && string.Equals(
          OriginalForceReason,
          other.OriginalForceReason,
          StringComparison.Ordinal)
        && string.Equals(
          LiveDocumentPath,
          other.LiveDocumentPath,
          StringComparison.Ordinal)
        && string.Equals(
          LiveDocumentFingerprint,
          other.LiveDocumentFingerprint,
          StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
      return Equals(obj as Stage03ComponentInputSignature);
    }

    public override int GetHashCode()
    {
      unchecked
      {
        int hash = StringComparer.Ordinal.GetHashCode(FileContextHash);
        hash = hash * 397
          ^ StringComparer.Ordinal.GetHashCode(OutputDirectory);
        hash = hash * 397 ^ (int)Mode;
        hash = hash * 397
          ^ StringComparer.Ordinal.GetHashCode(OriginalForceReason);
        hash = hash * 397
          ^ StringComparer.Ordinal.GetHashCode(LiveDocumentPath);
        hash = hash * 397
          ^ StringComparer.Ordinal.GetHashCode(LiveDocumentFingerprint);
        return hash;
      }
    }
  }

  internal sealed class Stage03ComponentRunToken
  {
    internal Stage03ComponentRunToken(
      long generation,
      Stage03ComponentInputSignature signature)
    {
      Generation = generation;
      Signature = signature
        ?? throw new ArgumentNullException(nameof(signature));
    }

    internal long Generation { get; }
    internal Stage03ComponentInputSignature Signature { get; }
  }

  internal sealed class Stage03ComponentStatePolicy
  {
    private Stage03ComponentInputSignature _signature;
    private bool _previousExecute;
    private long _generation;

    internal long Generation => _generation;

    internal static Stage03GateMode ResolveMode(bool allMustPass)
    {
      return allMustPass
        ? Stage03GateMode.Strict
        : Stage03GateMode.Force;
    }

    internal bool ObserveExecution(bool execute)
    {
      bool risingEdge = execute && !_previousExecute;
      _previousExecute = execute;
      return risingEdge;
    }

    internal bool UpdateSignature(Stage03ComponentInputSignature signature)
    {
      if (signature == null)
        throw new ArgumentNullException(nameof(signature));
      if (signature.Equals(_signature)) return false;
      _signature = signature;
      AdvanceGeneration();
      return true;
    }

    internal bool TryBegin(
      Stage03ComponentInputSignature signature,
      out Stage03ComponentRunToken token,
      out string error)
    {
      token = null;
      error = string.Empty;
      if (signature == null)
      {
        error = "Stage03 运行输入签名不能为空。";
        return false;
      }
      if (signature.Mode == Stage03GateMode.Force
        && string.IsNullOrWhiteSpace(signature.OriginalForceReason))
      {
        error = "Force 模式必须填写非空强制原因。";
        return false;
      }
      UpdateSignature(signature);
      AdvanceGeneration();
      token = new Stage03ComponentRunToken(_generation, signature);
      return true;
    }

    internal bool TryPublish(Stage03ComponentRunToken token)
    {
      return token != null
        && token.Generation == _generation
        && token.Signature.Equals(_signature);
    }

    private void AdvanceGeneration()
    {
      if (_generation == long.MaxValue)
        throw new InvalidOperationException(
          "Stage03 component generation 已耗尽。");
      _generation++;
    }
  }
}
