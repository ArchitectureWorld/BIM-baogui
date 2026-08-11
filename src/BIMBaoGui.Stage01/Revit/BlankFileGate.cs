using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIMBaoGui.Stage01.Core;

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
        if (!HasBlockingModelContent(element, out string reason)) continue;
        Add(result, element, element.Category, reason);
        if (result.Count >= maximum) break;
      }

      return result;
    }

    private static bool HasBlockingModelContent(Element element, out string reason)
    {
      reason = string.Empty;
      if (element == null) return false;
      if (IsAllowedTemplateElement(element)) return false;

      bool explicitModelContent =
        element is ImportInstance ||
        element is RevitLinkInstance ||
        element is DirectShape;

      bool knownPlacedModelContent = IsKnownPlacedModelContent(element);
      Category category = element.Category;
      bool isModelCategory = category != null && category.CategoryType == CategoryType.Model;
      bool isViewSpecific = false;
      try { isViewSpecific = element.ViewSpecific; }
      catch { }

      bool hasSpatialExtent = HasSpatialExtent(element);
      bool hasPhysicalGeometry = hasSpatialExtent && HasPhysicalGeometry(element);
      var facts = new BlankGateFacts(
        explicitModelContent,
        knownPlacedModelContent,
        isViewSpecific,
        isModelCategory,
        element.Location != null,
        hasSpatialExtent,
        hasPhysicalGeometry);

      bool blocking = BlankGatePolicy.IsBlocking(facts);
      if (!blocking) return false;

      if (explicitModelContent) reason = "外部链接、导入或 DirectShape";
      else if (knownPlacedModelContent) reason = "已放置的模型对象";
      else reason = "具有模型空间范围与实体几何";
      return true;
    }

    private static bool IsKnownPlacedModelContent(Element element)
    {
      string runtimeTypeName = element.GetType().Name;
      return element is HostObject
        || element is FamilyInstance
        || element is SpatialElement
        || element is ModelCurve
        || element is Group
        || string.Equals(runtimeTypeName, "TopographySurface", StringComparison.Ordinal)
        || string.Equals(runtimeTypeName, "SiteSubRegion", StringComparison.Ordinal)
        || string.Equals(runtimeTypeName, "Toposolid", StringComparison.Ordinal);
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
      if (element is LinePatternElement) return true;
      if (element is FillPatternElement) return true;
      if (element is ProjectLocation) return true;
      if (element is SiteLocation) return true;
      if (element is Phase) return true;
      if (element is DesignOption) return true;
      if (element is Family) return true;
      if (element.Category == null) return true;
      return element.Category.CategoryType != CategoryType.Model;
    }

    private static bool HasSpatialExtent(Element element)
    {
      try
      {
        BoundingBoxXYZ box = element.get_BoundingBox(null);
        if (box == null || box.Min == null || box.Max == null) return false;
        double x = Math.Abs(box.Max.X - box.Min.X);
        double y = Math.Abs(box.Max.Y - box.Min.Y);
        double z = Math.Abs(box.Max.Z - box.Min.Z);
        return x > 1e-9 || y > 1e-9 || z > 1e-9;
      }
      catch
      {
        return false;
      }
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
        // Template metadata that does not expose physical geometry is allowed.
        // Explicit model/link/import classes are handled before this method.
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

    private static void Add(ICollection<string> result, Element element, Category category, string reason)
    {
      string categoryName = category?.Name ?? "无类别";
      string typeName = element.GetType().Name;
      string elementName;
      try { elementName = element.Name; }
      catch { elementName = string.Empty; }
      string suffix = string.IsNullOrWhiteSpace(elementName) ? string.Empty : " / " + elementName;
      result.Add(categoryName + suffix + " / " + typeName + " / Id=" + element.Id.IntegerValue + " / 原因=" + reason);
    }
  }
}
