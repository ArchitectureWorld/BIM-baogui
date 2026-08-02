using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class MvdIfcNormalizer
  {
    private readonly MvdIfcNormalizationCatalog _catalog;

    public MvdIfcNormalizer()
      : this(MvdIfcNormalizationCatalog.Instance)
    {
    }

    internal MvdIfcNormalizer(MvdIfcNormalizationCatalog catalog)
    {
      _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public MvdIfcNormalizationResult Normalize(IfcStepDocument document)
    {
      EnsureIfc4(document);
      var messages = new List<string>();
      int matchingPropertyCount = 0;
      int normalizedPropertySetCount = 0;
      int normalizedPropertyNameCount = 0;
      int normalizedValueTypeCount = 0;
      var changedPropertySets = new HashSet<int>();

      foreach (Relationship relationship in ReadRelationships(document))
      {
        string originalPropertySetName = DecodeRequiredString(
          relationship.PropertySet.Arguments[2],
          "属性集名称");
        IReadOnlyList<int> propertyIds = IfcStepSyntax.ParseReferenceList(
          relationship.PropertySet.Arguments[4]);

        foreach (int ownerId in relationship.OwnerIds)
        {
          IfcStepEntity owner = document.GetEntity(ownerId);
          foreach (int propertyId in propertyIds)
          {
            IfcStepEntity property = document.GetEntity(propertyId);
            if (!string.Equals(
              property.Type,
              "IFCPROPERTYSINGLEVALUE",
              StringComparison.OrdinalIgnoreCase))
              continue;
            string propertyName = DecodeRequiredString(
              property.Arguments[0],
              "属性名称");
            if (!_catalog.TryResolve(
              owner.Type,
              originalPropertySetName,
              propertyName,
              out MvdIfcNormalizationRule rule))
              continue;

            matchingPropertyCount++;
            if (!string.Equals(
              originalPropertySetName,
              rule.CanonicalPropertySet,
              StringComparison.Ordinal))
            {
              if (changedPropertySets.Add(relationship.PropertySet.Id))
                normalizedPropertySetCount++;
              relationship.PropertySet.SetArgument(
                2,
                IfcStepSyntax.EncodeString(rule.CanonicalPropertySet));
            }
            if (!string.Equals(
              propertyName,
              rule.CanonicalProperty,
              StringComparison.Ordinal))
            {
              property.SetArgument(
                0,
                IfcStepSyntax.EncodeString(rule.CanonicalProperty));
              normalizedPropertyNameCount++;
            }

            string normalizedValue = NormalizeNominalValue(
              property.Arguments[2],
              rule.TargetType);
            if (!string.Equals(
              normalizedValue,
              property.Arguments[2],
              StringComparison.Ordinal))
            {
              property.SetArgument(2, normalizedValue);
              normalizedValueTypeCount++;
            }
          }
        }
      }

      int removedDuplicates = RemoveInternalDuplicates(document);
      messages.Add("匹配 MVD 属性：" + matchingPropertyCount);
      messages.Add("规范化属性集名称：" + normalizedPropertySetCount);
      messages.Add("规范化属性名称：" + normalizedPropertyNameCount);
      messages.Add("规范化 IFC 值类型：" + normalizedValueTypeCount);
      messages.Add("移除 HIFC 重复属性：" + removedDuplicates);
      return new MvdIfcNormalizationResult
      {
        Success = matchingPropertyCount > 0,
        MatchingPropertyCount = matchingPropertyCount,
        NormalizedPropertySetCount = normalizedPropertySetCount,
        NormalizedPropertyNameCount = normalizedPropertyNameCount,
        NormalizedValueTypeCount = normalizedValueTypeCount,
        RemovedDuplicatePropertyCount = removedDuplicates,
        Messages = messages
      };
    }

    public MvdIfcValidationResult Validate(IfcStepDocument document)
    {
      var errors = new List<string>();
      try
      {
        EnsureIfc4(document);
      }
      catch (Exception exception)
      {
        errors.Add(exception.Message);
      }

      var projects = document.OfType("IFCPROJECT").ToArray();
      if (projects.Length == 0) errors.Add("IFC 不包含 IfcProject。");
      int matchingPropertyCount = 0;

      foreach (Relationship relationship in ReadRelationships(document))
      {
        string propertySetName = DecodeRequiredString(
          relationship.PropertySet.Arguments[2],
          "属性集名称");
        IReadOnlyList<int> propertyIds = IfcStepSyntax.ParseReferenceList(
          relationship.PropertySet.Arguments[4]);
        foreach (int ownerId in relationship.OwnerIds)
        {
          IfcStepEntity owner = document.GetEntity(ownerId);
          foreach (int propertyId in propertyIds)
          {
            IfcStepEntity property = document.GetEntity(propertyId);
            if (!string.Equals(
              property.Type,
              "IFCPROPERTYSINGLEVALUE",
              StringComparison.OrdinalIgnoreCase))
              continue;
            string propertyName = DecodeRequiredString(
              property.Arguments[0],
              "属性名称");

            if (string.Equals(
              propertySetName,
              "数据",
              StringComparison.Ordinal)
              && IsInternalAlias(owner.Type, propertyName))
            {
              errors.Add(
                owner.Type + " 的数据属性集仍包含内部别名：" + propertyName);
              continue;
            }

            if (!_catalog.TryResolve(
              owner.Type,
              propertySetName,
              propertyName,
              out MvdIfcNormalizationRule rule))
              continue;
            matchingPropertyCount++;
            if (!string.Equals(
              propertySetName,
              rule.CanonicalPropertySet,
              StringComparison.Ordinal))
              errors.Add("属性集未使用官方标识：" + propertySetName);
            if (!string.Equals(
              propertyName,
              rule.CanonicalProperty,
              StringComparison.Ordinal))
              errors.Add("属性未使用官方名称：" + propertyName);
            if (!HasTargetType(property.Arguments[2], rule.TargetType))
              errors.Add(
                propertyName
                + " 类型错误，要求 "
                + rule.TargetType
                + "，实际 "
                + property.Arguments[2]);
          }
        }
      }

      if (matchingPropertyCount == 0)
        errors.Add("IFC 上未找到可验收的 MVD 属性。");
      return new MvdIfcValidationResult
      {
        Success = errors.Count == 0,
        MatchingPropertyCount = matchingPropertyCount,
        Messages = errors.Count == 0
          ? new[] { "MVD IFC 回读验收通过。" }
          : errors
      };
    }

    private int RemoveInternalDuplicates(IfcStepDocument document)
    {
      List<Relationship> relationships = ReadRelationships(document).ToList();
      Dictionary<int, int> propertyReferenceCounts = document
        .OfType("IFCPROPERTYSET")
        .SelectMany(propertySet => IfcStepSyntax.ParseReferenceList(
          propertySet.Arguments[4]))
        .GroupBy(id => id)
        .ToDictionary(group => group.Key, group => group.Count());
      int removed = 0;

      foreach (IGrouping<int, Relationship> group in relationships
        .GroupBy(relationship => relationship.PropertySet.Id))
      {
        IfcStepEntity propertySet = group.First().PropertySet;
        string propertySetName = DecodeRequiredString(
          propertySet.Arguments[2],
          "属性集名称");
        if (!string.Equals(propertySetName, "数据", StringComparison.Ordinal))
          continue;

        string[] ownerTypes = group
          .SelectMany(relationship => relationship.OwnerIds)
          .Select(document.GetEntity)
          .Select(owner => owner.Type)
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .ToArray();
        IReadOnlyList<int> propertyIds = IfcStepSyntax.ParseReferenceList(
          propertySet.Arguments[4]);
        var remaining = new List<int>();
        foreach (int propertyId in propertyIds)
        {
          IfcStepEntity property = document.GetEntity(propertyId);
          string propertyName = DecodeRequiredString(
            property.Arguments[0],
            "属性名称");
          bool internalAlias = ownerTypes.Any(
            ownerType => IsInternalAlias(ownerType, propertyName));
          if (!internalAlias)
          {
            remaining.Add(propertyId);
            continue;
          }

          removed++;
          propertyReferenceCounts[propertyId]--;
          if (propertyReferenceCounts[propertyId] == 0) property.Delete();
        }

        if (remaining.Count > 0)
        {
          if (remaining.Count != propertyIds.Count)
            propertySet.SetArgument(
              4,
              IfcStepSyntax.FormatReferenceList(remaining));
          continue;
        }

        propertySet.Delete();
        foreach (Relationship relationship in group)
          relationship.Entity.Delete();
      }

      return removed;
    }

    private bool IsInternalAlias(string entity, string propertyName)
    {
      return _catalog.Rules
        .Where(rule => string.Equals(
          rule.Entity,
          entity,
          StringComparison.OrdinalIgnoreCase))
        .SelectMany(rule => rule.InternalAliases)
        .Any(alias => string.Equals(
          alias,
          propertyName,
          StringComparison.Ordinal));
    }

    private static IEnumerable<Relationship> ReadRelationships(
      IfcStepDocument document)
    {
      foreach (IfcStepEntity entity in document.OfType(
        "IFCRELDEFINESBYPROPERTIES"))
      {
        if (entity.Arguments.Count < 6)
          throw new InvalidDataException(
            "IfcRelDefinesByProperties 参数数量无效：#" + entity.Id);
        IReadOnlyList<int> ownerIds = IfcStepSyntax.ParseReferenceList(
          entity.Arguments[4]);
        int propertySetId = IfcStepSyntax.ParseReference(entity.Arguments[5]);
        IfcStepEntity propertySet = document.GetEntity(propertySetId);
        if (!string.Equals(
          propertySet.Type,
          "IFCPROPERTYSET",
          StringComparison.OrdinalIgnoreCase))
          continue;
        if (propertySet.Arguments.Count < 5)
          throw new InvalidDataException(
            "IfcPropertySet 参数数量无效：#" + propertySet.Id);
        yield return new Relationship(entity, ownerIds, propertySet);
      }
    }

    private static string NormalizeNominalValue(
      string token,
      string targetType)
    {
      if (string.Equals(token?.Trim(), "$", StringComparison.Ordinal))
        return "$";
      if (!IfcStepSyntax.TryParseTypedValue(
        token,
        out string sourceType,
        out string inner))
        throw new InvalidDataException("IfcPropertySingleValue 值无效：" + token);
      string target = targetType.Trim().ToUpperInvariant();
      ValidateInnerValue(target, inner);
      if (string.Equals(sourceType, target, StringComparison.Ordinal))
        return token;
      return IfcStepSyntax.FormatTypedValue(target, inner);
    }

    private static bool HasTargetType(string token, string targetType)
    {
      if (!IfcStepSyntax.TryParseTypedValue(
        token,
        out string actualType,
        out string inner))
        return false;
      string target = targetType.Trim().ToUpperInvariant();
      try
      {
        ValidateInnerValue(target, inner);
      }
      catch
      {
        return false;
      }
      return string.Equals(actualType, target, StringComparison.Ordinal);
    }

    private static void ValidateInnerValue(string targetType, string inner)
    {
      switch (targetType)
      {
        case "IFCLABEL":
        case "IFCTEXT":
        case "IFCIDENTIFIER":
        case "IFCURIREFERENCE":
        case "IFCDATE":
        case "IFCDATETIME":
          IfcStepSyntax.DecodeString(inner);
          return;
        case "IFCBOOLEAN":
        case "IFCLOGICAL":
          if (inner == ".T." || inner == ".F." || inner == ".U.") return;
          throw new InvalidDataException("IFC 布尔值无效：" + inner);
        case "IFCINTEGER":
        case "IFCTIMESTAMP":
          if (long.TryParse(
            inner,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out _)) return;
          throw new InvalidDataException("IFC 整数值无效：" + inner);
        default:
          if (IfcStepSyntax.IsFiniteNumber(inner)) return;
          throw new InvalidDataException("IFC 数值无效：" + inner);
      }
    }

    private static string DecodeRequiredString(string token, string label)
    {
      try
      {
        return IfcStepSyntax.DecodeString(token);
      }
      catch (Exception exception)
      {
        throw new InvalidDataException(label + "无效：" + token, exception);
      }
    }

    private static void EnsureIfc4(IfcStepDocument document)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (!string.Equals(document.Schema, "IFC4", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException(
          "MVD IFC 规范化首版仅支持 IFC4，当前 schema="
          + (document.Schema ?? string.Empty));
    }

    private sealed class Relationship
    {
      public Relationship(
        IfcStepEntity entity,
        IReadOnlyList<int> ownerIds,
        IfcStepEntity propertySet)
      {
        Entity = entity;
        OwnerIds = ownerIds;
        PropertySet = propertySet;
      }

      public IfcStepEntity Entity { get; }
      public IReadOnlyList<int> OwnerIds { get; }
      public IfcStepEntity PropertySet { get; }
    }
  }
}
