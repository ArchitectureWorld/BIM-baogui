using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Stage02;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage02RevitSelectionItem
  {
    internal Stage02RevitSelectionItem(
      Stage02ElementReference element,
      string roleHint)
      : this(element, roleHint, string.Empty)
    {
    }

    internal Stage02RevitSelectionItem(
      Stage02ElementReference element,
      string roleHint,
      string stage01RecordIdentity)
    {
      Element = element ?? throw new ArgumentNullException(nameof(element));
      RoleHint = roleHint ?? string.Empty;
      Stage01RecordIdentity = stage01RecordIdentity ?? string.Empty;
    }

    internal Stage02ElementReference Element { get; }
    internal string DocumentFingerprint => Element.DocumentFingerprint;
    internal string UniqueId => Element.UniqueId;
    internal int ElementId => Element.ElementId;
    internal string RoleHint { get; }
    internal string Stage01RecordIdentity { get; }
  }

  internal sealed class Stage02RevitSelectionResult
  {
    internal Stage02RevitSelectionResult(
      bool cancelled,
      IEnumerable<Stage02RevitSelectionItem> items,
      IEnumerable<string> messages)
      : this(
        Stage02SelectionModes.Legacy,
        cancelled,
        items,
        messages)
    {
    }

    internal Stage02RevitSelectionResult(
      string selectionMode,
      bool cancelled,
      IEnumerable<Stage02RevitSelectionItem> items,
      IEnumerable<string> messages)
    {
      SelectionMode = selectionMode ?? string.Empty;
      Cancelled = cancelled;
      Items = new ReadOnlyCollection<Stage02RevitSelectionItem>(
        (items ?? Array.Empty<Stage02RevitSelectionItem>()).ToArray());
      Messages = new ReadOnlyCollection<string>(
        (messages ?? Array.Empty<string>()).ToArray());
    }

    internal bool Cancelled { get; }
    internal string SelectionMode { get; }
    internal IReadOnlyList<Stage02RevitSelectionItem> Items { get; }
    internal IReadOnlyList<string> Messages { get; }
    internal bool Success => !Cancelled && Messages.Count == 0;
  }

  internal static class Stage02RevitSelectionService
  {
    internal static Stage02RevitSelectionResult ReadCurrentSelection(
      HBRFileContext context)
    {
      return ReadCurrentSelection(context, string.Empty);
    }

    internal static Stage02RevitSelectionResult ReadCurrentSelection(
      HBRFileContext context,
      string roleHint)
    {
      if (RevitHost.RunReadInHostContext(
        () => ReadCurrentSelectionCore(context, roleHint),
        out Stage02RevitSelectionResult result,
        out string error))
      {
        return result;
      }
      return Failed(error, Stage02SelectionModes.CurrentSelection);
    }

    internal static Stage02RevitSelectionResult PickElements(
      HBRFileContext context)
    {
      return PickElements(context, string.Empty);
    }

    internal static Stage02RevitSelectionResult PickElements(
      HBRFileContext context,
      string roleHint)
    {
      if (RevitHost.RunReadInHostContext(
        () => PickElementsCore(context, roleHint),
        out Stage02RevitSelectionResult result,
        out string error))
      {
        return result;
      }
      return Failed(error, Stage02SelectionModes.ExplicitPick);
    }

    internal static Stage02RevitSelectionResult ResolveElementIds(
      HBRFileContext context,
      IEnumerable<int> elementIds,
      string roleHint)
    {
      int[] frozenIds = (elementIds ?? Array.Empty<int>())
        .Distinct()
        .OrderBy(value => value)
        .ToArray();
      if (RevitHost.RunReadInHostContext(
        () => ResolveElementIdsCore(context, frozenIds, roleHint),
        out Stage02RevitSelectionResult result,
        out string error))
      {
        return result;
      }
      return Failed(error, Stage02SelectionModes.ExplicitIds);
    }

    internal static Stage02RevitSelectionResult SelectProjectInformation(
      HBRFileContext context,
      string roleHint)
    {
      if (RevitHost.RunReadInHostContext(
        () => SelectProjectInformationCore(context, roleHint),
        out Stage02RevitSelectionResult result,
        out string error))
      {
        return result;
      }
      return Failed(error, Stage02SelectionModes.ProjectInformation);
    }

    private static Stage02RevitSelectionResult ReadCurrentSelectionCore(
      HBRFileContext context,
      string roleHint)
    {
      RequireHost(out UIApplication uiApplication, out UIDocument uiDocument,
        out Document document);
      IReadOnlyList<string> blockers = ValidateDocument(
        context,
        uiApplication,
        document);
      if (blockers.Count > 0)
        return new Stage02RevitSelectionResult(
          Stage02SelectionModes.CurrentSelection,
          false,
          null,
          blockers);
      ICollection<ElementId> ids = uiDocument.Selection.GetElementIds();
      if (ids.Count == 0)
      {
        return Failed(
          "当前 Revit 选择集中没有元素。",
          Stage02SelectionModes.CurrentSelection);
      }
      return FromElements(
        uiApplication,
        document,
        ids.Select(document.GetElement),
        NormalizeRoleHint(roleHint),
        Stage02SelectionModes.CurrentSelection);
    }

    private static Stage02RevitSelectionResult PickElementsCore(
      HBRFileContext context,
      string roleHint)
    {
      RequireHost(out UIApplication uiApplication, out UIDocument uiDocument,
        out Document document);
      IReadOnlyList<string> blockers = ValidateDocument(
        context,
        uiApplication,
        document);
      if (blockers.Count > 0)
        return new Stage02RevitSelectionResult(
          Stage02SelectionModes.ExplicitPick,
          false,
          null,
          blockers);
      try
      {
        IList<Autodesk.Revit.DB.Reference> references =
          uiDocument.Selection.PickObjects(ObjectType.Element);
        return FromElements(
          uiApplication,
          document,
          references.Select(reference => document.GetElement(reference)),
          NormalizeRoleHint(roleHint),
          Stage02SelectionModes.ExplicitPick);
      }
      catch (Autodesk.Revit.Exceptions.OperationCanceledException)
      {
        return new Stage02RevitSelectionResult(
          Stage02SelectionModes.ExplicitPick,
          true,
          Array.Empty<Stage02RevitSelectionItem>(),
          Array.Empty<string>());
      }
    }

    private static Stage02RevitSelectionResult ResolveElementIdsCore(
      HBRFileContext context,
      IEnumerable<int> elementIds,
      string roleHint)
    {
      RequireHost(out UIApplication uiApplication, out _, out Document document);
      IReadOnlyList<string> blockers = ValidateDocument(
        context,
        uiApplication,
        document);
      if (blockers.Count > 0)
      {
        return new Stage02RevitSelectionResult(
          Stage02SelectionModes.ExplicitIds,
          false,
          null,
          blockers);
      }

      int[] ids = (elementIds ?? Array.Empty<int>())
        .Distinct()
        .OrderBy(value => value)
        .ToArray();
      if (ids.Length == 0)
      {
        return Failed(
          "元素Id入口没有提供任何 Id。",
          Stage02SelectionModes.ExplicitIds);
      }
      Element[] elements = ids
        .Select(value => document.GetElement(new ElementId(value)))
        .ToArray();
      int[] missing = ids
        .Where((value, index) => elements[index] == null)
        .ToArray();
      if (missing.Length > 0)
      {
        return Failed(
          "以下元素Id无法在当前文档解析："
          + string.Join(",", missing)
          + "。",
          Stage02SelectionModes.ExplicitIds);
      }
      return FromElements(
        uiApplication,
        document,
        elements,
        NormalizeRoleHint(roleHint),
        Stage02SelectionModes.ExplicitIds);
    }

    private static Stage02RevitSelectionResult SelectProjectInformationCore(
      HBRFileContext context,
      string roleHint)
    {
      RequireHost(out UIApplication uiApplication, out _, out Document document);
      IReadOnlyList<string> blockers = ValidateDocument(
        context,
        uiApplication,
        document);
      if (blockers.Count > 0)
      {
        return new Stage02RevitSelectionResult(
          Stage02SelectionModes.ProjectInformation,
          false,
          null,
          blockers);
      }
      string normalizedRole = NormalizeRoleHint(roleHint);
      if (normalizedRole.Length == 0) normalizedRole = "PROJECT";
      if (!IsProjectInformationRole(normalizedRole))
      {
        return Failed(
          "ProjectInformation 专用入口只接受 PROJECT、SITE 或 BUILDING 角色提示。",
          Stage02SelectionModes.ProjectInformation);
      }
      return FromElements(
        uiApplication,
        document,
        new[] { document.ProjectInformation },
        normalizedRole,
        Stage02SelectionModes.ProjectInformation);
    }

    internal static Stage02RevitSelectionResult
      ReadCurrentSelectionInHostContext(
        UIApplication uiApplication,
        UIDocument uiDocument,
        Document document)
    {
      if (uiApplication == null)
        throw new ArgumentNullException(nameof(uiApplication));
      if (uiDocument == null) throw new ArgumentNullException(nameof(uiDocument));
      if (document == null) throw new ArgumentNullException(nameof(document));
      ICollection<ElementId> ids = uiDocument.Selection.GetElementIds();
      if (ids.Count == 0)
      {
        return Failed(
          "确认时当前 Revit 选择集为空；必须重新预览。",
          Stage02SelectionModes.CurrentSelection);
      }
      return FromElements(
        uiApplication,
        document,
        ids.Select(document.GetElement),
        string.Empty,
        Stage02SelectionModes.CurrentSelection);
    }

    internal static Stage02ElementReference CreateReference(
      UIApplication uiApplication,
      Document document,
      Element element)
    {
      if (uiApplication == null)
        throw new ArgumentNullException(nameof(uiApplication));
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (element == null) throw new ArgumentNullException(nameof(element));
      string fingerprint = HBRDocumentFingerprint.Compute(
        document.PathName,
        document.Title,
        uiApplication.Application.VersionNumber);
      string category = GetBuiltInCategoryName(element);
      string elementKind = GetElementKind(document, element, category);
      ElementType type = document.GetElement(element.GetTypeId()) as ElementType;
      string familyName = type == null ? string.Empty : type.FamilyName;
      string typeName = type == null ? string.Empty : type.Name;
      return new Stage02ElementReference(
        fingerprint,
        document.Title,
        element.Id.IntegerValue,
        element.UniqueId,
        category,
        elementKind,
        familyName,
        typeName,
        element.Name);
    }

    internal static string GetBuiltInCategoryName(Element element)
    {
      if (element == null || element.Category == null) return string.Empty;
      int categoryId = element.Category.Id.IntegerValue;
      if (!Enum.IsDefined(typeof(BuiltInCategory), categoryId))
        return string.Empty;
      return ((BuiltInCategory)categoryId).ToString();
    }

    private static string GetElementKind(
      Document document,
      Element element,
      string category)
    {
      if (document.ProjectInformation != null
        && element.Id == document.ProjectInformation.Id)
        return "ProjectInformation";
      switch (category)
      {
        case "OST_Levels": return "Level";
        case "OST_Rooms": return "Room";
        case "OST_Areas": return "Area";
        case "OST_Walls": return "Wall";
        case "OST_Floors": return "Floor";
        case "OST_Roofs": return "Roof";
        case "OST_Windows":
        case "OST_Doors":
        case "OST_GenericModel": return "FamilyInstance";
        case "OST_StairsRuns": return "StairsRun";
        case "OST_DuctCurves": return "Duct";
        default: return element.GetType().Name;
      }
    }

    private static Stage02RevitSelectionResult FromElements(
      UIApplication uiApplication,
      Document document,
      IEnumerable<Element> elements,
      string roleHint,
      string selectionMode)
    {
      Stage02RevitSelectionItem[] items = (elements
        ?? Array.Empty<Element>())
        .Where(element => element != null)
        .GroupBy(element => element.UniqueId, StringComparer.Ordinal)
        .Select(group => new Stage02RevitSelectionItem(
          CreateReference(uiApplication, document, group.First()),
          roleHint))
        .OrderBy(item => item.UniqueId, StringComparer.Ordinal)
        .ToArray();
      return items.Length == 0
        ? Failed("没有可用于 Stage02 的有效 Revit 元素。", selectionMode)
        : new Stage02RevitSelectionResult(
          selectionMode,
          false,
          items,
          null);
    }

    private static IReadOnlyList<string> ValidateDocument(
      HBRFileContext context,
      UIApplication uiApplication,
      Document document)
    {
      RevitDocumentIdentity identity = RevitDocumentIdentityService.Read(
        uiApplication,
        document);
      return RevitDocumentIdentityService.Validate(context, identity);
    }

    private static void RequireHost(
      out UIApplication uiApplication,
      out UIDocument uiDocument,
      out Document document)
    {
      if (!RevitHost.TryGetContext(
        out uiApplication,
        out uiDocument,
        out document,
        out string error))
      {
        throw new InvalidOperationException(error);
      }
    }

    private static bool IsProjectInformationRole(string roleHint)
    {
      return string.Equals(roleHint, "PROJECT", StringComparison.Ordinal)
        || string.Equals(roleHint, "SITE", StringComparison.Ordinal)
        || string.Equals(roleHint, "BUILDING", StringComparison.Ordinal);
    }

    private static string NormalizeRoleHint(string roleHint)
    {
      return (roleHint ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static Stage02RevitSelectionResult Failed(string message)
    {
      return Failed(message, Stage02SelectionModes.Legacy);
    }

    private static Stage02RevitSelectionResult Failed(
      string message,
      string selectionMode)
    {
      return new Stage02RevitSelectionResult(
        selectionMode,
        false,
        Array.Empty<Stage02RevitSelectionItem>(),
        new[] { message ?? string.Empty });
    }
  }
}
