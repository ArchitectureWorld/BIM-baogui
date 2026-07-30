using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.Infrastructure
{
  internal sealed class Stage01RegistryProvider
  {
    private const string ResourceName = "BIMBaoGui.Stage01.Resources.stage01_file_initialization_registry_v0.1.json";
    private static readonly Lazy<Stage01RegistryProvider> LazyInstance = new Lazy<Stage01RegistryProvider>(Load);
    private readonly Dictionary<string, FieldDefinition> _fieldByKey;
    private readonly Dictionary<string, string> _defaults;

    private Stage01RegistryProvider(IReadOnlyList<FieldDefinition> fields, IDictionary<string, string> defaults)
    {
      Fields = fields;
      _fieldByKey = fields.ToDictionary(x => x.Key, x => x, StringComparer.Ordinal);
      _defaults = new Dictionary<string, string>(defaults, StringComparer.Ordinal);
      Groups = fields.Select(x => x.Group).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
      Conditions = new List<ConditionDefinition>
      {
        new ConditionDefinition("site.other_land", "其他分类用地", "10_项目条件"),
        new ConditionDefinition("site.road_redline", "道路红线", "10_项目条件"),
        new ConditionDefinition("site.road_centerline", "道路中心线", "10_项目条件"),
        new ConditionDefinition("site.internal_roads", "区内道路", "10_项目条件"),
        new ConditionDefinition("site.fire_lane", "消防道路", "10_项目条件"),
        new ConditionDefinition("site.fire_field", "消防登高／操作场地", "10_项目条件"),
        new ConditionDefinition("site.green", "绿地", "10_项目条件"),
        new ConditionDefinition("site.outdoor_parking", "室外停车场／车位", "10_项目条件"),
        new ConditionDefinition("site.civil_defense", "人防区域", "10_项目条件"),
        new ConditionDefinition("site.structures", "室外构筑物与设施", "10_项目条件")
      };
    }

    public static Stage01RegistryProvider Instance => LazyInstance.Value;
    public IReadOnlyList<FieldDefinition> Fields { get; }
    public IReadOnlyList<string> Groups { get; }
    public IReadOnlyList<ConditionDefinition> Conditions { get; }

    public FieldDefinition GetField(string key)
    {
      return key != null && _fieldByKey.TryGetValue(key, out FieldDefinition value) ? value : null;
    }

    public IReadOnlyList<FieldDefinition> FieldsForGroup(string group, bool showAll)
    {
      IEnumerable<FieldDefinition> query = Fields.Where(x => string.Equals(x.Group, group, StringComparison.Ordinal));
      if (!showAll)
        query = query.Where(x => x.Essential);
      return query.ToList();
    }

    public Stage01Model CreateDefaultModel()
    {
      var model = new Stage01Model();
      foreach (KeyValuePair<string, string> pair in _defaults)
        model.SetValue(pair.Key, pair.Value);

      SetIfEmpty(model, Stage01Keys.FileGuid, Guid.NewGuid().ToString("D"));
      SetIfEmpty(model, Stage01Keys.WorkflowVersion, "0.2.0");
      SetIfEmpty(model, Stage01Keys.InitializationStatus, "未初始化");
      SetIfEmpty(model, Stage01Keys.ModelFileType, "总平模型");
      SetIfEmpty(model, Stage01Keys.ModelScope, "项目总平面报规模型");
      SetIfEmpty(model, Stage01Keys.Stage, "规划报建");
      SetIfEmpty(model, Stage01Keys.CoordinateSystem, "CGCS2000");
      SetIfEmpty(model, Stage01Keys.ElevationSystem, "1985国家高程基准");
      SetIfEmpty(model, Stage01Keys.TrueNorthAngle, "0");
      SetIfEmpty(model, Stage01Keys.LengthUnit, "m");
      SetIfEmpty(model, Stage01Keys.AreaUnit, "m²");
      SetIfEmpty(model, Stage01Keys.AngleUnit, "°");
      model.ActiveGroup = "01_文件与项目身份";
      foreach (ConditionDefinition condition in Conditions)
        model.SetCondition(condition.Key, false);
      return model;
    }

    private static void SetIfEmpty(Stage01Model model, string key, string value)
    {
      if (string.IsNullOrWhiteSpace(model.GetValue(key))) model.SetValue(key, value);
    }

    private static Stage01RegistryProvider Load()
    {
      Assembly assembly = typeof(Stage01RegistryProvider).Assembly;
      using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
      {
        if (stream == null)
          throw new InvalidOperationException("内置字段注册表不存在。资源：" + ResourceName);
        using (var reader = new StreamReader(stream))
        {
          var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
          RegistryDto registry = serializer.Deserialize<RegistryDto>(reader.ReadToEnd());
          var fields = new List<FieldDefinition>();
          var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
          foreach (InternalFieldDto source in registry.internal_workflow_fields ?? new List<InternalFieldDto>())
          {
            fields.Add(MapInternal(source));
            if (!string.IsNullOrWhiteSpace(source.@default)) defaults[source.field_key] = source.@default;
          }
          foreach (MvdFieldDto source in registry.mvd_fields ?? new List<MvdFieldDto>())
            fields.Add(MapMvd(source));
          return new Stage01RegistryProvider(fields, defaults);
        }
      }
    }

    private static FieldDefinition MapInternal(InternalFieldDto source)
    {
      return new FieldDefinition
      {
        Key = source.field_key,
        Label = source.property,
        Group = source.ui_group,
        Kind = ParseKind(source.type),
        ReadOnly = source.source_kind == "system_generated" || source.source_kind == "system_rule" || source.source_kind == "Revit_scan",
        Essential = EssentialKeys.Contains(source.field_key),
        Deferred = false,
        Source = source.source_kind,
        Entity = "Workflow",
        Pset = "HBR",
        AllowedValues = source.allowed_values ?? Array.Empty<string>()
      };
    }

    private static FieldDefinition MapMvd(MvdFieldDto source)
    {
      return new FieldDefinition
      {
        Key = source.field_key,
        Label = source.property,
        Group = source.ui_group,
        Kind = ParseIfcKind(source.declared_ifc_type),
        ReadOnly = !source.write_in_stage01 || source.source_kind == "later_model_calculation_or_external_value",
        Essential = EssentialKeys.Contains(source.field_key),
        Deferred = !source.write_in_stage01,
        Source = source.source_kind,
        Entity = source.entity,
        Pset = source.pset,
        AllowedValues = Array.Empty<string>()
      };
    }

    private static FieldKind ParseKind(string value)
    {
      switch ((value ?? string.Empty).Trim().ToLowerInvariant())
      {
        case "number": return FieldKind.Number;
        case "integer": return FieldKind.Integer;
        case "boolean": return FieldKind.Boolean;
        case "enum": return FieldKind.Enum;
        case "guid": return FieldKind.Guid;
        default: return FieldKind.Text;
      }
    }

    private static FieldKind ParseIfcKind(string value)
    {
      string normalized = (value ?? string.Empty).ToLowerInvariant();
      if (normalized.Contains("real") || normalized.Contains("measure")) return FieldKind.Number;
      if (normalized.Contains("integer")) return FieldKind.Integer;
      if (normalized.Contains("boolean")) return FieldKind.Boolean;
      if (normalized.Contains("date")) return FieldKind.DateTime;
      return FieldKind.Text;
    }

    private static readonly HashSet<string> EssentialKeys = new HashSet<string>(StringComparer.Ordinal)
    {
      Stage01Keys.SubitemCode,
      Stage01Keys.SubitemName,
      Stage01Keys.ModelFileType,
      Stage01Keys.ModelScope,
      Stage01Keys.FileGuid,
      Stage01Keys.WorkflowVersion,
      Stage01Keys.InitializationStatus,
      Stage01Keys.TrueNorthAngle,
      Stage01Keys.LengthUnit,
      Stage01Keys.AreaUnit,
      Stage01Keys.AngleUnit,
      Stage01Keys.ProjectNumber,
      Stage01Keys.ProjectName,
      Stage01Keys.ProjectAddress,
      Stage01Keys.OwnerOrganization,
      Stage01Keys.DesignOrganization,
      Stage01Keys.Stage,
      Stage01Keys.BaseX,
      Stage01Keys.BaseY,
      Stage01Keys.BaseElevation,
      Stage01Keys.CoordinateSystem,
      Stage01Keys.ElevationSystem,
      "IfcOrganization|Pset_组织通用属性集|企业名称",
      "IfcOrganization|Pset_组织通用属性集|社会统一信用代码",
      "IfcOrganization|Pset_组织通用属性集|项目参建类型",
      "IfcOrganization|Pset_组织通用属性集|联系人姓名",
      "IfcOrganization|Pset_组织通用属性集|联系人手机号码"
    };

    private sealed class RegistryDto
    {
      public List<InternalFieldDto> internal_workflow_fields { get; set; }
      public List<MvdFieldDto> mvd_fields { get; set; }
    }

    private sealed class InternalFieldDto
    {
      public string field_key { get; set; }
      public string property { get; set; }
      public string type { get; set; }
      public string ui_group { get; set; }
      public string source_kind { get; set; }
      public string[] allowed_values { get; set; }
      public string @default { get; set; }
    }

    private sealed class MvdFieldDto
    {
      public string field_key { get; set; }
      public string property { get; set; }
      public string ui_group { get; set; }
      public string declared_ifc_type { get; set; }
      public string source_kind { get; set; }
      public bool write_in_stage01 { get; set; }
      public string entity { get; set; }
      public string pset { get; set; }
    }
  }
}
