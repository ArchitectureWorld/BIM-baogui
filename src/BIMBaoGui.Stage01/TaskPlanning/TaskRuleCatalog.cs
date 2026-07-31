using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.TaskPlanning
{
  internal static class TaskRuleCatalog
  {
    private static readonly IReadOnlyList<TaskRuleDefinition> Rules = BuildRules();

    public static IReadOnlyList<TaskRuleDefinition> ForModelType(string modelFileType)
    {
      return Rules
        .Where(rule => string.Equals(rule.ModelFileType, modelFileType, StringComparison.Ordinal))
        .OrderBy(rule => rule.Item.Sequence)
        .ThenBy(rule => rule.Item.TaskId, StringComparer.Ordinal)
        .ToArray();
    }

    private static IReadOnlyList<TaskRuleDefinition> BuildRules()
    {
      var rules = new List<TaskRuleDefinition>();
      AddSiteRules(rules);
      AddAboveGroundRules(rules);
      AddUndergroundRules(rules);
      return rules;
    }

    private static void AddSiteRules(ICollection<TaskRuleDefinition> rules)
    {
      string model = PlanningTargetRequirementPolicy.SiteModel;
      rules.Add(Rule(model, "SITE.SKELETON", "总平空间基准骨架", "site.skeleton", 10, true,
        attrs: new[] { "坐标系统", "高程系统", "真北方向" },
        geometry: new[] { "项目基点与共享坐标有效", "总平计算平面有效" }));
      rules.Add(Rule(model, "SITE.TOTAL_LAND", "规划总用地", "site.total_land", 20, false,
        attrs: new[] { "名称", "用地类型", "投影面积" },
        deps: new[] { "SITE.SKELETON" },
        geometry: new[] { "边界闭合", "无自交", "面积大于零" },
        properties: new[] { "投影面积与几何一致" }));
      rules.Add(Rule(model, "SITE.NET_LAND", "规划净用地", "site.net_land", 30, false,
        attrs: new[] { "名称", "用地类型", "投影面积" },
        deps: new[] { "SITE.TOTAL_LAND" },
        geometry: new[] { "边界闭合", "位于规划总用地内", "面积大于零" },
        properties: new[] { "投影面积与几何一致" }));
      rules.Add(Rule(model, "SITE.BUILDING_FOOTPRINT", "建筑轮廓或建筑占地表达", "site.building_footprint", 40, false,
        attrs: new[] { "建筑名称", "建筑编号", "占地面积" },
        deps: new[] { "SITE.NET_LAND" },
        geometry: new[] { "轮廓闭合", "位于规划净用地内", "建筑轮廓不重复" },
        targets: new[] { PlanningTargetCatalog.BuildingDensityCode }));

      rules.Add(Conditional(model, "SITE.OTHER_LAND", "其他分类用地", "site.other_land", "site.other_land", 50,
        attrs: new[] { "名称", "用地分类", "投影面积" },
        deps: new[] { "SITE.TOTAL_LAND" },
        geometry: new[] { "边界闭合", "用地关系有效" }));
      rules.Add(Conditional(model, "SITE.ROAD_REDLINE", "道路红线", "site.road_redline", "site.road_redline", 60,
        attrs: new[] { "名称", "红线类型" },
        deps: new[] { "SITE.SKELETON" },
        geometry: new[] { "曲线连续", "无无效短线" }));
      rules.Add(Conditional(model, "SITE.ROAD_CENTERLINE", "道路中心线", "site.road_centerline", "site.road_centerline", 70,
        attrs: new[] { "名称", "道路等级" },
        deps: new[] { "SITE.ROAD_REDLINE" },
        geometry: new[] { "中心线连续", "与道路红线关系有效" }));
      rules.Add(Conditional(model, "SITE.INTERNAL_ROADS", "区内道路", "site.internal_roads", "site.internal_roads", 80,
        attrs: new[] { "道路名称", "道路类型", "道路宽度" },
        deps: new[] { "SITE.NET_LAND" },
        geometry: new[] { "道路范围闭合", "位于规划净用地内" }));
      rules.Add(Conditional(model, "SITE.FIRE_LANE", "消防道路", "site.fire_lane", "site.fire_lane", 90,
        attrs: new[] { "名称", "道路宽度", "转弯半径" },
        deps: new[] { "SITE.NET_LAND" },
        geometry: new[] { "消防道路连续", "消防道路范围有效" }));
      rules.Add(Conditional(model, "SITE.FIRE_FIELD", "消防登高或操作场地", "site.fire_field", "site.fire_field", 100,
        attrs: new[] { "名称", "场地类型", "投影面积" },
        deps: new[] { "SITE.FIRE_LANE" },
        geometry: new[] { "场地边界闭合", "与服务建筑关系有效" }));
      rules.Add(Conditional(model, "SITE.GREEN", "绿地", "site.green", "site.green", 110,
        attrs: new[] { "绿地类型", "投影面积", "折算系数" },
        deps: new[] { "SITE.NET_LAND" },
        geometry: new[] { "绿地边界闭合", "绿地不越界", "绿地不重复统计" },
        properties: new[] { "折算面积计算有效" },
        targets: new[] { PlanningTargetCatalog.GreenRateCode }));
      rules.Add(Conditional(model, "SITE.OUTDOOR_PARKING", "室外停车场或车位", "site.outdoor_parking", "site.outdoor_parking", 120,
        attrs: new[] { "停车类型", "车位数量", "投影面积" },
        deps: new[] { "SITE.NET_LAND" },
        geometry: new[] { "停车范围有效", "车位不重复" }));
      rules.Add(Conditional(model, "SITE.CIVIL_DEFENSE", "人防区域", "site.civil_defense", "site.civil_defense", 130,
        attrs: new[] { "名称", "人防类型", "投影面积" },
        deps: new[] { "SITE.NET_LAND" },
        geometry: new[] { "人防范围闭合", "范围关系有效" }));
      rules.Add(Conditional(model, "SITE.STRUCTURES", "室外构筑物与设施", "site.structures", "site.structures", 140,
        attrs: new[] { "名称", "设施类型", "数量" },
        deps: new[] { "SITE.NET_LAND" },
        geometry: new[] { "设施位置位于项目范围内" }));
      rules.Add(Rule(model, "SITE.TARGET_CHECK", "总平规划控制目标复核", "site.target_check", 900, false,
        deps: new[] { "SITE.NET_LAND", "SITE.BUILDING_FOOTPRINT" },
        targets: new[]
        {
          PlanningTargetCatalog.BuildingDensityCode,
          PlanningTargetCatalog.FloorAreaRatioCode,
          PlanningTargetCatalog.GreenRateCode
        }));
    }

    private static void AddAboveGroundRules(ICollection<TaskRuleDefinition> rules)
    {
      string model = PlanningTargetRequirementPolicy.AboveGroundModel;
      rules.Add(Rule(model, "ABOVE.SKELETON", "地上建筑空间骨架", "building.above.skeleton", 10, true,
        attrs: new[] { "建筑编号", "建筑名称", "标高基准" },
        geometry: new[] { "建筑定位有效", "标高序列有效" }));
      rules.Add(Rule(model, "ABOVE.LEVELS", "地上楼层骨架", "building.above.levels", 20, true,
        attrs: new[] { "楼层名称", "楼层序号", "楼层标高" },
        deps: new[] { "ABOVE.SKELETON" },
        geometry: new[] { "楼层标高不重复", "楼层顺序有效" }));
      rules.Add(Rule(model, "ABOVE.BODY", "地上建筑主体", "building.above.body", 30, false,
        attrs: new[] { "建筑功能", "地上层数", "建筑高度", "建筑面积" },
        deps: new[] { "ABOVE.LEVELS" },
        geometry: new[] { "主体轮廓闭合", "楼层与主体关系有效" },
        properties: new[] { "层数与楼层一致", "高度与标高一致", "面积与几何一致" },
        targets: new[] { PlanningTargetCatalog.FloorAreaRatioCode }));
      rules.Add(Conditional(model, "ABOVE.ROOF", "屋顶及屋面构件", "building.above.roof", "building.roof", 40,
        attrs: new[] { "屋顶类型", "最高点标高" },
        deps: new[] { "ABOVE.BODY" },
        geometry: new[] { "屋顶与主体关系有效" }));
      rules.Add(Conditional(model, "ABOVE.BALCONY", "阳台", "building.above.balcony", "building.balcony", 50,
        attrs: new[] { "阳台类型", "计容方式", "投影面积" },
        deps: new[] { "ABOVE.BODY" },
        properties: new[] { "阳台面积统计有效" }));
      rules.Add(Conditional(model, "ABOVE.CANOPY", "雨篷或挑檐", "building.above.canopy", "building.canopy", 60,
        attrs: new[] { "构件类型", "投影面积" },
        deps: new[] { "ABOVE.BODY" }));
      rules.Add(Rule(model, "ABOVE.INHERIT_TARGETS", "继承项目规划控制目标", "building.above.target_inheritance", 900, false,
        deps: new[] { "ABOVE.BODY" },
        targets: new[]
        {
          PlanningTargetCatalog.BuildingDensityCode,
          PlanningTargetCatalog.FloorAreaRatioCode,
          PlanningTargetCatalog.GreenRateCode
        }));
    }

    private static void AddUndergroundRules(ICollection<TaskRuleDefinition> rules)
    {
      string model = PlanningTargetRequirementPolicy.UndergroundModel;
      rules.Add(Rule(model, "UNDERGROUND.SKELETON", "地下建筑空间骨架", "building.underground.skeleton", 10, true,
        attrs: new[] { "建筑编号", "建筑名称", "地下定位基准" },
        geometry: new[] { "地下定位有效", "地下标高序列有效" }));
      rules.Add(Rule(model, "UNDERGROUND.LEVELS", "地下楼层骨架", "building.underground.levels", 20, true,
        attrs: new[] { "楼层名称", "楼层序号", "楼层标高" },
        deps: new[] { "UNDERGROUND.SKELETON" },
        geometry: new[] { "地下楼层标高不重复", "地下楼层顺序有效" }));
      rules.Add(Rule(model, "UNDERGROUND.BODY", "地下建筑主体", "building.underground.body", 30, false,
        attrs: new[] { "地下层数", "地下建筑面积", "地下空间类型" },
        deps: new[] { "UNDERGROUND.LEVELS" },
        geometry: new[] { "地下主体轮廓闭合", "地下范围关系有效" },
        properties: new[] { "地下层数与楼层一致", "地下面积与几何一致" }));
      rules.Add(Conditional(model, "UNDERGROUND.PARKING", "地下停车空间", "building.underground.parking", "underground.parking", 40,
        attrs: new[] { "停车类型", "机动车位", "非机动车位" },
        deps: new[] { "UNDERGROUND.BODY" },
        geometry: new[] { "停车范围有效", "车位不重复" }));
      rules.Add(Conditional(model, "UNDERGROUND.CIVIL_DEFENSE", "地下人防空间", "building.underground.civil_defense", "site.civil_defense", 50,
        attrs: new[] { "人防类型", "防护单元", "人防面积" },
        deps: new[] { "UNDERGROUND.BODY" },
        geometry: new[] { "人防范围闭合", "防护单元关系有效" }));
      rules.Add(Rule(model, "UNDERGROUND.INHERIT_TARGETS", "继承项目规划控制目标", "building.underground.target_inheritance", 900, false,
        deps: new[] { "UNDERGROUND.BODY" },
        targets: new[]
        {
          PlanningTargetCatalog.BuildingDensityCode,
          PlanningTargetCatalog.FloorAreaRatioCode,
          PlanningTargetCatalog.GreenRateCode
        }));
    }

    private static TaskRuleDefinition Rule(
      string model,
      string id,
      string name,
      string objectCode,
      int sequence,
      bool skeleton,
      IEnumerable<string> attrs = null,
      IEnumerable<string> deps = null,
      IEnumerable<string> geometry = null,
      IEnumerable<string> properties = null,
      IEnumerable<string> targets = null)
    {
      return new TaskRuleDefinition(model, new HBRTaskPlanItem(
        id, name, objectCode, HBRTaskRequirement.Required, string.Empty, sequence, skeleton,
        attrs, deps, geometry, properties, targets));
    }

    private static TaskRuleDefinition Conditional(
      string model,
      string id,
      string name,
      string objectCode,
      string conditionKey,
      int sequence,
      IEnumerable<string> attrs = null,
      IEnumerable<string> deps = null,
      IEnumerable<string> geometry = null,
      IEnumerable<string> properties = null,
      IEnumerable<string> targets = null)
    {
      return new TaskRuleDefinition(model, new HBRTaskPlanItem(
        id, name, objectCode, HBRTaskRequirement.Conditional, conditionKey, sequence, false,
        attrs, deps, geometry, properties, targets));
    }
  }
}
