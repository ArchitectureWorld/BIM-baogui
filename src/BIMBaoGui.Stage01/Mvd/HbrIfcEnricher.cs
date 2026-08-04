using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class HbrIfcEnricher
  {
    private static readonly Guid GlobalIdNamespace = Guid.Parse(
      "b3f9dc18-f6b4-5bd8-9d65-2ebed89f63c8");

    public HbrIfcEnrichmentResult Apply(
      IfcStepDocument document,
      IEnumerable<HbrIfcEnrichmentValue> values)
    {
      return Apply(document, values, null);
    }

    internal HbrIfcEnrichmentResult Apply(
      IfcStepDocument document,
      IEnumerable<HbrIfcEnrichmentValue> values,
      IHbrIfcOperationObserver observer)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (values == null) throw new ArgumentNullException(nameof(values));
      List<HbrIfcEnrichmentValue> materialized = values.ToList();
      List<PlannedValue> executionPlan = CreateExecutionPlan(materialized);
      HbrIfcEnrichmentValue[] executionValues = executionPlan
        .Select(item => item.Value)
        .ToArray();
      if (!SupportsIfc4Schema(document))
        throw new InvalidDataException("HBR IFC enrichment 仅支持 IFC4。");

      try
      {
        document.ValidateStructure();
        if (executionValues.Length == 0)
          return new HbrIfcEnrichmentResult
          {
            Success = true,
            Fields = Array.Empty<HbrIfcEnrichmentFieldResult>()
          };

        IfcStepDocument candidate = document.Clone(observer);
        HbrIfcEnrichmentResult result = ApplyCandidate(
          candidate,
          executionValues,
          observer);
        result.Fields = RestoreOriginalFieldOrder(
          result.Fields,
          executionPlan);
        if (result.CreatedProperties != 0
          || result.CreatedPropertySets != 0
          || result.CreatedRelationships != 0
          || result.UpdatedProperties != 0)
        {
          document.ReplaceWith(candidate);
        }
        return result;
      }
      catch (EnrichmentFailure exception)
      {
        return CreateFailureResult(
          materialized,
          executionPlan,
          exception);
      }
      catch (Exception exception) when (
        exception is InvalidDataException
        || exception is ArgumentException
        || exception is InvalidOperationException
        || exception is KeyNotFoundException
        || exception is OverflowException)
      {
        return CreateMutationFailureResult(materialized, exception);
      }
    }

    internal static bool SupportsIfc4Schema(IfcStepDocument document)
    {
      return document != null && string.Equals(
        document.Schema,
        "IFC4",
        StringComparison.OrdinalIgnoreCase);
    }

    internal static HbrIfcBatchInspectionResult
      InspectExistingFields(
        IfcStepDocument document,
        IReadOnlyList<HbrIfcEnrichmentValue> values,
        IHbrIfcOperationObserver observer)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (values == null) throw new ArgumentNullException(nameof(values));
      if (!SupportsIfc4Schema(document))
        return CreateBatchInspectionFailure(
          values,
          HbrIfcErrorCodes.InvalidValue,
          "HBR IFC inspection 仅支持 IFC4。");
      try
      {
        document.ValidateStructure();
      }
      catch (Exception exception) when (
        exception is InvalidDataException
        || exception is ArgumentException
        || exception is InvalidOperationException
        || exception is KeyNotFoundException
        || exception is OverflowException)
      {
        return CreateBatchInspectionFailure(
          values,
          HbrIfcErrorCodes.InvalidValue,
          "IFC inspection 文档结构无效：" + exception.Message);
      }
      if (values.Count == 0)
        return CreateBatchInspectionResult(
          Array.Empty<HbrIfcFieldInspectionResult>());

      HbrIfcGraphIndex graph;
      try
      {
        graph = new HbrIfcGraphIndex(document, observer);
      }
      catch (Exception exception) when (
        exception is InvalidDataException
        || exception is ArgumentException
        || exception is InvalidOperationException
        || exception is KeyNotFoundException
        || exception is OverflowException)
      {
        return CreateBatchInspectionFailure(
          values,
          HbrIfcErrorCodes.InvalidValue,
          "IFC inspection 全图索引失败：" + exception.Message);
      }

      var prepared = new PreparedInspection[values.Count];
      var results = new HbrIfcFieldInspectionResult[values.Count];
      for (int index = 0; index < values.Count; index++)
      {
        try
        {
          ResolvedOwner resolvedOwner = ResolveOwner(
            graph,
            values[index],
            index);
          string typedValue = FormatTypedValue(values[index], index);
          prepared[index] = new PreparedInspection(
            index,
            values[index],
            typedValue,
            resolvedOwner.Entity);
        }
        catch (EnrichmentFailure exception)
        {
          results[index] = CreateInspectionFailure(
            exception.ErrorCode,
            exception.Message,
            propertyIdentity: values[index]?.PropertyIdentity);
        }
      }

      PreparedInspection firstPrepared = prepared.FirstOrDefault(
        item => item != null);
      if (firstPrepared == null)
        return CreateBatchInspectionResult(results);
      try
      {
        graph.ValidateGraphOnce(firstPrepared.ValueIndex);
      }
      catch (EnrichmentFailure exception)
      {
        foreach (PreparedInspection field in prepared.Where(item => item != null))
          results[field.ValueIndex] = CreateInspectionFailure(
            exception.ErrorCode,
            exception.Message,
            field.Owner.Id,
            propertyIdentity: field.Value.PropertyIdentity);
        return CreateBatchInspectionResult(
          results,
          exception.ErrorCode,
          exception.Message);
      }
      catch (Exception exception) when (
        exception is InvalidDataException
        || exception is ArgumentException
        || exception is InvalidOperationException
        || exception is KeyNotFoundException
        || exception is OverflowException)
      {
        foreach (PreparedInspection field in prepared.Where(item => item != null))
          results[field.ValueIndex] = CreateInspectionFailure(
            HbrIfcErrorCodes.InvalidValue,
            "IFC inspection 全图校验失败：" + exception.Message,
            field.Owner.Id,
            propertyIdentity: field.Value.PropertyIdentity);
        return CreateBatchInspectionResult(
          results,
          HbrIfcErrorCodes.InvalidValue,
          "IFC inspection 全图校验失败：" + exception.Message);
      }

      Observe(
        observer,
        HbrIfcOperationKind.BatchInspection,
        prepared.Count(item => item != null));
      foreach (PreparedInspection field in prepared.Where(item => item != null))
      {
        Observe(observer, HbrIfcOperationKind.IndexedFieldLookup);
        try
        {
          results[field.ValueIndex] = InspectExistingField(graph, field);
        }
        catch (EnrichmentFailure exception)
        {
          results[field.ValueIndex] = CreateInspectionFailure(
            exception.ErrorCode,
            exception.Message,
            field.Owner.Id,
            propertyIdentity: field.Value.PropertyIdentity);
        }
      }
      return CreateBatchInspectionResult(results);
    }

    private static HbrIfcFieldInspectionResult InspectExistingField(
      HbrIfcGraphIndex graph,
      PreparedInspection field)
    {
      string propertyIdentity = field.Value.PropertyIdentity;
      ExistingTarget target = graph.FindExistingTarget(
        field.Owner,
        field.Value.PropertySetName,
        field.ValueIndex);
      if (target == null || !target.OwnerRelated)
        return CreateInspectionFailure(
          HbrIfcErrorCodes.IfcFieldNotFound,
          "exact owner 路径未找到目标 Pset。",
          field.Owner.Id,
          propertyIdentity: propertyIdentity);
      IfcStepEntity property = graph.FindExistingProperty(
        target.PropertySet,
        field.Value.PropertyName,
        field.ValueIndex);
      if (property == null)
        return CreateInspectionFailure(
          HbrIfcErrorCodes.IfcFieldNotFound,
          "exact Pset 未引用目标 property。",
          field.Owner.Id,
          target.PropertySet.Id,
          relationshipId: target.Relationship.Id,
          propertyIdentity: propertyIdentity);

      string token = property.Arguments[2];
      if (!IfcStepSyntax.TryParseTypedValue(
        token,
        out string actualType,
        out _))
        return CreateInspectionFailure(
          HbrIfcErrorCodes.IfcValueMismatch,
          "目标 property nominal value 不是 typed token。",
          field.Owner.Id,
          target.PropertySet.Id,
          property.Id,
          target.Relationship.Id,
          typedToken: token,
          propertyIdentity: propertyIdentity);
      IfcStepSyntax.TryParseTypedValue(
        field.TypedValue,
        out string expectedType,
        out _);
      if (!string.Equals(actualType, expectedType, StringComparison.Ordinal))
        return CreateInspectionFailure(
          HbrIfcErrorCodes.IfcTypeMismatch,
          "目标 property declared IFC type 不匹配。",
          field.Owner.Id,
          target.PropertySet.Id,
          property.Id,
          target.Relationship.Id,
          actualType,
          token,
          propertyIdentity);
      if (!string.Equals(token, field.TypedValue, StringComparison.Ordinal))
        return CreateInspectionFailure(
          HbrIfcErrorCodes.IfcValueMismatch,
          "目标 property typed value 不匹配。",
          field.Owner.Id,
          target.PropertySet.Id,
          property.Id,
          target.Relationship.Id,
          actualType,
          token,
          propertyIdentity);

      return new HbrIfcFieldInspectionResult(
        propertyIdentity,
        true,
        string.Empty,
        "IFC 字段 exact 回读通过。",
        ownerId: field.Owner.Id,
        propertyId: property.Id,
        propertySetId: target.PropertySet.Id,
        relationshipId: target.Relationship.Id,
        actualIfcType: actualType,
        typedToken: token);
    }

    private static HbrIfcBatchInspectionResult CreateBatchInspectionFailure(
        IReadOnlyList<HbrIfcEnrichmentValue> values,
        string errorCode,
        string message)
    {
      HbrIfcFieldInspectionResult[] fields = values
        .Select(value => CreateInspectionFailure(
          errorCode,
          message,
          propertyIdentity: value?.PropertyIdentity))
        .ToArray();
      return CreateBatchInspectionResult(fields, errorCode, message);
    }

    private static HbrIfcBatchInspectionResult CreateBatchInspectionResult(
      IReadOnlyList<HbrIfcFieldInspectionResult> fields,
      string globalErrorCode = null,
      string globalMessage = null)
    {
      HbrIfcFieldInspectionResult firstFailure = fields.FirstOrDefault(
        field => !field.Success);
      bool success = globalErrorCode == null && firstFailure == null;
      return new HbrIfcBatchInspectionResult(
        success,
        success
          ? string.Empty
          : globalErrorCode ?? firstFailure.ErrorCode,
        success
          ? "IFC 批量字段 exact 回读通过。"
          : globalMessage ?? firstFailure.Message,
        fields);
    }

    private static HbrIfcFieldInspectionResult CreateInspectionFailure(
      string errorCode,
      string message,
      int? ownerId = null,
      int? propertySetId = null,
      int? propertyId = null,
      int? relationshipId = null,
      string actualIfcType = null,
      string typedToken = null,
      string propertyIdentity = null)
    {
      return new HbrIfcFieldInspectionResult(
        propertyIdentity,
        false,
        errorCode,
        message,
        ownerId,
        propertyId,
        propertySetId,
        relationshipId,
        actualIfcType,
        typedToken);
    }

    private static HbrIfcEnrichmentResult ApplyCandidate(
      IfcStepDocument document,
      IReadOnlyList<HbrIfcEnrichmentValue> values,
      IHbrIfcOperationObserver observer = null)
    {
      var fields = new List<HbrIfcEnrichmentFieldResult>();
      int createdProperties = 0;
      int createdPropertySets = 0;
      int createdRelationships = 0;
      int updatedProperties = 0;
      var batchTypedValues = new Dictionary<string, string>(
        StringComparer.Ordinal);
      var graph = new HbrIfcGraphIndex(
        document,
        observer);
      var pendingFields = new List<PendingField>();

      for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
      {
        HbrIfcEnrichmentValue value = values[valueIndex];
        ResolvedOwner resolvedOwner = ResolveOwner(
          graph,
          value,
          valueIndex);
        IfcStepEntity owner = resolvedOwner.Entity;
        string typedValue = FormatTypedValue(value, valueIndex);
        string batchTargetKey = owner.Id.ToString(CultureInfo.InvariantCulture)
          + "|" + (value.PropertySetName?.Length ?? -1)
          + ":" + value.PropertySetName
          + "|" + (value.PropertyName?.Length ?? -1)
          + ":" + value.PropertyName;
        if (batchTypedValues.TryGetValue(
          batchTargetKey,
          out string previousTypedValue))
        {
          if (!string.Equals(
            previousTypedValue,
            typedValue,
            StringComparison.Ordinal))
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcPropertyConflict,
              "同批 enrichment 对同一 IFC property 提供了不同 typed token。");
        }
        else
        {
          batchTypedValues.Add(batchTargetKey, typedValue);
        }
        graph.ValidateGraphOnce(valueIndex);
        ExistingTarget existing = graph.FindExistingTarget(
          owner,
          value.PropertySetName,
          valueIndex);
        IfcStepEntity property = existing == null
          ? null
          : graph.FindExistingProperty(
            existing.PropertySet,
            value.PropertyName,
            valueIndex);
        string expectedName = IfcStepSyntax.EncodeString(value.PropertyName);
        bool propertyRequiresMutation = property != null
          && (!string.Equals(
              property.Arguments[0],
              expectedName,
              StringComparison.Ordinal)
            || !string.Equals(
              property.Arguments[2],
              typedValue,
              StringComparison.Ordinal));
        if (existing != null
          && existing.OwnerRelated
          && (property == null || propertyRequiresMutation)
          && graph.PropertySetServesOtherOwner(
            existing.PropertySet.Id,
            owner.Id))
          throw new EnrichmentFailure(
            valueIndex,
            HbrIfcErrorCodes.IfcPropertySetConflict,
            "目标 Pset 同时服务其他 owner，禁止外溢写入。");
        if (propertyRequiresMutation
          && graph.PropertyIsSharedByAnotherSet(
            property.Id,
            existing.PropertySet.Id))
          throw new EnrichmentFailure(
            valueIndex,
            HbrIfcErrorCodes.IfcPropertyConflict,
            "目标 property 被其他 Pset 共享，禁止外溢更新。");
        if (property == null)
        {
          property = document.AddEntity(
            "IFCPROPERTYSINGLEVALUE",
            new[]
            {
              expectedName,
              "$",
              typedValue,
              "$"
            });
          graph.RegisterEntity(property);
          createdProperties++;
        }
        else
        {
          bool changed = false;
          if (!string.Equals(
            property.Arguments[0],
            expectedName,
            StringComparison.Ordinal))
          {
            property.SetArgument(0, expectedName);
            changed = true;
          }
          if (!string.Equals(
            property.Arguments[2],
            typedValue,
            StringComparison.Ordinal))
          {
            property.SetArgument(2, typedValue);
            changed = true;
          }
          if (changed) updatedProperties++;
        }

        IfcStepEntity propertySet;
        IfcStepEntity relationship;
        if (existing != null)
        {
          propertySet = existing.PropertySet;
          relationship = existing.Relationship;
          if (relationship == null)
          {
            string relationshipGlobalId = IfcGuidCodec.CreateDeterministic(
              GlobalIdNamespace,
              "RELATIONSHIP|" + resolvedOwner.GlobalId
              + "|" + value.PropertySetName);
            graph.EnsureGlobalIdAvailable(
              relationshipGlobalId,
              valueIndex,
              HbrIfcErrorCodes.IfcRelationshipConflict);
            relationship = document.AddEntity(
              "IFCRELDEFINESBYPROPERTIES",
              new[]
              {
                IfcStepSyntax.EncodeString(relationshipGlobalId),
                "$",
                "$",
                "$",
                IfcStepSyntax.FormatReferenceList(new[] { owner.Id }),
                "#" + propertySet.Id.ToString(CultureInfo.InvariantCulture)
              });
            graph.RegisterRelationship(relationship);
            createdRelationships++;
          }
          graph.AttachProperty(propertySet, property);
        }
        else
        {
          string ownerKey = resolvedOwner.GlobalId;
          string propertySetGlobalId = IfcGuidCodec.CreateDeterministic(
            GlobalIdNamespace,
            "PSET|" + ownerKey + "|" + value.PropertySetName);
          graph.EnsureGlobalIdAvailable(
            propertySetGlobalId,
            valueIndex,
            HbrIfcErrorCodes.IfcPropertySetConflict);
          propertySet = document.AddEntity(
            "IFCPROPERTYSET",
            new[]
            {
              IfcStepSyntax.EncodeString(propertySetGlobalId),
              "$",
              IfcStepSyntax.EncodeString(value.PropertySetName),
              "$",
              IfcStepSyntax.FormatReferenceList(new[] { property.Id })
            });
          graph.RegisterPropertySet(propertySet);
          createdPropertySets++;

          string relationshipGlobalId = IfcGuidCodec.CreateDeterministic(
            GlobalIdNamespace,
            "RELATIONSHIP|" + ownerKey + "|" + value.PropertySetName);
          graph.EnsureGlobalIdAvailable(
            relationshipGlobalId,
            valueIndex,
            HbrIfcErrorCodes.IfcRelationshipConflict);
          relationship = document.AddEntity(
            "IFCRELDEFINESBYPROPERTIES",
            new[]
            {
              IfcStepSyntax.EncodeString(relationshipGlobalId),
              "$",
              "$",
              "$",
              IfcStepSyntax.FormatReferenceList(new[] { owner.Id }),
              "#" + propertySet.Id.ToString(CultureInfo.InvariantCulture)
            });
          graph.RegisterRelationship(relationship);
          createdRelationships++;
        }

        pendingFields.Add(new PendingField(
          valueIndex,
          value,
          typedValue,
          owner,
          property,
          propertySet,
          relationship));
      }

      graph.FlushDirtyPropertySets();
      var inspectionContext = new BatchInspectionContext(
        document,
        pendingFields,
        observer);
      foreach (PendingField pending in pendingFields)
      {
        HbrIfcFieldInspectionResult inspection =
          inspectionContext.Inspect(pending);
        if (!inspection.Success)
          throw new EnrichmentFailure(
            pending.ValueIndex,
            inspection.ErrorCode,
            "IFC enrichment exact 回读失败：" + inspection.Message);
        if (inspection.OwnerId != pending.Owner.Id
          || inspection.PropertyId != pending.Property.Id
          || inspection.PropertySetId != pending.PropertySet.Id
          || inspection.RelationshipId != pending.Relationship.Id)
          throw new EnrichmentFailure(
            pending.ValueIndex,
            HbrIfcErrorCodes.IfcValueMismatch,
            "IFC enrichment exact 回读路径 ID 与写入计划不一致。");

        fields.Add(new HbrIfcEnrichmentFieldResult
        {
          PropertyIdentity = pending.Value.PropertyIdentity,
          Success = true,
          ErrorCode = string.Empty,
          Message = "IFC 字段 enrichment 成功。",
          ExactInspectionPassed = true,
          OwnerId = pending.Owner.Id,
          PropertyId = pending.Property.Id,
          PropertySetId = pending.PropertySet.Id,
          RelationshipId = pending.Relationship.Id
        });
      }

      return new HbrIfcEnrichmentResult
      {
        Success = fields.All(field => field.Success),
        CreatedProperties = createdProperties,
        CreatedPropertySets = createdPropertySets,
        CreatedRelationships = createdRelationships,
        UpdatedProperties = updatedProperties,
        Fields = fields
      };
    }

    private static void Observe(
      IHbrIfcOperationObserver observer,
      HbrIfcOperationKind kind,
      int itemCount = 1)
    {
      if (observer == null) return;
      try
      {
        observer.Observe(new HbrIfcOperationEvent(kind, itemCount));
      }
      catch
      {
      }
    }

    private static HbrIfcEnrichmentResult CreateFailureResult(
      IReadOnlyList<HbrIfcEnrichmentValue> values,
      IReadOnlyList<PlannedValue> executionPlan,
      EnrichmentFailure failure)
    {
      int originalFailureIndex = executionPlan[failure.ValueIndex]
        .OriginalIndex;
      return new HbrIfcEnrichmentResult
      {
        Success = false,
        CreatedProperties = 0,
        CreatedPropertySets = 0,
        CreatedRelationships = 0,
        UpdatedProperties = 0,
        Fields = values.Select((value, index) =>
          new HbrIfcEnrichmentFieldResult
          {
            PropertyIdentity = value?.PropertyIdentity,
            Success = false,
            ErrorCode = index == originalFailureIndex
              ? failure.ErrorCode
              : HbrIfcErrorCodes.TransactionAborted,
            Message = index == originalFailureIndex
              ? failure.Message
              : "同批 IFC enrichment 已事务中止。"
          }).ToArray()
      };
    }

    private static List<PlannedValue> CreateExecutionPlan(
      IReadOnlyList<HbrIfcEnrichmentValue> values)
    {
      return values.Select((value, index) => new PlannedValue(
          value,
          index))
        .OrderBy(item => CanonicalOwnerType(item.Value), StringComparer.Ordinal)
        .ThenBy(item => item.Value?.OwnerGlobalId ?? string.Empty,
          StringComparer.Ordinal)
        .ThenBy(item => CanonicalOwnerStrategy(item.Value),
          StringComparer.Ordinal)
        .ThenBy(item => item.Value?.PropertySetName ?? string.Empty,
          StringComparer.Ordinal)
        .ThenBy(item => item.Value?.PropertyName ?? string.Empty,
          StringComparer.Ordinal)
        .ThenBy(item => item.Value?.PropertyIdentity ?? string.Empty,
          StringComparer.Ordinal)
        .ThenBy(item => item.Value?.SemanticKey ?? string.Empty,
          StringComparer.Ordinal)
        .ThenBy(item => CanonicalDeclaredType(item.Value),
          StringComparer.Ordinal)
        .ThenBy(item => item.Value?.CanonicalValue ?? string.Empty,
          StringComparer.Ordinal)
        .ThenBy(item => item.OriginalIndex)
        .ToList();
    }

    private static IReadOnlyList<HbrIfcEnrichmentFieldResult>
      RestoreOriginalFieldOrder(
        IReadOnlyList<HbrIfcEnrichmentFieldResult> fields,
        IReadOnlyList<PlannedValue> executionPlan)
    {
      if (fields == null || fields.Count != executionPlan.Count)
        throw new InvalidDataException(
          "IFC enrichment 字段结果数量与执行计划不一致。");
      var originalOrder = new HbrIfcEnrichmentFieldResult[fields.Count];
      for (int executionIndex = 0;
        executionIndex < executionPlan.Count;
        executionIndex++)
      {
        originalOrder[executionPlan[executionIndex].OriginalIndex] =
          fields[executionIndex];
      }
      return originalOrder;
    }

    private static string CanonicalOwnerType(HbrIfcEnrichmentValue value)
    {
      return value?.OwnerEntityType?.Trim().ToUpperInvariant()
        ?? string.Empty;
    }

    private static string CanonicalOwnerStrategy(HbrIfcEnrichmentValue value)
    {
      return value?.OwnerStrategy?.Trim().ToUpperInvariant()
        ?? string.Empty;
    }

    private static string CanonicalDeclaredType(HbrIfcEnrichmentValue value)
    {
      return value?.DeclaredIfcType?.Trim().ToUpperInvariant()
        ?? string.Empty;
    }

    private static HbrIfcEnrichmentResult CreateMutationFailureResult(
      IReadOnlyList<HbrIfcEnrichmentValue> values,
      Exception exception)
    {
      return new HbrIfcEnrichmentResult
      {
        Success = false,
        CreatedProperties = 0,
        CreatedPropertySets = 0,
        CreatedRelationships = 0,
        UpdatedProperties = 0,
        Fields = values.Select(value => new HbrIfcEnrichmentFieldResult
        {
          PropertyIdentity = value?.PropertyIdentity,
          Success = false,
          ExactInspectionPassed = false,
          ErrorCode = HbrIfcErrorCodes.IfcMutationFailed,
          Message = "IFC candidate mutation 失败：" + exception.Message
        }).ToArray()
      };
    }

    private static ResolvedOwner ResolveOwner(
      HbrIfcGraphIndex graph,
      HbrIfcEnrichmentValue value,
      int valueIndex)
    {
      ValidateValueContract(value, valueIndex);

      IReadOnlyList<IfcStepEntity> candidates =
        graph.GetEntitiesByType(value.OwnerEntityType);
      if (!string.IsNullOrWhiteSpace(value.OwnerGlobalId))
      {
        if (!HbrIfcRelatedObjectTypes.Contains(value.OwnerEntityType))
          throw new EnrichmentFailure(
            valueIndex,
            HbrIfcErrorCodes.InvalidValue,
            "IFC owner entity type 不允许用于 IfcRelDefinesByProperties.RelatedObjects。");
        if (!IfcGuidCodec.IsValid(value.OwnerGlobalId))
          throw new EnrichmentFailure(
            valueIndex,
            HbrIfcErrorCodes.InvalidValue,
            "IFC owner GlobalId 无效。");
        IReadOnlyList<IfcStepEntity> matches = graph.FindOwnersByGlobalId(
          value.OwnerEntityType,
          value.OwnerGlobalId,
          valueIndex);
        if (matches.Count == 0)
          throw new EnrichmentFailure(
            valueIndex,
            HbrIfcErrorCodes.IfcOwnerNotFound,
            "按 entity type + GlobalId 找不到 IFC owner。");
        if (matches.Count > 1)
          throw new EnrichmentFailure(
            valueIndex,
            HbrIfcErrorCodes.IfcOwnerConflict,
            "entity type + GlobalId 匹配到多个 IFC owner。");
        return new ResolvedOwner(matches[0], value.OwnerGlobalId);
      }

      if (!string.Equals(
        value.OwnerStrategy,
        HbrIfcOwnerStrategies.SingleEntityByType,
        StringComparison.Ordinal))
        throw new EnrichmentFailure(
          valueIndex,
          HbrIfcErrorCodes.RuleNotImplemented,
          "当前 IFC owner strategy 尚未实现。");
      string canonicalType = value.OwnerEntityType.Trim().ToUpperInvariant();
      if (canonicalType != "IFCPROJECT"
        && canonicalType != "IFCSITE"
        && canonicalType != "IFCBUILDING")
        throw new EnrichmentFailure(
          valueIndex,
          HbrIfcErrorCodes.RuleNotImplemented,
          "SINGLE_ENTITY_BY_TYPE 仅支持已验证空间根实体。");
      if (!HbrIfcRelatedObjectTypes.Contains(value.OwnerEntityType))
        throw new EnrichmentFailure(
          valueIndex,
          HbrIfcErrorCodes.InvalidValue,
          "IFC owner entity type 不允许用于 IfcRelDefinesByProperties.RelatedObjects。");
      string singleCandidateGlobalId = null;
      foreach (IfcStepEntity candidate in candidates)
        singleCandidateGlobalId = ReadOwnerGlobalId(candidate, valueIndex);
      if (candidates.Count == 0)
        throw new EnrichmentFailure(
          valueIndex,
          HbrIfcErrorCodes.IfcOwnerNotFound,
          "按 entity type 找不到唯一 IFC owner。");
      if (candidates.Count > 1)
        throw new EnrichmentFailure(
          valueIndex,
          HbrIfcErrorCodes.IfcOwnerConflict,
          "按 entity type 匹配到多个 IFC owner。");
      return new ResolvedOwner(
        candidates[0],
        singleCandidateGlobalId);
    }

    private static void ValidateValueContract(
      HbrIfcEnrichmentValue value,
      int valueIndex)
    {
      if (value == null)
        throw new EnrichmentFailure(
          valueIndex,
          HbrIfcErrorCodes.InvalidValue,
          "IFC enrichment 输入不能为空。");
      if (string.IsNullOrWhiteSpace(value.OwnerEntityType))
        throw new EnrichmentFailure(
          valueIndex,
          HbrIfcErrorCodes.InvalidValue,
          "IFC owner entity type 不能为空。");
      if (string.IsNullOrWhiteSpace(value.PropertySetName)
        || string.IsNullOrWhiteSpace(value.PropertyName)
        || string.IsNullOrWhiteSpace(value.PropertyIdentity)
        || string.IsNullOrWhiteSpace(value.SemanticKey))
        throw new EnrichmentFailure(
          valueIndex,
          HbrIfcErrorCodes.InvalidValue,
          "IFC Pset/property 名称、property identity 和 semantic key 均不能为空。");
    }

    private static void ValidateGlobalIdArgument(
      IfcStepEntity entity,
      int valueIndex,
      string errorCode)
    {
      string globalId;
      try
      {
        globalId = IfcStepSyntax.DecodeString(entity.Arguments[0]);
      }
      catch (Exception exception)
      {
        throw new EnrichmentFailure(
          valueIndex,
          errorCode,
          entity.Type + " GlobalId 不是 STEP 字符串：#" + entity.Id,
          exception);
      }
      if (!IfcGuidCodec.IsValid(globalId))
        throw new EnrichmentFailure(
          valueIndex,
          errorCode,
          entity.Type + " GlobalId 不是规范 22 字符值：#" + entity.Id);
    }

    private static string ReadOwnerGlobalId(
      IfcStepEntity entity,
      int valueIndex)
    {
      string globalId;
      try
      {
        if (entity.Arguments.Count == 0)
          throw new InvalidDataException();
        globalId = IfcStepSyntax.DecodeString(entity.Arguments[0]);
      }
      catch (Exception exception)
      {
        throw new EnrichmentFailure(
          valueIndex,
          HbrIfcErrorCodes.InvalidValue,
          "IFC owner 第 0 参数不是有效 GlobalId 字符串。",
          exception);
      }
      if (!IfcGuidCodec.IsValid(globalId))
        throw new EnrichmentFailure(
          valueIndex,
          HbrIfcErrorCodes.InvalidValue,
          "IFC owner GlobalId 不是规范的 22 字符值。");
      return globalId;
    }

    private static string FormatTypedValue(
      HbrIfcEnrichmentValue value,
      int valueIndex)
    {
      string type = value.DeclaredIfcType?.Trim().ToUpperInvariant();
      HbrIfcCanonicalValueDecision decision =
        HbrIfcCanonicalValuePolicy.Validate(type, value.CanonicalValue);
      if (!decision.Success)
        throw InvalidTypedValue(valueIndex, decision.Message);
      string inner = decision.RequiresStringEncoding
        ? EncodeTypedString(decision.NormalizedValue, valueIndex)
        : decision.NormalizedValue;
      return IfcStepSyntax.FormatTypedValue(type, inner);
    }

    private static string EncodeTypedString(string value, int valueIndex)
    {
      try
      {
        return IfcStepSyntax.EncodeString(value);
      }
      catch (InvalidDataException exception)
      {
        throw new EnrichmentFailure(
          valueIndex,
          HbrIfcErrorCodes.InvalidValue,
          "IFC typed string 无效：" + exception.Message,
          exception);
      }
    }

    private static EnrichmentFailure InvalidTypedValue(
      int valueIndex,
      string message)
    {
      return new EnrichmentFailure(
        valueIndex,
        HbrIfcErrorCodes.InvalidValue,
        message);
    }

    private sealed class HbrIfcGraphIndex
    {
      private readonly IfcStepDocument _document;
      private readonly IHbrIfcOperationObserver _observer;
      private readonly Dictionary<string, List<IfcStepEntity>> _entitiesByType =
        new Dictionary<string, List<IfcStepEntity>>(
          StringComparer.OrdinalIgnoreCase);
      private readonly Dictionary<string, Dictionary<string, List<IfcStepEntity>>>
        _ownersByTypeAndGlobalId =
          new Dictionary<string, Dictionary<string, List<IfcStepEntity>>>(
            StringComparer.OrdinalIgnoreCase);
      private readonly Dictionary<string, List<IfcStepEntity>> _globalIdOccupants =
        new Dictionary<string, List<IfcStepEntity>>(StringComparer.Ordinal);
      private readonly Dictionary<int, PropertySetNode> _propertySetsById =
        new Dictionary<int, PropertySetNode>();
      private readonly Dictionary<int,
        Dictionary<string, List<RelationshipNode>>>
        _relationshipsByOwnerAndPropertySetName =
          new Dictionary<int, Dictionary<string, List<RelationshipNode>>>();
      private readonly Dictionary<int, List<RelationshipNode>>
        _relationshipsByPropertySet =
          new Dictionary<int, List<RelationshipNode>>();
      private readonly Dictionary<string, HashSet<PropertySetNode>>
        _unownedPropertySetsByName =
          new Dictionary<string, HashSet<PropertySetNode>>(
            StringComparer.Ordinal);
      private readonly Dictionary<int, HashSet<int>> _propertySetIdsByProperty =
        new Dictionary<int, HashSet<int>>();
      private readonly List<IfcStepEntity> _typeObjects =
        new List<IfcStepEntity>();
      private readonly HashSet<int> _typeOwnedPropertySetIds =
        new HashSet<int>();
      private readonly HashSet<PropertySetNode> _dirtyPropertySets =
        new HashSet<PropertySetNode>();
      private bool _graphValidated;

      public HbrIfcGraphIndex(
        IfcStepDocument document,
        IHbrIfcOperationObserver observer)
      {
        _document = document;
        _observer = observer;
        int visited = 0;
        foreach (IfcStepEntity entity in document.EnumerateEntities(observer))
        {
          visited++;
          AddToTypeIndex(entity);
          AddToGlobalIdIndex(entity);
        }
        Observe(
          observer,
          HbrIfcOperationKind.GraphIndexFullPass,
          visited);
      }

      public IReadOnlyList<IfcStepEntity> GetEntitiesByType(string type)
      {
        if (type != null
          && _entitiesByType.TryGetValue(
            type,
            out List<IfcStepEntity> entities))
          return entities;
        return Array.Empty<IfcStepEntity>();
      }

      public IReadOnlyList<IfcStepEntity> FindOwnersByGlobalId(
        string type,
        string globalId,
        int valueIndex)
      {
        if (!_ownersByTypeAndGlobalId.TryGetValue(
          type,
          out Dictionary<string, List<IfcStepEntity>> ownersByGlobalId))
        {
          ownersByGlobalId = new Dictionary<string, List<IfcStepEntity>>(
            StringComparer.Ordinal);
          foreach (IfcStepEntity candidate in GetEntitiesByType(type))
          {
            string candidateGlobalId = ReadOwnerGlobalId(
              candidate,
              valueIndex);
            AddToList(ownersByGlobalId, candidateGlobalId, candidate);
          }
          _ownersByTypeAndGlobalId.Add(type, ownersByGlobalId);
        }
        if (ownersByGlobalId.TryGetValue(
          globalId,
          out List<IfcStepEntity> matches))
          return matches;
        return Array.Empty<IfcStepEntity>();
      }

      public void ValidateGraphOnce(int valueIndex)
      {
        if (_graphValidated) return;
        Observe(_observer, HbrIfcOperationKind.GraphValidation);

        foreach (IfcStepEntity typeObject in _typeObjects)
        {
          if (!HbrIfcTypeObjectSemantics.TryResolveHasPropertySets(
            _document,
            typeObject,
            out IReadOnlyList<int> definitionIds,
            out string typeObjectError))
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcPropertySetConflict,
              typeObjectError);
          foreach (int definitionId in definitionIds)
          {
            IfcStepEntity definition = _document.GetEntity(definitionId);
            if (string.Equals(
              definition.Type,
              "IFCPROPERTYSET",
              StringComparison.OrdinalIgnoreCase))
              _typeOwnedPropertySetIds.Add(definitionId);
          }
        }

        foreach (IfcStepEntity property in GetEntitiesByType(
          "IFCPROPERTYSINGLEVALUE"))
        {
          if (property.Arguments.Count != 4)
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcPropertyConflict,
              "IfcPropertySingleValue 必须恰好包含 4 个参数：#"
              + property.Id);
        }

        foreach (IfcStepEntity relationship in GetEntitiesByType(
          "IFCRELDEFINESBYPROPERTIES"))
        {
          if (relationship.Arguments.Count != 6)
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcRelationshipConflict,
              "IfcRelDefinesByProperties 必须恰好包含 6 个参数：#"
              + relationship.Id);
          ValidateGlobalIdArgument(
            relationship,
            valueIndex,
            HbrIfcErrorCodes.IfcRelationshipConflict);
          IReadOnlyList<int> parsedOwnerIds;
          HashSet<int> ownerIds;
          try
          {
            parsedOwnerIds = IfcStepSyntax.ParseReferenceList(
              relationship.Arguments[4]);
            ownerIds = new HashSet<int>(parsedOwnerIds);
          }
          catch (Exception exception)
          {
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcRelationshipConflict,
              "IfcRelDefinesByProperties 引用语法无效：#"
              + relationship.Id,
              exception);
          }
          if (!HbrIfcRelationshipSemantics.TryValidateRelatedObjects(
            _document,
            parsedOwnerIds,
            out string ownerError))
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcRelationshipConflict,
              ownerError + " 关系：#" + relationship.Id);
          if (!HbrIfcRelationshipSemantics.TryResolvePropertySetDefinitions(
            _document,
            relationship.Arguments[5],
            out IReadOnlyList<int> definitionIds,
            out string definitionError))
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcRelationshipConflict,
              definitionError + " 关系：#" + relationship.Id);
          foreach (int definitionId in definitionIds)
          {
            IfcStepEntity definition = _document.GetEntity(definitionId);
            if (!string.Equals(
              definition.Type,
              "IFCPROPERTYSET",
              StringComparison.OrdinalIgnoreCase))
              continue;
            RegisterRelationshipNode(new RelationshipNode(
              relationship,
              ownerIds,
              definitionId));
          }
        }

        foreach (IfcStepEntity propertySet in GetEntitiesByType(
          "IFCPROPERTYSET"))
        {
          if (propertySet.Arguments.Count != 5)
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcPropertySetConflict,
              "IfcPropertySet 必须恰好包含 5 个参数：#"
              + propertySet.Id);
          ValidateGlobalIdArgument(
            propertySet,
            valueIndex,
            HbrIfcErrorCodes.IfcPropertySetConflict);
          string propertySetName;
          try
          {
            propertySetName = IfcStepSyntax.DecodeString(
              propertySet.Arguments[2]);
          }
          catch (Exception exception)
          {
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcPropertySetConflict,
              "IfcPropertySet 名称不是有效 STEP 字符串：#"
              + propertySet.Id,
              exception);
          }

          if (!HbrIfcPropertySetSemantics.TryResolveHasProperties(
            _document,
            propertySet,
            out IReadOnlyList<int> propertyIds,
            out string propertySetErrorCode,
            out string propertySetError))
            throw new EnrichmentFailure(
              valueIndex,
              propertySetErrorCode,
              propertySetError);
          foreach (int propertyId in propertyIds)
          {
            IfcStepEntity property = _document.GetEntity(propertyId);
            if (!string.Equals(
              property.Type,
              "IFCPROPERTYSINGLEVALUE",
              StringComparison.OrdinalIgnoreCase))
              continue;
            try
            {
              IfcStepSyntax.DecodeString(property.Arguments[0]);
            }
            catch (Exception exception)
            {
              throw new EnrichmentFailure(
                valueIndex,
                HbrIfcErrorCodes.IfcPropertyConflict,
                "IfcPropertySingleValue 名称不是有效 STEP 字符串：#"
                + property.Id,
                exception);
            }
          }
          RegisterPropertySetNode(new PropertySetNode(
            propertySet,
            propertySetName,
            propertyIds));
        }
        _graphValidated = true;
      }

      public ExistingTarget FindExistingTarget(
        IfcStepEntity owner,
        string propertySetName,
        int valueIndex)
      {
        if (_relationshipsByOwnerAndPropertySetName.TryGetValue(
          owner.Id,
          out Dictionary<string, List<RelationshipNode>> relationshipsByName)
          && relationshipsByName.TryGetValue(
            propertySetName,
            out List<RelationshipNode> relationships))
        {
          if (relationships.Any(relationship =>
            _typeOwnedPropertySetIds.Contains(relationship.PropertySetId)))
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcPropertySetConflict,
              "目标 IfcPropertySet 同时由 IfcTypeObject 与 occurrence relationship 引用。");
          int exactPropertySetCount = relationships
            .Select(relationship => relationship.PropertySetId)
            .Distinct()
            .Count();
          if (exactPropertySetCount > 1)
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcPropertySetConflict,
              "同一 owner 匹配到多个同名 IfcPropertySet。");
          if (relationships.Count > 1)
            throw new EnrichmentFailure(
              valueIndex,
              HbrIfcErrorCodes.IfcRelationshipConflict,
              "同一 owner 与目标 Pset 存在多个关系。");
          if (relationships.Count == 1)
          {
            RelationshipNode relationship = relationships[0];
            return new ExistingTarget(
              _propertySetsById[relationship.PropertySetId].Entity,
              relationship.Entity,
              true);
          }
        }

        if (!_unownedPropertySetsByName.TryGetValue(
          propertySetName,
          out HashSet<PropertySetNode> unowned)
          || unowned.Count == 0)
          return null;
        if (unowned.Count > 1)
          throw new EnrichmentFailure(
            valueIndex,
            HbrIfcErrorCodes.IfcPropertySetConflict,
            "文档包含多个完全未归属的同名 IfcPropertySet。");
        return new ExistingTarget(unowned.Single().Entity, null, false);
      }

      public IfcStepEntity FindExistingProperty(
        IfcStepEntity propertySet,
        string propertyName,
        int valueIndex)
      {
        PropertySetNode node = _propertySetsById[propertySet.Id];
        if (!node.PropertiesByName.TryGetValue(
          propertyName,
          out List<IfcStepEntity> matches))
          return null;
        if (matches.Count > 1)
          throw new EnrichmentFailure(
            valueIndex,
            HbrIfcErrorCodes.IfcPropertyConflict,
            "目标 IfcPropertySingleValue 重复：" + propertyName);
        IfcStepEntity match = matches[0];
        if (!string.Equals(
          match.Type,
          "IFCPROPERTYSINGLEVALUE",
          StringComparison.OrdinalIgnoreCase))
          throw new EnrichmentFailure(
            valueIndex,
            HbrIfcErrorCodes.IfcPropertyConflict,
            "同名 IFC property 不是 IfcPropertySingleValue：#" + match.Id);
        return match;
      }

      public bool PropertySetServesOtherOwner(
        int propertySetId,
        int ownerId)
      {
        if (!_relationshipsByPropertySet.TryGetValue(
          propertySetId,
          out List<RelationshipNode> relationships))
          return false;
        return relationships.SelectMany(item => item.OwnerIds)
          .Any(candidateOwnerId => candidateOwnerId != ownerId);
      }

      public bool PropertyIsSharedByAnotherSet(
        int propertyId,
        int targetPropertySetId)
      {
        return _propertySetIdsByProperty.TryGetValue(
            propertyId,
            out HashSet<int> propertySetIds)
          && propertySetIds.Any(id => id != targetPropertySetId);
      }

      public void EnsureGlobalIdAvailable(
        string globalId,
        int valueIndex,
        string errorCode)
      {
        if (!_globalIdOccupants.TryGetValue(
          globalId,
          out List<IfcStepEntity> occupants)
          || occupants.Count == 0)
          return;
        throw new EnrichmentFailure(
          valueIndex,
          errorCode,
          "确定性 IFC GlobalId 已被不同语义实体占用：#"
          + occupants[0].Id);
      }

      public void RegisterEntity(IfcStepEntity entity)
      {
        AddToTypeIndex(entity);
        AddToGlobalIdIndex(entity);
      }

      public void RegisterPropertySet(IfcStepEntity propertySet)
      {
        RegisterEntity(propertySet);
        string name = IfcStepSyntax.DecodeString(propertySet.Arguments[2]);
        IReadOnlyList<int> propertyIds = IfcStepSyntax.ParseReferenceList(
          propertySet.Arguments[4]);
        RegisterPropertySetNode(new PropertySetNode(
          propertySet,
          name,
          propertyIds));
      }

      public void RegisterRelationship(IfcStepEntity relationship)
      {
        RegisterEntity(relationship);
        RegisterRelationshipNode(new RelationshipNode(
          relationship,
          IfcStepSyntax.ParseReferenceList(relationship.Arguments[4]),
          IfcStepSyntax.ParseReference(relationship.Arguments[5])));
      }

      public void AttachProperty(
        IfcStepEntity propertySet,
        IfcStepEntity property)
      {
        PropertySetNode node = _propertySetsById[propertySet.Id];
        if (!node.PropertyIdSet.Add(property.Id)) return;
        node.PropertyIds.Add(property.Id);
        AddPropertyName(node, property);
        AddToSet(_propertySetIdsByProperty, property.Id, propertySet.Id);
        _dirtyPropertySets.Add(node);
      }

      public void FlushDirtyPropertySets()
      {
        foreach (PropertySetNode propertySet in _dirtyPropertySets
          .OrderBy(item => item.Entity.Id))
        {
          propertySet.Entity.SetArgument(
            4,
            IfcStepSyntax.FormatReferenceList(propertySet.PropertyIds));
        }
        _dirtyPropertySets.Clear();
      }

      private void RegisterPropertySetNode(PropertySetNode node)
      {
        _propertySetsById.Add(node.Entity.Id, node);
        if (_relationshipsByPropertySet.TryGetValue(
          node.Entity.Id,
          out List<RelationshipNode> relationships)
          && relationships.Count != 0)
        {
          foreach (RelationshipNode relationship in relationships)
            RegisterOwnerPropertySetRelationship(node, relationship);
        }
        else if (!_typeOwnedPropertySetIds.Contains(node.Entity.Id))
        {
          AddToSet(_unownedPropertySetsByName, node.Name, node);
        }
        foreach (int propertyId in node.PropertyIds)
        {
          AddToSet(_propertySetIdsByProperty, propertyId, node.Entity.Id);
          AddPropertyName(node, _document.GetEntity(propertyId));
        }
      }

      private void RegisterRelationshipNode(RelationshipNode node)
      {
        AddToList(
          _relationshipsByPropertySet,
          node.PropertySetId,
          node);
        if (_propertySetsById.TryGetValue(
          node.PropertySetId,
          out PropertySetNode propertySet))
        {
          RemoveUnownedPropertySet(propertySet);
          RegisterOwnerPropertySetRelationship(propertySet, node);
        }
      }

      private void RegisterOwnerPropertySetRelationship(
        PropertySetNode propertySet,
        RelationshipNode relationship)
      {
        foreach (int ownerId in relationship.OwnerIds)
        {
          if (!_relationshipsByOwnerAndPropertySetName.TryGetValue(
            ownerId,
            out Dictionary<string, List<RelationshipNode>> relationshipsByName))
          {
            relationshipsByName =
              new Dictionary<string, List<RelationshipNode>>(
                StringComparer.Ordinal);
            _relationshipsByOwnerAndPropertySetName.Add(
              ownerId,
              relationshipsByName);
          }
          AddToList(relationshipsByName, propertySet.Name, relationship);
        }
      }

      private void RemoveUnownedPropertySet(PropertySetNode propertySet)
      {
        if (!_unownedPropertySetsByName.TryGetValue(
          propertySet.Name,
          out HashSet<PropertySetNode> unowned))
          return;
        unowned.Remove(propertySet);
        if (unowned.Count == 0)
          _unownedPropertySetsByName.Remove(propertySet.Name);
      }

      private static void AddPropertyName(
        PropertySetNode propertySet,
        IfcStepEntity property)
      {
        if (property.Arguments.Count == 0) return;
        string name;
        try
        {
          name = IfcStepSyntax.DecodeString(property.Arguments[0]);
        }
        catch
        {
          return;
        }
        AddToList(propertySet.PropertiesByName, name, property);
      }

      private void AddToTypeIndex(IfcStepEntity entity)
      {
        AddToList(_entitiesByType, entity.Type, entity);
        if (HbrIfcTypeObjectTypes.Contains(entity.Type))
          _typeObjects.Add(entity);
      }

      private void AddToGlobalIdIndex(IfcStepEntity entity)
      {
        if (!HbrIfcRootCarrierTypes.Contains(entity.Type)
          || entity.Arguments.Count == 0)
          return;
        string globalId;
        try
        {
          globalId = IfcStepSyntax.DecodeString(entity.Arguments[0]);
        }
        catch
        {
          return;
        }
        if (IfcGuidCodec.IsValid(globalId))
          AddToList(_globalIdOccupants, globalId, entity);
      }

      private static void AddToList<TKey, TValue>(
        IDictionary<TKey, List<TValue>> values,
        TKey key,
        TValue value)
      {
        if (!values.TryGetValue(key, out List<TValue> list))
        {
          list = new List<TValue>();
          values.Add(key, list);
        }
        list.Add(value);
      }

      private static void AddToSet<TKey, TValue>(
        IDictionary<TKey, HashSet<TValue>> values,
        TKey key,
        TValue value)
      {
        if (!values.TryGetValue(key, out HashSet<TValue> set))
        {
          set = new HashSet<TValue>();
          values.Add(key, set);
        }
        set.Add(value);
      }

      private sealed class PropertySetNode
      {
        public PropertySetNode(
          IfcStepEntity entity,
          string name,
          IEnumerable<int> propertyIds)
        {
          Entity = entity;
          Name = name;
          PropertyIds = propertyIds.ToList();
          PropertyIdSet = new HashSet<int>(PropertyIds);
        }

        public IfcStepEntity Entity { get; }
        public string Name { get; }
        public List<int> PropertyIds { get; }
        public HashSet<int> PropertyIdSet { get; }
        public Dictionary<string, List<IfcStepEntity>> PropertiesByName { get; }
          = new Dictionary<string, List<IfcStepEntity>>(StringComparer.Ordinal);
      }

      private sealed class RelationshipNode
      {
        public RelationshipNode(
          IfcStepEntity entity,
          IEnumerable<int> ownerIds,
          int propertySetId)
        {
          Entity = entity;
          OwnerIds = new HashSet<int>(ownerIds);
          PropertySetId = propertySetId;
        }

        public IfcStepEntity Entity { get; }
        public HashSet<int> OwnerIds { get; }
        public int PropertySetId { get; }
      }
    }

    private sealed class PendingField
    {
      public PendingField(
        int valueIndex,
        HbrIfcEnrichmentValue value,
        string typedValue,
        IfcStepEntity owner,
        IfcStepEntity property,
        IfcStepEntity propertySet,
        IfcStepEntity relationship)
      {
        ValueIndex = valueIndex;
        Value = value;
        TypedValue = typedValue;
        Owner = owner;
        Property = property;
        PropertySet = propertySet;
        Relationship = relationship;
      }

      public int ValueIndex { get; }
      public HbrIfcEnrichmentValue Value { get; }
      public string TypedValue { get; }
      public IfcStepEntity Owner { get; }
      public IfcStepEntity Property { get; }
      public IfcStepEntity PropertySet { get; }
      public IfcStepEntity Relationship { get; }
    }

    private sealed class PreparedInspection
    {
      public PreparedInspection(
        int valueIndex,
        HbrIfcEnrichmentValue value,
        string typedValue,
        IfcStepEntity owner)
      {
        ValueIndex = valueIndex;
        Value = value;
        TypedValue = typedValue;
        Owner = owner;
      }

      public int ValueIndex { get; }
      public HbrIfcEnrichmentValue Value { get; }
      public string TypedValue { get; }
      public IfcStepEntity Owner { get; }
    }

    private sealed class BatchInspectionContext
    {
      private readonly IfcStepDocument _document;
      private readonly IHbrIfcOperationObserver _observer;
      private readonly Dictionary<int, ParsedOwner> _owners =
        new Dictionary<int, ParsedOwner>();
      private readonly Dictionary<int, ParsedRelationship> _relationships =
        new Dictionary<int, ParsedRelationship>();
      private readonly Dictionary<int, ParsedPropertySet> _propertySets =
        new Dictionary<int, ParsedPropertySet>();
      private readonly Dictionary<int, ParsedProperty> _properties =
        new Dictionary<int, ParsedProperty>();

      public BatchInspectionContext(
        IfcStepDocument document,
        IReadOnlyList<PendingField> fields,
        IHbrIfcOperationObserver observer)
      {
        _document = document;
        _observer = observer;
        Observe(
          observer,
          HbrIfcOperationKind.BatchInspection,
          fields.Count);
      }

      public HbrIfcFieldInspectionResult Inspect(PendingField field)
      {
        Observe(_observer, HbrIfcOperationKind.IndexedFieldLookup);
        string propertyIdentity = field.Value.PropertyIdentity;

        ParsedOwner owner = GetOwner(field.Owner);
        if (owner.Error != null)
          return InspectionFailure(
            propertyIdentity,
            HbrIfcErrorCodes.InvalidValue,
            owner.Error,
            field.Owner.Id);
        if (!string.Equals(
          field.Owner.Type,
          field.Value.OwnerEntityType,
          StringComparison.OrdinalIgnoreCase)
          || !string.IsNullOrWhiteSpace(field.Value.OwnerGlobalId)
          && !string.Equals(
            owner.GlobalId,
            field.Value.OwnerGlobalId,
            StringComparison.Ordinal))
          return InspectionFailure(
            propertyIdentity,
            HbrIfcErrorCodes.IfcOwnerNotFound,
            "exact owner 与写入输入不匹配。",
            field.Owner.Id);

        ParsedRelationship relationship = GetRelationship(
          field.Relationship);
        if (relationship.Error != null)
          return InspectionFailure(
            propertyIdentity,
            HbrIfcErrorCodes.IfcRelationshipConflict,
            relationship.Error,
            field.Owner.Id,
            relationshipId: field.Relationship.Id);
        if (!relationship.OwnerIds.Contains(field.Owner.Id)
          || !relationship.PropertySetIds.Contains(field.PropertySet.Id))
          return InspectionFailure(
            propertyIdentity,
            HbrIfcErrorCodes.IfcRelationshipConflict,
            "exact relationship 路径与写入计划不一致。",
            field.Owner.Id,
            field.PropertySet.Id,
            relationshipId: field.Relationship.Id);

        ParsedPropertySet propertySet = GetPropertySet(field.PropertySet);
        if (propertySet.Error != null)
          return InspectionFailure(
            propertyIdentity,
            propertySet.ErrorCode,
            propertySet.Error,
            field.Owner.Id,
            field.PropertySet.Id,
            relationshipId: field.Relationship.Id);
        if (!string.Equals(
          propertySet.Name,
          field.Value.PropertySetName,
          StringComparison.Ordinal)
          || !propertySet.PropertyIds.Contains(field.Property.Id))
          return InspectionFailure(
            propertyIdentity,
            HbrIfcErrorCodes.IfcPropertySetConflict,
            "exact Pset 名称或 property 引用与写入计划不一致。",
            field.Owner.Id,
            field.PropertySet.Id,
            relationshipId: field.Relationship.Id);

        ParsedProperty property = GetProperty(field.Property);
        if (property.Error != null)
          return InspectionFailure(
            propertyIdentity,
            HbrIfcErrorCodes.IfcPropertyConflict,
            property.Error,
            field.Owner.Id,
            field.PropertySet.Id,
            field.Property.Id,
            field.Relationship.Id);
        if (!string.Equals(
          property.Name,
          field.Value.PropertyName,
          StringComparison.Ordinal))
          return InspectionFailure(
            propertyIdentity,
            HbrIfcErrorCodes.IfcPropertyConflict,
            "exact property 名称与写入输入不一致。",
            field.Owner.Id,
            field.PropertySet.Id,
            field.Property.Id,
            field.Relationship.Id);
        if (!IfcStepSyntax.TryParseTypedValue(
          property.TypedToken,
          out string actualType,
          out _))
          return InspectionFailure(
            propertyIdentity,
            HbrIfcErrorCodes.IfcValueMismatch,
            "目标 property nominal value 不是 typed token。",
            field.Owner.Id,
            field.PropertySet.Id,
            field.Property.Id,
            field.Relationship.Id,
            typedToken: property.TypedToken);
        IfcStepSyntax.TryParseTypedValue(
          field.TypedValue,
          out string expectedType,
          out _);
        if (!string.Equals(actualType, expectedType, StringComparison.Ordinal))
          return InspectionFailure(
            propertyIdentity,
            HbrIfcErrorCodes.IfcTypeMismatch,
            "目标 property declared IFC type 不匹配。",
            field.Owner.Id,
            field.PropertySet.Id,
            field.Property.Id,
            field.Relationship.Id,
            actualType,
            property.TypedToken);
        if (!string.Equals(
          property.TypedToken,
          field.TypedValue,
          StringComparison.Ordinal))
          return InspectionFailure(
            propertyIdentity,
            HbrIfcErrorCodes.IfcValueMismatch,
            "目标 property typed value 不匹配。",
            field.Owner.Id,
            field.PropertySet.Id,
            field.Property.Id,
            field.Relationship.Id,
            actualType,
            property.TypedToken);

        return new HbrIfcFieldInspectionResult(
          propertyIdentity,
          true,
          string.Empty,
          "IFC 字段 exact 批量回读通过。",
          ownerId: field.Owner.Id,
          propertyId: field.Property.Id,
          propertySetId: field.PropertySet.Id,
          relationshipId: field.Relationship.Id,
          actualIfcType: actualType,
          typedToken: property.TypedToken);
      }

      private ParsedOwner GetOwner(IfcStepEntity entity)
      {
        if (_owners.TryGetValue(entity.Id, out ParsedOwner parsed))
          return parsed;
        if (entity.Arguments.Count == 0)
          parsed = new ParsedOwner(null, "owner 缺少 GlobalId 参数。");
        else
        {
          try
          {
            string globalId = IfcStepSyntax.DecodeString(entity.Arguments[0]);
            parsed = IfcGuidCodec.IsValid(globalId)
              ? new ParsedOwner(globalId, null)
              : new ParsedOwner(null, "owner GlobalId 无效。");
          }
          catch (Exception exception)
          {
            parsed = new ParsedOwner(null, exception.Message);
          }
        }
        _owners.Add(entity.Id, parsed);
        return parsed;
      }

      private ParsedRelationship GetRelationship(IfcStepEntity entity)
      {
        if (_relationships.TryGetValue(
          entity.Id,
          out ParsedRelationship parsed))
          return parsed;
        if (!string.Equals(
            entity.Type,
            "IFCRELDEFINESBYPROPERTIES",
            StringComparison.OrdinalIgnoreCase)
          || entity.Arguments.Count != 6)
          parsed = new ParsedRelationship(
            null,
            null,
            "relationship 类型或参数数量无效。");
        else
        {
          try
          {
            IReadOnlyList<int> ownerIds = IfcStepSyntax.ParseReferenceList(
              entity.Arguments[4]);
            if (!HbrIfcRelationshipSemantics.TryValidateRelatedObjects(
              _document,
              ownerIds,
              out string ownerError))
              parsed = new ParsedRelationship(null, null, ownerError);
            else if (!HbrIfcRelationshipSemantics
              .TryResolvePropertySetDefinitions(
                _document,
                entity.Arguments[5],
                out IReadOnlyList<int> definitionIds,
                out string definitionError))
              parsed = new ParsedRelationship(null, null, definitionError);
            else
              parsed = new ParsedRelationship(
                new HashSet<int>(ownerIds),
                new HashSet<int>(definitionIds.Where(id => string.Equals(
                  _document.GetEntity(id).Type,
                  "IFCPROPERTYSET",
                  StringComparison.OrdinalIgnoreCase))),
                null);
          }
          catch (Exception exception)
          {
            parsed = new ParsedRelationship(null, null, exception.Message);
          }
        }
        _relationships.Add(entity.Id, parsed);
        return parsed;
      }

      private ParsedPropertySet GetPropertySet(IfcStepEntity entity)
      {
        if (_propertySets.TryGetValue(
          entity.Id,
          out ParsedPropertySet parsed))
          return parsed;
        if (!string.Equals(
            entity.Type,
            "IFCPROPERTYSET",
            StringComparison.OrdinalIgnoreCase)
          || entity.Arguments.Count != 5)
          parsed = new ParsedPropertySet(
            null,
            null,
            HbrIfcErrorCodes.IfcPropertySetConflict,
            "Pset 类型或参数数量无效。");
        else
        {
          if (HbrIfcPropertySetSemantics.TryResolveHasProperties(
            _document,
            entity,
            out IReadOnlyList<int> propertyIds,
            out string propertySetErrorCode,
            out string propertySetError))
          {
            try
            {
              parsed = new ParsedPropertySet(
                IfcStepSyntax.DecodeString(entity.Arguments[2]),
                new HashSet<int>(propertyIds),
                null,
                null);
            }
            catch (Exception exception)
            {
              parsed = new ParsedPropertySet(
                null,
                null,
                HbrIfcErrorCodes.IfcPropertySetConflict,
                exception.Message);
            }
          }
          else
            parsed = new ParsedPropertySet(
              null,
              null,
              propertySetErrorCode,
              propertySetError);
        }
        _propertySets.Add(entity.Id, parsed);
        return parsed;
      }

      private ParsedProperty GetProperty(IfcStepEntity entity)
      {
        if (_properties.TryGetValue(entity.Id, out ParsedProperty parsed))
          return parsed;
        if (!string.Equals(
            entity.Type,
            "IFCPROPERTYSINGLEVALUE",
            StringComparison.OrdinalIgnoreCase)
          || entity.Arguments.Count != 4)
          parsed = new ParsedProperty(
            null,
            null,
            "property 类型或参数数量无效。");
        else
        {
          try
          {
            parsed = new ParsedProperty(
              IfcStepSyntax.DecodeString(entity.Arguments[0]),
              entity.Arguments[2],
              null);
          }
          catch (Exception exception)
          {
            parsed = new ParsedProperty(null, null, exception.Message);
          }
        }
        _properties.Add(entity.Id, parsed);
        return parsed;
      }

      private static HbrIfcFieldInspectionResult InspectionFailure(
        string propertyIdentity,
        string errorCode,
        string message,
        int? ownerId = null,
        int? propertySetId = null,
        int? propertyId = null,
        int? relationshipId = null,
        string actualIfcType = null,
        string typedToken = null)
      {
        return new HbrIfcFieldInspectionResult(
          propertyIdentity,
          false,
          errorCode,
          message,
          ownerId,
          propertyId,
          propertySetId,
          relationshipId,
          actualIfcType,
          typedToken);
      }

      private sealed class ParsedOwner
      {
        public ParsedOwner(string globalId, string error)
        {
          GlobalId = globalId;
          Error = error;
        }

        public string GlobalId { get; }
        public string Error { get; }
      }

      private sealed class ParsedRelationship
      {
        public ParsedRelationship(
          HashSet<int> ownerIds,
          HashSet<int> propertySetIds,
          string error)
        {
          OwnerIds = ownerIds;
          PropertySetIds = propertySetIds;
          Error = error;
        }

        public HashSet<int> OwnerIds { get; }
        public HashSet<int> PropertySetIds { get; }
        public string Error { get; }
      }

      private sealed class ParsedPropertySet
      {
        public ParsedPropertySet(
          string name,
          HashSet<int> propertyIds,
          string errorCode,
          string error)
        {
          Name = name;
          PropertyIds = propertyIds;
          ErrorCode = errorCode;
          Error = error;
        }

        public string Name { get; }
        public HashSet<int> PropertyIds { get; }
        public string ErrorCode { get; }
        public string Error { get; }
      }

      private sealed class ParsedProperty
      {
        public ParsedProperty(
          string name,
          string typedToken,
          string error)
        {
          Name = name;
          TypedToken = typedToken;
          Error = error;
        }

        public string Name { get; }
        public string TypedToken { get; }
        public string Error { get; }
      }
    }

    private sealed class ExistingTarget
    {
      public ExistingTarget(
        IfcStepEntity propertySet,
        IfcStepEntity relationship,
        bool ownerRelated)
      {
        PropertySet = propertySet;
        Relationship = relationship;
        OwnerRelated = ownerRelated;
      }

      public IfcStepEntity PropertySet { get; }
      public IfcStepEntity Relationship { get; }
      public bool OwnerRelated { get; }
    }

    private sealed class PlannedValue
    {
      public PlannedValue(
        HbrIfcEnrichmentValue value,
        int originalIndex)
      {
        Value = value;
        OriginalIndex = originalIndex;
      }

      public HbrIfcEnrichmentValue Value { get; }
      public int OriginalIndex { get; }
    }

    private sealed class ResolvedOwner
    {
      public ResolvedOwner(IfcStepEntity entity, string globalId)
      {
        Entity = entity;
        GlobalId = globalId;
      }

      public IfcStepEntity Entity { get; }
      public string GlobalId { get; }
    }

    private sealed class EnrichmentFailure : Exception
    {
      public EnrichmentFailure(
        int valueIndex,
        string errorCode,
        string message,
        Exception innerException = null)
        : base(message, innerException)
      {
        ValueIndex = valueIndex;
        ErrorCode = errorCode;
      }

      public int ValueIndex { get; }
      public string ErrorCode { get; }
    }
  }
}
