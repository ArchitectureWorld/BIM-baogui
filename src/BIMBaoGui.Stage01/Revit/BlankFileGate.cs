using System;
using System.Collections.Generic;
using System.Linq;
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
        if (IsAllowedTemplateElement(element)) continue;
        Category category = element.Category;
        if (element is ImportInstance || element is RevitLinkInstance || element is DirectShape)
        {
          Add(result, element, category);
        }
        else if (category != null && category.CategoryType == CategoryType.Model)
        {
          Add(result, element, category);
        }

        if (result.Count >= maximum) break;
      }

      return result;
    }

    private static bool IsAllowedTemplateElement(Element element)
    {
      if (element == null) return true;
      if (element is DataStorage) return true;
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

    private static void Add(ICollection<string> result, Element element, Category category)
    {
      string categoryName = category?.Name ?? element.GetType().Name;
      string elementName;
      try { elementName = element.Name; }
      catch { elementName = string.Empty; }
      string suffix = string.IsNullOrWhiteSpace(elementName) ? string.Empty : " / " + elementName;
      result.Add(categoryName + suffix + " / Id=" + element.Id.IntegerValue);
    }
  }
}
