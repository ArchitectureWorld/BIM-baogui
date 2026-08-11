namespace BIMBaoGui.Stage01.Core
{
  /// <summary>
  /// Pure facts used by the Revit adapter to decide whether an element means
  /// that formal modelling has already started. Revit templates legitimately
  /// contain views, styles, levels, contour display metadata and other objects;
  /// those objects are not required to be absent.
  /// </summary>
  internal readonly struct BlankGateFacts
  {
    public BlankGateFacts(
      bool isExplicitModelContent,
      bool isKnownPlacedModelContent,
      bool isViewSpecific,
      bool isModelCategory,
      bool hasLocation,
      bool hasSpatialExtent,
      bool hasPhysicalGeometry)
    {
      IsExplicitModelContent = isExplicitModelContent;
      IsKnownPlacedModelContent = isKnownPlacedModelContent;
      IsViewSpecific = isViewSpecific;
      IsModelCategory = isModelCategory;
      HasLocation = hasLocation;
      HasSpatialExtent = hasSpatialExtent;
      HasPhysicalGeometry = hasPhysicalGeometry;
    }

    public bool IsExplicitModelContent { get; }
    public bool IsKnownPlacedModelContent { get; }
    public bool IsViewSpecific { get; }
    public bool IsModelCategory { get; }
    public bool HasLocation { get; }
    public bool HasSpatialExtent { get; }
    public bool HasPhysicalGeometry { get; }
  }

  internal static class BlankGatePolicy
  {
    public static bool IsBlocking(BlankGateFacts facts)
    {
      if (facts.IsExplicitModelContent) return true;
      if (facts.IsKnownPlacedModelContent) return true;
      if (facts.IsViewSpecific) return false;
      if (!facts.IsModelCategory) return false;

      // Location alone is not evidence of user-created model content. Some
      // Revit template metadata and display-style elements report a location.
      // Unknown model-category elements block only when they have both a real
      // model-space extent and inspectable physical geometry.
      return facts.HasSpatialExtent && facts.HasPhysicalGeometry;
    }
  }
}
