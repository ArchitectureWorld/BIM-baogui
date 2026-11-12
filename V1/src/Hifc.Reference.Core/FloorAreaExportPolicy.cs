using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Hifc.Reference
{
    // Export is a projection of saved inputs, not an area-edit operation.
    // Footprint/storey values and area/coefficient inputs remain independent.
    public static class FloorAreaExportPolicy
    {
        static string Amount(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
        static bool Region(ModelElementCheck row) => ReferenceCatalog.LegacySlot(row.Slot ?? "") == "建筑区域";
        static InputReviewItem Notice(ModelElementCheck level, string code, string detail, string value = null)
            => new InputReviewItem { ElementId=level?.Id ?? 0, Name=level?.Name ?? "楼层范围",
                PropertySet=FloorAreaPolicy.FloorSet, Property="楼层面积", Value=value,
                Code=code, Detail=detail, BlocksExport=false };

        public static FloorAreaPlan Build(ModelElementCheck[] rows, PlanningSettings settings)
        {
            rows=rows ?? new ModelElementCheck[0];
            settings=settings ?? new PlanningSettings();
            var notices=new List<InputReviewItem>();
            var selected=settings.LevelsConfirmed ? settings.BuildingLevelUniqueIds ?? new string[0]
                : rows.Where(Region).Select(r=>r.LevelUniqueId).Where(id=>!string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
            var levels=rows.Where(r=>r.Entity=="IfcBuildingStorey" && selected.Contains(r.UniqueId)).ToArray();
            if(selected.Distinct().Count()!=selected.Length || selected.Any(id=>levels.Count(l=>l.UniqueId==id)!=1))
                notices.Add(Notice(null,"FLOOR_AREA_LEVEL_SCOPE",
                    "楼层范围包含缺失或重复标高；本次不汇总该范围、不分配面积，保留模型已写值导出。"));

            foreach(var level in levels.OrderBy(l=>l.Id))
            {
                var regions=rows.Where(r=>r.LevelUniqueId==level.UniqueId && Region(r)).ToArray();
                var pending=rows.Where(r=>r.LevelUniqueId==level.UniqueId && string.IsNullOrWhiteSpace(r.Slot)
                    && r.Candidates.Any(c=>c.Slot=="建筑区域")).ToArray();
                if(pending.Length>0)
                    notices.Add(Notice(level,"FLOOR_AREA_UNCLASSIFIED",level.Name+"：存在 "+pending.Length+
                        " 个未分类建筑区域候选（"+string.Join("、",pending.Select(r=>r.Id))+"）；未将其计入已分类区域合计，也不自动赋类目。按当前属性导出。"));
                foreach(var issue in regions.Select(r=>r.AreaAggregationIssue).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct())
                    notices.Add(Notice(level,"FLOOR_AREA_SCOPE_REVIEW",level.Name+"："+issue+
                        "。仅提示投影范围问题，不扣减、不改写面积，按当前属性导出。"));
                if(regions.Length==0)continue;

                var floor=level.Fields.SingleOrDefault(f=>f.PropertySet==FloorAreaPolicy.FloorSet && f.Property=="楼层面积");
                var target=FloorAreaPolicy.Number(floor);
                var amounts=regions.Select(r=>FloorAreaPolicy.Number(r.Fields.SingleOrDefault(f=>
                    f.PropertySet==FloorAreaPolicy.RegionSet && f.Property=="投影面积"))).ToArray();
                if(amounts.Any(a=>!a.HasValue || a.Value<0))
                {
                    notices.Add(Notice(level,"FLOOR_AREA_VALUES_INCOMPLETE",level.Name+
                        "：已分类区域面积存在空值或无效值，无法汇总；不以几何或0补值。必填、类型及来源检查仍独立执行。",floor?.Value));
                    continue;
                }
                var total=amounts.Sum(a=>a.Value);
                if(!target.HasValue || target.Value<0)
                {
                    notices.Add(Notice(level,"FLOOR_AREA_VALUES_INCOMPLETE",level.Name+
                        "：标高面积属性未填写或无效；已分类区域投影属性合计 "+Amount(total)+
                        "㎡。不自动填写标高面积，保留各项当前值导出。",floor?.Value));
                    continue;
                }
                var difference=target.Value-total;
                if(Math.Abs(difference)<.005m)continue;
                var ratio=total>0 ? "（"+(Math.Abs(difference)/total*100m).ToString("F2",CultureInfo.InvariantCulture)+"%）" : "（区域合计为0，不计算比例）";
                notices.Add(Notice(level,"FLOOR_AREA_DIFFERENCE",level.Name+"：标高面积属性 "+Amount(target.Value)+
                    "㎡，已分类区域投影属性合计 "+Amount(total)+"㎡，差额 "+Amount(difference)+"㎡"+ratio+
                    "。这里只对照投影属性，基底、建筑面积和计容面积不强制等同；保留面积及计算/计容系数原值，不自动分摊，允许导出。",floor?.Value));
            }
            return new FloorAreaPlan { Notices=notices.ToArray() };
        }
    }
}
