using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class BlankFileGate
  {
    public static IReadOnlyList<string> FindBlockingElements(Document document, int maximum = 20)
    {
      var result = new List<string>();
      if (document == null) return result;

      foreach (Element element in new FilteredElementCollector(document).WhereElementIsNotElementType())
      {
        if (!HasBlockingModelContent(element)) continue;
        Add(result, element, element.Category);
        if (result.Count >= maximum) break;
      }

      return result;
    }

    private static bool HasBlockingModelContent(Element element)
    {
      if (element == null) return false;
      if (IsAllowedTemplateElement(element)) return false;

      // These objects represent real external/model content even when their
      // geometry cannot be evaluated in the current view.
      if (element is ImportInstance || element is RevitLinkInstance || element is DirectShape)
        return true;

      Category category = element.Category;
      if (category == null || category.CategoryType != CategoryType.Model)
        return false;

      // Real placed model elements normally have a location. Revit template
      // metadata such as contour line styles and line patterns does not.
      if (element.Location != null)
        return true;

      return HasPhysicalGeometry(element);
    }

    private static bool IsAllowedTemplateElement(Element element)
    {
      if (element == null) return true;
      if (element is ProjectInfo) return true;
      if (element is View) return true;
      if (element is Level) return true;
      if (element is Grid) return true;
      if (element is BasePoint) return true;
      if (element is SketchPlane) return true;
      if (element is ReferencePlane) return true;
      if (element is Material) return true;
      if (element is GraphicsStyle) return true;
      if (element is Phase) return true;
      if (element is DesignOption) return true;
      if (element is Family) return true;
      if (element.Category == null) return true;
      return element.Category.CategoryType != CategoryType.Model;
    }

    private static bool HasPhysicalGeometry(Element element)
    {
      try
      {
        var options = new Options
        {
          ComputeReferences = false,
          IncludeNonVisibleObjects = false,
          DetailLevel = ViewDetailLevel.Fine
        };
        GeometryElement geometry = element.get_Geometry(options);
        if (geometry == null) return false;
        foreach (GeometryObject item in geometry)
          if (HasPhysicalGeometry(item, 0)) return true;
      }
      catch
      {
        // A metadata element that does not expose geometry must not become a
        // false blocker. Explicit model/link/import classes are handled above.
      }
      return false;
    }

    private static bool HasPhysicalGeometry(GeometryObject geometry, int depth)
    {
      if (geometry == null || depth > 4) return false;

      var solid = geometry as Solid;
      if (solid != null)
      {
        try
        {
          if (solid.Faces.Size > 0 || solid.Edges.Size > 0 || solid.Volume > 1e-9)
            return true;
        }
        catch { }
        return false;
      }

      var mesh = geometry as Mesh;
      if (mesh != null) return mesh.NumTriangles > 0;

      var curve = geometry as Curve;
      if (curve != null)
      {
        try { return curve.Length > 1e-9; }
        catch { return true; }
      }

      var polyLine = geometry as PolyLine;
      if (polyLine != null)
      {
        try { return polyLine.GetCoordinates().Count > 1; }
        catch { return true; }
      }

      var instance = geometry as GeometryInstance;
      if (instance != null)
      {
        try
        {
          GeometryElement instanceGeometry = instance.GetInstanceGeometry();
          if (instanceGeometry == null) return false;
          foreach (GeometryObject child in instanceGeometry)
            if (HasPhysicalGeometry(child, depth + 1)) return true;
        }
        catch { }
      }

      return false;
    }

    private static void Add(ICollection<string> result, Element element, Category category)
    {
      string categoryName = category?.Name ?? "无类别";
      string typeName = element.GetType().Name;
      string elementName;
      try { elementName = element.Name; }
      catch { elementName = string.Empty; }
      string suffix = string.IsNullOrWhiteSpace(elementName) ? string.Empty : " / " + elementName;
      result.Add(categoryName + suffix + " / " + typeName + " / Id=" + element.Id.IntegerValue);
    }
  }
}
