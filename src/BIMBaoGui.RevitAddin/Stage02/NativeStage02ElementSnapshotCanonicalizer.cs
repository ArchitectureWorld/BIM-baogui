using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02ElementSnapshotCanonicalizer
  {
    private static readonly JavaScriptSerializer Serializer =
      new JavaScriptSerializer();

    internal static string Build(NativeStage02ElementSnapshot snapshot)
    {
      if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
      NativeStage02GeometryEvidence geometry = snapshot.Geometry
        ?? new NativeStage02GeometryEvidence();
      NativeStage02BoundingBoxEvidence box = geometry.BoundingBox
        ?? new NativeStage02BoundingBoxEvidence();
      var builder = new StringBuilder(1024);
      builder.Append('{');
      Property(builder, "documentFingerprint", snapshot.DocumentFingerprint, false);
      Property(builder, "uniqueId", snapshot.UniqueId, true);
      Property(builder, "elementId", snapshot.ElementId.ToString(
        CultureInfo.InvariantCulture), true);
      Property(builder, "category", snapshot.Category, true);
      Property(builder, "categoryName", snapshot.CategoryName, true);
      Property(builder, "clrType", snapshot.ClrType, true);
      Property(builder, "elementKind", snapshot.ElementKind, true);
      Property(builder, "elementName", snapshot.ElementName, true);
      Property(builder, "familyName", snapshot.FamilyName, true);
      Property(builder, "typeName", snapshot.TypeName, true);
      Property(builder, "levelName", snapshot.LevelName, true);
      builder.Append(",\"geometry\":{");
      Property(builder, "boundingBoxAvailable", box.Available ? "true" : "false", false);
      Property(builder, "minXFeet", Number(box.MinXFeet), true);
      Property(builder, "minYFeet", Number(box.MinYFeet), true);
      Property(builder, "minZFeet", Number(box.MinZFeet), true);
      Property(builder, "maxXFeet", Number(box.MaxXFeet), true);
      Property(builder, "maxYFeet", Number(box.MaxYFeet), true);
      Property(builder, "maxZFeet", Number(box.MaxZFeet), true);
      Property(builder, "locationKind", geometry.LocationKind, true);
      builder.Append(",\"locationCoordinatesFeet\":[");
      bool first = true;
      foreach (double coordinate in geometry.LocationCoordinatesFeet
        ?? Array.Empty<double>())
      {
        if (!first) builder.Append(',');
        first = false;
        builder.Append(Q(Number(coordinate)));
      }
      builder.Append(']');
      Property(
        builder,
        "approvedProjectedAreaSquareMetres",
        geometry.ApprovedProjectedAreaSquareMetres.HasValue
          ? Number(geometry.ApprovedProjectedAreaSquareMetres.Value)
          : string.Empty,
        true);
      Property(builder, "projectedAreaSource", geometry.ProjectedAreaSource, true);
      Property(builder, "geometryEvidenceHash", geometry.EvidenceHash, true);
      builder.Append("}}");
      return builder.ToString();
    }

    internal static string Sha256(NativeStage02ElementSnapshot snapshot)
    {
      using (SHA256 sha = SHA256.Create())
      {
        return string.Concat(sha.ComputeHash(new UTF8Encoding(false).GetBytes(
          Build(snapshot))).Select(value => value.ToString(
            "x2", CultureInfo.InvariantCulture)));
      }
    }

    private static string Number(double value)
    {
      return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static void Property(
      StringBuilder builder,
      string name,
      string value,
      bool comma)
    {
      if (comma) builder.Append(',');
      builder.Append(Q(name)).Append(':').Append(Q(value ?? string.Empty));
    }

    private static string Q(string value)
    {
      return Serializer.Serialize(value ?? string.Empty);
    }
  }
}
