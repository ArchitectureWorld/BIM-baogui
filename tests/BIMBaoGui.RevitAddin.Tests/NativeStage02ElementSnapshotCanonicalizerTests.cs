using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02ElementSnapshotCanonicalizerTests
  {
    [Fact]
    public void Fact_hash_excludes_role_assignment_but_includes_geometry_facts()
    {
      var snapshot = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "A",
        ElementId = 1,
        Category = "OST_BuildingPad",
        CategoryName = "建筑地坪",
        ClrType = "Autodesk.Revit.DB.Architecture.BuildingPad",
        ElementKind = "BuildingPad",
        ElementName = "绿地一",
        FamilyName = "绿地",
        TypeName = "集中绿地",
        LevelName = "场地标高",
        AssignedRoleId = "SITE_GREEN_OBJECT",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          BoundingBox = new NativeStage02BoundingBoxEvidence
          {
            Available = true,
            MinXFeet = 1.0 / 3.0,
            MinYFeet = 2,
            MinZFeet = 3,
            MaxXFeet = 4,
            MaxYFeet = 5,
            MaxZFeet = 6
          },
          LocationKind = "LocationPoint",
          LocationCoordinatesFeet = new[] { 1.0, 2.0, 3.0 },
          ApprovedProjectedAreaSquareMetres = 12.5,
          ProjectedAreaSource = "PLANAR_FACE",
          EvidenceHash = new string('b', 64)
        }
      };

      string before = NativeStage02ElementSnapshotCanonicalizer.Sha256(snapshot);
      snapshot.AssignedRoleId = "SITE_TOTAL_LAND";
      string afterRoleChange = NativeStage02ElementSnapshotCanonicalizer.Sha256(snapshot);
      snapshot.Geometry.ApprovedProjectedAreaSquareMetres = 12.6;
      string afterGeometryChange = NativeStage02ElementSnapshotCanonicalizer.Sha256(snapshot);

      Assert.Equal(before, afterRoleChange);
      Assert.NotEqual(before, afterGeometryChange);
      Assert.Contains("0.33333333333333331", NativeStage02ElementSnapshotCanonicalizer.Build(snapshot));
    }

    [Fact]
    public void Location_and_world_bounding_box_changes_are_snapshot_changes()
    {
      var snapshot = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "A",
        ElementId = 1,
        Category = "OST_Floors",
        CategoryName = "楼板",
        ClrType = "Autodesk.Revit.DB.Floor",
        ElementKind = "Floor",
        ElementName = "A",
        FamilyName = "F",
        TypeName = "T",
        LevelName = "L",
        Geometry = new NativeStage02GeometryEvidence
        {
          BoundingBox = new NativeStage02BoundingBoxEvidence
          {
            Available = true,
            MinXFeet = 0,
            MinYFeet = 0,
            MinZFeet = 0,
            MaxXFeet = 1,
            MaxYFeet = 1,
            MaxZFeet = 1
          },
          LocationKind = "LocationPoint",
          LocationCoordinatesFeet = new[] { 0.0, 0.0, 0.0 },
          EvidenceHash = new string('c', 64)
        }
      };

      string original = NativeStage02ElementSnapshotCanonicalizer.Sha256(snapshot);
      snapshot.Geometry.LocationCoordinatesFeet = new[] { 0.0, 0.0, 0.1 };
      string moved = NativeStage02ElementSnapshotCanonicalizer.Sha256(snapshot);
      snapshot.Geometry.LocationCoordinatesFeet = new[] { 0.0, 0.0, 0.0 };
      snapshot.Geometry.BoundingBox.MaxXFeet = 2;
      string resized = NativeStage02ElementSnapshotCanonicalizer.Sha256(snapshot);

      Assert.NotEqual(original, moved);
      Assert.NotEqual(original, resized);
    }
  }
}
