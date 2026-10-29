using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal static class ModelGeometry
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
                    if(!(element.GetGeometryObjectFromReference(reference) is PlanarFace face)||Math.Abs(face.FaceNormal.Z)<.999999)return null;
                    foreach(var loop in face.GetEdgesAsCurveLoops())
                        foreach(var curve in loop) {
                            if(!curve.IsBound)return null;
                            var ends=new[]{point(curve.GetEndPoint(0)),point(curve.GetEndPoint(1))}.OrderBy(p=>p,StringComparer.Ordinal);
                            segments.Add(curve.GetType().Name+"|"+string.Join(";",ends)+"|"+point(curve.Evaluate(.5,true))+"|"+(curve.Length*.3048).ToString("0.000000",CultureInfo.InvariantCulture));
                        }
                }
                return segments.Count==0?null:Evidence.Hash(string.Join("\n",segments.OrderBy(s=>s,StringComparer.Ordinal)));
            } catch(Autodesk.Revit.Exceptions.ApplicationException){return null;}
        }
        internal static void CheckAggregation(Document doc,ModelElementCheck[] rows)
        {
            foreach(var row in rows)row.AreaAggregationIssue=null;
            foreach(var group in rows.Where(r=>PlanningInputs.EffectiveSlot(r)=="建筑区域").GroupBy(r=>r.LevelUniqueId)) {
                var members=group.ToArray();
                for(int i=0;i<members.Length;i++)for(int j=i+1;j<members.Length;j++) {
                    var a=doc.GetElement(new ElementId(members[i].Id));var b=doc.GetElement(new ElementId(members[j].Id));
                    var boxA=a?.get_BoundingBox(null);var boxB=b?.get_BoundingBox(null);
                    if(boxA==null||boxB==null){members[i].AreaAggregationIssue=members[j].AreaAggregationIssue="无法核对区域投影是否重叠";continue;}
                    if(boxA.Max.X<=boxB.Min.X+1e-7||boxB.Max.X<=boxA.Min.X+1e-7||boxA.Max.Y<=boxB.Min.Y+1e-7||boxB.Max.Y<=boxA.Min.Y+1e-7)continue;
                    try {
                        var left=ProjectionSolids(a);var right=ProjectionSolids(b);
                        if(left==null||right==null)throw new InvalidOperationException("非水平顶面");
                        bool overlap=false;
                        foreach(var x in left)foreach(var y in right)
                            using(var intersection=BooleanOperationsUtils.ExecuteBooleanOperation(x,y,BooleanOperationsType.Intersect))
                                overlap|=intersection.Volume*.3048*.3048>1e-5;
                        foreach(var solid in left.Concat(right))solid.Dispose();
                        if(overlap)members[i].AreaAggregationIssue=members[j].AreaAggregationIssue="建筑区域投影重叠："+members[i].Id+" / "+members[j].Id+"；请确认计量范围，暂不汇总重复面积";
                    }catch(Exception ex) when(ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException) {
                        members[i].AreaAggregationIssue=members[j].AreaAggregationIssue="区域投影交叠未能可靠核对："+members[i].Id+" / "+members[j].Id;
                    }
                }
            }
        }
        static Solid[] ProjectionSolids(Element element)
        {
            if(!(element is Floor floor))return null;
            var solids=new List<Solid>();
            foreach(var reference in HostObjectUtils.GetTopFaces(floor)) {
                if(!(element.GetGeometryObjectFromReference(reference) is PlanarFace face)||Math.Abs(face.FaceNormal.Z)<.999999)return null;
                var translation=Transform.CreateTranslation(new XYZ(0,0,-face.Origin.Z));
                var loops=face.GetEdgesAsCurveLoops().Select(l=>CurveLoop.CreateViaTransform(l,translation)).ToList();
                solids.Add(GeometryCreationUtilities.CreateExtrusionGeometry(loops,XYZ.BasisZ,1));
            }
            return solids.ToArray();
        }
    }
}
