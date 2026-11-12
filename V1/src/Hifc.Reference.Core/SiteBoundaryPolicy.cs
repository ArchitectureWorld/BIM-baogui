using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Hifc.Reference
{
    // User-confirmed site requirement (2026-11-10), separate from frozen platform evidence.
    // These completeness findings are visible but do not add an export gate.
    public static class SiteBoundaryPolicy
    {
        public const string Slot="边界分段线";
        public const string PropertySet="Pset_边界分段线信息属性集";
        static readonly string[] Directions={"东边界","南边界","西边界","北边界"};

        public static InputReviewItem[] Review(ModelElementCheck[] rows,string kind,string profile)
        {
            if(kind!="总图"||!PlatformRules.IsActive(profile))return new InputReviewItem[0];
            var carriers=rows.Select((row,index)=>new{Row=row,Index=index})
                .Where(x=>x.Row.Fields.Any(f=>f.PropertySet==PropertySet)||x.Row.Classified&&
                    (ReferenceCatalog.LegacySlot(x.Row.Slot??"")==Slot||x.Row.Slots.Contains(Slot)))
                .GroupBy(x=>!string.IsNullOrEmpty(x.Row.UniqueId)?"uid:"+x.Row.UniqueId:
                    x.Row.Id>0?"id:"+x.Row.Id:"index:"+x.Index)
                .Select(g=>g.First().Row).ToArray();
            var issues=new List<InputReviewItem>();
            if(carriers.Length!=4)issues.Add(Item(null,"构件数量","SITE_BOUNDARY_COUNT",carriers.Length.ToString(CultureInfo.InvariantCulture),
                "边界分段线检查不通过：必须恰好 4 个独立构件，东、南、西、北各 1 个；当前 "+carriers.Length+" 个。"));
            var counts=Directions.ToDictionary(direction=>direction,direction=>carriers.Count(row=>Value(row,"边界类型")==direction));
            var missing=Directions.Where(direction=>counts[direction]==0).ToArray();
            var duplicate=Directions.Where(direction=>counts[direction]>1).ToArray();
            var unknown=carriers.Where(row=>!Directions.Contains(Value(row,"边界类型"))).ToArray();
            if(missing.Length>0||duplicate.Length>0||unknown.Length>0)
            {
                var detail="边界分段线四方位检查不通过："+string.Join("；",Directions.Select(direction=>direction+" "+counts[direction]+" 个"))+"。";
                if(missing.Length>0)detail+="缺少："+string.Join("、",missing)+"。";
                if(duplicate.Length>0)detail+="重复："+string.Join("；",duplicate.Select(direction=>direction+"（ID "+string.Join("、",carriers.Where(row=>Value(row,"边界类型")==direction).Select(row=>row.Id)) +"）"))+"。";
                if(unknown.Length>0)detail+="方位未填、非法或来源待核对：ID "+string.Join("、",unknown.Select(row=>row.Id))+"。";
                issues.Add(Item(null,"四方位完整性","SITE_BOUNDARY_DIRECTIONS",string.Join(" / ",Directions.Select(direction=>direction+" "+counts[direction])),detail));
            }
            foreach(var row in carriers.Where(row=>string.IsNullOrWhiteSpace(Value(row,"边界线编号"))))
                issues.Add(Item(row,"边界线编号","SITE_BOUNDARY_NUMBER_MISSING",null,"边界分段线编号未填写或来源待核对；四件编号须非空且唯一。"));
            foreach(var group in carriers.Where(row=>!string.IsNullOrWhiteSpace(Value(row,"边界线编号"))).GroupBy(row=>Value(row,"边界线编号")).Where(group=>group.Count()>1))
                foreach(var row in group)issues.Add(Item(row,"边界线编号","SITE_BOUNDARY_NUMBER_DUPLICATE",group.Key,
                    "边界分段线编号“"+group.Key+"”重复：ID "+string.Join("、",group.Select(item=>item.Id))+"；须分别编号，原值未改写。"));
            return issues.ToArray();
        }

        static string Value(ModelElementCheck row,string property)
        {
            var fields=row.Fields.Where(field=>field.PropertySet==PropertySet&&field.Property==property).ToArray();
            return fields.Length==1&&fields[0].Valid?fields[0].Value?.Trim():null;
        }
        static InputReviewItem Item(ModelElementCheck row,string property,string code,string value,string detail)
            =>new InputReviewItem{ElementId=row?.Id??0,Name=row?.Name??"总图边界分段线",PropertySet=PropertySet,
                Property=property,Code=code,Value=value,Detail=detail,BlocksExport=false};
    }
}
