using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.DB;
using BIMBaoGui.Stage01.Hifc;

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
    private const string SharedParameterResource =
      "BIMBaoGui.Stage01.Resources.GH_HIFC_SharedParameters.txt";
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
      PreflightExistingOfficialParameters(projections);

      var messages = new List<string>();
      EnsureDefinitionsAndBindings(document, projections, messages);

      foreach (ProjectionWrite projection in projections)
      {
        Parameter parameter = ResolveParameter(projection);
        OfficialParameterTypeDecision decision = ValidateParameterType(
          parameter,
          projection.Mapping.SharedParameterType,
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
          projection.Mapping.SharedParameterType,
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
          RequiresGeneratedDefinition = false
        });

        if (!string.IsNullOrWhiteSpace(
          item.Mapping.OfficialSourceParameterName))
        {
          projections.Add(new ProjectionWrite
          {
            Mapping = item.Mapping,
            Target = item.Target,
            RawValue = item.RawValue ?? string.Empty,
            Guid = item.Mapping.OfficialSourceParameterGuid,
            Name = item.Mapping.OfficialSourceParameterName,
            Kind = OfficialProjectionKind,
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
        IList<Parameter> exact = projection.Target.GetParameters(
          projection.Name);
        if (exact != null && exact.Count > 1)
          throw new InvalidOperationException(
            DuplicateOfficialSourceParameterCode
            + "：目标对象已经存在多个同名参数“"
            + projection.Name
            + "”，官方导出器的名称读取结果不确定。" );
        if (exact != null && exact.Count == 1)
        {
          projection.ExistingExactNameParameter = exact[0];
          projection.RequiresGeneratedDefinition = false;
          ValidateParameterType(
            exact[0],
            projection.Mapping.SharedParameterType,
            projection);
        }
      }
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
        BuildCombinedSharedParameterFile(definitions),
        new UTF8Encoding(false));
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
          bool inserted = document.ParameterBindings.Insert(
            definition,
            binding,
            BuiltInParameterGroup.PG_DATA);
          if (inserted)
            installed++;
          else
          {
            document.ParameterBindings.ReInsert(
              definition,
              binding,
              BuiltInParameterGroup.PG_DATA);
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

    private static string BuildCombinedSharedParameterFile(
      IEnumerable<ProjectionWrite> definitions)
    {
      string canonical = ReadEmbeddedText(SharedParameterResource)
        .Replace("\r\n", "\n")
        .Replace('\r', '\n')
        .TrimEnd('\n');
      string[] canonicalLines = canonical.Split('\n');
      int parameterHeaderIndex = FindParameterHeaderIndex(canonicalLines);

      ProjectionWrite[] aliases = definitions
        .Where(item => item.Kind == OfficialProjectionKind)
        .OrderBy(item => item.Mapping.PropertySet, StringComparer.Ordinal)
        .ThenBy(item => item.Name, StringComparer.Ordinal)
        .ToArray();
      AliasGroupDefinition[] aliasGroups = aliases
        .GroupBy(
          item => item.Mapping.PropertySet ?? string.Empty,
          StringComparer.Ordinal)
        .Select((group, index) => new AliasGroupDefinition
        {
          Id = 1000 + index,
          PropertySet = group.Key,
          Items = group
            .GroupBy(item => item.Guid)
            .Select(items => items.First())
            .ToArray()
        })
        .ToArray();

      var builder = new StringBuilder(canonical.Length + 16384);
      for (int index = 0; index < parameterHeaderIndex; index++)
        builder.AppendLine(canonicalLines[index]);
      AppendAliasGroupDefinitions(builder, aliasGroups);
      for (int index = parameterHeaderIndex;
        index < canonicalLines.Length;
        index++)
        builder.AppendLine(canonicalLines[index]);
      AppendAliasParameterDefinitions(builder, aliasGroups);
      return builder.ToString();
    }

    private static int FindParameterHeaderIndex(string[] lines)
    {
      for (int index = 0; index < (lines?.Length ?? 0); index++)
      {
        if ((lines[index] ?? string.Empty).StartsWith(
          "*PARAM\t",
          StringComparison.Ordinal))
          return index;
      }
      throw new InvalidDataException(
        "内置共享参数文件缺少 *PARAM 标题行。");
    }

    private static void AppendAliasGroupDefinitions(
      StringBuilder builder,
      IEnumerable<AliasGroupDefinition> groups)
    {
      foreach (AliasGroupDefinition group in groups
        ?? Enumerable.Empty<AliasGroupDefinition>())
      {
        builder.Append("GROUP\t")
          .Append(group.Id.ToString(CultureInfo.InvariantCulture))
          .Append("\tGH_HIFC_官方源_")
          .Append(Sanitize(group.PropertySet))
          .AppendLine();
      }
    }

    private static void AppendAliasParameterDefinitions(
      StringBuilder builder,
      IEnumerable<AliasGroupDefinition> groups)
    {
      foreach (AliasGroupDefinition group in groups
        ?? Enumerable.Empty<AliasGroupDefinition>())
      {
        foreach (ProjectionWrite item in group.Items
          ?? Array.Empty<ProjectionWrite>())
        {
          builder.Append("PARAM\t")
            .Append(item.Guid.ToString("D"))
            .Append('\t')
            .Append(Sanitize(item.Name))
            .Append('\t')
            .Append(OfficialParameterTypeContract.Resolve(
              item.Mapping.SharedParameterType).SemanticType)
            .Append("\t\t")
            .Append(group.Id.ToString(CultureInfo.InvariantCulture))
            .Append("\t1\tOfficial exact source alias | ")
            .Append(Sanitize(item.Mapping.IfcEntity))
            .Append(" | ")
            .Append(Sanitize(item.Mapping.PropertySet))
            .Append(" | ")
            .Append(Sanitize(item.Mapping.IfcProperty))
            .Append("\t1\t0")
            .AppendLine();
        }
      }
    }

    private static Parameter ResolveParameter(ProjectionWrite projection)
    {
      Parameter byGuid = projection.Target.get_Parameter(projection.Guid);
      if (byGuid != null) return byGuid;
      if (projection.ExistingExactNameParameter != null)
        return projection.ExistingExactNameParameter;

      IList<Parameter> exact = projection.Target.GetParameters(projection.Name);
      if (exact == null || exact.Count == 0)
        throw new InvalidOperationException(
          projection.Kind + " 参数未绑定：" + projection.Name);
      if (exact.Count > 1)
        throw new InvalidOperationException(
          DuplicateOfficialSourceParameterCode
          + "：写入时检测到多个同名参数“"
          + projection.Name
          + "”。" );
      return exact[0];
    }

    private static OfficialParameterTypeDecision ValidateParameterType(
      Parameter parameter,
      string sharedParameterType,
      ProjectionWrite projection)
    {
      string actualSemantic = GetActualSemantic(parameter);
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
          actualSemantic);
      }
      StorageType expectedStorage = ExpectedStorageType(decision.StorageKind);
      if (parameter == null || parameter.StorageType != expectedStorage)
        throw ParameterTypeMismatch(
          projection.Name,
          decision.SemanticType,
          actualSemantic);
      try
      {
        if (OfficialParameterTypeContract.IsCompatible(
          decision.SemanticType,
          actualSemantic))
          return decision;
      }
      catch (InvalidOperationException)
      {
      }
      throw ParameterTypeMismatch(
        projection.Name,
        decision.SemanticType,
        actualSemantic);
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

    private static StorageType ExpectedStorageType(
      OfficialParameterStorageKind storageKind)
    {
      switch (storageKind)
      {
        case OfficialParameterStorageKind.String:
          return StorageType.String;
        case OfficialParameterStorageKind.Integer:
          return StorageType.Integer;
        default:
          return StorageType.Double;
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

    private static string ReadEmbeddedText(string name)
    {
      using (Stream stream = Assembly.GetExecutingAssembly()
        .GetManifestResourceStream(name))
      {
        if (stream == null)
          throw new InvalidDataException("缺少嵌入资源：" + name);
        using (var reader = new StreamReader(stream))
          return reader.ReadToEnd();
      }
    }

    private static string Sanitize(string value)
    {
      return (value ?? string.Empty)
        .Replace('\t', ' ')
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();
    }

    private sealed class ProjectionWrite
    {
      public OfficialHifcMapping Mapping { get; set; }
      public Element Target { get; set; }
      public string RawValue { get; set; }
      public Guid Guid { get; set; }
      public string Name { get; set; }
      public string Kind { get; set; }
      public bool RequiresGeneratedDefinition { get; set; }
      public Parameter ExistingExactNameParameter { get; set; }
    }

    private sealed class AliasGroupDefinition
    {
      public int Id { get; set; }
      public string PropertySet { get; set; }
      public ProjectionWrite[] Items { get; set; }
    }
  }
}
