namespace BIMBaoGui.RevitAddin.Stage01
{
  internal enum NativeStage01FieldOperationState
  {
    NotAttempted,
    Succeeded,
    Failed,
    Blocked
  }

  internal sealed class NativeStage01FieldOutcome
  {
    internal string FieldKey { get; set; } = string.Empty;
    internal string Identity { get; set; } = string.Empty;
    internal string CurrentValue { get; set; } = string.Empty;
    internal string Unit { get; set; } = string.Empty;
    internal string Source { get; set; } = string.Empty;
    internal NativeStage01FieldOperationState WriteState { get; set; }
    internal NativeStage01FieldOperationState ReadbackState { get; set; }
    internal string ErrorCode { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }
}
