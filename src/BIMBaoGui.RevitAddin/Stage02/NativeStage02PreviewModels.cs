using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal enum NativeStage02FieldStatus
  {
    Correct,
    PendingBinding,
    PendingWrite,
    PendingInput,
    NotApplicable,
    RuntimeBlocked,
    Blocked
  }

  internal enum NativeStage02BindingAction
  {
    None,
    Create,
    MergeCategories,
    Blocked
  }

  internal enum NativeStage02ValueAction
  {
    None,
    Keep,
    Set,
    PendingInput
  }

  internal static class NativeStage02AssignmentActions
  {
    internal const string None = "None";
    internal const string SaveManualAssignment = "SaveManualAssignment";
    internal const string KeepManualAssignment = "KeepManualAssignment";
    internal const string RemoveManualAssignment = "RemoveManualAssignment";
  }

  internal sealed class NativeStage02ParameterEvidence
  {
    internal Guid ParameterGuid { get; set; }
    internal bool Exists { get; set; }
    internal bool ContractCompatible { get; set; } = true;
    internal bool BindingIncludesCategory { get; set; }
    internal bool IsReadOnly { get; set; }
    internal string CurrentCanonicalValue { get; set; } = string.Empty;
    internal IDictionary<string, string> AliasValues { get; set; } =
      new Dictionary<string, string>(StringComparer.Ordinal);
    internal string ContractMessage { get; set; } = string.Empty;
  }

  internal sealed class NativeStage02ElementEvidence
  {
    internal NativeStage02ElementSnapshot Element { get; set; }
    internal NativeStage02RoleMatchResult AutomaticRoleMatch { get; set; }
    internal NativeStage02RoleMatchResult ResolvedRoleMatch { get; set; }
    internal NativeStage02AssignmentMode AssignmentMode { get; set; } =
      NativeStage02AssignmentMode.Auto;
    internal string AssignmentSource { get; set; } = string.Empty;
    internal string AssignmentAction { get; set; } =
      NativeStage02AssignmentActions.None;
    internal string ManualCarrierEvidence { get; set; } = string.Empty;
    internal IDictionary<Guid, NativeStage02ParameterEvidence> Parameters
    {
      get;
      set;
    } = new Dictionary<Guid, NativeStage02ParameterEvidence>();
  }

  internal sealed class NativeStage02PreviewInput
  {
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal string ModelProfile { get; set; } = string.Empty;
    internal NativeStage02IdentificationMode IdentificationMode { get; set; } =
      NativeStage02IdentificationMode.Automatic;
    internal string BulkRoleId { get; set; } = string.Empty;
    internal IReadOnlyList<NativeStage02RoleOverride> RoleOverrides { get; set; } =
      Array.Empty<NativeStage02RoleOverride>();
    internal IDictionary<string, bool> Conditions { get; set; } =
      new Dictionary<string, bool>(StringComparer.Ordinal);
    internal IEnumerable<NativeStage02ElementEvidence> Elements { get; set; } =
      Array.Empty<NativeStage02ElementEvidence>();
  }

  internal sealed class NativeStage02FieldPlan
  {
    internal NativeStage02PropertyDefinition Property { get; set; }
    internal NativeStage02FieldStatus Status { get; set; }
    internal NativeStage02BindingAction BindingAction { get; set; }
    internal NativeStage02ValueAction ValueAction { get; set; }
    internal string CurrentCanonicalValue { get; set; } = string.Empty;
    internal string ProposedCanonicalValue { get; set; } = string.Empty;
    internal string ValueSource { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
    internal bool StrictExportReady { get; set; }
  }

  internal sealed class NativeStage02ElementPlan
  {
    internal NativeStage02ElementSnapshot Element { get; set; }
    internal NativeStage02RoleMatchStatus RoleMatchStatus { get; set; }
    internal NativeStage02RoleMatchStatus AutomaticRoleStatus { get; set; }
    internal string AutomaticRoleId { get; set; } = string.Empty;
    internal string RoleId { get; set; } = string.Empty;
    internal string EffectiveRoleId { get; set; } = string.Empty;
    internal string RoleMatchSource { get; set; } = string.Empty;
    internal NativeStage02AssignmentMode AssignmentMode { get; set; } =
      NativeStage02AssignmentMode.Auto;
    internal string AssignmentSource { get; set; } = string.Empty;
    internal string AssignmentAction { get; set; } =
      NativeStage02AssignmentActions.None;
    internal string ManualCarrierEvidence { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
    internal IReadOnlyList<NativeStage02FieldPlan> Fields { get; set; } =
      Array.Empty<NativeStage02FieldPlan>();

    internal bool IsBlocked =>
      RoleMatchStatus != NativeStage02RoleMatchStatus.Matched
      || Fields.Any(value => value.Status == NativeStage02FieldStatus.Blocked);

    internal bool HasActionableWork =>
      AssignmentAction == NativeStage02AssignmentActions.SaveManualAssignment
      || AssignmentAction == NativeStage02AssignmentActions.RemoveManualAssignment
      || Fields.Any(value =>
        value.BindingAction == NativeStage02BindingAction.Create
        || value.BindingAction == NativeStage02BindingAction.MergeCategories
        || value.ValueAction == NativeStage02ValueAction.Set);
  }

  internal sealed class NativeStage02Preview
  {
    internal string SchemaVersion { get; set; } =
      "HBR_NATIVE_STAGE02_PREVIEW_V2";
    internal string RulePackageId { get; set; } = string.Empty;
    internal string RulePackageVersion { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal string ModelProfile { get; set; } = string.Empty;
    internal NativeStage02IdentificationMode IdentificationMode { get; set; } =
      NativeStage02IdentificationMode.Automatic;
    internal string BulkRoleId { get; set; } = string.Empty;
    internal IReadOnlyList<NativeStage02RoleOverride> RoleOverrides { get; set; } =
      Array.Empty<NativeStage02RoleOverride>();
    internal IReadOnlyDictionary<string, bool> Conditions { get; set; } =
      new ReadOnlyDictionary<string, bool>(
        new Dictionary<string, bool>(StringComparer.Ordinal));
    internal IReadOnlyList<NativeStage02ElementPlan> Elements { get; set; } =
      Array.Empty<NativeStage02ElementPlan>();
    internal string CanonicalJson { get; set; } = string.Empty;
    internal string PreviewHash { get; set; } = string.Empty;
    internal int BlockedElementCount { get; set; }
    internal int ActionableElementCount { get; set; }
    internal int CorrectFieldCount { get; set; }
    internal int PendingBindingFieldCount { get; set; }
    internal int PendingWriteFieldCount { get; set; }
    internal int PendingInputFieldCount { get; set; }
    internal int RuntimeBlockedFieldCount { get; set; }
  }
}
