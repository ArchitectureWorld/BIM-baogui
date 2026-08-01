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
    private const string AmbiguousSourceCode = "OFFICIAL_SOURCE_NAME_AMBIGUOUS";

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

      ValidateOfficialSourceAmbiguity(items);
      List<ProjectionWrite> projections = ExpandProjections(items);
      ValidateConflictingWrites(projections);
      PreflightExistingOfficialParameters(projections);

      var messages = new List<string>();
      EnsureDefinitionsAndBindings(document, projections, messages);

      foreach (ProjectionWrite projection in projections)
      {
        Parameter parameter = ResolveParameter(projection);
        ValidateStorageType(
          parameter,
          projection.Mapping.SharedParameterType,
          projection);
        if (parameter.IsReadOnly)
          throw new InvalidOperationException(
            projection.Kind + " 参数只读：" + projection.Name);
        SetValue(parameter, projection.RawValue);
      }

      document.Regenerate();
      foreach (ProjectionWrite projection in projections)
      {
        Parameter parameter = ResolveParameter(projection);
        if (!ReadbackMatches(parameter, projection.RawValue))
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
      return projections;
    }

    private static void ValidateOfficialSourceAmbiguity(
      IEnumerable<OfficialParameterWriteItem> items)
    {
      var conflicts = items
        .Where(item => !string.IsNullOrWhiteSpace(
          item.Mapping.OfficialSourceParameterName))
        .GroupBy(item =>
          item.Target.Id.IntegerValue.ToString(CultureInfo.InvariantCulture)
          + "|"
          + item.Mapping.OfficialSourceParameterName,
          StringComparer.Ordinal)
        .Select(group => new
        {
          Group = group,
          Properties = group
            .Select(item => item.Mapping.PropertyId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        })
        .Where(item => item.Properties.Length > 1)
        .ToArray();

      if (conflicts.Length == 0) return;
      IEnumerable<string> details = conflicts.Select(item =>
        item.Group.First().Mapping.OfficialSourceParameterName
        + " → "
        + string.Join(", ", item.Group.Select(value =>
          value.Mapping.PropertySet + "." + value.Mapping.IfcProperty)
          .Distinct(StringComparer.Ordinal)));
      throw new InvalidOperationException(
        AmbiguousSourceCode
        + "：官方配置按同一精确参数名读取多个不同属性，无法安全区分。"
        + string.Join("；", details));
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
            AmbiguousSourceCode
            + "：目标对象已经存在多个同名参数“"
            + projection.Name
            + "”，官方导出器的名称读取结果不确定。" );
        if (exact != null && exact.Count == 1)
        {
          projection.ExistingExactNameParameter = exact[0];
          projection.RequiresGeneratedDefinition = false;
          ValidateStorageType(
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
            .Append(NormalizeSharedParameterType(
              item.Mapping.SharedParameterType))
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
          AmbiguousSourceCode
          + "：写入时检测到多个同名参数“"
          + projection.Name
          + "”。" );
      return exact[0];
    }

    private static void ValidateStorageType(
      Parameter parameter,
      string sharedParameterType,
      ProjectionWrite projection)
    {
      StorageType expected = ExpectedStorageType(sharedParameterType);
      if (parameter == null || parameter.StorageType != expected)
        throw new InvalidOperationException(
          "参数类型不匹配："
          + projection.Name
          + "，期望="
          + expected
          + "，实际="
          + (parameter == null
            ? "Missing"
            : parameter.StorageType.ToString()));
    }

    private static StorageType ExpectedStorageType(string type)
    {
      string normalized = NormalizeSharedParameterType(type);
      if (normalized == "TEXT") return StorageType.String;
      if (normalized == "INTEGER" || normalized == "YESNO")
        return StorageType.Integer;
      return StorageType.Double;
    }

    private static string NormalizeSharedParameterType(string type)
    {
      string normalized = (type ?? string.Empty).Trim().ToUpperInvariant();
      switch (normalized)
      {
        case "TEXT":
        case "INTEGER":
        case "YESNO":
        case "LENGTH":
        case "AREA":
        case "VOLUME":
        case "ANGLE":
        case "NUMBER":
          return normalized;
        default:
          throw new InvalidOperationException(
            "不支持的共享参数类型：" + type);
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

    private static void SetValue(Parameter parameter, string raw)
    {
      raw = raw ?? string.Empty;
      switch (parameter.StorageType)
      {
        case StorageType.String:
          if (!parameter.Set(raw))
            throw new InvalidOperationException("文本参数写入失败。");
          return;
        case StorageType.Integer:
          if (!parameter.Set(ParseInteger(
            parameter.Definition.ParameterType,
            raw)))
            throw new InvalidOperationException("整数参数写入失败。");
          return;
        case StorageType.Double:
          double internalValue = ToInternalValue(
            parameter.Definition.ParameterType,
            ParseDouble(raw));
          if (!parameter.Set(internalValue))
            throw new InvalidOperationException("数值参数写入失败。");
          return;
        default:
          throw new InvalidOperationException(
            "不支持参数存储类型：" + parameter.StorageType);
      }
    }

    private static int ParseInteger(ParameterType type, string raw)
    {
      if (type == ParameterType.YesNo)
      {
        string normalized = (raw ?? string.Empty)
          .Trim()
          .ToLowerInvariant();
        if (normalized == "1"
          || normalized == "true"
          || normalized == "是"
          || normalized == "yes")
          return 1;
        if (normalized == "0"
          || normalized == "false"
          || normalized == "否"
          || normalized == "no")
          return 0;
        throw new FormatException(
          "布尔值只接受 true/false、是/否、1/0。");
      }
      if (!int.TryParse(
        raw,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out int value))
        throw new FormatException("不是有效整数：" + raw);
      return value;
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

    private static double ToInternalValue(ParameterType type, double value)
    {
      if (type == ParameterType.Length)
        return UnitUtils.ConvertToInternalUnits(
          value,
          DisplayUnitType.DUT_METERS);
      if (type == ParameterType.Area)
        return UnitUtils.ConvertToInternalUnits(
          value,
          DisplayUnitType.DUT_SQUARE_METERS);
      if (type == ParameterType.Volume)
        return UnitUtils.ConvertToInternalUnits(
          value,
          DisplayUnitType.DUT_CUBIC_METERS);
      if (type == ParameterType.Angle)
        return UnitUtils.ConvertToInternalUnits(
          value,
          DisplayUnitType.DUT_DECIMAL_DEGREES);
      return value;
    }

    private static bool ReadbackMatches(
      Parameter parameter,
      string expected)
    {
      switch (parameter.StorageType)
      {
        case StorageType.String:
          return string.Equals(
            parameter.AsString() ?? string.Empty,
            expected ?? string.Empty,
            StringComparison.Ordinal);
        case StorageType.Integer:
          return parameter.AsInteger()
            == ParseInteger(parameter.Definition.ParameterType, expected);
        case StorageType.Double:
          double target = ToInternalValue(
            parameter.Definition.ParameterType,
            ParseDouble(expected));
          return Math.Abs(parameter.AsDouble() - target) <= 1e-8;
        default:
          return false;
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
