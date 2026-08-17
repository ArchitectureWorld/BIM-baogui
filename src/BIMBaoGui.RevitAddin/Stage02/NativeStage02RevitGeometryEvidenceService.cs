using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02RevitGeometryEvidenceService
  {
    private const double PlanarZToleranceMetres = 1e-6;

    internal static NativeStage02GeometryEvidence Capture(
      Document document,
      Element element)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (element == null) throw new ArgumentNullException(nameof(element));
      var evidence = new NativeStage02GeometryEvidence
      {
        BoundingBox = CaptureBoundingBox(element),
        ShortCurveToleranceMetres = FeetToMetres(
          document.Application.ShortCurveTolerance)
      };
      CaptureLocation(element, evidence);

      bool supportedSurface = element is Floor
        || element is DirectShape
        || element is FamilyInstance
        || string.Equals(
          element.GetType().Name,
          "BuildingPad",
          StringComparison.Ordinal);
      var faces = new List<FaceCandidate>();
      if (supportedSurface)
      {
        GeometryElement source = element.get_Geometry(new Options
        {
          ComputeReferences = false,
          IncludeNonVisibleObjects = false,
          DetailLevel = ViewDetailLevel.Fine
        });
        CollectPlanarFaces(source, Transform.Identity, false, faces);
      }

      FaceCandidate accepted = null;
      if (faces.Count > 0)
      {
        FaceCandidate[] ordered = faces
          .OrderByDescending(value => value.AreaSquareMetres)
          .ToArray();
        accepted = ordered[0];
        double equalTolerance = Math.Max(
          0.01,
          accepted.AreaSquareMetres * 0.001);
        if (ordered.Skip(1).Any(value => Math.Abs(
          value.AreaSquareMetres - accepted.AreaSquareMetres) <= equalTolerance))
        {
          evidence.CaptureCode = "GEOMETRY_CAPTURE_AMBIGUOUS";
          accepted = null;
        }
      }

      double? faceArea = null;
      if (accepted != null)
      {
        faceArea = accepted.AreaSquareMetres;
        evidence.PlanarLoopsMetres = accepted.Loops;
        evidence.TopologySource = accepted.FromInstance
          ? "INSTANCE_PLANAR_FACE"
          : "PLANAR_FACE";
      }
      double? hostArea = HostAreaSquareMetres(element);
      if (faceArea.HasValue && hostArea.HasValue)
      {
        double tolerance = Math.Max(0.01, faceArea.Value * 0.001);
        if (Math.Abs(faceArea.Value - hostArea.Value) > tolerance)
        {
          evidence.CaptureCode = "GEOMETRY_AREA_SOURCE_MISMATCH";
          evidence.ApprovedProjectedAreaSquareMetres = null;
          evidence.ProjectedAreaSource = string.Empty;
        }
        else
        {
          evidence.ApprovedProjectedAreaSquareMetres = faceArea.Value;
          evidence.ProjectedAreaSource = evidence.TopologySource;
        }
      }
      else if (faceArea.HasValue)
      {
        evidence.ApprovedProjectedAreaSquareMetres = faceArea.Value;
        evidence.ProjectedAreaSource = evidence.TopologySource;
      }
      else if (hostArea.HasValue)
      {
        evidence.ApprovedProjectedAreaSquareMetres = hostArea.Value;
        evidence.ProjectedAreaSource = "HOST_AREA_COMPUTED";
      }

      ApplyCaptureStatus(
        evidence,
        supportedSurface,
        accepted != null,
        hostArea.HasValue,
        (evidence.CurveChainsMetres?.Count ?? 0) > 0);
      evidence.EvidenceHash = NativeStage02SemanticAssignmentCanonicalizer.Sha256(
        Canonical(evidence));
      return evidence;
    }

    internal static void ApplyCaptureStatus(
      NativeStage02GeometryEvidence evidence,
      bool supportedSurface,
      bool hasAcceptedPlanarFace,
      bool hasApprovedHostArea,
      bool hasCurveTopology)
    {
      if (evidence == null) throw new ArgumentNullException(nameof(evidence));
      if (supportedSurface
        && !hasAcceptedPlanarFace
        && !hasApprovedHostArea
        && !hasCurveTopology
        && string.IsNullOrWhiteSpace(evidence.CaptureCode))
        evidence.CaptureCode = "GEOMETRY_CAPTURE_UNSUPPORTED";
    }

    private static NativeStage02BoundingBoxEvidence CaptureBoundingBox(
      Element element)
    {
      BoundingBoxXYZ box = element.get_BoundingBox(null);
      if (box == null) return new NativeStage02BoundingBoxEvidence();
      Transform transform = box.Transform ?? Transform.Identity;
      // BoundingBoxXYZ.Transform is applied to all 8 corners before world min/max.
      XYZ[] corners =
      {
        new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
        new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
        new XYZ(box.Min.X, box.Max.Y, box.Min.Z),
        new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
        new XYZ(box.Max.X, box.Min.Y, box.Min.Z),
        new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
        new XYZ(box.Max.X, box.Max.Y, box.Min.Z),
        new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
      };
      XYZ[] world = corners.Select(transform.OfPoint).ToArray();
      return new NativeStage02BoundingBoxEvidence
      {
        Available = true,
        MinXFeet = world.Min(value => value.X),
        MinYFeet = world.Min(value => value.Y),
        MinZFeet = world.Min(value => value.Z),
        MaxXFeet = world.Max(value => value.X),
        MaxYFeet = world.Max(value => value.Y),
        MaxZFeet = world.Max(value => value.Z)
      };
    }

    private static void CaptureLocation(
      Element element,
      NativeStage02GeometryEvidence evidence)
    {
      LocationPoint point = element.Location as LocationPoint;
      if (point?.Point != null)
      {
        evidence.LocationKind = "LocationPoint";
        evidence.LocationCoordinatesFeet = new[]
        {
          point.Point.X,
          point.Point.Y,
          point.Point.Z
        };
        return;
      }
      LocationCurve curveLocation = element.Location as LocationCurve;
      if (curveLocation?.Curve == null) return;
      IList<XYZ> points = curveLocation.Curve.Tessellate();
      evidence.LocationKind = "LocationCurve";
      evidence.LocationCoordinatesFeet = points.SelectMany(value => new[]
      {
        value.X,
        value.Y,
        value.Z
      }).ToArray();
      evidence.CurveChainsMetres = new IReadOnlyList<double>[]
      {
        points.SelectMany(value => new[]
        {
          FeetToMetres(value.X),
          FeetToMetres(value.Y)
        }).ToArray()
      };
      evidence.TopologySource = "LOCATION_CURVE_TESSELLATION";
    }

    private static void CollectPlanarFaces(
      GeometryElement geometry,
      Transform accumulatedTransform,
      bool fromInstance,
      ICollection<FaceCandidate> faces)
    {
      if (geometry == null) return;
      foreach (GeometryObject item in geometry)
      {
        GeometryInstance instance = item as GeometryInstance;
        if (instance != null)
        {
          // GetInstanceGeometry() returns the complete instance geometry in model
          // coordinates; reading instance.Transform records the full nested transform.
          Transform instanceTransform = accumulatedTransform.Multiply(
            instance.Transform);
          GeometryElement instanceGeometry = instance.GetInstanceGeometry();
          CollectPlanarFaces(
            instanceGeometry,
            Transform.Identity,
            true,
            faces);
          if (instanceTransform == null) continue;
          continue;
        }
        Solid solid = item as Solid;
        if (solid == null || solid.Faces == null || solid.Faces.Size == 0)
          continue;
        foreach (Face rawFace in solid.Faces)
        {
          PlanarFace face = rawFace as PlanarFace;
          if (face == null || face.FaceNormal == null
            || face.FaceNormal.Z < 1.0 - 1e-9)
            continue;
          IReadOnlyList<IReadOnlyList<double>> loops;
          if (!TryLoops(face, accumulatedTransform, out loops)) continue;
          double area = SquareFeetToSquareMetres(face.Area);
          if (!Finite(area) || area <= 0) continue;
          faces.Add(new FaceCandidate
          {
            AreaSquareMetres = area,
            Loops = loops,
            FromInstance = fromInstance
          });
        }
      }
    }

    private static bool TryLoops(
      PlanarFace face,
      Transform transform,
      out IReadOnlyList<IReadOnlyList<double>> loops)
    {
      var result = new List<IReadOnlyList<double>>();
      foreach (CurveLoop curveLoop in face.GetEdgesAsCurveLoops())
      {
        var points = new List<XYZ>();
        foreach (Curve curve in curveLoop)
        {
          IList<XYZ> tessellated = curve.Tessellate();
          foreach (XYZ point in tessellated)
          {
            XYZ world = (transform ?? Transform.Identity).OfPoint(point);
            if (points.Count == 0 || points[points.Count - 1].DistanceTo(world) > 1e-9)
              points.Add(world);
          }
        }
        if (points.Count < 3) continue;
        if (points[0].DistanceTo(points[points.Count - 1]) > 1e-9)
          points.Add(points[0]);
        double minZ = points.Min(value => FeetToMetres(value.Z));
        double maxZ = points.Max(value => FeetToMetres(value.Z));
        if (maxZ - minZ > PlanarZToleranceMetres)
        {
          loops = Array.Empty<IReadOnlyList<double>>();
          return false;
        }
        result.Add(points.SelectMany(value => new[]
        {
          FeetToMetres(value.X),
          FeetToMetres(value.Y)
        }).ToArray());
      }
      loops = result;
      return result.Count > 0;
    }

    private static double? HostAreaSquareMetres(Element element)
    {
      bool allowed = element is Floor || string.Equals(
        element.GetType().Name,
        "BuildingPad",
        StringComparison.Ordinal);
      if (!allowed) return null;
      Parameter parameter = element.get_Parameter(
        BuiltInParameter.HOST_AREA_COMPUTED);
      if (parameter == null || parameter.StorageType != StorageType.Double
        || !parameter.HasValue)
        return null;
      double value = SquareFeetToSquareMetres(parameter.AsDouble());
      return Finite(value) && value > 0 ? value : (double?)null;
    }

    private static string Canonical(NativeStage02GeometryEvidence evidence)
    {
      NativeStage02BoundingBoxEvidence box = evidence.BoundingBox
        ?? new NativeStage02BoundingBoxEvidence();
      var builder = new StringBuilder(4096);
      builder.Append(box.Available ? "1" : "0");
      foreach (double value in new[]
      {
        box.MinXFeet, box.MinYFeet, box.MinZFeet,
        box.MaxXFeet, box.MaxYFeet, box.MaxZFeet
      })
        builder.Append('|').Append(Number(value));
      builder.Append('|').Append(evidence.LocationKind ?? string.Empty);
      foreach (double value in evidence.LocationCoordinatesFeet
        ?? Array.Empty<double>())
        builder.Append('|').Append(Number(value));
      builder.Append('|').Append(
        evidence.ApprovedProjectedAreaSquareMetres.HasValue
          ? Number(evidence.ApprovedProjectedAreaSquareMetres.Value)
          : string.Empty);
      builder.Append('|').Append(evidence.ProjectedAreaSource ?? string.Empty);
      AppendTopology(builder, evidence.PlanarLoopsMetres);
      AppendTopology(builder, evidence.CurveChainsMetres);
      builder.Append('|').Append(Number(evidence.ShortCurveToleranceMetres));
      builder.Append('|').Append(evidence.TopologySource ?? string.Empty);
      builder.Append('|').Append(evidence.CaptureCode ?? string.Empty);
      return builder.ToString();
    }

    private static void AppendTopology(
      StringBuilder builder,
      IReadOnlyList<IReadOnlyList<double>> topology)
    {
      foreach (IReadOnlyList<double> chain in topology
        ?? Array.Empty<IReadOnlyList<double>>())
      {
        builder.Append("|[");
        foreach (double value in chain ?? Array.Empty<double>())
          builder.Append(Number(value)).Append(',');
        builder.Append(']');
      }
    }

    private static double FeetToMetres(double value)
    {
      return UnitUtils.ConvertFromInternalUnits(
        value,
        DisplayUnitType.DUT_METERS);
    }

    private static double SquareFeetToSquareMetres(double value)
    {
      return UnitUtils.ConvertFromInternalUnits(
        value,
        DisplayUnitType.DUT_SQUARE_METERS);
    }

    private static string Number(double value)
    {
      return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static bool Finite(double value)
    {
      return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private sealed class FaceCandidate
    {
      internal double AreaSquareMetres { get; set; }
      internal IReadOnlyList<IReadOnlyList<double>> Loops { get; set; } =
        Array.Empty<IReadOnlyList<double>>();
      internal bool FromInstance { get; set; }
    }
  }
}
