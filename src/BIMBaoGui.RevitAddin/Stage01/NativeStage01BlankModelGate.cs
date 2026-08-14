using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal static class NativeStage01BlankModelGate
  {
    internal static IReadOnlyList<string> FindBlockingElements(
      Document document,
      int maximum = 20)
    {
      var result = new List<string>();
      if (document == null) return result;
      foreach (Element element in new FilteredElementCollector(document)
        .WhereElementIsNotElementType())
      {
        if (!IsBlocking(element)) continue;
        result.Add(Describe(element));
        if (result.Count >= maximum) break;
      }
      return result;
    }

    private static bool IsBlocking(Element element)
    {
      if (element == null || IsAllowedTemplateElement(element)) return false;
      if (element is ImportInstance
        || element is RevitLinkInstance
        || element is DirectShape
        || element is HostObject
        || element is FamilyInstance
        || element is SpatialElement
        || element is ModelCurve
        || element is Group)
      {
        return true;
      }
      Category category = element.Category;
      if (category == null || category.CategoryType != CategoryType.Model)
        return false;
      try
      {
        if (element.ViewSpecific) return false;
      }
      catch
      {
      }
      if (element.Location != null) return true;
      try
      {
        BoundingBoxXYZ box = element.get_BoundingBox(null);
        if (box == null || box.Min == null || box.Max == null) return false;
        return Math.Abs(box.Max.X - box.Min.X) > 1e-9
          || Math.Abs(box.Max.Y - box.Min.Y) > 1e-9
          || Math.Abs(box.Max.Z - box.Min.Z) > 1e-9;
      }
      catch
      {
        return false;
      }
    }

    private static bool IsAllowedTemplateElement(Element element)
    {
      if (element is ProjectInfo
        || element is View
        || element is Level
        || element is Grid
        || element is BasePoint
        || element is SketchPlane
        || element is ReferencePlane
        || element is Material
        || element is GraphicsStyle
        || element is LinePatternElement
        || element is FillPatternElement
        || element is ProjectLocation
        || element is SiteLocation
        || element is Phase
        || element is DesignOption
        || element is Family)
      {
        return true;
      }
      return element.Category == null
        || element.Category.CategoryType != CategoryType.Model;
    }

    private static string Describe(Element element)
    {
      string name;
      try
      {
        name = element.Name ?? string.Empty;
      }
      catch
      {
        name = string.Empty;
      }
      return (element.Category?.Name ?? "无类别")
        + (name.Length == 0 ? string.Empty : " / " + name)
        + " / " + element.GetType().Name
        + " / Id=" + element.Id.IntegerValue;
    }
  }
}
