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

  internal static class NativeStage02InteractionService
  {
    internal static NativeStage02SelectionResult ReadCurrentSelection(
      UIApplication application)
    {
      UIDocument uiDocument = application?.ActiveUIDocument;
      Document document = uiDocument?.Document;
      if (document == null)
        return Failure(
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
        return Failure(
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
        return Failure(
          NativeStage02ScopeMode.InteractiveSelection,
          "SELECTION_CANCELLED");
      }
    }

    private static NativeStage02SelectionResult FromElements(
      NativeStage02ScopeMode scope,
      IEnumerable<Element> elements)
    {
      Element[] resolved = (elements ?? Array.Empty<Element>())
        .Where(value => value != null
          && !string.IsNullOrWhiteSpace(value.UniqueId))
        .GroupBy(value => value.UniqueId.Trim(), StringComparer.Ordinal)
        .Select(group => group
          .OrderBy(value => value.Id.IntegerValue)
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
          resolved.Select(value => value.Id.IntegerValue).ToArray()),
        ElementUniqueIds = new ReadOnlyCollection<string>(
          resolved.Select(value => value.UniqueId.Trim()).ToArray())
      };
    }

    private static NativeStage02SelectionResult Failure(
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
