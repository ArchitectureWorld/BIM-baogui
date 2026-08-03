using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using BIMBaoGui.Stage01.Hifc;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class OfficialParameterWriteItem
  {
    public OfficialHifcMapping Mapping { get; set; }
    public Element Target { get; set; }
    public string RawValue { get; set; } = string.Empty;
  }

  internal sealed class OfficialParameterProjectionResult
  {
    public int PropertyValueCount { get; set; }
    public int ParameterWriteCount { get; set; }
    public int CanonicalWriteCount { get; set; }
    public int OfficialSourceWriteCount { get; set; }
    public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
  }

  internal static class OfficialParameterProjectionService
  {
    private const string CanonicalProjectionKind = "CANONICAL_INTERNAL";
    private const string OfficialProjectionKind = "OFFICIAL_EXACT_SOURCE_NAME";
    private const string DuplicateOfficialSourceParameterCode =
      "OFFICIAL_SOURCE_PARAMETER_DUPLICATE";

    public static OfficialParameterProjectionResult WriteAndVerify(
      Document document,
      IEnumerable<OfficialParameterWriteItem> sourceItems)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (!document.IsModifiable)
        throw new InvalidOperationException(
          "OfficialParameterProjectionService 必须在活动 Revit 事务中运行。");

      List<OfficialParameterWriteItem> items = (sourceItems
        ?? Enumerable.Empty<OfficialParameterWriteItem>())
        .Where(item => item?.Mapping != null && item.Target != null)
        .ToList();
      if (items.Count == 0)
        return new OfficialParameterProjectionResult
        {
          Messages = new[] { "没有需要写入的标准属性值。" }
        };

      List<ProjectionWrite> projections = ExpandProjections(items);
      ValidateConflictingWrites(projections);
      var messages = new List<string>();
      RemoveLegacyOfficialBindings(document, projections, messages);
      PreflightExistingOfficialParameters(projections);
      EnsureDefinitionsAndBindings(document, projections, messages);

      foreach (ProjectionWrite projection in projections)
      {
        Parameter parameter = ResolveParameter(projection);
        OfficialParameterTypeDecision decision = ValidateParameterType(
          parameter,
          projection.SharedParameterType,
          projection);
        if (parameter.IsReadOnly)
          throw new InvalidOperationException(
            projection.Kind + " 参数只读：" + projection.Name);
        SetValue(
          parameter,
          projection.RawValue,
          decision);
      }

      document.Regenerate();
      foreach (ProjectionWrite projection in projections)
      {
        Parameter parameter = ResolveParameter(projection);
        OfficialParameterTypeDecision decision = ValidateParameterType(
          parameter,
          projection.SharedParameterType,
          projection);
        if (!ReadbackMatches(
          parameter,
          projection.RawValue,
          decision))
          throw new InvalidOperationException(
            projection.Kind
            + " 参数回读不一致："
            + projection.Name
            + "；属性="
            + projection.Mapping.PropertySet
            + "."
            + projection.Mapping.IfcProperty);
      }

      int canonicalCount = projections.Count(item =>
        item.Kind == CanonicalProjectionKind);
      int officialCount = projections.Count(item =>
        item.Kind == OfficialProjectionKind);
      messages.Add(
        "REVIT_WRITE_VERIFIED："
        + items.Count
        + " 个业务属性已写入 "
        + canonicalCount
        + " 个内部唯一参数和 "
        + officialCount
        + " 个官方精确源参数，并完成回读。" );
      messages.Add(
        "官方精确源参数名来自已提取导出规则：有 sourceParameterOverride 时使用覆盖名；"
        + "否则使用 IFC 属性原名。" );

      return new OfficialParameterProjectionResult
      {
        PropertyValueCount = items.Count,
        ParameterWriteCount = projections.Count,
        CanonicalWriteCount = canonicalCount,
        OfficialSourceWriteCount = officialCount,
        Messages = messages
      };
    }

    private static List<ProjectionWrite> ExpandProjections(
      IEnumerable<OfficialParameterWriteItem> items)
    {
      var projections = new List<ProjectionWrite>();
      foreach (OfficialParameterWriteItem item in items)
      {
        projections.Add(new ProjectionWrite
        {
          Mapping = item.Mapping,
          Target = item.Target,
          RawValue = item.RawValue ?? string.Empty,
          Guid = item.Mapping.ParameterGuid,
          Name = item.Mapping.ParameterName,
          Kind = CanonicalProjectionKind,
          SharedParameterType = item.Mapping.SharedParameterType,
          RequiresGeneratedDefinition = false
        });

        if (!string.IsNullOrWhiteSpace(
          item.Mapping.OfficialSourceParameterName))
        {
          projections.Add(new ProjectionWrite
          {
            Mapping = item.Mapping,
            Target = item.Target,
            RawValue = OfficialSourceValuePolicy.Normalize(
              item.Mapping.IfcDataType,
              item.RawValue),
            Guid = item.Mapping.OfficialSourceParameterGuid,
            Name = item.Mapping.OfficialSourceParameterName,
            Kind = OfficialProjectionKind,
            SharedParameterType = item.Mapping.OfficialSourceParameterType,
            RequiresGeneratedDefinition = true
          });
        }
      }
      return FoldOfficialSourceAliases(projections);
    }

    private static List<ProjectionWrite> FoldOfficialSourceAliases(
      IEnumerable<ProjectionWrite> projections)
    {
      List<ProjectionWrite> all = (projections
        ?? Enumerable.Empty<ProjectionWrite>())
        .ToList();
      OfficialSourceAliasWrite<ProjectionWrite>[] aliases = all
        .Where(item => item.Kind == OfficialProjectionKind)
        .Select(item => new OfficialSourceAliasWrite<ProjectionWrite>
        {
          TargetElementId = item.Target.Id.IntegerValue,
          AliasGuid = item.Guid,
          Item = item,
          RawValue = item.RawValue,
          OfficialSourceName = item.Name,
          PropertySet = item.Mapping.PropertySet,
          IfcProperty = item.Mapping.IfcProperty
        })
        .ToArray();
      List<ProjectionWrite> foldedAliases = OfficialSourceAliasWritePolicy
        .Fold(aliases)
        .Select(item => item.Item)
        .ToList();
      return all
        .Where(item => item.Kind != OfficialProjectionKind)
        .Concat(foldedAliases)
        .ToList();
    }

    private static void ValidateConflictingWrites(
      IEnumerable<ProjectionWrite> projections)
    {
      IGrouping<string, ProjectionWrite> conflict = projections
        .GroupBy(item =>
          item.Target.Id.IntegerValue.ToString(CultureInfo.InvariantCulture)
          + "|"
          + item.Guid.ToString("D"),
          StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(group => group
          .Select(item => item.RawValue ?? string.Empty)
          .Distinct(StringComparer.Ordinal)
          .Count() > 1);
      if (conflict == null) return;
      ProjectionWrite first = conflict.First();
      throw new InvalidOperationException(
        "同一目标参数收到多个冲突值："
        + first.Name
        + " / ElementId="
        + first.Target.Id.IntegerValue);
    }

    private static void PreflightExistingOfficialParameters(
      IEnumerable<ProjectionWrite> projections)
    {
      foreach (ProjectionWrite projection in projections
        .Where(item => item.Kind == OfficialProjectionKind))
      {
        IList<Parameter> exact = GetExactNameParametersInExpectedGroup(
          projection);
        if (exact != null && exact.Count > 1)
          throw new InvalidOperationException(
            DuplicateOfficialSourceParameterCode
            + "：目标对象已经存在多个同名同组参数“"
            + projection.Name
            + "”；参数组="
            + projection.Mapping.OfficialSourceParameterGroup
            + "。" );
        if (exact != null && exact.Count == 1)
        {
          projection.ExistingExactNameParameter = exact[0];
          projection.RequiresGeneratedDefinition = false;
          ValidateParameterType(
            exact[0],
            projection.SharedParameterType,
            projection);
        }
      }
    }

    private static void RemoveLegacyOfficialBindings(
      Document document,
      IEnumerable<ProjectionWrite> projections,
      ICollection<string> messages)
    {
      int removed = 0;
      foreach (ProjectionWrite projection in projections
        .Where(item => item.Kind == OfficialProjectionKind)
        .GroupBy(item => item.Mapping.LegacyOfficialSourceParameterGuid)
        .Select(group => group.First()))
      {
        Guid legacyGuid = projection.Mapping.LegacyOfficialSourceParameterGuid;
        if (legacyGuid == Guid.Empty || legacyGuid == projection.Guid) continue;

        SharedParameterElement legacy = SharedParameterElement.Lookup(
          document,
          legacyGuid);
        if (legacy == null) continue;
        Definition definition = legacy.GetDefinition();
        if (definition == null) continue;
        if (!string.Equals(
          definition.Name,
          projection.Name,
          StringComparison.Ordinal))
          throw new InvalidOperationException(
            "LEGACY_OFFICIAL_SOURCE_GUID_COLLISION：GUID="
            + legacyGuid.ToString("D")
            + "；期望参数="
            + projection.Name
            + "；实际参数="
            + definition.Name);
        if (document.ParameterBindings.Remove(definition)) removed++;
      }
      if (removed > 0)
        messages.Add("已迁移并移除 " + removed + " 个旧版官方源参数绑定。");
    }

    private static void EnsureDefinitionsAndBindings(
      Document document,
      IEnumerable<ProjectionWrite> projections,
      ICollection<string> messages)
    {
      List<ProjectionWrite> definitions = projections
        .Where(item => item.Kind == CanonicalProjectionKind
          || item.RequiresGeneratedDefinition)
        .GroupBy(item => item.Guid)
        .Select(group => group.First())
        .ToList();
      if (definitions.Count == 0) return;

      Autodesk.Revit.ApplicationServices.Application application =
        document.Application;
      string previous = application.SharedParametersFilename;
      string temporary = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui_HIFC_"
        + Guid.NewGuid().ToString("N")
        + ".txt");
      File.WriteAllText(
        temporary,
        HbrSharedParameterTextProjection.CreateText(HbrRuleDatabase.Current),
        Encoding.Unicode);
      try
      {
        application.SharedParametersFilename = temporary;
        DefinitionFile file = application.OpenSharedParameterFile();
        if (file == null)
          throw new InvalidOperationException(
            "无法打开临时 H-IFC 共享参数文件。");

        int installed = 0;
        int rebound = 0;
        foreach (ProjectionWrite projection in definitions)
        {
          ExternalDefinition definition = FindDefinition(
            file,
            projection.Guid);
          if (definition == null)
            throw new InvalidOperationException(
              "共享参数定义缺失：" + projection.Name);
          if (!Enum.TryParse(
            projection.Mapping.Category,
            out BuiltInCategory categoryId))
            throw new InvalidOperationException(
              "不支持的 Revit 类别：" + projection.Mapping.Category);
          Category category = Category.GetCategory(document, categoryId);
          if (category == null)
            throw new InvalidOperationException(
              "当前文档不支持类别：" + projection.Mapping.Category);

          CategorySet categories = application.Create.NewCategorySet();
          categories.Insert(category);
          Binding binding = projection.Mapping.IsTypeBinding
            ? (Binding)application.Create.NewTypeBinding(categories)
            : application.Create.NewInstanceBinding(categories);
          BuiltInParameterGroup parameterGroup = ResolveParameterGroup(
            projection);
          bool inserted;
          try
          {
            inserted = document.ParameterBindings.Insert(
              definition,
              binding,
              parameterGroup);
          }
          catch (Exception exception)
          {
            throw BindingFailure(
              "BINDING_INSERT_FAILED",
              projection,
              exception);
          }
          if (inserted)
            installed++;
          else
          {
            bool reinserted;
            try
            {
              reinserted = document.ParameterBindings.ReInsert(
                definition,
                binding,
                parameterGroup);
            }
            catch (Exception exception)
            {
              throw BindingFailure(
                "BINDING_REINSERT_FAILED",
                projection,
                exception);
            }
            if (!reinserted)
              throw BindingFailure(
                "BINDING_REINSERT_FAILED",
                projection,
                new InvalidOperationException("ReInsert 返回 false。"));
            rebound++;
          }
        }
        messages.Add(
          "参数定义与绑定：新装 "
          + installed
          + "，校正 "
          + rebound
          + "。" );
      }
      finally
      {
        application.SharedParametersFilename = previous;
        try { File.Delete(temporary); } catch { }
      }
    }

    private static InvalidOperationException BindingFailure(
      string code,
      ProjectionWrite projection,
      Exception exception)
    {
      return new InvalidOperationException(
        code
        + "：参数=" + projection.Name
        + "；GUID=" + projection.Guid.ToString("D")
        + "；类别=" + projection.Mapping.Category
        + "；参数组=" + GetExpectedParameterGroupLabel(projection)
        + "；投影=" + projection.Kind
        + "；原始异常=" + exception.GetType().FullName
        + "：" + exception.Message,
        exception);
    }

    private static Parameter ResolveParameter(ProjectionWrite projection)
    {
      Parameter byGuid = projection.Target.get_Parameter(projection.Guid);
      if (byGuid != null) return byGuid;
      if (projection.ExistingExactNameParameter != null)
        return projection.ExistingExactNameParameter;

      IList<Parameter> exact = GetExactNameParametersInExpectedGroup(projection);
      if (exact == null || exact.Count == 0)
        throw new InvalidOperationException(
          projection.Kind
          + " 参数未绑定："
          + projection.Name
          + "；参数组="
          + GetExpectedParameterGroupLabel(projection));
      if (exact.Count > 1)
        throw new InvalidOperationException(
          DuplicateOfficialSourceParameterCode
          + "：写入时检测到多个同名同组参数“"
          + projection.Name
          + "”。" );
      return exact[0];
    }

    private static IList<Parameter> GetExactNameParametersInExpectedGroup(
      ProjectionWrite projection)
    {
      BuiltInParameterGroup expected = ResolveParameterGroup(projection);
      return (projection.Target.GetParameters(projection.Name)
        ?? new List<Parameter>())
        .Where(parameter => parameter?.Definition != null
          && parameter.Definition.ParameterGroup == expected)
        .ToList();
    }

    private static BuiltInParameterGroup ResolveParameterGroup(
      ProjectionWrite projection)
    {
      if (projection.Kind == CanonicalProjectionKind)
        return BuiltInParameterGroup.PG_DATA;

      switch ((projection.Mapping.OfficialSourceParameterGroup
        ?? string.Empty).Trim())
      {
      case "材质和装饰":
        return BuiltInParameterGroup.PG_MATERIALS;
      case "阶段化":
        return BuiltInParameterGroup.PG_PHASING;
      default:
        throw new InvalidOperationException(
          "UNSUPPORTED_OFFICIAL_SOURCE_PARAMETER_GROUP：参数="
          + projection.Name
          + "；官方参数组="
          + projection.Mapping.OfficialSourceParameterGroup);
      }
    }

    private static string GetExpectedParameterGroupLabel(
      ProjectionWrite projection)
    {
      return projection.Kind == CanonicalProjectionKind
        ? "数据"
        : projection.Mapping.OfficialSourceParameterGroup;
    }

    private static OfficialParameterTypeDecision ValidateParameterType(
      Parameter parameter,
      string sharedParameterType,
      ProjectionWrite projection)
    {
      OfficialParameterTypeDecision decision;
      try
      {
        decision = OfficialParameterTypeContract.Resolve(sharedParameterType);
      }
      catch (InvalidOperationException)
      {
        throw ParameterTypeMismatch(
          projection.Name,
          sharedParameterType,
          GetActualStorageDescription(parameter));
      }
      if (parameter == null)
        throw ParameterTypeMismatch(
          projection.Name,
          decision.SemanticType,
          GetActualStorageDescription(parameter));
      OfficialParameterCompatibilityResult compatibility =
        OfficialParameterTypeContract.CheckCompatibility(
          decision.SemanticType,
          GetStorageKind(parameter.StorageType),
          () => GetActualSemantic(parameter));
      if (!compatibility.StorageMatches)
        throw ParameterTypeMismatch(
          projection.Name,
          decision.SemanticType,
          GetActualStorageDescription(parameter));
      if (compatibility.SemanticMatches) return decision;
      throw ParameterTypeMismatch(
        projection.Name,
        decision.SemanticType,
        compatibility.ActualSemantic);
    }

    private static InvalidOperationException ParameterTypeMismatch(
      string parameterName,
      string expectedSemantic,
      string actualSemantic)
    {
      return new InvalidOperationException(
        "参数语义类型不匹配："
        + parameterName
        + "，期望=" + (expectedSemantic ?? string.Empty).Trim()
        + "，实际=" + actualSemantic);
    }

    private static string GetActualSemantic(Parameter parameter)
    {
      return parameter?.Definition == null
        ? "Missing"
        : parameter.Definition.ParameterType.ToString();
    }

    private static string GetActualStorageDescription(Parameter parameter)
    {
      return parameter == null
        ? "Missing"
        : "StorageType." + parameter.StorageType;
    }

    private static OfficialParameterStorageKind GetStorageKind(
      StorageType storageType)
    {
      switch (storageType)
      {
        case StorageType.String:
          return OfficialParameterStorageKind.String;
        case StorageType.Integer:
          return OfficialParameterStorageKind.Integer;
        case StorageType.Double:
          return OfficialParameterStorageKind.Double;
        default:
          return OfficialParameterStorageKind.Unsupported;
      }
    }

    private static ExternalDefinition FindDefinition(
      DefinitionFile file,
      Guid guid)
    {
      foreach (DefinitionGroup group in file.Groups)
      {
        foreach (Definition definition in group.Definitions)
        {
          if (definition is ExternalDefinition external
            && external.GUID == guid)
            return external;
        }
      }
      return null;
    }

    private static void SetValue(
      Parameter parameter,
      string raw,
      OfficialParameterTypeDecision decision)
    {
      raw = raw ?? string.Empty;
      switch (decision.ValueRoute)
      {
        case OfficialParameterValueRoute.Text:
          if (!parameter.Set(raw))
            throw new InvalidOperationException("文本参数写入失败。");
          return;
        case OfficialParameterValueRoute.Integer:
          if (!parameter.Set(ParseInteger(raw)))
            throw new InvalidOperationException("整数参数写入失败。");
          return;
        case OfficialParameterValueRoute.YesNo:
          if (!parameter.Set(ParseYesNo(raw)))
            throw new InvalidOperationException("布尔参数写入失败。");
          return;
        default:
          double internalValue = ToInternalValue(
            decision.UnitRoute,
            ParseDouble(raw));
          if (!parameter.Set(internalValue))
            throw new InvalidOperationException("数值参数写入失败。");
          return;
      }
    }

    private static int ParseInteger(string raw)
    {
      if (!int.TryParse(
        raw,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out int value))
        throw new FormatException("不是有效整数：" + raw);
      return value;
    }

    private static int ParseYesNo(string raw)
    {
      string normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
      if (normalized == "1" || normalized == "true"
        || normalized == "是" || normalized == "yes")
        return 1;
      if (normalized == "0" || normalized == "false"
        || normalized == "否" || normalized == "no")
        return 0;
      throw new FormatException("布尔值只接受 true/false、是/否、1/0。");
    }

    private static double ParseDouble(string raw)
    {
      if (!double.TryParse(
        raw,
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out double value))
        throw new FormatException("不是有效数值：" + raw);
      return value;
    }

    private static double ToInternalValue(
      OfficialParameterUnitRoute unitRoute,
      double value)
    {
      switch (unitRoute)
      {
      case OfficialParameterUnitRoute.Meters:
        return UnitUtils.ConvertToInternalUnits(
          value,
          DisplayUnitType.DUT_METERS);
      case OfficialParameterUnitRoute.SquareMeters:
        return UnitUtils.ConvertToInternalUnits(
          value,
          DisplayUnitType.DUT_SQUARE_METERS);
      case OfficialParameterUnitRoute.CubicMeters:
        return UnitUtils.ConvertToInternalUnits(
          value,
          DisplayUnitType.DUT_CUBIC_METERS);
      case OfficialParameterUnitRoute.Degrees:
        return UnitUtils.ConvertToInternalUnits(
          value,
          DisplayUnitType.DUT_DECIMAL_DEGREES);
      default:
        return value;
      }
    }

    private static bool ReadbackMatches(
      Parameter parameter,
      string expected,
      OfficialParameterTypeDecision decision)
    {
      switch (decision.ValueRoute)
      {
        case OfficialParameterValueRoute.Text:
          return string.Equals(
            parameter.AsString() ?? string.Empty,
            expected ?? string.Empty,
            StringComparison.Ordinal);
        case OfficialParameterValueRoute.Integer:
          return parameter.AsInteger() == ParseInteger(expected);
        case OfficialParameterValueRoute.YesNo:
          return parameter.AsInteger() == ParseYesNo(expected);
        default:
          double target = ToInternalValue(
            decision.UnitRoute,
            ParseDouble(expected));
          return Math.Abs(parameter.AsDouble() - target) <= 1e-8;
      }
    }

    private sealed class ProjectionWrite
    {
      public OfficialHifcMapping Mapping { get; set; }
      public Element Target { get; set; }
      public string RawValue { get; set; }
      public Guid Guid { get; set; }
      public string Name { get; set; }
      public string Kind { get; set; }
      public string SharedParameterType { get; set; }
      public bool RequiresGeneratedDefinition { get; set; }
      public Parameter ExistingExactNameParameter { get; set; }
    }

  }
}
