using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Web.Contracts;
using ElementRecord = Hifc.Web.Contracts.ElementRecord;

namespace Hifc.Web.Revit
{
    internal static class NativeGeometry
    {
        internal static double? ProjectedAreaM2(Element element)
        {
            if(element is Area areaElement)
                return areaElement.Location!=null && areaElement.Area>0 ? UnitUtils.ConvertFromInternalUnits(areaElement.Area,DisplayUnitType.DUT_SQUARE_METERS) : (double?)null;
            // Planar native face area retains exact curves and openings. Coarse
            // triangulation previously lost curved boundary area in the real models.
            if (!(element is Floor) && !(element is RoofBase)) return null;
            try
            {
                double area = 0;
                foreach (var reference in HostObjectUtils.GetTopFaces((HostObject)element))
                {
                    var face = element.GetGeometryObjectFromReference(reference) as Face;
                    if (face == null) return null;
                    if (face is PlanarFace planar) area += planar.Area * Math.Abs(planar.FaceNormal.Z);
                    else
                    {
                        var fine=MeshArea(face.Triangulate(1));
                        var coarse=MeshArea(face.Triangulate(.8));
                        // Reject a non-converged surface instead of hiding the approximation.
                        if(Math.Abs(fine-coarse)>.001/(.3048*.3048)+fine*1e-6)return null;
                        area+=fine;
                    }
                }
                return area > 0 ? UnitUtils.ConvertFromInternalUnits(area, DisplayUnitType.DUT_SQUARE_METERS) : (double?)null;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
            catch (InvalidOperationException) { return null; }
        }
        static double MeshArea(Mesh mesh)
        {
            double sum=0;
            for(int i=0;i<mesh.NumTriangles;i++) {
                var t=mesh.get_Triangle(i);var a=t.get_Vertex(1)-t.get_Vertex(0);var b=t.get_Vertex(2)-t.get_Vertex(0);
                sum+=Math.Abs(a.X*b.Y-a.Y*b.X)/2;
            }
            return sum;
        }
        internal static string ProjectionKey(Element element)
        {
            if(!(element is Floor floor))return null;
            try {
                var segments=new List<string>();
                Func<XYZ,string> point=p=>(p.X*.3048).ToString("0.000000",CultureInfo.InvariantCulture)+","+(p.Y*.3048).ToString("0.000000",CultureInfo.InvariantCulture);
                foreach(var reference in HostObjectUtils.GetTopFaces(floor)) {
                    if(!(element.GetGeometryObjectFromReference(reference) is Face face))return null;
                    foreach(var loop in ProjectionLoops(face))
                        foreach(var curve in loop) {
                            if(!curve.IsBound)return null;
                            var ends=new[]{point(curve.GetEndPoint(0)),point(curve.GetEndPoint(1))}.OrderBy(p=>p,StringComparer.Ordinal);
                            segments.Add(curve.GetType().Name+"|"+string.Join(";",ends)+"|"+point(curve.Evaluate(.5,true))+"|"+(curve.Length*.3048).ToString("0.000000",CultureInfo.InvariantCulture));
                        }
                }
                return segments.Count==0?null:Canonical.Hash(string.Join("\n",segments.OrderBy(s=>s,StringComparer.Ordinal)));
            } catch(Autodesk.Revit.Exceptions.ApplicationException){return null;}
              catch(InvalidOperationException){return null;}
        }
        internal static void CheckAggregation(Document doc,ElementRecord[] rows)
        {
            var evidence=rows.ToDictionary(r=>r.UniqueId,r=>new List<NativeProjectionOverlap>());
            foreach(var group in rows.Where(r=>r.SourceEntity=="IfcSlab").GroupBy(r=>r.LevelUniqueId)) {
                var members=group.ToArray();
                for(int i=0;i<members.Length;i++)for(int j=i+1;j<members.Length;j++) {
                    var a=doc.GetElement(new ElementId(members[i].Id));var b=doc.GetElement(new ElementId(members[j].Id));
                    var boxA=a?.get_BoundingBox(null);var boxB=b?.get_BoundingBox(null);
                    if(boxA!=null&&boxB!=null&&(boxA.Max.X<=boxB.Min.X+1e-7||boxB.Max.X<=boxA.Min.X+1e-7||boxA.Max.Y<=boxB.Min.Y+1e-7||boxB.Max.Y<=boxA.Min.Y+1e-7))continue;
                    double? area=null;string error=null;Solid[] left=null,right=null;
                    try {
                        if(boxA==null||boxB==null)throw new InvalidOperationException("无法读取边界框");
                        left=ProjectionSolids(a);right=ProjectionSolids(b);
                        if(left==null||right==null)throw new InvalidOperationException("非单一水平顶面投影");
                        double overlap=0;
                        foreach(var x in left)foreach(var y in right)
                            using(var intersection=BooleanOperationsUtils.ExecuteBooleanOperation(x,y,BooleanOperationsType.Intersect))
                                overlap+=intersection.Volume*.3048*.3048;
                        area=overlap;
                    } catch(Exception ex) when(ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException) { error=ex.Message; }
                    finally { foreach(var solid in (left??new Solid[0]).Concat(right??new Solid[0]))solid.Dispose(); }
                    evidence[members[i].UniqueId].Add(new NativeProjectionOverlap{OtherUniqueId=members[j].UniqueId,OverlapM2=area,Error=error});
                    evidence[members[j].UniqueId].Add(new NativeProjectionOverlap{OtherUniqueId=members[i].UniqueId,OverlapM2=area,Error=error});
                }
            }
            foreach(var row in rows)row.ProjectionOverlaps=evidence[row.UniqueId].OrderBy(e=>e.OtherUniqueId,StringComparer.Ordinal).ToArray();
        }
        internal static IList<CurveLoop> ProjectionLoops(Face face)
        {
            var planar=face as PlanarFace;
            if(planar!=null&&Math.Abs(planar.FaceNormal.Z)<1e-9)throw new InvalidOperationException("顶面没有有效水平投影");
            var source=face.GetEdgesAsCurveLoops();
            // Retain analytic arcs/ellipses for horizontal faces. A rigid translation
            // cannot flatten a sloped face; straight edges need an XY projection.
            if(planar!=null&&Math.Abs(planar.FaceNormal.Z)>1-1e-12) {
                var translation=Transform.CreateTranslation(new XYZ(0,0,-planar.Origin.Z));
                return source.Select(l=>CurveLoop.CreateViaTransform(l,translation)).ToList();
            }
            var result=new List<CurveLoop>();
            foreach(var loop in source) {
                var projected=new CurveLoop();
                foreach(var curve in loop) {
                    if(!(curve is Line)||!curve.IsBound)throw new InvalidOperationException("斜面曲线边界的精确投影尚不支持");
                    var a=curve.GetEndPoint(0);var b=curve.GetEndPoint(1);
                    projected.Append(Line.CreateBound(new XYZ(a.X,a.Y,0),new XYZ(b.X,b.Y,0)));
                }
                result.Add(projected);
            }
            return result;
        }
        internal static Solid[] ProjectionSolids(Element element)
        {
            if(!(element is Floor floor))return null;
            var solids=new List<Solid>();
            try {
                foreach(var reference in HostObjectUtils.GetTopFaces(floor)) {
                    if(!(element.GetGeometryObjectFromReference(reference) is Face face))throw new InvalidOperationException("顶面不可读取");
                    var projected=GeometryCreationUtilities.CreateExtrusionGeometry(ProjectionLoops(face),XYZ.BasisZ,1);
                    solids.Add(projected);
                    // Revit may represent a shape-edited floor as a RuledFace even
                    // with straight boundary loops. Accept it only when its actual
                    // projected mesh agrees with the exact boundary extrusion and
                    // converges; folds or ambiguous projections remain blocked.
                    if(!(face is PlanarFace)) {
                        double measured=MeshArea(face.Triangulate(1));
                        double tolerance=1e-7/(.3048*.3048)+Math.Abs(measured)*1e-10;
                        if(Math.Abs(projected.Volume-measured)>tolerance||Math.Abs(MeshArea(face.Triangulate(.8))-measured)>tolerance)
                            throw new InvalidOperationException("顶面投影与边界或网格收敛校验不一致");
                    }
                }
                return solids.ToArray();
            }catch {
                foreach(var solid in solids)solid.Dispose();
                throw;
            }
        }
    }
}
