using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Hifc.Reference
{
    public sealed class FloorAreaAllocation
    {
        public int ElementId { get; set; }
        public string UniqueId { get; set; }
        public string LevelUniqueId { get; set; }
        public string Source { get; set; }
        public string ValueM2 { get; set; }
        public string FloorAreaM2 { get; set; }
        public string WeightM2 { get; set; }
        public string Reason { get; set; }
    }
    public sealed class FloorAreaPlan
    {
        public FloorAreaAllocation[] Allocations { get; set; } = new FloorAreaAllocation[0];
        public string[] Issues { get; set; } = new string[0];
    }
    public static class FloorAreaPolicy
    {
        public const string FloorSet="Pset_建筑楼层信息属性集";
        public const string RegionSet="Pset_建筑区域信息属性集";
        public static decimal? Number(FieldCheck field)
        {
            if(field==null||field.State=="人工清空"||field.State=="值非法"||field.State=="有歧义"||field.State=="类型待核验")return null;
            var text=field.MetricValue.HasValue?field.MetricValue.Value.ToString("R",CultureInfo.InvariantCulture):field.Value;
            return decimal.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)?value:(decimal?)null;
        }
        static decimal? Weight(ModelElementCheck region)
        {
            var field=region.Fields.SingleOrDefault(f=>f.PropertySet==RegionSet&&f.Property=="投影面积");
            if(field==null||field.State=="人工清空")return null;
            if(!string.IsNullOrWhiteSpace(field.Value))return Number(field);
            return decimal.TryParse(region.ProjectedAreaM2?.ToString("R",CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)?value:(decimal?)null;
        }
        public static FloorAreaPlan Build(ModelElementCheck[] rows,PlanningSettings settings)
        {
            var changes=new List<FloorAreaAllocation>();var issues=new List<string>();
            settings=settings??new PlanningSettings();
            var selected=settings.LevelsConfirmed?settings.BuildingLevelUniqueIds:PlanningInputs.DefaultBuildingLevels(rows,settings.RuleProfileId);
            var levels=rows.Where(r=>r.Entity=="IfcBuildingStorey"&&(selected??new string[0]).Contains(r.UniqueId)).ToArray();
            if(selected==null||selected.Distinct().Count()!=selected.Length||selected.Any(id=>levels.Count(l=>l.UniqueId==id)!=1))return new FloorAreaPlan();
            foreach(var level in levels)
            {
                var field=level.Fields.SingleOrDefault(f=>f.PropertySet==FloorSet&&f.Property=="楼层面积");
                var target=Number(field);
                if(!target.HasValue||target.Value<=0||string.IsNullOrWhiteSpace(field.Value))continue;
                var regions=rows.Where(r=>r.LevelUniqueId==level.UniqueId&&PlanningInputs.EffectiveSlot(r)=="建筑区域")
                    .OrderBy(r=>r.UniqueId,StringComparer.Ordinal).ToArray();
                if(regions.Length==0)continue;
                if(regions.Any(r=>!string.IsNullOrWhiteSpace(r.AreaAggregationIssue)))
                {issues.Add(level.Name+"：楼层面积同步未完成，建筑区域尚未确认齐全或存在投影重叠。");continue;}
                var weights=regions.Select(Weight).ToArray();
                if(weights.Any(w=>!w.HasValue||w.Value<0)||weights.Sum(w=>w.Value)<=0)
                {issues.Add(level.Name+"：楼层面积同步缺少有效区域面积，保留原值，请核对区域载体。");continue;}
                var total=weights.Sum(w=>w.Value);
                // Ignore source/export floating-point noise, but preserve a real manual override.
                if(Math.Abs(total-target.Value)<.005m&&regions.All(r=>Number(r.Fields.Single(f=>f.PropertySet==RegionSet&&f.Property=="投影面积")).HasValue))continue;
                // Unconfirmed candidates block redistribution, not export of already matching saved values.
                var pending=rows.Any(r=>r.LevelUniqueId==level.UniqueId&&string.IsNullOrEmpty(PlanningInputs.EffectiveSlot(r))&&r.Candidates.Any(c=>c.Slot=="建筑区域"));
                if(pending)
                {issues.Add(level.Name+"：楼层面积同步未完成，建筑区域尚未确认齐全或存在投影重叠。");continue;}
                var precision=Math.Max(2,(decimal.GetBits(target.Value)[3]>>16)&0xff);
                var shares=weights.Select(w=>target.Value*w.Value/total).ToArray();
                var values=shares.Select(v=>decimal.Round(v,precision,MidpointRounding.AwayFromZero)).ToArray();
                var order=Enumerable.Range(0,regions.Length).OrderByDescending(i=>weights[i].Value).ThenBy(i=>regions[i].UniqueId,StringComparer.Ordinal).ToArray();
                var remainder=target.Value-values.Sum();
                if(remainder>=0)values[order[0]]+=remainder;
                else foreach(var i in order){var deduct=Math.Min(values[i],-remainder);values[i]-=deduct;remainder+=deduct;if(remainder==0)break;}
                if(values.Sum()!=target.Value||values.Any(v=>v<0))throw new InvalidOperationException("楼层面积分摊精度校验失败。");
                for(int i=0;i<regions.Length;i++)
                {
                    var region=regions[i];var area=region.Fields.Single(f=>f.PropertySet==RegionSet&&f.Property=="投影面积");
                    if(Number(area)==values[i])continue;
                    changes.Add(new FloorAreaAllocation{ElementId=region.Id,UniqueId=region.UniqueId,LevelUniqueId=level.UniqueId,
                        Source=area.Source,ValueM2=values[i].ToString("F"+precision,CultureInfo.InvariantCulture),
                        FloorAreaM2=target.Value.ToString(CultureInfo.InvariantCulture),WeightM2=weights[i].Value.ToString(CultureInfo.InvariantCulture),
                        Reason="按已填写楼层面积 "+level.Name+" / "+target.Value.ToString(CultureInfo.InvariantCulture)+" m²，按区域原面积比例同步；几何不变"});
                }
            }
            return new FloorAreaPlan{Allocations=changes.ToArray(),Issues=issues.ToArray()};
        }
        public static FloorAreaPlan Populate(ModelElementCheck[] rows,PlanningSettings settings)
        {
            foreach(var f in rows.SelectMany(r=>r.Fields).Where(f=>f.FloorAreaOverride))
            {f.FloorAreaOverride=false;f.SuggestedValue=null;f.SuggestionUnit=null;f.SuggestionReason=null;}
            foreach(var row in rows)row.AreaSynchronizationIssue=null;
            var plan=Build(rows,settings);
            foreach(var c in plan.Allocations)
            {
                var f=rows.Single(r=>r.UniqueId==c.UniqueId).Fields.Single(x=>x.PropertySet==RegionSet&&x.Source==c.Source);
                f.SuggestedValue=c.ValueM2;f.SuggestionUnit="m²";f.SuggestionReason=c.Reason;f.FloorAreaOverride=true;
            }
            if(plan.Issues.Length>0&&rows.Length>0)rows[0].AreaSynchronizationIssue=string.Join("\n",plan.Issues);
            return plan;
        }
    }
}
