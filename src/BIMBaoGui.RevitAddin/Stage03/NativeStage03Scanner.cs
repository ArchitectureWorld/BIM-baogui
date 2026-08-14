using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.HifcCore;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage02;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal static class NativeStage03Scanner
  {
    internal static NativeStage03ScanResult Scan(
      UIApplication uiApplication,
      NativeStage03ScanRequest request)
    {
      if (uiApplication == null)
        throw new ArgumentNullException(nameof(uiApplication));
      NativeStage03ScanRequest safeRequest = request?.Clone()
        ?? new NativeStage03ScanRequest();
      Document document = uiApplication.ActiveUIDocument?.Document;
      var technical = new SortedSet<string>(StringComparer.Ordinal);
      var business = new SortedSet<string>(StringComparer.Ordinal);
      var messages = new List<string>();

      if (!string.Equals(
        uiApplication.Application.VersionNumber,
        "2020",
        StringComparison.Ordinal))
      {
        technical.Add(NativeStage03Codes.UnsupportedRevit);
        messages.Add("Stage03 仅支持 Revit 2020。" );
      }
      if (document == null
        || document.IsFamilyDocument
        || document.IsReadOnly
        || string.IsNullOrWhiteSpace(document.PathName))
      {
        technical.Add(NativeStage03Codes.DocumentUnavailable);
        messages.Add("当前文档必须是已保存、可写的 Revit 项目文件。" );
      }

      NativeStage01ReadResult stage01 = null;
      if (document != null)
      {
        stage01 = NativeStage01RevitReadService.Read(uiApplication);
        if (stage01?.StorageDecision == null || stage01.Model == null)
        {
          technical.Add(NativeStage03Codes.Stage01NotInitialized);
          messages.Add("Stage03 要求当前 RVT 已完成 Stage01 初始化。" );
        }
        else if (stage01.StorageDecision.State
          == NativeStage01StorageState.MigratableLegacy)
        {
          technical.Add(NativeStage03Codes.Stage01NotInitialized);
          messages.Add(
            "Stage01 数据迁移尚未确认；请先在 01 文件初始化中确认迁移并完成写入回读。" );
        }
        else if (stage01.StorageDecision.State
          != NativeStage01StorageState.Current)
        {
          technical.Add(NativeStage03Codes.Stage01NotInitialized);
          messages.Add("Stage01 Storage 未达到可检测的 Current 状态。" );
        }
        else
        {
          NativeStage03Stage01ValidationClassification stage01Validation =
            NativeStage03Stage01ValidationPolicy.Classify(
              stage01.Validation,
              NativeRuleCatalog.Current);
          foreach (string code in stage01Validation.TechnicalFatalCodes)
            technical.Add(code);
          foreach (string code in stage01Validation.BusinessBlockers)
            business.Add(code);
          messages.AddRange(stage01Validation.Messages);

          NativeProjectConditionDeclarationDecision declaration =
            NativeProjectConditionDeclarationPolicy.Evaluate(
              stage01.Model,
              NativeRuleCatalog.Current);
          if (!declaration.IsValid
            || stage01Validation.HasProjectConditionError)
          {
            technical.Add(NativeStage03Codes.ProjectConditionsUndeclared);
            messages.Add("项目条件尚未完成必填声明。" );
          }
        }
      }

      NativeStage02RevitPreviewResult stage02 = null;
      if (technical.Count == 0)
      {
        stage02 = NativeStage02RevitService.CreatePreview(
          uiApplication,
          new NativeStage02PreviewRequest
          {
            ScopeMode = NativeStage02ScopeMode.FullModel,
            CustomUniqueIds = Array.Empty<string>()
          });
        if (stage02 == null || !stage02.Success || stage02.Preview == null)
        {
          technical.Add(NativeStage03Codes.Stage02ScanFailed);
          messages.Add(stage02 == null
            ? "Stage02 现场扫描未返回结果。"
            : stage02.Status + "：" + string.Join(" ", stage02.Messages));
        }
      }

      var fields = new List<NativeStage03FieldEvidence>();
      var exportFields = new List<HifcFieldRequest>();
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      if (technical.Count == 0 && stage02?.Preview != null)
      {
        foreach (NativeStage02ElementPlan elementPlan in stage02.Preview.Elements
          .OrderBy(value => value.Element.UniqueId, StringComparer.Ordinal))
        {
          if (elementPlan.RoleMatchStatus
            != NativeStage02RoleMatchStatus.Matched)
          {
            business.Add(Code(
              NativeStage03Codes.CarrierBlocked,
              elementPlan.Element.ElementId,
              elementPlan.Element.UniqueId));
            messages.Add(
              "载体阻断｜Id=" + elementPlan.Element.ElementId + "｜"
              + elementPlan.RoleMatchStatus + "｜" + elementPlan.Message);
            continue;
          }
          Element owner = document.GetElement(elementPlan.Element.UniqueId);
          foreach (NativeStage02FieldPlan fieldPlan in elementPlan.Fields
            .Where(value => value.Property.StageOwnership.Contains(
              "STAGE03",
              StringComparer.Ordinal))
            .OrderBy(value => value.Property.PropertyId, StringComparer.Ordinal))
          {
            NativeStage03FieldEvidence field = BuildField(
              document,
              owner,
              elementPlan,
              fieldPlan,
              catalog,
              business);
            fields.Add(field);
            if ((safeRequest.Mode == NativeStage03Mode.Strict
                && field.StrictExportReady)
              || (safeRequest.Mode == NativeStage03Mode.ForcedTest
                && field.ExportableInForcedMode))
            {
              exportFields.Add(field.HifcField);
            }
          }
        }
      }

      NativeStage03GateDecision gate = NativeStage03GatePolicy.Evaluate(
        safeRequest.Mode,
        safeRequest.ForceReason,
        technical,
        business,
        exportFields.Count);
      RulePackageIdentity identity = catalog.Identity;
      string fingerprint = stage02?.Preview?.DocumentFingerprint
        ?? string.Empty;
      string stage01Hash = stage01?.StorageDecision?.ActualPayloadHash
        ?? string.Empty;
      var result = new NativeStage03ScanResult
      {
        Success = technical.Count == 0,
        Status = gate.AllowExport
          ? safeRequest.Mode == NativeStage03Mode.Strict
            ? "Stage03 严格预检通过"
            : "Stage03 强制测试预检通过"
          : "Stage03 预检阻断",
        Mode = safeRequest.Mode,
        ForceReason = safeRequest.ForceReason ?? string.Empty,
        AllowExport = gate.AllowExport,
        Forced = gate.Forced,
        RulePackageId = identity.PackageId,
        RulePackageVersion = identity.PackageVersion,
        RulePackageSha256 = identity.RulePackageSha256,
        DocumentFingerprint = fingerprint,
        DocumentTitle = document?.Title ?? string.Empty,
        DocumentPath = document?.PathName ?? string.Empty,
        Stage01PayloadSha256 = stage01Hash,
        TechnicalFatalCodes = Freeze(technical),
        BusinessBlockers = Freeze(business),
        Messages = Freeze(messages
          .Concat(gate.Blockers.Select(value => "BLOCKER：" + value))
          .Concat(gate.BypassedBusinessBlockers.Select(value =>
            "FORCED_BYPASS：" + value))),
        Fields = new ReadOnlyCollection<NativeStage03FieldEvidence>(fields
          .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
          .ThenBy(value => value.RoleId, StringComparer.Ordinal)
          .ThenBy(value => value.ElementId)
          .ThenBy(value => value.OwnerUniqueId, StringComparer.Ordinal)
          .ToArray()),
        ExportFields = new ReadOnlyCollection<HifcFieldRequest>(exportFields
          .OrderBy(value => value.PropertyIdentity, StringComparer.Ordinal)
          .ToArray())
      };
      result.ScanHash = NativeStage03Canonicalizer.ComputeHash(result);
      return result;
    }

    private static NativeStage03FieldEvidence BuildField(
      Document document,
      Element owner,
      NativeStage02ElementPlan elementPlan,
      NativeStage02FieldPlan fieldPlan,
      NativeStage02RuleCatalog catalog,
      ISet<string> business)
    {
      NativeStage02PropertyDefinition property = fieldPlan.Property;
      string roleId = ResolveRoleId(elementPlan, property, catalog);
      NativeCarrierRoleDefinition role = roleId.Length == 0
        ? null
        : catalog.CarrierRolesById[roleId];
      string ownerStrategy = property.OwnerStrategy.Length > 0
        ? property.OwnerStrategy
        : role?.IfcOwnerStrategy ?? string.Empty;
      string hifcOwnerStrategy = string.Empty;
      string ownerExportGuid = string.Empty;
      string ownerGlobalId = string.Empty;
      string ownerResolutionStatus = string.Empty;
      string ownerError = string.Empty;
      if (role == null || owner == null)
      {
        ownerResolutionStatus = "OWNER_CONTEXT_UNRESOLVED";
        ownerError = "无法唯一确定字段载体角色或 Revit owner。";
      }
      else if (string.Equals(
        ownerStrategy,
        HifcOwnerStrategies.SingleEntityByType,
        StringComparison.Ordinal))
      {
        if (!IsApprovedSingleEntityType(property.IfcEntity))
        {
          ownerResolutionStatus = "OWNER_TYPE_UNAPPROVED";
          ownerError = "该 IFC 实体尚未批准 SINGLE_ENTITY_BY_TYPE。";
        }
        else
        {
          hifcOwnerStrategy = HifcOwnerStrategies.SingleEntityByType;
          ownerResolutionStatus = "OWNER_TYPE_READY";
        }
      }
      else if (string.Equals(
        ownerStrategy,
        "BY_EXPORT_GUID",
        StringComparison.Ordinal))
      {
        try
        {
          Guid exportId = ExportUtils.GetExportId(document, owner.Id);
          NativeStage03ExportGuidOwnerDecision decision =
            NativeStage03ExportGuidOwnerPolicy.Resolve(
              ownerStrategy,
              property.IfcEntity,
              exportId);
          ownerExportGuid = decision.ExportGuid;
          ownerGlobalId = decision.OwnerGlobalId;
          ownerResolutionStatus = decision.Status;
          hifcOwnerStrategy = decision.HifcOwnerStrategy;
          if (!decision.Success) ownerError = decision.Message;
        }
        catch (Exception exception)
        {
          ownerResolutionStatus = "OWNER_EXPORT_GUID_READ_FAILED";
          ownerError = "读取 IFC owner Export GUID 失败：" + exception.Message;
        }
      }
      else if (string.Equals(
        ownerStrategy,
        HifcOwnerStrategies.GlobalId,
        StringComparison.Ordinal))
      {
        try
        {
          Guid exportId = ExportUtils.GetExportId(document, owner.Id);
          if (exportId == Guid.Empty)
          {
            ownerResolutionStatus = "OWNER_EXPORT_GUID_EMPTY";
            ownerError = "Revit ExportUtils.GetExportId 返回空 GUID。";
          }
          else
          {
            hifcOwnerStrategy = HifcOwnerStrategies.GlobalId;
            ownerExportGuid = exportId.ToString("D");
            ownerGlobalId = IfcGlobalId.Encode(exportId);
            ownerResolutionStatus = "OWNER_GUID_READY";
          }
        }
        catch (Exception exception)
        {
          ownerResolutionStatus = "OWNER_EXPORT_GUID_READ_FAILED";
          ownerError = "读取 IFC owner GlobalId 失败：" + exception.Message;
        }
      }
      else
      {
        ownerResolutionStatus = "OWNER_STRATEGY_UNSUPPORTED";
        ownerError = "IFC owner strategy 尚未实现：" + ownerStrategy;
      }

      bool active = fieldPlan.Status != NativeStage02FieldStatus.NotApplicable;
      string current = (fieldPlan.CurrentCanonicalValue ?? string.Empty).Trim();
      bool currentReady = fieldPlan.Status == NativeStage02FieldStatus.Correct
        && current.Length > 0;
      bool ownerReady = ownerError.Length == 0;
      bool strictReady = active
        && fieldPlan.StrictExportReady
        && currentReady
        && ownerReady;
      bool forcedReady = active
        && currentReady
        && ownerReady
        && property.RuntimeDecision != null
        && property.RuntimeDecision.Status
          != NativeRuntimeStatuses.NotImplemented;

      var messages = new List<string>();
      if (!string.IsNullOrWhiteSpace(fieldPlan.Message))
        messages.Add(fieldPlan.Message);
      if (ownerError.Length > 0) messages.Add(ownerError);
      if (active && !currentReady)
      {
        business.Add(Code(
          NativeStage03Codes.FieldNotReady,
          elementPlan.Element.ElementId,
          property.PropertyId));
      }
      if (active && currentReady && !fieldPlan.StrictExportReady)
      {
        business.Add(Code(
          NativeStage03Codes.RuntimeUnclassified,
          elementPlan.Element.ElementId,
          property.PropertyId));
      }
      if (active && !ownerReady)
      {
        business.Add(Code(
          NativeStage03Codes.OwnerNotResolvable,
          elementPlan.Element.ElementId,
          property.PropertyId));
      }

      string status = !active
        ? "NOT_APPLICABLE"
        : strictReady
          ? "STRICT_READY"
          : forcedReady
            ? "FORCED_TEST_READY"
            : "BLOCKED";
      string propertyIdentity = property.PropertyId + "|" + roleId + "|"
        + (owner?.UniqueId ?? string.Empty);
      HifcFieldRequest hifc = forcedReady || strictReady
        ? new HifcFieldRequest
        {
          PropertyIdentity = propertyIdentity,
          SemanticKey = property.PropertyId + "|" + roleId + "|"
            + (owner?.UniqueId ?? string.Empty),
          OwnerEntityType = property.IfcEntity,
          OwnerGlobalId = ownerGlobalId,
          OwnerStrategy = hifcOwnerStrategy,
          PropertySetName = property.IfcPropertySet,
          PropertyName = property.IfcProperty,
          DeclaredIfcType = property.DeclaredIfcType,
          CanonicalValue = current,
          CanonicalUnit = property.CanonicalUnit
        }
        : null;
      return new NativeStage03FieldEvidence
      {
        PropertyId = property.PropertyId,
        RoleId = roleId,
        Entity = property.IfcEntity,
        PropertySet = property.IfcPropertySet,
        IfcProperty = property.IfcProperty,
        DeclaredIfcType = property.DeclaredIfcType,
        CanonicalUnit = property.CanonicalUnit,
        Requirement = property.RequirementLevel,
        RuntimeStatus = property.RuntimeDecision?.Status ?? string.Empty,
        ElementId = elementPlan.Element.ElementId,
        OwnerUniqueId = owner?.UniqueId ?? string.Empty,
        OwnerStrategy = ownerStrategy,
        OwnerExportGuid = ownerExportGuid,
        OwnerGlobalId = ownerGlobalId,
        OwnerResolutionStatus = ownerResolutionStatus,
        CanonicalValue = current,
        Status = status,
        Message = string.Join("｜", messages.Distinct(StringComparer.Ordinal)),
        Active = active,
        StrictExportReady = strictReady,
        ExportableInForcedMode = forcedReady,
        HifcField = hifc
      };
    }

    private static string ResolveRoleId(
      NativeStage02ElementPlan element,
      NativeStage02PropertyDefinition property,
      NativeStage02RuleCatalog catalog)
    {
      string[] elementRoles = (element.RoleId ?? string.Empty)
        .Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
      string[] matches = elementRoles
        .Where(value => property.CarrierRoleIds.Contains(
          value,
          StringComparer.Ordinal))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
      if (matches.Length == 1) return matches[0];
      string[] byEntity = matches.Where(value =>
        catalog.CarrierRolesById.TryGetValue(
          value,
          out NativeCarrierRoleDefinition role)
        && string.Equals(
          role.IfcEntity,
          property.IfcEntity,
          StringComparison.Ordinal)).ToArray();
      return byEntity.Length == 1 ? byEntity[0] : string.Empty;
    }

    private static bool IsApprovedSingleEntityType(string entity)
    {
      return string.Equals(entity, "IfcProject", StringComparison.Ordinal)
        || string.Equals(entity, "IfcSite", StringComparison.Ordinal)
        || string.Equals(entity, "IfcBuilding", StringComparison.Ordinal);
    }

    private static string Code(string code, int elementId, string identity)
    {
      return code + ":" + elementId.ToString(CultureInfo.InvariantCulture)
        + ":" + (identity ?? string.Empty);
    }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values)
    {
      return new ReadOnlyCollection<string>((values
        ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray());
    }
  }

  internal static class NativeStage03Canonicalizer
  {
    private static readonly JavaScriptSerializer Serializer =
      new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
      };

    internal static string ComputeHash(NativeStage03ScanResult result)
    {
      return HifcCoreService.ComputeSha256(WriteTemporaryCanonical(result));
    }

    internal static string ToJson(NativeStage03ScanResult result)
    {
      if (result == null) throw new ArgumentNullException(nameof(result));
      var builder = new StringBuilder(32768);
      builder.Append('{');
      Property(builder, "schema", "HBR_NATIVE_STAGE03_SCAN_V1", false);
      Property(builder, "mode", result.Mode.ToString(), true);
      Property(builder, "forceReason", result.ForceReason, true);
      Property(builder, "rulePackageId", result.RulePackageId, true);
      Property(builder, "rulePackageVersion", result.RulePackageVersion, true);
      Property(builder, "rulePackageSha256", result.RulePackageSha256, true);
      Property(builder, "documentFingerprint", result.DocumentFingerprint, true);
      Property(builder, "documentPath", result.DocumentPath, true);
      Property(builder, "stage01PayloadSha256", result.Stage01PayloadSha256, true);
      ArrayProperty(builder, "technical", result.TechnicalFatalCodes);
      ArrayProperty(builder, "business", result.BusinessBlockers);
      builder.Append(",\"fields\":[");
      bool first = true;
      foreach (NativeStage03FieldEvidence field in result.Fields
        .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
        .ThenBy(value => value.RoleId, StringComparer.Ordinal)
        .ThenBy(value => value.ElementId)
        .ThenBy(value => value.OwnerUniqueId, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        first = false;
        builder.Append('{');
        Property(builder, "propertyId", field.PropertyId, false);
        Property(builder, "roleId", field.RoleId, true);
        Property(builder, "entity", field.Entity, true);
        Property(builder, "propertySet", field.PropertySet, true);
        Property(builder, "property", field.IfcProperty, true);
        Property(builder, "declaredType", field.DeclaredIfcType, true);
        Property(builder, "unit", field.CanonicalUnit, true);
        Property(builder, "requirement", field.Requirement, true);
        Property(builder, "runtimeStatus", field.RuntimeStatus, true);
        builder.Append(",\"elementId\":")
          .Append(field.ElementId.ToString(CultureInfo.InvariantCulture));
        Property(builder, "ownerUniqueId", field.OwnerUniqueId, true);
        Property(builder, "ownerStrategy", field.OwnerStrategy, true);
        Property(builder, "ownerExportGuid", field.OwnerExportGuid, true);
        Property(builder, "ownerGlobalId", field.OwnerGlobalId, true);
        Property(
          builder,
          "ownerResolutionStatus",
          field.OwnerResolutionStatus,
          true);
        Property(builder, "value", field.CanonicalValue, true);
        Property(builder, "status", field.Status, true);
        builder.Append(",\"strictReady\":")
          .Append(field.StrictExportReady ? "true" : "false")
          .Append(",\"forcedReady\":")
          .Append(field.ExportableInForcedMode ? "true" : "false")
          .Append('}');
      }
      builder.Append("]}");
      return builder.ToString();
    }

    private static string WriteTemporaryCanonical(NativeStage03ScanResult result)
    {
      string path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "BIMBaoGui.Stage03.Scan."
          + Guid.NewGuid().ToString("N") + ".json");
      try
      {
        System.IO.File.WriteAllText(
          path,
          ToJson(result),
          new UTF8Encoding(false));
        return path;
      }
      finally
      {
        // ComputeSha256 opens the path synchronously before control returns.
        // Deletion is intentionally performed by the caller below.
      }
    }

    private static void ArrayProperty(
      StringBuilder builder,
      string name,
      IEnumerable<string> values)
    {
      builder.Append(',').Append(Quote(name)).Append(":[");
      bool first = true;
      foreach (string value in (values ?? Array.Empty<string>())
        .OrderBy(item => item, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        first = false;
        builder.Append(Quote(value));
      }
      builder.Append(']');
    }

    private static void Property(
      StringBuilder builder,
      string name,
      string value,
      bool comma)
    {
      if (comma) builder.Append(',');
      builder.Append(Quote(name)).Append(':').Append(Quote(value));
    }

    private static string Quote(string value)
    {
      return Serializer.Serialize(value ?? string.Empty);
    }
  }
}
