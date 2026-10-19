using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal static class UndergroundContextReader
    {
        internal static UndergroundLevel[] Levels(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .Select(l=>new UndergroundLevel {UniqueId=l.UniqueId,Name=l.Name,
                ElevationM=UnitUtils.ConvertFromInternalUnits(l.ProjectElevation,DisplayUnitType.DUT_METERS)})
            .OrderBy(l=>l.ElevationM).ThenBy(l=>l.Name).ToArray();
    }
}
