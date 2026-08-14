using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal sealed class NativeStage02PreviewRequest
  {
    internal NativeStage02ScopeMode ScopeMode { get; set; } =
      NativeStage02ScopeMode.FullModel;
    internal NativeStage02IdentificationMode IdentificationMode { get; set; } =
      NativeStage02IdentificationMode.Automatic;
    internal IReadOnlyList<string> CustomUniqueIds { get; set; } =
      Array.Empty<string>();
    internal string BulkRoleId { get; set; } = string.Empty;
    internal IReadOnlyList<NativeStage02RoleOverride> RoleOverrides { get; set; } =
      Array.Empty<NativeStage02RoleOverride>();

    internal NativeStage02PreviewRequest Clone()
    {
      string[] ids = (CustomUniqueIds ?? Array.Empty<string>())
        .Select(value => (value ?? string.Empty).Trim())
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      NativeStage02RoleOverride[] overrides = (RoleOverrides
          ?? Array.Empty<NativeStage02RoleOverride>())
        .Where(value => value != null)
        .Select(value => new NativeStage02RoleOverride
        {
          ElementUniqueId = (value.ElementUniqueId ?? string.Empty).Trim(),
          RoleId = (value.RoleId ?? string.Empty).Trim()
        })
        .OrderBy(value => value.ElementUniqueId, StringComparer.Ordinal)
        .ThenBy(value => value.RoleId, StringComparer.Ordinal)
        .ToArray();
      return new NativeStage02PreviewRequest
      {
        ScopeMode = ScopeMode,
        IdentificationMode = IdentificationMode,
        CustomUniqueIds = new ReadOnlyCollection<string>(ids),
        BulkRoleId = (BulkRoleId ?? string.Empty).Trim(),
        RoleOverrides = new ReadOnlyCollection<NativeStage02RoleOverride>(overrides)
      };
    }
  }

  internal sealed class NativeStage02RevitPreviewResult
  {
    internal bool Success { get; set; }
    internal string Status { get; set; } = string.Empty;
    internal IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
    internal NativeStage02Preview Preview { get; set; }
    internal NativeStage02PreviewRequest ResolvedRequest { get; set; }
  }

  internal static class NativeStage02RevitService
  {
    internal static NativeStage02RevitPreviewResult CreatePreview(
      UIApplication uiApplication,
      NativeStage02PreviewRequest request)
    {
      if (uiApplication == null) throw new ArgumentNullException(nameof(uiApplication));
      NativeStage02PreviewRequest safeRequest = request?.Clone()
        ?? new NativeStage02PreviewRequest();
      UIDocument uiDocument = uiApplication.ActiveUIDocument;
      Document document = uiDocument?.Document;
      IReadOnlyList<string> environmentErrors = ValidateEnvironment(
        uiApplication,
        document);
      if (environmentErrors.Count > 0)
        return Failure("Stage02 当前不可预览", environmentErrors.ToArray());

      NativeStage01ReadResult stage01 = NativeStage01RevitReadService.Read(uiApplication);
      if (stage01?.StorageDecision == null || stage01.Model == null)
        return Failure("Stage02 等待文件初始化", "请先在 01 文件初始化中完成写入并回读。" );
      if (stage01.StorageDecision.State == NativeStage01StorageState.MigratableLegacy)
        return Failure(
          "Stage02 等待 Stage01 数据迁移确认",
          "检测到旧版 Stage01 Payload；请先在 01 文件初始化中确认迁移并完成写入回读。" );
      if (stage01.StorageDecision.State != NativeStage01StorageState.Current)
        return Failure("Stage02 等待文件初始化", "当前 Stage01 Storage 未达到可消费的 Current 状态。" );

      NativeProjectConditionDeclarationDecision declaration =
        NativeProjectConditionDeclarationPolicy.Evaluate(
          stage01.Model,
          NativeRuleCatalog.Current);
      if (!declaration.IsValid)
        return Failure(
          "Stage02 等待项目条件声明",
          "请先在 01 文件初始化中选择一个或多个实际项目条件，或勾选“无上述项目条件（已确认）”。" );

      string modelProfile = stage01.Model.GetValue(NativeStage01Keys.ModelFileType);
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      if (string.IsNullOrWhiteSpace(modelProfile)
        || !catalog.CarrierRoles.Any(role => role.ModelFileTypes.Contains(
          modelProfile,
          StringComparer.Ordinal)))
        return Failure("Stage02 模型类型阻断", "Stage01 模型文件类型不属于当前 HBR 数据库。" );

      string documentFingerprint = ComputeDocumentFingerprint(
        uiApplication,
        document,
        stage01);
      IReadOnlyList<string> resolvedCustomIds = ResolveCustomIds(
        uiDocument,
        document,
        safeRequest);
      NativeStage02RoleAssignmentDecision assignmentDecision =
        NativeStage02RoleAssignmentPolicy.Resolve(
          safeRequest.ScopeMode,
          safeRequest.IdentificationMode,
          resolvedCustomIds,
          safeRequest.BulkRoleId,
          safeRequest.RoleOverrides);
      if (!assignmentDecision.Accepted)
        return Failure(
          "Stage02 语义角色分配阻断",
          assignmentDecision.ErrorCode + "：" + assignmentDecision.Message);

      var resolvedRequest = new NativeStage02PreviewRequest
      {
        ScopeMode = safeRequest.ScopeMode,
        IdentificationMode = safeRequest.IdentificationMode,
        CustomUniqueIds = new ReadOnlyCollection<string>(
          assignmentDecision.SelectedUniqueIds.ToArray()),
        BulkRoleId = safeRequest.BulkRoleId,
        RoleOverrides = new ReadOnlyCollection<NativeStage02RoleOverride>(
          safeRequest.RoleOverrides.Select(value => new NativeStage02RoleOverride
          {
            ElementUniqueId = value.ElementUniqueId,
            RoleId = value.RoleId
          }).ToArray())
      };

      NativeStage02ElementSnapshot[] inventory = new FilteredElementCollector(document)
        .WhereElementIsNotElementType()
        .Select(element => CreateSnapshot(document, element, documentFingerprint))
        .Where(value => value != null)
        .ToArray();
      NativeStage02InventoryDecision inventoryDecision =
        NativeStage02InventoryPolicy.Resolve(
          resolvedRequest.ScopeMode,
          inventory,
          resolvedRequest.CustomUniqueIds,
          catalog.AllRevitCategories);
      if (!inventoryDecision.Accepted)
        return Failure(
          "Stage02 扫描范围阻断",
          inventoryDecision.ErrorCode + "：" + inventoryDecision.Message);

      NativeStage02SemanticAssignmentReadResult persisted =
        NativeStage02SemanticAssignmentRevitService.Read(document);
      if (persisted.State == NativeStage02SemanticAssignmentStorageState.Corrupt
        || persisted.State == NativeStage02SemanticAssignmentStorageState.UnsupportedFuture)
        return Failure(
          "Stage02 语义角色存储阻断",
          persisted.State + "：" + persisted.Message);

      var assignmentsByElement = assignmentDecision.Assignments.ToDictionary(
        value => value.ElementUniqueId,
        StringComparer.Ordinal);
      IReadOnlyDictionary<string, bool> conditions =
        new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(
          stage01.Model.Conditions,
          StringComparer.Ordinal));
      var evidence = new List<NativeStage02ElementEvidence>();
      foreach (NativeStage02ElementSnapshot snapshot in inventoryDecision.Elements)
      {
        NativeStage02ResolvedAssignment currentAssignment;
        assignmentsByElement.TryGetValue(snapshot.UniqueId, out currentAssignment);
        NativeStage02SemanticAssignmentRecord savedAssignment;
        persisted.AssignmentsByElement.TryGetValue(snapshot.UniqueId, out savedAssignment);

        NativeStage02RoleMatchResult role = ResolveEffectiveRole(
          snapshot,
          currentAssignment,
          savedAssignment,
          modelProfile,
          conditions,
          catalog);
        if (role.Status != NativeStage02RoleMatchStatus.Matched)
        {
          evidence.Add(new NativeStage02ElementEvidence
          {
            Element = snapshot,
            ResolvedRoleMatch = role
          });
          continue;
        }
        Element live = document.GetElement(snapshot.UniqueId);
        if (live == null)
        {
          evidence.Add(new NativeStage02ElementEvidence
          {
            Element = snapshot,
            ResolvedRoleMatch = role
          });
          continue;
        }
        var parameters = new Dictionary<Guid, NativeStage02ParameterEvidence>();
        foreach (NativeStage02PropertyDefinition property in role.CandidateRoleIds
          .SelectMany(roleId => catalog.PropertiesForRole(roleId))
          .GroupBy(value => value.PropertyId, StringComparer.Ordinal)
          .Select(group => group.First())
          .OrderBy(value => value.PropertyId, StringComparer.Ordinal))
        {
          parameters[property.ParameterGuid] = ReadParameterEvidence(
            document,
            live,
            property);
        }
        evidence.Add(new NativeStage02ElementEvidence
        {
          Element = snapshot,
          ResolvedRoleMatch = role,
          Parameters = parameters
        });
      }

      NativeStage02Preview preview = NativeStage02PreviewCompiler.Compile(
        new NativeStage02PreviewInput
        {
          DocumentFingerprint = documentFingerprint,
          ModelProfile = modelProfile,
          Conditions = new Dictionary<string, bool>(stage01.Model.Conditions, StringComparer.Ordinal),
          Elements = evidence
        },
        catalog);
      var messages = new List<string>
      {
        "已扫描 " + preview.Elements.Count.ToString(CultureInfo.InvariantCulture) + " 个规则相关构件。",
        "识别方式：" + resolvedRequest.IdentificationMode,
        "预览 SHA-256：" + preview.PreviewHash
      };
      if (persisted.StaleElementUniqueIds.Count > 0)
        messages.Add(
          "检测到 " + persisted.StaleElementUniqueIds.Count.ToString(CultureInfo.InvariantCulture)
          + " 条已删除构件的 Stage02 stale role 记录；不会转移到其他构件。" );
      return new NativeStage02RevitPreviewResult
      {
        Success = true,
        Status = "Stage02 预览已生成",
        Preview = preview,
        ResolvedRequest = resolvedRequest,
        Messages = new ReadOnlyCollection<string>(messages)
      };
    }

    private static NativeStage02RoleMatchResult ResolveEffectiveRole(
      NativeStage02ElementSnapshot snapshot,
      NativeStage02ResolvedAssignment currentAssignment,
      NativeStage02SemanticAssignmentRecord savedAssignment,
      string modelProfile,
      IReadOnlyDictionary<string, bool> conditions,
      NativeStage02RuleCatalog catalog)
    {
      if (currentAssignment != null)
      {
        if (currentAssignment.AssignmentMode == NativeStage02AssignmentMode.Auto)
          return NativeStage02RoleMatcher.Match(snapshot, catalog.CarrierRoles, modelProfile);
        return ResolveManualRole(
          snapshot,
          currentAssignment.RoleId,
          currentAssignment.Source,
          modelProfile,
          conditions,
          catalog);
      }

      if (savedAssignment != null
        && savedAssignment.AssignmentMode == NativeStage02AssignmentMode.Manual)
      {
        return ResolveManualRole(
          snapshot,
          savedAssignment.RoleId,
          "PersistedManual",
          modelProfile,
          conditions,
          catalog);
      }

      return NativeStage02RoleMatcher.Match(snapshot, catalog.CarrierRoles, modelProfile);
    }

    private static NativeStage02RoleMatchResult ResolveManualRole(
      NativeStage02ElementSnapshot snapshot,
      string roleId,
      string source,
      string modelProfile,
      IReadOnlyDictionary<string, bool> conditions,
      NativeStage02RuleCatalog catalog)
    {
      NativeStage02ManualCarrierDecision manual =
        NativeStage02ManualCarrierPolicy.Evaluate(
          roleId,
          modelProfile,
          conditions,
          snapshot,
          NativeStage02ManualRoleCatalog.Current.Roles);
      if (!manual.Accepted)
      {
        return new NativeStage02RoleMatchResult(
          NativeStage02RoleMatchStatus.AssignedRoleConflict,
          string.Empty,
          "MANUAL_SEMANTIC_ASSIGNMENT",
          string.IsNullOrWhiteSpace(roleId) ? Array.Empty<string>() : new[] { roleId },
          manual.ErrorCode + "：" + manual.Message);
      }
      snapshot.AssignedRoleId = roleId;
      return new NativeStage02RoleMatchResult(
        NativeStage02RoleMatchStatus.Matched,
        roleId,
        source,
        new[] { roleId },
        string.Empty);
    }

    private static IReadOnlyList<string> ValidateEnvironment(
      UIApplication uiApplication,
      Document document)
    {
      var errors = new List<string>();
      if (document == null) errors.Add("当前没有活动 Revit 项目文档。" );
      if (!string.Equals(
        uiApplication.Application.VersionNumber,
        "2020",
        StringComparison.Ordinal))
        errors.Add("当前原生插件仅允许 Revit 2020。" );
      if (document != null)
      {
        if (document.IsFamilyDocument) errors.Add("族文档不进入 Stage02。" );
        if (document.IsReadOnly) errors.Add("当前 RVT 为只读。" );
        if (string.IsNullOrWhiteSpace(document.PathName)) errors.Add("请先保存 RVT 文件。" );
      }
      return errors;
    }

    private static IReadOnlyList<string> ResolveCustomIds(
      UIDocument uiDocument,
      Document document,
      NativeStage02PreviewRequest request)
    {
      if (request.ScopeMode == NativeStage02ScopeMode.FullModel)
        return Array.Empty<string>();
      string[] explicitIds = (request.CustomUniqueIds ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      if (explicitIds.Length > 0) return explicitIds;
      if (uiDocument == null) return Array.Empty<string>();
      return new ReadOnlyCollection<string>(
        uiDocument.Selection.GetElementIds()
          .Select(document.GetElement)
          .Where(value => value != null)
          .Select(value => value.UniqueId)
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .Distinct(StringComparer.Ordinal)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray());
    }

    private static NativeStage02ElementSnapshot CreateSnapshot(
      Document document,
      Element element,
      string documentFingerprint)
    {
      if (element == null) return null;
      Category category = element.Category;
      bool isProjectInformation = element is ProjectInfo;
      bool isModelElement = isProjectInformation
        || (category != null && category.CategoryType == CategoryType.Model);
      return new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = documentFingerprint,
        UniqueId = element.UniqueId ?? string.Empty,
        ElementId = element.Id.IntegerValue,
        Category = CategoryKey(category),
        ElementKind = ElementKind(element),
        ElementName = SafeName(element),
        FamilyName = FamilyName(document, element),
        TypeName = TypeName(document, element),
        LevelName = LevelName(document, element),
        AssignedRoleId = string.Empty,
        IsElementType = element is ElementType,
        IsViewSpecific = SafeViewSpecific(element),
        IsImported = element is ImportInstance,
        IsLinked = element is RevitLinkInstance,
        IsModelElement = isModelElement
      };
    }

    private static NativeStage02ParameterEvidence ReadParameterEvidence(
      Document document,
      Element element,
      NativeStage02PropertyDefinition property)
    {
      Element target = ResolveTarget(document, element, property.BindingScope);
      SharedParameterElement shared = SharedParameterElement.Lookup(
        document,
        property.ParameterGuid);
      InternalDefinition definition = shared?.GetDefinition();
      ElementBinding binding = definition == null
        ? null
        : NativeStage02ParameterBindingService.FindBinding(document.ParameterBindings, definition);
      Parameter parameter = target.get_Parameter(property.ParameterGuid);
      var messages = new List<string>();
      bool compatible = true;
      if (definition != null && !string.Equals(
        definition.Name,
        property.ParameterName,
        StringComparison.Ordinal))
      {
        compatible = false;
        messages.Add("固定 GUID 参数名称冲突。" );
      }
      if (definition != null && !string.Equals(
        definition.ParameterType.ToString(),
        property.ParameterType,
        StringComparison.OrdinalIgnoreCase))
      {
        compatible = false;
        messages.Add("固定 GUID 参数类型冲突。" );
      }
      if (binding != null)
      {
        bool typeBinding = binding is TypeBinding;
        bool expectedType = string.Equals(property.BindingScope, "TYPE", StringComparison.Ordinal);
        if (typeBinding != expectedType)
        {
          compatible = false;
          messages.Add("固定 GUID 参数绑定范围冲突。" );
        }
      }
      if (parameter != null && !string.Equals(
        parameter.StorageType.ToString(),
        property.StorageType,
        StringComparison.Ordinal))
      {
        compatible = false;
        messages.Add("固定 GUID 参数 StorageType 冲突。" );
      }

      string current = string.Empty;
      if (parameter != null && compatible)
      {
        try { current = NativeStage02ValueCodec.Read(parameter, property); }
        catch (Exception exception)
        {
          compatible = false;
          messages.Add(exception.Message);
        }
      }
      var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (string alias in property.SuggestionAliases
        .Concat(property.LegacyParameterNames)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal))
      {
        Parameter aliasParameter = target.LookupParameter(alias);
        if (aliasParameter == null) continue;
        try
        {
          string value = NativeStage02ValueCodec.Read(aliasParameter, property);
          if (!string.IsNullOrWhiteSpace(value)) aliases[alias] = value;
        }
        catch { }
      }
      NativeStage02SemanticSuggestionDecision suggestion =
        NativeStage02SemanticValueSuggestionPolicy.Evaluate(
          property.SuggestionKind,
          property.SuggestionAliases,
          TypeName(document, element),
          null);
      if (suggestion.Status == NativeStage02SemanticSuggestionStatus.Suggested
        && !string.IsNullOrWhiteSpace(suggestion.CanonicalValue))
        aliases[suggestion.CanonicalValue] = suggestion.CanonicalValue;

      return new NativeStage02ParameterEvidence
      {
        ParameterGuid = property.ParameterGuid,
        Exists = parameter != null,
        ContractCompatible = compatible,
        BindingIncludesCategory = NativeStage02ParameterBindingService.IncludesCategory(
          binding,
          element.Category),
        IsReadOnly = parameter != null && parameter.IsReadOnly,
        CurrentCanonicalValue = current,
        AliasValues = aliases,
        ContractMessage = string.Join(" ", messages)
      };
    }

    internal static Element ResolveTarget(
      Document document,
      Element element,
      string bindingScope)
    {
      if (!string.Equals(bindingScope, "TYPE", StringComparison.Ordinal)) return element;
      ElementId typeId = element.GetTypeId();
      if (typeId == null || typeId == ElementId.InvalidElementId)
        throw new InvalidOperationException("TYPE 参数目标元素没有有效 ElementType。" );
      return document.GetElement(typeId)
        ?? throw new InvalidOperationException("无法解析 TYPE 参数目标 ElementType。" );
    }

    private static string ComputeDocumentFingerprint(
      UIApplication uiApplication,
      Document document,
      NativeStage01ReadResult stage01)
    {
      string raw = string.Join("|", new[]
      {
        document.PathName ?? string.Empty,
        document.Title ?? string.Empty,
        uiApplication.Application.VersionNumber ?? string.Empty,
        stage01.Model.GetValue(NativeStage01Keys.FileGuid),
        stage01.StorageDecision.ActualPayloadHash ?? string.Empty
      });
      return NativeStage02PreviewCanonicalizer.Sha256(raw);
    }

    private static string CategoryKey(Category category)
    {
      if (category == null) return string.Empty;
      return ((BuiltInCategory)category.Id.IntegerValue).ToString();
    }

    private static string ElementKind(Element element)
    {
      if (element is ProjectInfo) return "ProjectInformation";
      if (element is Wall) return "Wall";
      if (element is Floor) return "Floor";
      if (element is RoofBase) return "Roof";
      if (element is Autodesk.Revit.DB.Architecture.StairsRun) return "StairsRun";
      if (element is Autodesk.Revit.DB.Mechanical.Duct) return "Duct";
      if (element is FamilyInstance) return "FamilyInstance";
      return element.GetType().Name;
    }

    private static string SafeName(Element element)
    {
      try { return element.Name ?? string.Empty; }
      catch { return string.Empty; }
    }

    private static bool SafeViewSpecific(Element element)
    {
      try { return element.ViewSpecific; }
      catch { return false; }
    }

    private static string FamilyName(Document document, Element element)
    {
      FamilyInstance instance = element as FamilyInstance;
      if (instance?.Symbol?.Family != null)
        return instance.Symbol.Family.Name ?? string.Empty;
      ElementType type = TypeElement(document, element);
      try { return type?.FamilyName ?? string.Empty; }
      catch { return string.Empty; }
    }

    private static string TypeName(Document document, Element element)
    {
      FamilyInstance instance = element as FamilyInstance;
      if (instance?.Symbol != null) return instance.Symbol.Name ?? string.Empty;
      return SafeName(TypeElement(document, element));
    }

    private static ElementType TypeElement(Document document, Element element)
    {
      ElementId typeId = element.GetTypeId();
      if (typeId == null || typeId == ElementId.InvalidElementId) return null;
      return document.GetElement(typeId) as ElementType;
    }

    private static string LevelName(Document document, Element element)
    {
      FamilyInstance familyInstance = element as FamilyInstance;
      if (familyInstance != null && familyInstance.LevelId != ElementId.InvalidElementId)
        return SafeName(document.GetElement(familyInstance.LevelId));
      BuiltInParameter[] parameters =
      {
        BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
        BuiltInParameter.FAMILY_LEVEL_PARAM,
        BuiltInParameter.LEVEL_PARAM
      };
      foreach (BuiltInParameter builtIn in parameters)
      {
        Parameter parameter = element.get_Parameter(builtIn);
        if (parameter == null || parameter.StorageType != StorageType.ElementId) continue;
        ElementId levelId = parameter.AsElementId();
        if (levelId != null && levelId != ElementId.InvalidElementId)
          return SafeName(document.GetElement(levelId));
      }
      return string.Empty;
    }

    private static NativeStage02RevitPreviewResult Failure(
      string status,
      params string[] messages)
    {
      return new NativeStage02RevitPreviewResult
      {
        Success = false,
        Status = status ?? string.Empty,
        Messages = messages ?? Array.Empty<string>()
      };
    }
  }
}
