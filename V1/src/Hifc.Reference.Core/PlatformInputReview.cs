using System;
using System.Collections.Generic;
using System.Linq;

namespace Hifc.Reference
{
    public sealed class InputReviewItem
    {
        public int ElementId { get; set; }
        public string Name { get; set; }
        public string PropertySet { get; set; }
        public string Property { get; set; }
        public string Value { get; set; }
        public string DefaultValue { get; set; }
        public string Code { get; set; }
        public string Detail { get; set; }
        public string CurrentText => string.IsNullOrWhiteSpace(Value) ? "未填写" : Value;
        public string DefaultText => string.IsNullOrWhiteSpace(DefaultValue) ? "无已知默认值" : DefaultValue;
    }

    // Review metadata describes consequences; it must never supply a model value.
    public static class PlatformInputReview
    {
        const string RoadSet="Pset_道路边界线信息属性集";
        const string BuildingUsageHint="建筑用途名称必填：请按实际用途确认并写入 Revit；缺失可能导致平台按住宅匹配法规。";
        public static string RequiredInputHint(FieldCheck field) => field.BuildingUsage
            ? BuildingUsageHint
            : "消防道路宽度需人工填写并写入 Revit。";
        public static void ApplyManualPolicy(ModelElementCheck[] rows)
        {
            foreach(var row in rows)
            {
                var use=row.Fields.FirstOrDefault(f=>f.PropertySet==RoadSet&&f.Property=="区内道路类型");
                var fire=use?.Value?.Trim()=="消防道路";
                foreach(var field in row.Fields.Where(f=>f.PropertySet==RoadSet&&f.Property=="道路宽度"))
                {
                    field.FireRoadWidth=fire;
                    field.AllowedEmpty=!fire;
                }
                foreach(var field in row.Fields.Where(f=>f.BuildingUsage))field.AllowedEmpty=false;
            }
        }

        public static string DefaultHint(string profileId,string pset,string property,string value,string parent=null,FieldCheck[] context=null)
        {
            if(!PlatformRules.IsActive(profileId)||!string.IsNullOrWhiteSpace(value))return "";
            if(PlatformRules.CanonicalPropertySet(pset)=="Pset_建筑技术信息属性集"&&property=="建筑用途名称")return BuildingUsageHint;
            var fallback=DefaultFor(pset,property,parent,context);
            return string.IsNullOrEmpty(fallback)?"":"未填写：平台可能采用默认“"+fallback+"”；请按实际用途核对，默认值未写入模型。";
        }

        static string DefaultFor(string pset,string property,string parent,FieldCheck[] context)
        {
            var rules=PlatformRules.Rules(pset);
            if(PlatformRules.CanonicalPropertySet(pset)=="Pset_停车位信息属性集")
            {
                var outdoor=context?.FirstOrDefault(f=>f.PropertySet==PlatformRules.CanonicalPropertySet(pset)&&f.Property=="是否为室外停车场")?.Value?.Trim();
                if(new[]{"1","是","true"}.Contains(outdoor))rules=rules.Where(r=>r.EntityCode=="203").ToArray();
                else if(new[]{"0","否","false"}.Contains(outdoor))rules=rules.Where(r=>r.EntityCode=="104").ToArray();
            }
            var fields=rules.SelectMany(r=>r.Fields).Where(f=>f.Name==property).ToArray();
            if(fields.Length==0)return null;
            if(fields.All(f=>!f.DisplayRequired)&&(property=="名称"||property=="版本"))return null;
            if(fields[0].InternalName=="公建大类")return PlatformRules.Choices(pset,property).FirstOrDefault();
            if(fields[0].InternalName=="公建小类")
                return string.IsNullOrWhiteSpace(parent) ? "住宅（主功能也缺失时）" : "子功能待确认（取决于主功能）";
            var values=fields.Select(f=>f.DefaultValue??"").Distinct(StringComparer.Ordinal).ToArray();
            return values.Length==1?values[0]:"取决于室内/室外等计算分支";
        }

