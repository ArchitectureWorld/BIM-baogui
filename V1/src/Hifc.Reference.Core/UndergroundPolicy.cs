using System;
using System.Globalization;
using System.Linq;

namespace Hifc.Reference
{
    public sealed class UndergroundSettings
    {
        public bool Confirmed { get; set; }
        public double? GroundElevationM { get; set; }
        public string[] LevelUniqueIds { get; set; } = new string[0];
        public string BaseLevelUniqueId { get; set; }
    }
    public static class FootprintPolicy
    {
        public static void Populate(FieldCheck field, UndergroundSettings settings, UndergroundLevel[] levels, string levelUniqueId)
        {
            if (field.Property != "建筑基底" || string.IsNullOrWhiteSpace(settings?.BaseLevelUniqueId)) return;
            var selected = (levels ?? new UndergroundLevel[0]).SingleOrDefault(l => l.UniqueId == settings.BaseLevelUniqueId);
            var empty = (field.State == "缺参数" || field.State == "未填写") && string.IsNullOrWhiteSpace(field.Value);
            if (selected == null)
            {
                if (empty) field.SuggestedValue = null;
                field.Detail = "已确认的基底楼层被删除或替换，请在“楼层 / 基底 / 基准”中重新确认。";
                return;
            }
            var suggested = levelUniqueId == selected.UniqueId ? "1" : "0";
            if (empty) field.SuggestedValue = suggested;
            var current = string.Equals(field.Value,"false",StringComparison.OrdinalIgnoreCase) ? "0"
                : string.Equals(field.Value,"true",StringComparison.OrdinalIgnoreCase) ? "1" : field.Value;
            field.SuggestionReason = "人工确认整栋建筑基底楼层：“" + selected.Name + "”；"
                + (empty ? "仅补齐空值" : "已有值保留" + (current != suggested ? "，与当前楼层设置不同，请核对" : ""));
        }
    }
    public sealed class UndergroundLevel
    {
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public double ElevationM { get; set; }
    }
    public sealed class UndergroundResult
    {
        public int? Count { get; set; }
        public double? HeightM { get; set; }
        public double? GroundElevationM { get; set; }
        public string Note { get; set; }
    }
    public static class UndergroundPolicy
    {
        public static UndergroundResult Evaluate(UndergroundSettings settings, UndergroundLevel[] levels)
        {
            Func<string,UndergroundResult> incomplete = reason=>new UndergroundResult {Note=reason};
            if(settings?.Confirmed!=true)return incomplete("地下指标尚未确认；可在“地下楼层 / 基准”中选择建筑楼层和地坪基准，已有值保留。");
            if(!settings.GroundElevationM.HasValue || !Finite(settings.GroundElevationM.Value))return incomplete("请确认有效的地坪基准（项目标高，m）。");
            var ids=settings.LevelUniqueIds??new string[0];
            if(ids.Distinct().Count()!=ids.Length)return incomplete("地下楼层重复，请重新确认。");
            var selected=ids.Select(id=>(levels??new UndergroundLevel[0]).SingleOrDefault(l=>l.UniqueId==id)).ToArray();
            if(selected.Any(l=>l==null))return incomplete("已确认的地下楼层被删除或替换，请重新确认；本轮不沿用旧层数。");
            if(selected.Any(l=>!Finite(l.ElevationM)||l.ElevationM>=settings.GroundElevationM.Value-1e-6))return incomplete("所选地下楼层不低于地坪基准，请核对建筑楼层与基准。");
            var ordered=selected.OrderBy(l=>l.ElevationM).ToArray();
            if(ordered.Skip(1).Where((l,i)=>Math.Abs(l.ElevationM-ordered[i].ElevationM)<1e-6).Any())return incomplete("所选楼层存在同标高，请排除重复的结构或辅助标高后重新确认。");
            return new UndergroundResult {Count=selected.Length,GroundElevationM=settings.GroundElevationM,
                HeightM=selected.Length==0?0:settings.GroundElevationM.Value-selected.Min(l=>l.ElevationM),
                Note="本轮按人工确认的建筑楼层重新计算；高度为地坪基准至最低所选楼层标高差（m），属于建议，可按实际地下建筑高度调整。"};
        }
        static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        public static void Populate(FieldCheck field,UndergroundResult result,double? existingMetric=null)
        {
            if(!new[]{"地下建筑层数","地下建筑高度","地坪标高"}.Contains(field.Property))return;
            result=result??new UndergroundResult {Note="请先确认地下楼层和地坪基准。"};
            double? proposed=field.Property=="地下建筑层数"?result.Count:field.Property=="地下建筑高度"?result.HeightM:result.GroundElevationM;
            if(!proposed.HasValue){field.Detail=result.Note;return;}
            var text=proposed.Value.ToString("0.######",CultureInfo.InvariantCulture);
            if((field.State=="缺参数"||field.State=="未填写")&&string.IsNullOrWhiteSpace(field.Value))
            {field.SuggestedValue=text;field.SuggestionUnit=field.Property=="地下建筑层数"?null:"m";field.SuggestionReason=result.Note;}
            else
            {
                double current;
                var known=existingMetric.HasValue || double.TryParse(field.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out current);
                current=existingMetric??(double.TryParse(field.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var parsed)?parsed:double.NaN);
                field.SuggestionReason=(known&&Math.Abs(current-proposed.Value)>1e-5?"现值与本轮建议不同；保留现值，请人工核对。":"已有值保留。")+" 本轮建议："+text+"；"+result.Note;
            }
        }
    }
}
