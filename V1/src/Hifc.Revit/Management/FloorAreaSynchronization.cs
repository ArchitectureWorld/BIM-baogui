using System;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal static class FloorAreaSynchronization
    {
        private static ModelSnapshot Read(Document doc)
        {
            var job=new CaseScanJob(doc,"报规","单体建筑",null,PlatformContext.Settings(doc));
            while(!job.Complete)job.ReadNext(doc,500);
            return job.Snapshot(doc);
        }
        internal static FloorAreaPlan ForExport(Document doc,Element[] mappedElements)
        {
            if(PlatformContext.Stage(doc)!="报规"||!mappedElements.Any(e=>PlatformContext.CanonicalSlot(RevitMappingService.Slot(doc,e),PlatformContext.ProfileId(doc))=="建筑区域"))
                return new FloorAreaPlan();
            var scan=Read(doc);
            return FloorAreaExportPolicy.Build(scan.Elements,scan.Planning);
        }
        internal static string Fingerprint(Document doc)
        {
            var scan=Read(doc);
            return Evidence.Hash(Evidence.Json(new{planning=scan.Planning,rows=scan.Elements
                .Where(r=>r.Entity=="IfcBuildingStorey"||PlanningInputs.EffectiveSlot(r)=="建筑区域"||r.Candidates.Any(c=>c.Slot=="建筑区域"))
                .OrderBy(r=>r.UniqueId,StringComparer.Ordinal).Select(r=>new{r.UniqueId,r.LevelUniqueId,r.Slot,r.Candidates,r.ProjectionKey,r.ProjectedAreaM2,r.AreaAggregationIssue,
                    fields=r.Fields.Where(f=>f.PropertySet==FloorAreaPolicy.FloorSet&&f.Property=="楼层面积"||f.PropertySet==FloorAreaPolicy.RegionSet&&f.Property=="投影面积")
                        .Select(f=>new{f.Source,f.Value,f.MetricValue,f.State,f.SourceSignature}).ToArray()}).ToArray()}));
        }
    }
}
