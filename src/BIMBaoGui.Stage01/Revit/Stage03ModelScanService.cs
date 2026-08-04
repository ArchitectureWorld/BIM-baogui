using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Mvd;
using BIMBaoGui.Stage01.Revit.Parameters;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Stage02;
using BIMBaoGui.Stage01.Stage03;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class Stage03IfcOwnerSnapshot
  {
    internal string Entity { get; set; } = string.Empty;
    internal string Role { get; set; } = string.Empty;
    internal int ElementId { get; set; }
    internal string UniqueId { get; set; } = string.Empty;
    internal string RuleOwnerStrategy { get; set; } = string.Empty;
    internal string GlobalId { get; set; } = string.Empty;
  }

  internal sealed class Stage03ModelScanResult
  {
    internal bool Success => TechnicalFatalCodes.Count == 0;
    internal string FileGuid { get; set; } = string.Empty;
    internal string DocumentFingerprint { get; set; } = string.Empty;
    internal string DocumentTitle { get; set; } = string.Empty;
    internal string RevitVersion { get; set; } = string.Empty;
    internal string ActiveProfileId { get; set; } = string.Empty;
    internal string RulePackageId { get; set; } = string.Empty;
    internal string RulePackageVersion { get; set; } = string.Empty;
    internal string RulePackageSha256 { get; set; } = string.Empty;
    internal IReadOnlyList<Stage03CarrierResult> Carriers { get; set; } =
      Array.Empty<Stage03CarrierResult>();
    internal IReadOnlyList<Stage03FieldResult> Fields { get; set; } =
      Array.Empty<Stage03FieldResult>();
    internal IReadOnlyList<Stage03IfcOwnerSnapshot> IfcOwners { get; set; } =
      Array.Empty<Stage03IfcOwnerSnapshot>();
    internal IReadOnlyList<HbrIfcEnrichmentValue> EnrichmentValues
    {
      get;
      set;
    } = Array.Empty<HbrIfcEnrichmentValue>();
    internal IReadOnlyList<string> TechnicalFatalCodes { get; set; } =
      Array.Empty<string>();
    internal IReadOnlyList<Stage03Diagnostic> Diagnostics { get; set; } =
      Array.Empty<Stage03Diagnostic>();
  }

  internal sealed class Stage03ModelScanService
  {
    private readonly HbrRuleDatabase _database;

    internal Stage03ModelScanService(HbrRuleDatabase database)
    {
      _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    internal Stage03ModelScanResult Scan(
      UIApplication uiApplication,
      Document document,
      HBRFileContext context)
    {
      if (uiApplication == null)
        throw new ArgumentNullException(nameof(uiApplication));
      if (document == null) throw new ArgumentNullException(nameof(document));

      HbrRulePackage package = _database.Package;
      var result = new Stage03ModelScanResult
      {
        FileGuid = context == null ? string.Empty : context.FileGuid,
        DocumentTitle = document.Title ?? string.Empty,
        RevitVersion = uiApplication.Application.VersionNumber ?? string.Empty,
        ActiveProfileId = context == null
          ? string.Empty
          : context.ModelFileType,
        RulePackageId = package.PackageId,
        RulePackageVersion = package.PackageVersion,
        RulePackageSha256 = package.RulePackageSha256
      };
      var technicalCodes = new SortedSet<string>(StringComparer.Ordinal);
      var diagnostics = new List<Stage03Diagnostic>();

      RevitDocumentIdentity identity = RevitDocumentIdentityService.Read(
        uiApplication,
        document);
      result.DocumentFingerprint = identity.DocumentFingerprint;
      ValidateContext(
        context,
        identity,
        package,
        technicalCodes,
        diagnostics);

      HbrModelProfile profile = null;
      if (context != null
        && !_database.ProfilesByModelFileType.TryGetValue(
          context.ModelFileType,
          out profile))
      {
        AddTechnical(
          technicalCodes,
          diagnostics,
          Stage03TechnicalFatalCodes.WrongDocument,
          "活动 HBR model profile 不存在于当前规则包。");
      }
      if (profile != null)
      {
        Stage03ActivationStateDecision activationDecision =
          Stage03ActivationStatePolicy.Evaluate(
            _database,
            context.ModelFileType,
            context.ProjectConditions,
            context.ActivatedRuleIds,
            context.NotApplicableRuleIds);
        if (!activationDecision.Success)
        {
          AddTechnical(
            technicalCodes,
            diagnostics,
            Stage03TechnicalFatalCodes.WrongDocument,
            activationDecision.Message);
        }
      }

      if (technicalCodes.Count > 0 || profile == null)
      {
        result.TechnicalFatalCodes = Freeze(technicalCodes);
        result.Diagnostics = FreezeDiagnostics(diagnostics);
        return result;
      }

      HbrCarrierRole[] roles = package.CarrierRoles
        .Where(role => role.ModelFileTypes.Contains(
          profile.ProfileId,
          StringComparer.Ordinal))
        .OrderBy(role => role.RoleId, StringComparer.Ordinal)
        .ToArray();
      HbrRuleProperty[] properties = package.Properties
        .Where(property => property.StageOwnership.Contains(
          "STAGE03",
          StringComparer.Ordinal))
        .OrderBy(property => property.PropertyId, StringComparer.Ordinal)
        .ToArray();

      Dictionary<string, List<Element>> elementsByCategory =
        BuildElementIndex(document, roles, technicalCodes, diagnostics);
      Dictionary<string, Stage03CarrierCandidateSnapshot> candidateSnapshots =
        BuildCandidateSnapshots(
          uiApplication,
          document,
          package,
          elementsByCategory.Values.SelectMany(values => values));
      Dictionary<int, ParameterBindingEvidence> bindingsByDefinitionId =
        BuildBindingIndex(document);
      Dictionary<Guid, SharedParameterEvidence> parametersByGuid =
        BuildSharedParameterIndex(
          document,
          properties,
          bindingsByDefinitionId);

      var carriers = new List<Stage03CarrierResult>();
      var fields = new List<Stage03FieldResult>();
      var owners = new List<Stage03IfcOwnerSnapshot>();
      var enrichmentValues = new List<HbrIfcEnrichmentValue>();
      foreach (HbrCarrierRole role in roles)
      {
        HbrRuleProperty[] roleProperties = properties
          .Where(property => property.CarrierRoleIds.Contains(
            role.RoleId,
            StringComparer.Ordinal))
          .ToArray();
        if (roleProperties.Length == 0) continue;

        List<Element> categoryCandidates = role.RevitCategories
          .SelectMany(category => ElementsFor(
            elementsByCategory,
            category))
          .Where(element => element != null)
          .GroupBy(element => element.UniqueId, StringComparer.Ordinal)
          .Select(group => group.First())
          .OrderBy(element => element.Id.IntegerValue)
          .ThenBy(element => element.UniqueId, StringComparer.Ordinal)
          .ToList();
        var matchDecisions = new Dictionary<string,
          Stage03CarrierMatchDecision>(StringComparer.Ordinal);
        foreach (Element candidate in categoryCandidates)
        {
          Stage03CarrierCandidateSnapshot snapshot =
            candidateSnapshots[candidate.UniqueId];
          matchDecisions[candidate.UniqueId] =
            Stage03CarrierMatchPolicy.Evaluate(role, snapshot, roles);
        }
        List<Element> candidates = categoryCandidates
          .Where(element => matchDecisions[element.UniqueId].Accepted)
          .ToList();

        if (candidates.Count > 0)
        {
          foreach (Element rejected in categoryCandidates.Where(element =>
            Stage03CarrierScanAggregationPolicy.ShouldReportAlongsideAccepted(
                matchDecisions[element.UniqueId])))
          {
            Stage03CarrierMatchDecision decision =
              matchDecisions[rejected.UniqueId];
            carriers.Add(Carrier(
              role,
              rejected,
              decision.Status,
              true,
              decision.Message));
            foreach (HbrRuleProperty property in roleProperties)
            {
              fields.Add(BuildCarrierFailureField(
                property,
                role,
                context,
                decision.Status,
                rejected));
            }
          }
        }

        if (candidates.Count == 0)
        {
          Stage03FieldStatus missingStatus = ResolveCarrierFailureStatus(
            categoryCandidates,
            matchDecisions);
          foreach (Element mismatch in categoryCandidates)
          {
            Stage03CarrierMatchDecision decision =
              matchDecisions[mismatch.UniqueId];
            carriers.Add(Carrier(
              role,
              mismatch,
              decision.Status,
              true,
              decision.Message));
          }
          if (categoryCandidates.Count == 0)
          {
            carriers.Add(new Stage03CarrierResult
            {
              Entity = role.IfcEntity,
              Role = role.RoleId,
              Status = Stage03FieldStatus.MissingCarrier,
              Active = true,
              IsBusinessBlocker = true,
              Messages = Freeze(new[] { "活动载体角色未找到 Revit 元素。" })
            });
          }
          foreach (HbrRuleProperty property in roleProperties)
          {
            fields.Add(BuildCarrierFailureField(
              property,
              role,
              context,
              missingStatus));
          }
          continue;
        }

        if (role.Cardinality.Max.HasValue
          && candidates.Count > role.Cardinality.Max.Value)
        {
          foreach (Element candidate in candidates)
          {
            carriers.Add(Carrier(
              role,
              candidate,
              Stage03FieldStatus.AmbiguousCarrier,
              true,
              "载体数量超过规则 cardinality.max，系统不会猜测。"));
          }
          foreach (HbrRuleProperty property in roleProperties)
          {
            fields.Add(BuildCarrierFailureField(
              property,
              role,
              context,
              Stage03FieldStatus.AmbiguousCarrier));
          }
          continue;
        }

        foreach (Element candidate in candidates)
        {
          carriers.Add(Carrier(
            role,
            candidate,
            Stage03FieldStatus.Pass,
            true,
            string.Empty));
          OwnerDecision ownerDecision = BuildOwner(document, role, candidate);
          owners.Add(ownerDecision.Owner);
          foreach (HbrRuleProperty property in roleProperties)
          {
            FieldDecision fieldDecision = BuildField(
              document,
              context,
              role,
              candidate,
              ownerDecision,
              property,
              parametersByGuid[property.Revit.ParameterGuid]);
            fields.Add(fieldDecision.Field);
            if (fieldDecision.EnrichmentValue != null)
              enrichmentValues.Add(fieldDecision.EnrichmentValue);
          }
        }
      }

      result.Carriers = Freeze(carriers
        .OrderBy(value => value.Entity, StringComparer.Ordinal)
        .ThenBy(value => value.Role, StringComparer.Ordinal)
        .ThenBy(value => value.ElementId)
        .ThenBy(value => value.UniqueId, StringComparer.Ordinal));
      result.Fields = Freeze(fields
        .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
        .ThenBy(value => value.Entity, StringComparer.Ordinal)
        .ThenBy(value => value.Role, StringComparer.Ordinal)
        .ThenBy(value => value.ElementId)
        .ThenBy(value => value.OwnerUniqueId, StringComparer.Ordinal));
      result.IfcOwners = Freeze(owners
        .GroupBy(OwnerIdentity, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(value => value.Entity, StringComparer.Ordinal)
        .ThenBy(value => value.Role, StringComparer.Ordinal)
        .ThenBy(value => value.ElementId)
        .ThenBy(value => value.UniqueId, StringComparer.Ordinal));
      result.EnrichmentValues = Freeze(enrichmentValues
        .OrderBy(value => value.PropertyIdentity, StringComparer.Ordinal));
      result.TechnicalFatalCodes = Freeze(technicalCodes);
      result.Diagnostics = FreezeDiagnostics(diagnostics);
      return result;
    }

    private static void ValidateContext(
      HBRFileContext context,
      RevitDocumentIdentity identity,
      HbrRulePackage package,
      ISet<string> technicalCodes,
      ICollection<Stage03Diagnostic> diagnostics)
    {
      if (identity == null)
      {
        AddTechnical(
          technicalCodes,
          diagnostics,
          Stage03TechnicalFatalCodes.DocumentUnavailable,
          "无法读取当前 Revit 文档身份。");
        return;
      }
      if (!string.Equals(
        identity.RevitVersion,
        "2020",
        StringComparison.Ordinal))
      {
        AddTechnical(
          technicalCodes,
          diagnostics,
          Stage03TechnicalFatalCodes.UnsupportedRevit,
          "Stage03 标准 IFC4 导出仅支持 Revit 2020。");
      }
      if (identity.IsFamilyDocument
        || identity.IsReadOnly
        || string.IsNullOrWhiteSpace(identity.DocumentPath))
      {
        AddTechnical(
          technicalCodes,
          diagnostics,
          Stage03TechnicalFatalCodes.DocumentUnavailable,
          "活动文档必须是已保存、可修改的 Revit 项目文档。");
      }
      Stage03ContextIdentityDecision contextDecision =
        Stage03ContextIdentityPolicy.Evaluate(
          context,
          package.PackageId,
          package.PackageVersion,
          package.RulePackageSha256,
          identity.DocumentFingerprint,
          identity.DocumentTitle);
      if (!contextDecision.Success)
      {
        AddTechnical(
          technicalCodes,
          diagnostics,
          Stage03TechnicalFatalCodes.WrongDocument,
          string.Join(" ", contextDecision.Messages));
        return;
      }
      IReadOnlyList<string> identityBlockers =
        RevitDocumentIdentityService.Validate(context, identity);
      if (identityBlockers.Count > 0)
      {
        AddTechnical(
          technicalCodes,
          diagnostics,
          Stage03TechnicalFatalCodes.WrongDocument,
          string.Join(" ", identityBlockers));
      }
    }

    private static Dictionary<string, List<Element>> BuildElementIndex(
      Document document,
      IEnumerable<HbrCarrierRole> roles,
      ISet<string> technicalCodes,
      ICollection<Stage03Diagnostic> diagnostics)
    {
      var categoryIds = new SortedDictionary<int, BuiltInCategory>();
      foreach (string name in roles
        .SelectMany(role => role.RevitCategories)
        .Distinct(StringComparer.Ordinal))
      {
        BuiltInCategory category;
        if (!Enum.TryParse(name, out category)
          || !Enum.IsDefined(typeof(BuiltInCategory), category))
        {
          AddTechnical(
            technicalCodes,
            diagnostics,
            Stage03TechnicalFatalCodes.InvalidFieldStatus,
            "规则包包含未知 BuiltInCategory：" + name);
          continue;
        }
        categoryIds[(int)category] = category;
      }

      var unique = new Dictionary<string, Element>(StringComparer.Ordinal);
      if (document.ProjectInformation != null)
      {
        unique[document.ProjectInformation.UniqueId] =
          document.ProjectInformation;
      }
      if (categoryIds.Count > 0)
      {
        var filter = new ElementMulticategoryFilter(
          categoryIds.Values.ToArray());
        foreach (Element element in new FilteredElementCollector(document)
          .WhereElementIsNotElementType()
          .WherePasses(filter)
          .ToElements())
        {
          if (element == null || string.IsNullOrWhiteSpace(element.UniqueId))
            continue;
          unique[element.UniqueId] = element;
        }
      }

      var index = new Dictionary<string, List<Element>>(StringComparer.Ordinal);
      foreach (Element element in unique.Values)
      {
        string category = Stage02RevitSelectionService
          .GetBuiltInCategoryName(element);
        if (category.Length == 0) continue;
        List<Element> values;
        if (!index.TryGetValue(category, out values))
        {
          values = new List<Element>();
          index.Add(category, values);
        }
        values.Add(element);
      }
      foreach (List<Element> values in index.Values)
      {
        values.Sort((left, right) =>
        {
          int comparison = left.Id.IntegerValue.CompareTo(
            right.Id.IntegerValue);
          return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.UniqueId, right.UniqueId);
        });
      }
      return index;
    }

    private static Dictionary<string, Stage03CarrierCandidateSnapshot>
      BuildCandidateSnapshots(
        UIApplication uiApplication,
        Document document,
        HbrRulePackage package,
        IEnumerable<Element> elements)
    {
      Element[] uniqueElements = (elements ?? Array.Empty<Element>())
        .Where(element => element != null)
        .Where(element => !string.IsNullOrWhiteSpace(element.UniqueId))
        .GroupBy(element => element.UniqueId, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(element => element.Id.IntegerValue)
        .ThenBy(element => element.UniqueId, StringComparer.Ordinal)
        .ToArray();
      IReadOnlyDictionary<string, string> savedRoles =
        Stage02MetadataStorage.ReadSavedRoles(
          document,
          uniqueElements.Select(element => element.UniqueId),
          package.PackageId,
          package.PackageVersion,
          package.RulePackageSha256);
      var result = new Dictionary<string,
        Stage03CarrierCandidateSnapshot>(StringComparer.Ordinal);
      foreach (Element element in uniqueElements)
      {
        Stage02ElementReference reference =
          Stage02RevitSelectionService.CreateReference(
            uiApplication,
            document,
            element);
        string savedRoleId;
        savedRoles.TryGetValue(element.UniqueId, out savedRoleId);
        result.Add(element.UniqueId, new Stage03CarrierCandidateSnapshot
        {
          UniqueId = reference.UniqueId,
          Category = reference.Category,
          ElementKind = reference.ElementKind,
          ElementName = reference.ElementName,
          FamilyName = reference.FamilyName,
          TypeName = reference.TypeName,
          SavedRoleId = savedRoleId ?? string.Empty
        });
      }
      return result;
    }

    private static Stage03FieldStatus ResolveCarrierFailureStatus(
      IReadOnlyCollection<Element> categoryCandidates,
      IReadOnlyDictionary<string, Stage03CarrierMatchDecision>
        matchDecisions)
    {
      if (categoryCandidates == null || categoryCandidates.Count == 0)
        return Stage03FieldStatus.MissingCarrier;
      Stage03FieldStatus[] statuses = categoryCandidates
        .Select(element => matchDecisions[element.UniqueId].Status)
        .ToArray();
      if (statuses.Contains(Stage03FieldStatus.AmbiguousCarrier))
        return Stage03FieldStatus.AmbiguousCarrier;
      if (statuses.Contains(Stage03FieldStatus.CarrierNameMismatch))
        return Stage03FieldStatus.CarrierNameMismatch;
      return Stage03FieldStatus.CarrierCategoryMismatch;
    }

    private static Dictionary<int, ParameterBindingEvidence>
      BuildBindingIndex(Document document)
    {
      var result = new Dictionary<int, ParameterBindingEvidence>();
      DefinitionBindingMapIterator iterator =
        document.ParameterBindings.ForwardIterator();
      iterator.Reset();
      while (iterator.MoveNext())
      {
        InternalDefinition definition = iterator.Key as InternalDefinition;
        ElementBinding binding = iterator.Current as ElementBinding;
        if (definition == null || binding == null) continue;
        var categories = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Category category in binding.Categories)
        {
          if (category == null) continue;
          int categoryId = category.Id.IntegerValue;
          if (Enum.IsDefined(typeof(BuiltInCategory), categoryId))
            categories.Add(((BuiltInCategory)categoryId).ToString());
        }
        result[definition.Id.IntegerValue] = new ParameterBindingEvidence
        {
          Scope = binding is TypeBinding
            ? "TYPE"
            : binding is InstanceBinding ? "INSTANCE" : string.Empty,
          Categories = Freeze(categories)
        };
      }
      return result;
    }

    private static Dictionary<Guid, SharedParameterEvidence>
      BuildSharedParameterIndex(
        Document document,
        IEnumerable<HbrRuleProperty> properties,
        IReadOnlyDictionary<int, ParameterBindingEvidence>
          bindingsByDefinitionId)
    {
      var result = new Dictionary<Guid, SharedParameterEvidence>();
      foreach (Guid guid in properties
        .Select(property => property.Revit.ParameterGuid)
        .Distinct()
        .OrderBy(value => value))
      {
        SharedParameterElement shared = SharedParameterElement.Lookup(
          document,
          guid);
        InternalDefinition definition = shared == null
          ? null
          : shared.GetDefinition();
        ParameterBindingEvidence binding = null;
        if (definition != null)
        {
          bindingsByDefinitionId.TryGetValue(
            definition.Id.IntegerValue,
            out binding);
        }
        result.Add(guid, new SharedParameterEvidence
        {
          Exists = shared != null && definition != null,
          Name = definition == null ? string.Empty : definition.Name,
          Visible = definition != null && definition.Visible,
          HideWhenNoValue = shared != null && shared.ShouldHideWhenNoValue(),
          ParameterType = definition == null
            ? string.Empty
            : definition.ParameterType.ToString(),
          Binding = binding
        });
      }
      return result;
    }

    private static Stage03FieldResult BuildCarrierFailureField(
      HbrRuleProperty property,
      HbrCarrierRole role,
      HBRFileContext context,
      Stage03FieldStatus carrierStatus,
      Element owner = null)
    {
      Stage03RequirementApplicabilityDecision applicability =
        ResolveApplicability(
        property,
        context);
      Stage03FieldResult field = BaseField(
        property,
        role,
        owner,
        applicability);
      field.CarrierStatus = carrierStatus;
      field.ParameterStatus = Stage03FieldStatus.NotEvaluated;
      field.RevitStatus = Stage03FieldStatus.NotEvaluated;
      field.Status = applicability.Active
        ? carrierStatus
        : Stage03FieldStatus.NotApplicable;
      field.IsBusinessBlocker = applicability.Active;
      field.Messages = Freeze(new[]
      {
        carrierStatus == Stage03FieldStatus.AmbiguousCarrier
          ? "载体不唯一，未读取任一参数。"
          : "未找到符合规则类别与 ElementKind 的字段载体。"
      }.Concat(applicability.Messages));
      return field;
    }

    private static FieldDecision BuildField(
      Document document,
      HBRFileContext context,
      HbrCarrierRole role,
      Element owner,
      OwnerDecision ownerDecision,
      HbrRuleProperty property,
      SharedParameterEvidence evidence)
    {
      Stage03RequirementApplicabilityDecision applicability =
        ResolveApplicability(
        property,
        context);
      Stage03FieldResult field = BaseField(
        property,
        role,
        owner,
        applicability);
      var messages = new List<string>(applicability.Messages);
      if (!applicability.Active)
      {
        field.ParameterStatus = Stage03FieldStatus.NotEvaluated;
        field.RevitStatus = Stage03FieldStatus.NotEvaluated;
        field.Status = Stage03FieldStatus.NotApplicable;
        field.Messages = Freeze(messages);
        return new FieldDecision(field, null);
      }

      if (!string.Equals(
          role.IfcEntity,
          property.Ifc.Entity,
          StringComparison.Ordinal)
        || !string.Equals(
          role.IfcOwnerStrategy,
          property.IfcWrite.OwnerStrategy,
          StringComparison.Ordinal))
      {
        field.RevitStatus = Stage03FieldStatus.InvalidValue;
        messages.Add("字段 IFC owner 合同与载体角色不一致。");
      }
      Stage03IfcOwnerStrategyDecision strategy =
        Stage03IfcOwnerStrategyPolicy.Evaluate(
          property.IfcWrite.OwnerStrategy);
      if (!strategy.Implemented)
      {
        field.RevitStatus = strategy.Status;
        messages.Add(strategy.Message);
        return CompleteField(field, property, messages, null);
      }
      if (!ownerDecision.Success)
      {
        field.RevitStatus = Stage03FieldStatus.IfcOwnerNotFound;
        messages.Add(ownerDecision.Message);
      }

      if (!evidence.Exists)
      {
        field.ParameterStatus = Stage03FieldStatus.MissingParameter;
        messages.Add("文档中不存在固定 GUID 的 SharedParameterElement 定义。");
        return CompleteField(field, property, messages, null);
      }
      if (!evidence.Visible
        || evidence.HideWhenNoValue
        || !property.Revit.Visible)
      {
        field.ParameterStatus = Stage03FieldStatus.InvalidValue;
        messages.Add("固定 GUID 共享参数不是持续可见的业务参数。");
        return CompleteField(field, property, messages, null);
      }
      if (!IsAllowedParameterName(property, evidence.Name)
        || !string.Equals(
          property.Revit.ParameterType,
          evidence.ParameterType,
          StringComparison.OrdinalIgnoreCase))
      {
        field.ParameterStatus = Stage03FieldStatus.InvalidValue;
        messages.Add("固定 GUID 参数定义的名称或 ParameterType 与规则不一致。");
        return CompleteField(field, property, messages, null);
      }
      if (evidence.Binding == null)
      {
        field.ParameterStatus = Stage03FieldStatus.MissingParameter;
        messages.Add("固定 GUID 共享参数没有项目参数 BindingMap 证据。");
        return CompleteField(field, property, messages, null);
      }
      string ownerCategory = Stage02RevitSelectionService
        .GetBuiltInCategoryName(owner);
      if (!string.Equals(
          property.Revit.BindingScope,
          evidence.Binding.Scope,
          StringComparison.Ordinal)
        || !evidence.Binding.Categories.Contains(
          ownerCategory,
          StringComparer.Ordinal))
      {
        field.ParameterStatus = Stage03FieldStatus.InvalidValue;
        messages.Add("固定 GUID 参数的绑定 scope 或类别与规则不一致。");
        return CompleteField(field, property, messages, null);
      }

      Element target = ResolveParameterTarget(document, owner, property);
      field.RevitValueSource = string.Equals(
        property.Revit.BindingScope,
        "TYPE",
        StringComparison.Ordinal)
          ? "GUID_TYPE:" + (target == null ? string.Empty : target.UniqueId)
            + "|OWNER:" + owner.UniqueId
          : "GUID_INSTANCE:" + owner.UniqueId;
      if (target == null)
      {
        field.ParameterStatus = Stage03FieldStatus.MissingParameter;
        messages.Add("TYPE 绑定无法解析类型目标元素。");
        return CompleteField(field, property, messages, null);
      }
      Parameter parameter = target.get_Parameter(property.Revit.ParameterGuid);
      if (parameter == null)
      {
        field.ParameterStatus = Stage03FieldStatus.MissingParameter;
        messages.Add("具体载体上缺少固定 GUID 参数值入口。");
        return CompleteField(field, property, messages, null);
      }
      if (!parameter.UserModifiable || !property.Revit.UserModifiable)
      {
        field.ParameterStatus = Stage03FieldStatus.InvalidValue;
        messages.Add("固定 GUID 参数不是规则要求的用户可修改参数。");
        return CompleteField(field, property, messages, null);
      }

      HbrParameterReadDecision read = HbrParameterValueConverter
        .TryReadCanonicalValue(parameter, property);
      field.RevitRawValue = read.RawValue;
      field.RevitNormalizedValue = read.CanonicalValue;
      if (!read.Success)
      {
        field.RevitStatus = Stage03FieldStatus.InvalidValue;
        messages.Add(read.Message);
        return CompleteField(field, property, messages, null);
      }
      if (!read.HasValue || read.CanonicalValue.Length == 0)
      {
        if (RequiresNonBlank(property.Requirement.Level))
        {
          field.RevitStatus = Stage03FieldStatus.EmptyRequiredValue;
          messages.Add("活动字段的 Revit 可见值为空。");
        }
        return CompleteField(field, property, messages, null);
      }

      HbrIfcCanonicalValueDecision canonical =
        HbrIfcCanonicalValuePolicy.Validate(
          property.Ifc.DeclaredType,
          read.CanonicalValue);
      if (!canonical.Success)
      {
        field.RevitStatus = Stage03FieldStatus.InvalidValue;
        messages.Add(canonical.Message);
        return CompleteField(field, property, messages, null);
      }
      field.RevitNormalizedValue = canonical.NormalizedValue;
      if (field.RevitStatus != Stage03FieldStatus.Pass)
        return CompleteField(field, property, messages, null);

      string propertyIdentity = property.PropertyId + "|"
        + role.RoleId + "|" + owner.UniqueId;
      string globalId = ownerDecision.Owner.GlobalId;
      var enrichment = new HbrIfcEnrichmentValue
      {
        OwnerEntityType = property.Ifc.Entity,
        OwnerGlobalId = globalId,
        OwnerStrategy = globalId.Length == 0
          ? HbrIfcOwnerStrategies.SingleEntityByType
          : HbrIfcOwnerStrategies.GlobalId,
        PropertySetName = property.Ifc.PropertySet,
        PropertyName = property.Ifc.Property,
        DeclaredIfcType = property.Ifc.DeclaredType,
        CanonicalValue = canonical.NormalizedValue,
        PropertyIdentity = propertyIdentity,
        SemanticKey = property.CanonicalKey + "|" + owner.UniqueId
      };
      return CompleteField(field, property, messages, enrichment);
    }

    private static FieldDecision CompleteField(
      Stage03FieldResult field,
      HbrRuleProperty property,
      IEnumerable<string> messages,
      HbrIfcEnrichmentValue enrichment)
    {
      field.Status = Stage03FieldStatusPolicy.Resolve(
        field.Active,
        field.Applicability,
        field.CarrierStatus,
        field.ParameterStatus,
        field.RevitStatus,
        property.Requirement.Level);
      field.IsBusinessBlocker = field.Active
        && field.Status != Stage03FieldStatus.Pass
        && field.Status != Stage03FieldStatus.NotApplicable;
      field.Messages = Freeze(messages
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal));
      return new FieldDecision(
        field,
        field.Status == Stage03FieldStatus.Pass
          || field.Status == Stage03FieldStatus.UnclassifiedRequirement
            ? enrichment
            : null);
    }

    private static Stage03FieldResult BaseField(
      HbrRuleProperty property,
      HbrCarrierRole role,
      Element owner,
      Stage03RequirementApplicabilityDecision applicability)
    {
      return new Stage03FieldResult
      {
        PropertyId = property.PropertyId,
        ContractKind = property.ContractKind,
        Requirement = property.Requirement.Level,
        Applicability = applicability.Applicability,
        Entity = property.Ifc.Entity,
        PropertySet = property.Ifc.PropertySet,
        IfcProperty = property.Ifc.Property,
        Role = role.RoleId,
        ElementId = owner == null ? 0 : owner.Id.IntegerValue,
        OwnerUniqueId = owner == null ? string.Empty : owner.UniqueId,
        ParameterGuid = property.Revit.ParameterGuid.ToString("D"),
        ParameterName = property.Revit.ParameterName,
        ParameterScope = property.Revit.BindingScope,
        CarrierStatus = Stage03FieldStatus.Pass,
        ParameterStatus = Stage03FieldStatus.Pass,
        RevitStatus = Stage03FieldStatus.Pass,
        RawIfcStatus = Stage03FieldStatus.NotEvaluated,
        FinalIfcStatus = Stage03FieldStatus.NotEvaluated,
        Status = applicability.Active
          ? Stage03FieldStatus.Pass
          : Stage03FieldStatus.NotApplicable,
        Active = applicability.Active,
        IsBusinessBlocker = false,
        Messages = Freeze(applicability.Messages)
      };
    }

    private static Stage03RequirementApplicabilityDecision
      ResolveApplicability(
      HbrRuleProperty property,
      HBRFileContext context)
    {
      return Stage03RequirementApplicabilityPolicy.Evaluate(
        property.Requirement.Level,
        property.Requirement.ConditionId,
        context == null ? null : context.ProjectConditions);
    }

    private static OwnerDecision BuildOwner(
      Document document,
      HbrCarrierRole role,
      Element element)
    {
      var owner = new Stage03IfcOwnerSnapshot
      {
        Entity = role.IfcEntity,
        Role = role.RoleId,
        ElementId = element.Id.IntegerValue,
        UniqueId = element.UniqueId ?? string.Empty,
        RuleOwnerStrategy = role.IfcOwnerStrategy
      };
      Stage03IfcOwnerStrategyDecision strategy =
        Stage03IfcOwnerStrategyPolicy.Evaluate(role.IfcOwnerStrategy);
      if (!strategy.Implemented)
        return new OwnerDecision(false, owner, strategy.Message);
      if (!strategy.UsesExportGuid)
        return new OwnerDecision(true, owner, string.Empty);
      try
      {
        Guid exportId = ExportUtils.GetExportId(document, element.Id);
        if (exportId == Guid.Empty)
          return new OwnerDecision(
            false,
            owner,
            "Revit export id 为空，无法建立 IFC owner 映射。");
        owner.GlobalId = IfcGuidCodec.Encode(exportId);
        return new OwnerDecision(true, owner, string.Empty);
      }
      catch (Exception exception)
      {
        return new OwnerDecision(
          false,
          owner,
          "读取 Revit export id 失败：" + exception.Message);
      }
    }

    private static Stage03CarrierResult Carrier(
      HbrCarrierRole role,
      Element element,
      Stage03FieldStatus status,
      bool active,
      string message)
    {
      return new Stage03CarrierResult
      {
        Entity = role.IfcEntity,
        Role = role.RoleId,
        ElementId = element == null ? 0 : element.Id.IntegerValue,
        UniqueId = element == null ? string.Empty : element.UniqueId,
        Category = element == null
          ? string.Empty
          : Stage02RevitSelectionService.GetBuiltInCategoryName(element),
        Name = element == null ? string.Empty : element.Name ?? string.Empty,
        Status = status,
        Active = active,
        IsBusinessBlocker = active
          && status != Stage03FieldStatus.Pass
          && status != Stage03FieldStatus.NotApplicable,
        Messages = string.IsNullOrWhiteSpace(message)
          ? Array.Empty<string>()
          : Freeze(new[] { message })
      };
    }

    private static Element ResolveParameterTarget(
      Document document,
      Element owner,
      HbrRuleProperty property)
    {
      if (string.Equals(
        property.Revit.BindingScope,
        "INSTANCE",
        StringComparison.Ordinal))
      {
        return owner;
      }
      if (string.Equals(
        property.Revit.BindingScope,
        "TYPE",
        StringComparison.Ordinal))
      {
        ElementId typeId = owner.GetTypeId();
        return typeId == null || typeId == ElementId.InvalidElementId
          ? null
          : document.GetElement(typeId);
      }
      return null;
    }

    private static string ElementKind(Document document, Element element)
    {
      if (document.ProjectInformation != null
        && element.Id == document.ProjectInformation.Id)
        return "ProjectInformation";
      switch (Stage02RevitSelectionService.GetBuiltInCategoryName(element))
      {
        case "OST_Levels": return "Level";
        case "OST_Rooms": return "Room";
        case "OST_Areas": return "Area";
        case "OST_Walls": return "Wall";
        case "OST_Floors": return "Floor";
        case "OST_Roofs": return "Roof";
        case "OST_Windows":
        case "OST_Doors":
        case "OST_GenericModel": return "FamilyInstance";
        case "OST_StairsRuns": return "StairsRun";
        case "OST_DuctCurves": return "Duct";
        default: return element.GetType().Name;
      }
    }

    private static bool RequiresNonBlank(string requirementLevel)
    {
      return string.Equals(
          requirementLevel,
          "REQUIRED",
          StringComparison.Ordinal)
        || string.Equals(
          requirementLevel,
          "CONDITIONAL",
          StringComparison.Ordinal)
        || string.Equals(
          requirementLevel,
          "UNCLASSIFIED",
          StringComparison.Ordinal);
    }

    private static bool IsAllowedParameterName(
      HbrRuleProperty property,
      string actualName)
    {
      return string.Equals(
          property.Revit.ParameterName,
          actualName,
          StringComparison.Ordinal)
        || property.Revit.LegacyNames.Contains(
          actualName,
          StringComparer.Ordinal);
    }

    private static IEnumerable<Element> ElementsFor(
      IReadOnlyDictionary<string, List<Element>> index,
      string category)
    {
      List<Element> values;
      return index.TryGetValue(category, out values)
        ? values
        : Enumerable.Empty<Element>();
    }

    private static string OwnerIdentity(Stage03IfcOwnerSnapshot owner)
    {
      return owner.Entity + "|" + owner.Role + "|"
        + owner.ElementId.ToString(CultureInfo.InvariantCulture) + "|"
        + owner.UniqueId;
    }

    private static void AddTechnical(
      ISet<string> codes,
      ICollection<Stage03Diagnostic> diagnostics,
      string code,
      string message)
    {
      codes.Add(code);
      diagnostics.Add(new Stage03Diagnostic
      {
        Code = code,
        Stage = "REVIT_SCAN",
        Severity = "ERROR",
        Message = message ?? string.Empty
      });
    }

    private static IReadOnlyList<Stage03Diagnostic> FreezeDiagnostics(
      IEnumerable<Stage03Diagnostic> diagnostics)
    {
      return Freeze((diagnostics ?? Array.Empty<Stage03Diagnostic>())
        .OrderBy(value => value.Code, StringComparer.Ordinal)
        .ThenBy(value => value.Stage, StringComparer.Ordinal)
        .ThenBy(value => value.Message, StringComparer.Ordinal));
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
    {
      return new ReadOnlyCollection<T>(
        (values ?? Enumerable.Empty<T>()).ToArray());
    }

    private sealed class ParameterBindingEvidence
    {
      internal string Scope { get; set; } = string.Empty;
      internal IReadOnlyList<string> Categories { get; set; } =
        Array.Empty<string>();
    }

    private sealed class SharedParameterEvidence
    {
      internal bool Exists { get; set; }
      internal string Name { get; set; } = string.Empty;
      internal bool Visible { get; set; }
      internal bool HideWhenNoValue { get; set; }
      internal string ParameterType { get; set; } = string.Empty;
      internal ParameterBindingEvidence Binding { get; set; }
    }

    private sealed class OwnerDecision
    {
      internal OwnerDecision(
        bool success,
        Stage03IfcOwnerSnapshot owner,
        string message)
      {
        Success = success;
        Owner = owner;
        Message = message ?? string.Empty;
      }

      internal bool Success { get; }
      internal Stage03IfcOwnerSnapshot Owner { get; }
      internal string Message { get; }
    }

    private sealed class FieldDecision
    {
      internal FieldDecision(
        Stage03FieldResult field,
        HbrIfcEnrichmentValue enrichmentValue)
      {
        Field = field;
        EnrichmentValue = enrichmentValue;
      }

      internal Stage03FieldResult Field { get; }
      internal HbrIfcEnrichmentValue EnrichmentValue { get; }
    }
  }
}
