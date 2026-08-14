using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal enum NativeStage02ScopeMode
  {
    FullModel,
    CustomSelection
  }

  internal static class NativeStage02InventoryCodes
  {
    internal const string ScopeInputConflict = "SCOPE_INPUT_CONFLICT";
    internal const string SelectionEmpty = "SELECTION_EMPTY";
    internal const string SelectionElementMissing =
      "SELECTION_ELEMENT_MISSING";
    internal const string SelectionElementNotEligible =
      "SELECTION_ELEMENT_NOT_ELIGIBLE";
    internal const string AutoRoleUnsupported = "AUTO_ROLE_UNSUPPORTED";

    // Legacy names stay available while callers migrate to the precise codes.
    internal const string CustomScopeEmpty = SelectionEmpty;
    internal const string CustomElementUnavailable = SelectionElementMissing;
  }

  internal sealed class NativeStage02ElementSnapshot
  {
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal string UniqueId { get; set; } = string.Empty;
    internal int ElementId { get; set; }
    internal string Category { get; set; } = string.Empty;
    internal string CategoryName { get; set; } = string.Empty;
    internal string ClrType { get; set; } = string.Empty;
    internal string ElementKind { get; set; } = string.Empty;
    internal string ElementName { get; set; } = string.Empty;
    internal string FamilyName { get; set; } = string.Empty;
    internal string TypeName { get; set; } = string.Empty;
    internal string LevelName { get; set; } = string.Empty;
    internal string AssignedRoleId { get; set; } = string.Empty;
    internal bool IsElementType { get; set; }
    internal bool IsViewSpecific { get; set; }
    internal bool IsImported { get; set; }
    internal bool IsLinked { get; set; }
    internal bool IsModelElement { get; set; }
  }

  internal sealed class NativeStage02InventoryDecision
  {
    private NativeStage02InventoryDecision(
      bool accepted,
      string errorCode,
      string message,
      IEnumerable<NativeStage02ElementSnapshot> elements)
    {
      Accepted = accepted;
      ErrorCode = errorCode ?? string.Empty;
      Message = message ?? string.Empty;
      Elements = new ReadOnlyCollection<NativeStage02ElementSnapshot>(
        (elements ?? Array.Empty<NativeStage02ElementSnapshot>()).ToArray());
    }

    internal bool Accepted { get; }
    internal string ErrorCode { get; }
    internal string Message { get; }
    internal IReadOnlyList<NativeStage02ElementSnapshot> Elements { get; }

    internal static NativeStage02InventoryDecision Success(
      IEnumerable<NativeStage02ElementSnapshot> elements)
    {
      return new NativeStage02InventoryDecision(
        true,
        string.Empty,
        string.Empty,
        elements);
    }

    internal static NativeStage02InventoryDecision Failure(
      string errorCode,
      string message)
    {
      return new NativeStage02InventoryDecision(
        false,
        errorCode,
        message,
        Array.Empty<NativeStage02ElementSnapshot>());
    }
  }

  internal static class NativeStage02InventoryPolicy
  {
    internal static NativeStage02InventoryDecision Resolve(
      NativeStage02ScopeMode scopeMode,
      IEnumerable<NativeStage02ElementSnapshot> inventory,
      IEnumerable<string> customUniqueIds,
      IEnumerable<string> allowedCategories)
    {
      string[] customIds = (customUniqueIds ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

      if (scopeMode == NativeStage02ScopeMode.FullModel
        && customIds.Length > 0)
      {
        return NativeStage02InventoryDecision.Failure(
          NativeStage02InventoryCodes.ScopeInputConflict,
          "全模型模式不得同时携带残留的当前选择。" );
      }

      NativeStage02ElementSnapshot[] all = (inventory
          ?? Array.Empty<NativeStage02ElementSnapshot>())
        .Where(value => value != null)
        .ToArray();

      if (scopeMode == NativeStage02ScopeMode.CustomSelection)
      {
        return NativeStage02SelectionInventoryPolicy.Resolve(
          all,
          customIds);
      }

      var allowed = new HashSet<string>(
        (allowedCategories ?? Array.Empty<string>())
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .Select(value => value.Trim()),
        StringComparer.Ordinal);
      NativeStage02ElementSnapshot[] automaticEligible = all
        .Where(value => IsAutomaticInventoryEligible(value, allowed))
        .GroupBy(value => value.UniqueId, StringComparer.Ordinal)
        .Select(group => group
          .OrderBy(value => value.ElementId)
          .First())
        .OrderBy(value => value.UniqueId, StringComparer.Ordinal)
        .ToArray();

      return NativeStage02InventoryDecision.Success(automaticEligible);
    }

    private static bool IsAutomaticInventoryEligible(
      NativeStage02ElementSnapshot element,
      ISet<string> allowedCategories)
    {
      return NativeStage02SelectionInventoryPolicy.IsEligible(element)
        && !string.IsNullOrWhiteSpace(element.Category)
        && allowedCategories.Contains(element.Category);
    }
  }
}
