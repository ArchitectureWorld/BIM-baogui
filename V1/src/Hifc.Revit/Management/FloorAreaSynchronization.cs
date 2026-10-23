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
            var scan=Read(doc);var plan=FloorAreaPolicy.Build(scan.Elements,scan.Planning);
            if(plan.Issues.Length>0)throw new InvalidOperationException(string.Join("\n",plan.Issues));
            var mapped=mappedElements.Select(e=>e.UniqueId).ToArray();
            if(plan.Allocations.Any(c=>!mapped.Contains(c.UniqueId)))throw new InvalidOperationException("楼层面积同步包含尚未写入分类的建筑区域，请先完成全面检查并写入。");
            return plan;
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
