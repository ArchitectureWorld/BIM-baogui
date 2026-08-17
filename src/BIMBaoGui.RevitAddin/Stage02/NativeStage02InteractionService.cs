using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal sealed class NativeStage02SelectionResult
  {
    internal bool Succeeded { get; set; }
    internal string Code { get; set; } = string.Empty;
    internal NativeStage02ScopeMode ScopeMode { get; set; }
    internal IReadOnlyList<int> ElementIds { get; set; } = Array.Empty<int>();
    internal IReadOnlyList<string> ElementUniqueIds { get; set; } =
      Array.Empty<string>();
  }

  internal sealed class NativeStage02LiveElementReference
  {
    internal int ElementId { get; set; }
    internal string UniqueId { get; set; } = string.Empty;
  }

  internal static class NativeStage02SelectionRequestPolicy
  {
    internal static NativeStage02SelectionResult FromLiveReferences(
      NativeStage02ScopeMode scope,
      IEnumerable<NativeStage02LiveElementReference> references)
    {
      if (scope != NativeStage02ScopeMode.CurrentSelection
        && scope != NativeStage02ScopeMode.InteractiveSelection)
        return Failure(scope, "SELECTION_SCOPE_INVALID");
      NativeStage02LiveElementReference[] resolved = (references
          ?? Array.Empty<NativeStage02LiveElementReference>())
        .Where(value => value != null
          && value.ElementId > 0
          && !string.IsNullOrWhiteSpace(value.UniqueId))
        .Select(value => new NativeStage02LiveElementReference
        {
          ElementId = value.ElementId,
          UniqueId = value.UniqueId.Trim()
        })
        .GroupBy(value => value.UniqueId, StringComparer.Ordinal)
        .Select(group => group
          .OrderBy(value => value.ElementId)
          .First())
        .OrderBy(value => value.UniqueId, StringComparer.Ordinal)
        .ToArray();
      if (resolved.Length == 0)
        return Failure(scope, "SELECTION_EMPTY");
      return new NativeStage02SelectionResult
      {
        Succeeded = true,
        Code = "OK",
        ScopeMode = scope,
        ElementIds = new ReadOnlyCollection<int>(
          resolved.Select(value => value.ElementId).ToArray()),
        ElementUniqueIds = new ReadOnlyCollection<string>(
          resolved.Select(value => value.UniqueId).ToArray())
      };
    }

    internal static NativeStage02SelectionResult CancelledInteractiveSelection()
    {
      return Failure(
        NativeStage02ScopeMode.InteractiveSelection,
        "SELECTION_CANCELLED");
    }

    internal static NativeStage02SelectionResult Failure(
      NativeStage02ScopeMode scope,
      string code)
    {
      return new NativeStage02SelectionResult
      {
        Succeeded = false,
        Code = code ?? string.Empty,
        ScopeMode = scope,
        ElementIds = Array.Empty<int>(),
        ElementUniqueIds = Array.Empty<string>()
      };
    }

    internal static NativeStage02PreviewRequest Apply(
      NativeStage02PreviewRequest request,
      NativeStage02SelectionResult selection)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      if (selection == null) throw new ArgumentNullException(nameof(selection));
      if (!selection.Succeeded)
        throw new InvalidOperationException(selection.Code ?? "SELECTION_FAILED");
      if (selection.ScopeMode != NativeStage02ScopeMode.CurrentSelection
        && selection.ScopeMode != NativeStage02ScopeMode.InteractiveSelection)
        throw new InvalidOperationException("SELECTION_SCOPE_INVALID");
      NativeStage02PreviewRequest snapshot = request.Clone();
      if (snapshot.ScopeMode != selection.ScopeMode)
        throw new InvalidOperationException("SELECTION_SCOPE_MISMATCH");
      string[] uniqueIds = (selection.ElementUniqueIds ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      if (uniqueIds.Length == 0)
        throw new InvalidOperationException("SELECTION_EMPTY");
      snapshot.CustomUniqueIds = new ReadOnlyCollection<string>(uniqueIds);
      return snapshot.Clone();
    }
  }

  internal static class NativeStage02InteractionService
  {
    internal static NativeStage02SelectionResult ReadCurrentSelection(
      UIApplication application)
    {
      UIDocument uiDocument = application?.ActiveUIDocument;
      Document document = uiDocument?.Document;
      if (document == null)
        return NativeStage02SelectionRequestPolicy.Failure(
          NativeStage02ScopeMode.CurrentSelection,
          "SELECTION_DOCUMENT_MISSING");
      return FromElements(
        NativeStage02ScopeMode.CurrentSelection,
        uiDocument.Selection.GetElementIds()
          .Select(document.GetElement));
    }

    internal static NativeStage02SelectionResult PickElements(
      UIApplication application)
    {
      UIDocument uiDocument = application?.ActiveUIDocument;
      Document document = uiDocument?.Document;
      if (document == null)
        return NativeStage02SelectionRequestPolicy.Failure(
          NativeStage02ScopeMode.InteractiveSelection,
          "SELECTION_DOCUMENT_MISSING");
      try
      {
        IList<Reference> references = uiDocument.Selection.PickObjects(
          ObjectType.Element,
          new NativeStage02PickFilter(),
          "请选择报规构件，完成后点击完成");
        return FromElements(
          NativeStage02ScopeMode.InteractiveSelection,
          (references ?? Array.Empty<Reference>())
            .Select(reference => document.GetElement(reference.ElementId)));
      }
      catch (Autodesk.Revit.Exceptions.OperationCanceledException)
      {
        return NativeStage02SelectionRequestPolicy
          .CancelledInteractiveSelection();
      }
    }

    private static NativeStage02SelectionResult FromElements(
      NativeStage02ScopeMode scope,
      IEnumerable<Element> elements)
    {
      return NativeStage02SelectionRequestPolicy.FromLiveReferences(
        scope,
        (elements ?? Array.Empty<Element>())
        .Where(value => value != null
          && !string.IsNullOrWhiteSpace(value.UniqueId))
        .Select(value => new NativeStage02LiveElementReference
        {
          ElementId = value.Id.IntegerValue,
          UniqueId = value.UniqueId
        }));
    }

    private sealed class NativeStage02PickFilter : ISelectionFilter
    {
      public bool AllowElement(Element element)
      {
        if (element == null
          || element is ElementType
          || element is ImportInstance
          || element is RevitLinkInstance
          || element.ViewSpecific
          || string.IsNullOrWhiteSpace(element.UniqueId))
          return false;
        return element is ProjectInfo
          || element.Category?.CategoryType == CategoryType.Model;
      }

      public bool AllowReference(Reference reference, XYZ position)
      {
        return false;
      }
    }
  }
}
