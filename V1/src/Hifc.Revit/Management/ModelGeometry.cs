using System;
using Autodesk.Revit.DB;

namespace Hifc.Revit.Management
{
    internal static class ModelGeometry
    {
        internal static double? ProjectedAreaM2(Element element)
        {
            // Top-face triangulation respects floor openings and sloped faces.
            // Bounding boxes and reference-case areas cannot establish model area.
            if (!(element is Floor) && !(element is RoofBase)) return null;
            try
            {
                double area = 0;
                foreach (var reference in HostObjectUtils.GetTopFaces((HostObject)element))
                {
                    var face = element.GetGeometryObjectFromReference(reference) as Face;
                    if (face == null) return null;
                    var mesh = face.Triangulate();
                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        var triangle = mesh.get_Triangle(i);
                        var a = triangle.get_Vertex(1) - triangle.get_Vertex(0);
                        var b = triangle.get_Vertex(2) - triangle.get_Vertex(0);
                        area += Math.Abs(a.X * b.Y - a.Y * b.X) / 2;
                    }
                }
                return area > 0 ? UnitUtils.ConvertFromInternalUnits(area, DisplayUnitType.DUT_SQUARE_METERS) : (double?)null;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
            catch (InvalidOperationException) { return null; }
        }
    }
}
