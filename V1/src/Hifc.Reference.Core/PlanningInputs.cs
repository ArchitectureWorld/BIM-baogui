using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Hifc.Reference
{
    public sealed class PlanningRoleChoice
    {
        public string UniqueId { get; set; }
        public string Slot { get; set; }
        public string Identity { get; set; }
    }
    public sealed class PlanningSettings
    {
        public string RuleProfileId { get; set; } = "";
        public PlanningRoleChoice[] Roles { get; set; } = new PlanningRoleChoice[0];
        public Dictionary<string,string> CommonValues { get; set; } = new Dictionary<string,string>();
        public bool LevelsConfirmed { get; set; }
        public string[] BuildingLevelUniqueIds { get; set; } = new string[0];
        public string TopBoundaryLevelUniqueId { get; set; }
    }
    public sealed class PlanningValueOverride
    {
        public string OwnerUniqueId { get; set; }
        public string PropertySet { get; set; }
        public string Source { get; set; }
        public string Binding { get; set; }
        public string Value { get; set; }
        public double? MetricValue { get; set; }
    }
    public static class PlanningInputs
    {
        public static ReferenceProperty[] CommonFields(string kind, string ruleProfileId)
        {
            if (!PlatformRules.IsActive(ruleProfileId)) return CommonFields(kind);
            var common = new[] { "主功能类别", "子功能类别", "公建大类", "公建小类", "计容系数", "计算系数",
                "建筑名称", "建筑编号", "建筑类型名称", "建筑类别", "建筑用途名称", "建筑状态", "临时永久",
                "结构类型", "耐火等级", "安全等级", "形状", "民用建筑", "山墙开窗", "绿地类型", "折算系数",
                "区内道路类型", "道路类型", "车场类型", "停车位类型", "公建车位", "红线宽度", "红线类型" };
            return CaseWorkflow.ForModel("报规", kind, ruleProfileId).SelectMany(r => r.Properties)
                .Where(p => common.Contains(p.Property)).GroupBy(p => p.PropertySet + "|" + p.Property)
                .Select(g => g.First()).ToArray();
        }
        public static string ParentValue(FieldCheck[] fields, string pset, bool includeSuggestions = false)
        {
            var parent = fields.FirstOrDefault(f => f.PropertySet == pset &&
                (f.Property == "主功能类别" || f.Property == "公建大类"));
            return !string.IsNullOrWhiteSpace(parent?.Value) ? parent.Value :
                includeSuggestions && parent?.State != "人工清空" ? parent?.SuggestedValue : null;
        }
        public static void ValidateFields(ModelElementCheck[] rows)
        {
            foreach (var row in rows) ControlLinePolicy.Populate(row);
            PlatformInputReview.ApplyManualPolicy(rows);
            foreach (var row in rows)
                foreach (var field in row.Fields.Where(f=>PlatformRules.IsActive(f.RuleProfileId)))
                    field.AllowedEmpty = string.IsNullOrEmpty(field.RequiredValue) && !field.ManualInputRequired && (!PlatformRules.RequiredFields(field.PropertySet).Contains(field.Property) ||
                        IsComputeExcluded(row.Fields,field.PropertySet) && !IsComputeField(field));
            foreach (var row in rows)
                foreach (var field in row.Fields.Where(f => PlatformRules.IsActive(f.RuleProfileId) &&
                    !string.IsNullOrWhiteSpace(f.Value) && (f.State == "有效" || f.State == "值非法")))
                {
                    var error = ModelChecks.InputError(field, field.Value, ParentValue(row.Fields, field.PropertySet));
                    field.State = error == null ? "有效" : "值非法";
                    if (error != null) field.Detail = error;
                }
        }
        static bool IsComputeField(FieldCheck field) => PlatformRules.Rules(field.PropertySet).Any(r=>r.ComputeField==field.Property);
        public static bool IsComputeExcluded(FieldCheck[] fields, string pset)
        {
            var gate=PlatformRules.Rules(pset).Select(r=>r.ComputeField).FirstOrDefault(s=>!string.IsNullOrWhiteSpace(s));
            var value=fields.FirstOrDefault(f=>f.PropertySet==pset&&f.Property==gate)?.Value?.Trim();
            return value=="0"||value=="否"||string.Equals(value,"false",StringComparison.OrdinalIgnoreCase);
        }
        public static bool AcceptableForRules(FieldCheck field, FieldCheck[] fields, string profileId)
        {
            if (field.RequiresRuleWrite) return false;
            if (!PlatformRules.IsActive(profileId)) return field.Acceptable;
            if (!string.IsNullOrWhiteSpace(field.Value)) return field.Valid &&
                ModelChecks.InputError(field,field.Value,ParentValue(fields,field.PropertySet),profileId)==null;
            if(field.ManualInputRequired || field.State!="未填写"&&field.State!="缺参数")return false;
            return !PlatformRules.RequiredFields(field.PropertySet).Contains(field.Property) ||
                IsComputeExcluded(fields,field.PropertySet)&&!IsComputeField(field);
        }
        public static ModelElementCheck[] WithDraftValues(ModelElementCheck[] rows,PlanningValueOverride[] values)
        {
            var serializer=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=128*1024*1024};
            var result=serializer.Deserialize<ModelElementCheck[]>(serializer.Serialize(rows));
            foreach(var field in result.SelectMany(r=>r.Fields)) {
                var value=(values??new PlanningValueOverride[0]).LastOrDefault(v=>v.OwnerUniqueId==field.OwnerUniqueId &&
                    v.PropertySet==field.PropertySet && v.Source==field.Source && v.Binding==field.Binding);
                if(value==null)continue;
                field.Value=value.Value;field.MetricValue=value.MetricValue;field.SuggestedValue=null;
                field.State=string.IsNullOrWhiteSpace(value.Value)?"人工清空":"有效";
            }
            ValidateFields(result);
            return result;
        }
        public static string Identity(ModelElementCheck row) => string.Join("|",new[]{
            row.UniqueId,row.Name,row.TypeName,row.SourceEntity??row.Entity,row.LevelUniqueId,row.ProjectionKey,
            row.ProjectedAreaM2?.ToString("0.######",CultureInfo.InvariantCulture)});
        public static ClassificationCandidate[] Candidates(ModelElementCheck row, string kind, PlanningSettings settings)
        {
            var candidates=CaseWorkflow.GetCandidates(row,kind,settings?.RuleProfileId);
            if(!string.IsNullOrWhiteSpace(row.Slot))return candidates;
            var choices=(settings?.Roles??new PlanningRoleChoice[0]).Where(c=>c.UniqueId==row.UniqueId).ToArray();
            if(choices.Length==0)return candidates;
            var choice=choices[0];
            var rule=CaseWorkflow.ForModel("报规",kind,settings?.RuleProfileId).SingleOrDefault(r=>r.Slot==choice.Slot);
            bool valid=choices.Length==1 && choice.Identity==Identity(row) && rule!=null && CaseWorkflow.CanClassify(rule,row)
                && string.IsNullOrEmpty(row.CoverageNote);
            return new[]{new ClassificationCandidate{Slot=choice.Slot,CanPrepare=valid,RequiresReview=!valid,
                Evidence=valid?"本项目计算设置已确认业务范围；当前名称、载体、楼层及投影与确认记录一致":
                    "计算设置中的对象身份、几何或载体已变化，请重新确认；未复用旧分类"}};
        }
        public static string EffectiveSlot(ModelElementCheck row) => !string.IsNullOrWhiteSpace(row.Slot)?row.Slot:
            row.Candidates.Length==1 && row.Candidates[0].CanPrepare ? row.Candidates[0].Slot : null;
        public static string[] DefaultBuildingLevels(ModelElementCheck[] rows, string profileId) => rows
            .Where(r=>EffectiveSlot(r)=="建筑区域" || PlatformRules.IsActive(profileId)&&EffectiveSlot(r)=="室内停车场")
            .Select(r=>r.LevelUniqueId).Where(id=>!string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        static bool Empty(FieldCheck field)=>(field.State=="缺参数"||field.State=="未填写")&&string.IsNullOrWhiteSpace(field.Value);
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        static double? Number(string text)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&Finite(value)?value:(double?)null;
        static void Suggest(FieldCheck field,string value,string reason,string unit=null)
        {
            if(Empty(field)){field.SuggestedValue=value;field.SuggestionUnit=unit;field.SuggestionReason=reason;}
            else if(value!=null)field.SuggestionReason="已有值保留；本轮计算建议 "+value+(unit==null?"":" "+unit)+"；"+reason;
            if(value==null)field.Detail=reason;
        }
        static void Metric(ModelElementCheck row,string property,double? value,string reason,string unit=null)
        {
            foreach(var field in row.Fields.Where(f=>f.Property==property))
                Suggest(field,value?.ToString("0.######",CultureInfo.InvariantCulture),reason,unit);
        }
        public static ReferenceProperty[] CommonFields(string kind)
        {
            var allowed=new Dictionary<string,string[]> {
                ["建筑区域"]=new[]{"主功能类别","子功能类别","计容系数","计算系数"},
                ["建筑技术"]=new[]{"建筑名称","建筑编号","建筑类型名称","建筑类别","建筑用途名称","建筑状态","临时永久","结构类型","耐火等级","安全等级"},
                ["绿地"]=new[]{"绿地类型","折算系数"}
            };
            return CaseWorkflow.ForModel("报规",kind).Where(r=>allowed.ContainsKey(r.Slot))
                .SelectMany(r=>r.Properties.Where(p=>allowed[r.Slot].Contains(p.Property))).ToArray();
        }
        public static string[] DuplicateLandIssues(ModelElementCheck[] rows) => rows
            .Where(r=>EffectiveSlot(r)=="规划净用地"&&!string.IsNullOrEmpty(r.ProjectionKey))
            .GroupBy(r=>r.ProjectionKey).Where(g=>g.Count()>1)
            .Select(g=>"规划净用地重复投影："+string.Join("、",g.Select(r=>r.Id+" / "+r.Name))+"；请仅确认一个计量载体").ToArray();

        public static void Populate(ModelElementCheck[] rows, PlanningSettings settings, UndergroundSettings underground)
        {
            settings=settings??new PlanningSettings();
            FloorAreaPolicy.Populate(rows,settings);
            foreach(var field in rows.SelectMany(r=>r.Fields)) {
                field.RuleProfileId=settings.RuleProfileId??"";
                // Recompute only suggestions owned by the common-value policy.
                // A changed parent/profile or removed common value must not reuse them.
                if(field.SuggestionReason?.StartsWith("本项目人工确认的统一设置",StringComparison.Ordinal)==true) {
                    field.SuggestedValue=null;field.SuggestionUnit=null;field.SuggestionReason=null;
                }
            }
            var common=CommonFields("总图",settings.RuleProfileId).Concat(CommonFields("单体建筑",settings.RuleProfileId))
                .GroupBy(p=>p.PropertySet+"|"+p.Property).ToDictionary(g=>g.Key,g=>g.First());
            foreach(var row in rows)
                foreach(var field in row.Fields)
                {
                    var key=ReferenceCatalog.LegacySlot(field.PropertySet)+"|"+field.Property;
                    if(!common.ContainsKey(field.PropertySet+"|"+field.Property) ||
                        settings.CommonValues==null || !settings.CommonValues.TryGetValue(key,out var value) || string.IsNullOrWhiteSpace(value))continue;
                    field.RuleProfileId=settings.RuleProfileId??"";
                    var parent=ParentValue(row.Fields,field.PropertySet,true);
                    settings.CommonValues.TryGetValue(ReferenceCatalog.LegacySlot(field.PropertySet)+"|"+(field.Property=="子功能类别"?"主功能类别":"公建大类"),out var commonParent);
                    if(ClassificationInputs.IsChild(field.Property)) {
                        // Explicit custom pairs may be reused within their own
                        // parent scope, never copied into a different existing parent.
                        if(string.IsNullOrWhiteSpace(parent))parent=commonParent;
                        else if(!string.IsNullOrWhiteSpace(commonParent)&&parent.Trim()!=commonParent.Trim())continue;
                        if(string.IsNullOrWhiteSpace(commonParent)&&PlatformRules.IsActive(settings.RuleProfileId)&&PlatformRules.ValueError(field.PropertySet,field.Property,value,parent)!=null)continue;
                    }
                    var error=ModelChecks.InputError(field,value,parent);
                    if(error!=null || field.RawType=="Real" && Number(value)<0)
                    {field.Detail="项目统一设置的值无效，请重新确认："+value+(error==null?"":"；"+error);continue;}
                    Suggest(field,value,"本项目人工确认的统一设置；仅补齐空值，可在构件属性中单独调整");
                }
            ValidateFields(rows);
            var regions=rows.Where(r=>EffectiveSlot(r)=="建筑区域").ToArray();
            var allLevels=rows.Where(r=>r.Entity=="IfcBuildingStorey"&&r.LevelElevationM.HasValue).ToArray();
            var selectedIds=settings.LevelsConfirmed?(settings.BuildingLevelUniqueIds??new string[0]):
                DefaultBuildingLevels(rows,settings.RuleProfileId);
            var levels=allLevels.Where(r=>selectedIds.Contains(r.UniqueId)).OrderBy(r=>r.LevelElevationM).ToArray();
            bool validLevels=selectedIds.Length>0 && selectedIds.Distinct().Count()==selectedIds.Length &&
                selectedIds.All(id=>allLevels.Count(l=>l.UniqueId==id)==1) &&
                levels.Select(l=>Math.Round(l.LevelElevationM.Value,6)).Distinct().Count()==levels.Length;
            var pending=rows.Where(r=>string.IsNullOrEmpty(EffectiveSlot(r))&&r.Candidates.Any(c=>c.Slot=="建筑区域")).ToArray();
            var projectRows=rows.Where(r=>r.Entity=="IfcBuilding").ToArray();
            if(!validLevels)
            {
                foreach(var level in allLevels)foreach(var property in new[]{"建筑层高","计算标高","楼层面积"})
                    Metric(level,property,null,"尚未确定实际建筑楼层，或已确认楼层已被删除/替换；请在计算设置核对。");
                return;
            }
            for(int index=0;index<levels.Length;index++)
            {
                var level=levels[index];
                var upper=index+1<levels.Length?levels[index+1]:allLevels.SingleOrDefault(l=>l.UniqueId==settings.TopBoundaryLevelUniqueId);
                double? height=upper?.LevelElevationM>level.LevelElevationM?upper.LevelElevationM-level.LevelElevationM:null;
                Metric(level,"计算标高",level.LevelElevationM,"当前 Revit 楼层项目标高；采用项目坐标基准","m");
                Metric(level,"建筑层高",height,height.HasValue?"当前实际楼层至下一确认楼层/上边界的标高差；不采用辅助标高":
                    "最高实际楼层缺少已确认上边界，未以 0 或其他楼层高度代填","m");
                var levelRegions=regions.Where(r=>r.LevelUniqueId==level.UniqueId).ToArray();
                var area=SumAreas(levelRegions,false);
                if(pending.Any(r=>r.LevelUniqueId==level.UniqueId))area=null;
                Metric(level,"楼层面积",area,area.HasValue?"本层已确认建筑区域的投影面积合计，保留孔洞；未乘计容系数":
                    "本层区域未确认齐全，或投影缺失/重叠；暂不输出部分面积作为整层面积","m²");
            }
            bool completeScope=regions.Length>0 && pending.Length==0 && regions.All(r=>levels.Any(l=>l.UniqueId==r.LevelUniqueId));
            foreach(var project in projectRows)
            {
                var ground=underground?.Confirmed==true?underground.GroundElevationM:null;
                Metric(project,"地上建筑层数",ground.HasValue&&completeScope?(double?)levels.Count(l=>l.LevelElevationM>=ground.Value-1e-6):null,
                    "按已确认的实际建筑楼层及地坪基准计数；不使用未承载建筑区域的辅助标高");
                var area=completeScope?SumAreas(regions,true):null;
                Metric(project,"建筑面积",area,area.HasValue?"已确认建筑区域投影面积 × 各自计算系数合计（非计容面积）":
                    "建筑区域范围、面积或计算系数尚不完整；未将部分区域当作整栋建筑面积","m²");
                var footprint=regions.Where(r=>r.LevelUniqueId==underground?.BaseLevelUniqueId).ToArray();
                var footprintArea=completeScope&&!string.IsNullOrWhiteSpace(underground?.BaseLevelUniqueId)?SumAreas(footprint,false):null;
                Metric(project,"投影面积",footprintArea,"仅汇总人工确认基底楼层的建筑区域投影；请核对整栋建筑基底统计范围","m²");
            }
        }
        static double? SumAreas(ModelElementCheck[] regions,bool calculated)
        {
            if(regions.Length==0 || regions.Any(r=>!string.IsNullOrWhiteSpace(r.AreaAggregationIssue)))return null;
            double total=0;
            foreach(var region in regions)
            {
                var areaField=region.Fields.FirstOrDefault(f=>f.Property=="投影面积"&&f.PropertySet=="Pset_建筑区域信息属性集");
                var area=areaField?.FloorAreaOverride==true?Number(areaField.SuggestedValue):areaField?.State=="人工清空"?null:!string.IsNullOrWhiteSpace(areaField?.Value)?areaField.MetricValue??Number(areaField.Value):region.ProjectedAreaM2;
                if(!area.HasValue||!Finite(area.Value)||area.Value<=0)return null;
                double factor=1;
                if(calculated) {
                    var field=region.Fields.FirstOrDefault(f=>f.Property=="计算系数"&&f.PropertySet=="Pset_建筑区域信息属性集");
                    var value=Number(!string.IsNullOrWhiteSpace(field?.Value)?field.Value:field?.SuggestedValue);
                    if(!value.HasValue||value.Value<0)return null;factor=value.Value;
                }
                total+=area.Value*factor;
            }
            return total;
        }
        public static string[] Issues(ModelElementCheck[] rows, string kind, string ruleProfileId)
        {
            if (!PlatformRules.IsActive(ruleProfileId)) return Issues(rows, kind);
            var issues = new List<string>(DuplicateLandIssues(rows));
            issues.AddRange(PlatformInputReview.Build(rows,ruleProfileId)
                .Where(i=>i.Code=="HIFC_PLATFORM_DEFAULT_RISK")
                .GroupBy(i=>i.PropertySet+"|"+i.Property+"|"+i.DefaultValue)
                .Select(g=>ReferenceCatalog.LegacySlot(g.First().PropertySet)+" / "+g.First().Property+"："+g.Count()+" 项未填写；"+g.First().Detail));
            foreach (var rule in CaseWorkflow.ForModel("报规", kind, ruleProfileId))
            {
                var pset = rule.Properties.First().PropertySet;
                var owners = rows.Where(r => EffectiveSlot(r) == rule.Slot || r.Slots.Contains(rule.Slot) ||
                    !r.InventoryOnly && (rule.Slot == "建筑技术" && r.Entity == "IfcBuilding" ||
                        rule.Slot == "建筑楼层" && r.Entity == "IfcBuildingStorey")).ToArray();
                if (owners.Length == 0) {
                    if (rule.Required) issues.Add(rule.Slot + "：未确认参与计算的载体");
                    continue;
                }
                var gaps = new List<string>(); var invalid = new List<string>(); var pending = 0;
                foreach (var owner in owners)
                {
                    var fields = owner.Fields.Where(f => f.PropertySet == pset).ToArray();
                    var branches = PlatformRules.Rules(pset);
                    var gate = branches.Select(b => b.ComputeField).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                    bool excluded = IsComputeExcluded(fields,pset);
                    var needed = PlatformRules.RequiredFields(pset).Where(name => !excluded || name == gate).ToArray();
                    foreach (var name in needed)
                    {
                        var field = fields.FirstOrDefault(f => f.Property == name);
                        if (field == null) { gaps.Add(name); continue; }
                        var error = ModelChecks.InputError(field, field.Value, ParentValue(fields, pset), ruleProfileId);
                        if (field.Valid && error == null) continue;
                        if (string.IsNullOrWhiteSpace(field.Value) && !string.IsNullOrWhiteSpace(field.SuggestedValue) &&
                            ModelChecks.InputError(field, field.SuggestedValue, ParentValue(fields, pset, true), ruleProfileId) == null) pending++;
                        else gaps.Add(name);
                    }
                    // Optional nonempty values still have to belong to the selected rule vocabulary.
                    foreach (var field in fields.Where(f => !string.IsNullOrWhiteSpace(f.Value))) {
                        var error = ModelChecks.InputError(field, field.Value, ParentValue(fields, pset), ruleProfileId);
                        if (error != null || field.State == "值非法") invalid.Add(field.Property);
                    }
                }
                if (gaps.Count > 0) issues.Add(rule.Slot + "：平台计算输入待补充/核对（" +
                    string.Join("、", gaps.GroupBy(p=>p).Select(g=>g.Key+" "+g.Count()+" 项")) + "）；可由模型、统一设置或人工调整提供");
                if (invalid.Count > 0) issues.Add(rule.Slot + "：已有值不符合所选平台规则（" + string.Join("、", invalid.Distinct()) + "）；原值保留，请核对");
                if (pending > 0) issues.Add(rule.Slot + "：已有 " + pending + " 项建议值，尚未写入模型");
            }
            var unconfirmed=rows.Count(r=>r.Candidates.Any(c=>c.RequiresReview));
            if(unconfirmed>0)issues.Add("业务范围待确认 "+unconfirmed+" 个对象；请在计算设置或管理清单核对");
            issues.AddRange(rows.Where(r=>!string.IsNullOrWhiteSpace(r.AreaAggregationIssue)).Select(r=>r.Id+"："+r.AreaAggregationIssue));
            issues.AddRange(rows.Where(r=>r.Fields.Any(f=>f.Property=="投影面积"&&f.SuggestionReason?.Contains("与本轮模型投影")==true))
                .Select(r=>r.Id+" / "+r.Name+"：已有面积与当前模型几何不同；保留现值，请核对"));
            issues.AddRange(rows.Select(r=>r.AreaSynchronizationIssue).Where(s=>!string.IsNullOrEmpty(s)));
            return issues.Distinct().ToArray();
        }
        public static string[] Issues(ModelElementCheck[] rows, string kind)
        {
            var issues=new List<string>(DuplicateLandIssues(rows));
            var needed=new Dictionary<string,string[]>{
                ["规划净用地"]=new[]{"名称","类型","版本","投影面积"},
                ["建筑区域"]=new[]{"主功能类别","子功能类别","计容系数","计算系数","投影面积"},
                ["建筑技术"]=new[]{"建筑名称","建筑类型名称","建筑类别","地上建筑层数","地下建筑层数","建筑面积","投影面积"},
                ["建筑楼层"]=new[]{"楼层名称","建筑层高","计算标高","楼层面积","建筑基底"}
            };
            var slots=kind=="总图"?new[]{"规划净用地"}:kind=="单体建筑"?new[]{"建筑区域","建筑技术","建筑楼层"}:new string[0];
            foreach(var slot in slots)
            {
                var owners=rows.Where(r=>EffectiveSlot(r)==slot||r.Slots.Contains(slot) ||
                    slot=="建筑技术"&&r.Entity=="IfcBuilding" || slot=="建筑楼层"&&r.Entity=="IfcBuildingStorey").ToArray();
                if(owners.Length==0){issues.Add(slot+"：未确认参与计算的载体");continue;}
                var inputs=owners.SelectMany(r=>r.Fields.Where(f=>ReferenceCatalog.LegacySlot(f.PropertySet)==slot && needed[slot].Contains(f.Property))).ToArray();
                var gaps=inputs.Where(f=>
                    !f.Valid && string.IsNullOrWhiteSpace(f.SuggestedValue)).Select(f=>f.Property).GroupBy(p=>p)
                    .Select(g=>g.Key+" "+g.Count()+" 项").ToArray();
                if(gaps.Length>0)issues.Add(slot+"：计算输入待补充/核对（"+string.Join("、",gaps)+"）；可由模型、统一设置或人工调整提供");
                var suggested=inputs.Count(f=>!f.Valid&&!string.IsNullOrWhiteSpace(f.SuggestedValue));
                if(suggested>0)issues.Add(slot+"：已有 "+suggested+" 项建议值，尚未写入模型");
            }
            var unconfirmed=rows.Count(r=>r.Candidates.Any(c=>c.RequiresReview));
            if(unconfirmed>0)issues.Add("业务范围待确认 "+unconfirmed+" 个对象；请在计算设置或管理清单核对");
            issues.AddRange(rows.Where(r=>!string.IsNullOrWhiteSpace(r.AreaAggregationIssue)).Select(r=>r.Id+"："+r.AreaAggregationIssue));
            issues.AddRange(rows.Where(r=>r.Fields.Any(f=>f.Property=="投影面积"&&f.SuggestionReason?.Contains("与本轮模型投影")==true))
                .Select(r=>r.Id+" / "+r.Name+"：已有面积与当前模型几何不同；保留现值，请核对"));
            issues.AddRange(rows.Select(r=>r.AreaSynchronizationIssue).Where(s=>!string.IsNullOrEmpty(s)));
            return issues.Distinct().ToArray();
        }
    }
}
