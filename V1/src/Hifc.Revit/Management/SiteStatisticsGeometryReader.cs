using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Hifc.Revit.Management
{
    internal static class SiteStatisticsGeometryReader
    {
        internal static Solid[] Project(Element element, Transform placement)
        {
            var local = new List<Solid>();
            var result = new List<Solid>();
            try
            {
                if (element is Floor) local.AddRange(ModelGeometry.ProjectionSolids(element) ?? new Solid[0]);
                else if (element is Area area) local.Add(AreaProjection(area));
                else
                {
                    using (var options = new Options { DetailLevel = ViewDetailLevel.Fine })
                    using (var geometry = element.get_Geometry(options))
                        if (geometry != null) Collect(geometry, local);
                }
                if (local.Count == 0) throw new InvalidOperationException("构件没有可测量的面域投影");
                if (Math.Abs(Math.Abs(placement.BasisZ.Z) - 1) > 1e-9)
                    throw new InvalidOperationException("倾斜链接的总图水平投影需单独复核");
                using (var xy = Transform.Identity)
                {
                    xy.Origin = new XYZ(placement.Origin.X, placement.Origin.Y, 0);
                    xy.BasisX = new XYZ(placement.BasisX.X, placement.BasisX.Y, 0);
                    xy.BasisY = new XYZ(placement.BasisY.X, placement.BasisY.Y, 0);
                    foreach (var solid in local) result.Add(SolidUtils.CreateTransformed(solid, xy));
                }
                return result.ToArray();
            }
            catch { foreach (var solid in result) solid.Dispose(); throw; }
            finally { foreach (var solid in local) solid.Dispose(); }
        }

        private static Solid AreaProjection(Area area)
        {
            if (area.Location == null || area.Area <= 0) throw new InvalidOperationException("面积对象尚未放置或未形成有效边界");
            using (var options = new SpatialElementBoundaryOptions())
            {
                var segments = area.GetBoundarySegments(options);
                if (segments == null || segments.Count == 0) throw new InvalidOperationException("面积对象边界不可读取");
                var loops = new List<CurveLoop>();
                try
                {
                    foreach (var ring in segments)
                    {
                        var loop = new CurveLoop(); loops.Add(loop);
                        foreach (var segment in ring)
                        {
                            using (var curve = segment.GetCurve())
                            {
                                var z = curve.GetEndPoint(0).Z;
                                if (Math.Abs(curve.GetEndPoint(1).Z - z) > 1e-7) throw new InvalidOperationException("面积对象边界不在水平面内");
                                using (var flatten = Transform.CreateTranslation(new XYZ(0,0,-z))) loop.Append(curve.CreateTransformed(flatten));
                            }
                        }
                    }
                    return GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, 1);
                }
                finally { foreach (var loop in loops) loop.Dispose(); }
            }
        }

        private static void Collect(GeometryElement geometry, List<Solid> projected)
        {
            foreach (var item in geometry)
            {
                if (item is GeometryInstance instance)
                {
                    using (var nested = instance.GetInstanceGeometry()) Collect(nested, projected);
                }
                else if (item is Solid solid && solid.Volume > 1e-9)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (!(face is PlanarFace planar))
                        {
                            var bounds = face.GetBoundingBox();
                            if (face.ComputeNormal(new UV((bounds.Min.U + bounds.Max.U)/2, (bounds.Min.V + bounds.Max.V)/2)).Z > 1e-9)
                                throw new InvalidOperationException("曲面总图载体需要进一步核对水平投影");
                            continue;
                        }
                        if (planar.FaceNormal.Z <= 1e-9) continue;
                        var loops = ModelGeometry.ProjectionLoops(face);
                        try { projected.Add(GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, 1)); }
                        finally { foreach (var loop in loops) loop.Dispose(); }
                    }
                }
            }
        }

        internal static decimal UnionArea(IEnumerable<Solid> source)
        {
            var solids = source.ToArray();
            if (solids.Length == 0) throw new InvalidOperationException("投影面域为空");
            Solid union = SolidUtils.Clone(solids[0]);
            try
            {
                foreach (var solid in solids.Skip(1))
                {
                    var next = BooleanOperationsUtils.ExecuteBooleanOperation(union, solid, BooleanOperationsType.Union);
                    union.Dispose(); union = next;
                }
                return (decimal)(union.Volume * .09290304);
            }
            finally { union.Dispose(); }
        }
    }
}
