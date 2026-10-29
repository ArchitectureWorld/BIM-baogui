using System;
using System.Linq;
using System.Web.Script.Serialization;
using Hifc.Reference;

internal static class SummaryCacheTests
{
    const string Profile="hubei-420626-planning-20261020";
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run(Action<string,Action> check)
    {
        check("summary_candidates_preserve_explicit_roles_and_staged_suggestions",()=>{
            var rows=new[]{
                new ModelElementCheck{Id=1,UniqueId="balcony",Name="阳台",Entity="IfcSlab",Category="楼板"},
                new ModelElementCheck{Id=2,UniqueId="explicit-region",Name="阳台",Entity="IfcSlab",Category="楼板",Slot="建筑区域",Slots=new[]{"建筑区域"}},
                new ModelElementCheck{Id=3,UniqueId="parking",Name="未命名区域",Entity="IfcSpace",AreaScheme="建筑面积",SuggestedSlots=new[]{"停车位"}},
                new ModelElementCheck{Id=4,UniqueId="multi-role-region",Name="楼面",Entity="IfcSlab",Category="楼板",Slots=new[]{"建筑区域"}}
            };
            var serializer=new JavaScriptSerializer();var original=serializer.Serialize(rows);
            var summary=CaseWorkflow.Summarize("报规","单体建筑",rows,Profile);
            Assert(summary.Roles.Single(r=>r.Slot=="阳台").Candidates==1,"an explicitly classified region was suggested again as a balcony");
            Assert(summary.Roles.Single(r=>r.Slot=="停车位").Candidates==1,"staged parking suggestion disappeared");
            var region=summary.Roles.Single(r=>r.Slot=="建筑区域");
            Assert(region.Count==2&&region.Candidates==0,"explicit or multi-role classification was counted as a pending candidate");
            Assert(summary.ElementCount==4&&summary.ClassifiedCount==2&&summary.ReviewCount==1&&summary.MissingRequired==2&&!summary.Ready,"summary totals or completion changed");
            Assert(summary.Roles.Select(r=>r.Slot).SequenceEqual(summary.Roles.Select(r=>r.Slot).OrderBy(s=>s,StringComparer.Ordinal)),"role ordering changed");
            Assert(serializer.Serialize(rows)==original,"summarizing mutated the scan or staged suggestions");
        });
        check("summary_candidates_are_recomputed_after_profile_or_row_changes",()=>{
            var row=new ModelElementCheck{Id=1,UniqueId="parking-area",Name="室内停车场",Entity="IfcSpace",AreaScheme="建筑面积"};
            var active=CaseWorkflow.Summarize("报规","单体建筑",new[]{row},Profile);
            Assert(active.Roles.Single(r=>r.Slot=="室内停车场").Candidates==1&&active.Roles.Single(r=>r.Slot=="停车场").Candidates==0,"active indoor parking route changed");
            var legacy=CaseWorkflow.Summarize("报规","单体建筑",new[]{row},"");
            Assert(legacy.Roles.Single(r=>r.Slot=="停车场").Candidates==1,"a candidate result leaked across rule profiles");
            row.Name="未命名区域";
            var renamed=CaseWorkflow.Summarize("报规","单体建筑",new[]{row},Profile);
            Assert(renamed.Roles.All(r=>r.Candidates==0),"a candidate result survived a model-row change in a later summary");
        });
    }
}
