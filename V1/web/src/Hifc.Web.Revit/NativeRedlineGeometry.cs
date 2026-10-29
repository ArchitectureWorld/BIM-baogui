using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    // Geometry evidence only. Role recognition, outline comparison and tolerance
    // decisions belong to the server's frozen reference rules.
    internal static class NativeRedlineGeometry
    {
        private sealed class Read : IDisposable
        {
            internal NativeRedlineCarrier Carrier;
            internal Solid Solid;
            public void Dispose() { Solid?.Dispose(); }
        }
        internal static NativeRedlineEvidence Capture(Document document, string[] uniqueIds)
        {
            var reads = new List<Read>(); var pairs = new List<NativeRedlinePair>();
            try {
                foreach (var id in uniqueIds.Distinct().OrderBy(x => x, StringComparer.Ordinal))
                {
                    var element = document.GetElement(id) ?? throw new InvalidOperationException("红线资料候选载体已删除，请重新全面检查。");
                    reads.Add(ReadCarrier(document, element));
                }
                for (int i = 0; i < reads.Count; i++) for (int j = i + 1; j < reads.Count; j++)
                {
                    var pair = new NativeRedlinePair { LeftUniqueId = reads[i].Carrier.UniqueId, RightUniqueId = reads[j].Carrier.UniqueId };
                    try {
                        if (reads[i].Solid == null || reads[j].Solid == null) throw new InvalidOperationException("候选载体未取得可靠单实体。");
                        using (var common = BooleanOperationsUtils.ExecuteBooleanOperation(reads[i].Solid, reads[j].Solid, BooleanOperationsType.Intersect)) pair.IntersectionVolumeM3 = common.Volume * .3048 * .3048 * .3048;
                    } catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException) { pair.Error = ex.Message; }
                    pairs.Add(pair);
                }
                var evidence = new NativeRedlineEvidence { Carriers = reads.Select(r => r.Carrier).ToArray(), Pairs = pairs.ToArray(), ProjectLocationName = document.ActiveProjectLocation?.Name };
                evidence.Fingerprint = Canonical.Hash(JsonWire.Serialize(evidence)); return evidence;
            } finally { foreach (var read in reads) read.Dispose(); }
        }
        private static Read ReadCarrier(Document document, Element element)
        {
            var output = new Read { Carrier = new NativeRedlineCarrier { ElementId = element.Id.IntegerValue, UniqueId = element.UniqueId, Name = element.Name, GeometryFingerprint = Fingerprint(document, element) } };
            var solids = new List<Solid>();
            try {
                using (var options = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false, ComputeReferences = false })
                using (var geometry = element.get_Geometry(options)) Solids(geometry, solids);
                if (solids.Count != 1) throw new InvalidOperationException("需要单一明确实体；当前有效实体数：" + solids.Count);
                output.Solid = solids[0]; solids.Clear(); var top = new List<PlanarFace>(); var bottom = new List<PlanarFace>();
                foreach (Face face in output.Solid.Faces)
                {
                    if (!(face is PlanarFace planar)) throw new InvalidOperationException("含曲面，暂不支持水平直线棱柱核对。");
                    var z = planar.FaceNormal.Z;
                    if (z > 1 - 1e-8) top.Add(planar); else if (z < -1 + 1e-8) bottom.Add(planar); else if (Math.Abs(z) > 1e-8) throw new InvalidOperationException("含斜面，不能以标高差代替实体核对。");
                }
                if (top.Count != 1 || bottom.Count != 1) throw new InvalidOperationException("顶面或底面不唯一。");
                var topPoints = Points(top[0]); var bottomPoints = Points(bottom[0]);
                output.Carrier.TopInternal = topPoints.Select(Internal).ToArray(); output.Carrier.BottomInternal = bottomPoints.Select(Internal).ToArray();
                output.Carrier.TopShared = topPoints.Select(p => Shared(document, p)).ToArray(); output.Carrier.BottomShared = bottomPoints.Select(p => Shared(document, p)).ToArray();
                output.Carrier.BottomM = bottom[0].Origin.Z * .3048; output.Carrier.TopM = top[0].Origin.Z * .3048; output.Carrier.HorizontalLinearFaces = true;
                // Deliberately do not assert matching top/bottom polygons here.
            } catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException) { output.Carrier.UnsupportedReason = ex.Message; output.Carrier.HorizontalLinearFaces = false; }
            finally { foreach (var solid in solids) solid.Dispose(); }
            return output;
        }
        internal static string Fingerprint(Document document, Element element)
        {
            var evidence = new List<string>();
            try {
                using (var options = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false, ComputeReferences = false })
                using (var geometry = element.get_Geometry(options)) Describe(geometry, evidence);
                foreach (var point in new[] { XYZ.Zero, XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ }) evidence.Add("Shared:" + Point(point) + ":" + JsonWire.Serialize(Shared(document, point)));
                return Canonical.Hash(string.Join("\n", evidence.OrderBy(s => s, StringComparer.Ordinal)));
            } catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException) { return Canonical.Hash("unavailable:" + ex.GetType().Name + ":" + ex.Message); }
        }
        private static void Describe(GeometryElement geometry, List<string> evidence)
        {
            if (geometry == null) { evidence.Add("NoGeometry"); return; }
            foreach (var item in geometry)
            {
                if (item is GeometryInstance instance) { using (var transformed = instance.GetInstanceGeometry()) Describe(transformed, evidence); }
                else if (item is Solid solid)
                {
                    evidence.Add("Volume:" + solid.Volume.ToString("R", CultureInfo.InvariantCulture));
                    foreach (Face face in solid.Faces) { evidence.Add("Area:" + face.Area.ToString("R", CultureInfo.InvariantCulture)); using (var mesh = face.Triangulate(1)) DescribeMesh(mesh, evidence); }
                    foreach (Edge edge in solid.Edges) using (var curve = edge.AsCurve()) evidence.Add("Edge:" + curve.GetType().Name + ":" + string.Join(";", curve.Tessellate().Select(Point)));
                }
                else if (item is Mesh mesh) DescribeMesh(mesh, evidence);
                else if (item is Curve curve) evidence.Add("Curve:" + curve.GetType().Name + ":" + string.Join(";", curve.Tessellate().Select(Point)));
            }
        }
        private static void DescribeMesh(Mesh mesh, List<string> evidence)
        {
            for (int i = 0; i < mesh.NumTriangles; i++) { var triangle = mesh.get_Triangle(i); evidence.Add("Triangle:" + string.Join(";", new[] { Point(triangle.get_Vertex(0)), Point(triangle.get_Vertex(1)), Point(triangle.get_Vertex(2)) }.OrderBy(s => s, StringComparer.Ordinal))); }
        }
        private static string Point(XYZ p) => p.X.ToString("R", CultureInfo.InvariantCulture) + "," + p.Y.ToString("R", CultureInfo.InvariantCulture) + "," + p.Z.ToString("R", CultureInfo.InvariantCulture);
        private static NativePoint3 Internal(XYZ point) => new NativePoint3 { X = point.X * .3048, Y = point.Y * .3048, Z = point.Z * .3048 };
        private static NativePoint3 Shared(Document document, XYZ point)
        {
            if (document.ActiveProjectLocation == null) throw new InvalidOperationException("缺少当前共享坐标位置。");
            using (var position = document.ActiveProjectLocation.GetProjectPosition(point)) return new NativePoint3 { X = position.EastWest * .3048, Y = position.NorthSouth * .3048, Z = position.Elevation * .3048 };
        }
        private static void Solids(GeometryElement geometry, List<Solid> solids)
        {
            if (geometry == null) throw new InvalidOperationException("未取得物理实体。");
            foreach (var item in geometry)
            {
                if (item is Mesh) throw new InvalidOperationException("含网格，不能只核对部分实体。");
                if (item is Solid solid && solid.Faces.Size > 0) { if (solid.Volume <= 1e-12) throw new InvalidOperationException("存在退化或零厚度实体。"); solids.Add(SolidUtils.CreateTransformed(solid, Transform.Identity)); }
                else if (item is GeometryInstance instance) using (var transformed = instance.GetInstanceGeometry()) Solids(transformed, solids);
            }
        }
        private static XYZ[] Points(PlanarFace face)
        {
            var loops = face.GetEdgesAsCurveLoops();
            try {
                if (loops.Count != 1) throw new InvalidOperationException("存在多环或孔洞。"); var curves = loops[0].ToArray();
                if (curves.Length < 3 || curves.Any(c => !(c is Line) || !c.IsBound)) throw new InvalidOperationException("存在曲线或未闭合边。");
                for (int i = 0; i < curves.Length; i++) if (curves[i].GetEndPoint(1).DistanceTo(curves[(i + 1) % curves.Length].GetEndPoint(0)) > 1e-7) throw new InvalidOperationException("轮廓不连续或未闭合。");
                return curves.Select(c => c.GetEndPoint(0)).ToArray();
            } finally { foreach (var loop in loops) loop.Dispose(); }
        }
    }
}