        public static InputReviewItem[] Build(ModelElementCheck[] rows,string profileId)
        {
            var result=new List<InputReviewItem>();
            ApplyManualPolicy(rows);
            foreach(var row in rows)
            {
                foreach(var field in row.Fields.Where(f=>f.ManualInputRequired&&!f.Valid))
                    result.Add(Item(row,field.PropertySet,field.Property,field.Value,null,
                        field.BuildingUsage?"BUILDING_USAGE_REQUIRED":"FIRE_ROAD_WIDTH_REQUIRED",RequiredInputHint(field)));
                if(!PlatformRules.IsActive(profileId))continue;
                // Success-case 04 exposed mapped fields absent from the platform's
                // visible-field list. Report these separately; no defaults, manual
                // requirements or export blockers are introduced by this coverage.
                foreach(var field in row.Fields.Where(f=>string.IsNullOrWhiteSpace(f.Value)&&
                    (f.State=="未填写"||f.State=="缺参数"||f.State=="人工清空")&&
                    (f.PropertySet=="Pset_建筑技术信息属性集"&&new[]{"人数","建筑体积","室内外高差最大值"}.Contains(f.Property)||
                     f.PropertySet=="Pset_申报信息属性集"&&new[]{"基点坐标X","基点坐标Y"}.Contains(f.Property))))
                    result.Add(Item(row,field.PropertySet,field.Property,field.Value,null,"MAPPED_INPUT_MISSING",
                        "当前映射字段未填写，本次 IFC 不会包含该项；请按项目资料核对。此提示不新增必填要求。"));
                foreach(var group in row.Fields.GroupBy(f=>f.PropertySet))
                {
                    var fields=group.ToArray();
                    if(PlanningInputs.IsComputeExcluded(fields,group.Key))continue;
                    var parent=PlanningInputs.ParentValue(fields,group.Key);
                    foreach(var rule in PlatformRules.Fields(group.Key).Where(f=>f.ShowProperty))
                    {
                        var field=fields.FirstOrDefault(f=>f.Property==rule.Name);
                        var value=field?.Value;
                        if(field?.ManualInputRequired==true&&!field.Valid)continue;
                        if(field?.State=="有歧义")
                        {
                            result.Add(Item(row,group.Key,rule.Name,value,null,"HIFC_PLATFORM_SOURCE_CONFLICT",field.Detail));
                            continue;
                        }
                        var fallback=DefaultFor(group.Key,rule.Name,parent,fields);
                        if(string.IsNullOrWhiteSpace(value))
                        {
                            if(rule.DisplayRequired||!string.IsNullOrEmpty(fallback))
                                result.Add(Item(row,group.Key,rule.Name,value,fallback,
                                    string.IsNullOrEmpty(fallback)?"HIFC_PLATFORM_INPUT_MISSING":"HIFC_PLATFORM_DEFAULT_RISK",
                                    string.IsNullOrEmpty(fallback)?"缺少平台计算输入；请核对来源后写入。":DefaultHint(profileId,group.Key,rule.Name,value,parent,fields)));
                        }
                        else
                        {
                            var error=PlatformRules.ValueError(group.Key,rule.Name,value,parent);
                            if(error!=null||field!=null&&!field.Valid)
                                result.Add(Item(row,group.Key,rule.Name,value,null,"HIFC_PLATFORM_INPUT_REVIEW",error??field.Detail??"当前值需核对。"));
                        }
                    }
                }
            }
            return result.GroupBy(i=>i.ElementId+"|"+i.PropertySet+"|"+i.Property).Select(g=>g.First()).ToArray();
        }

        static InputReviewItem Item(ModelElementCheck row,string pset,string property,string value,string fallback,string code,string detail)
            => new InputReviewItem{ElementId=row.Id,Name=row.Name,PropertySet=pset,Property=property,Value=value,DefaultValue=fallback,Code=code,Detail=detail};
    }
}
