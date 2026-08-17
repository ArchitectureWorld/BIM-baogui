using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.HifcCore;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Stage02B;
using BIMBaoGui.RevitAddin.Workflow;

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
      var messages = new List<string>();
      NativeStage03TechnicalPreflightEvidence preflight =
        NativeStage03TechnicalPreflightService.Probe(
          uiApplication, safeRequest.OutputDirectory);
      foreach (string code in preflight.FatalCodes) technical.Add(code);

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
      bool stage01Current = false;
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
          stage01Current = true;
          NativeStage03Stage01ValidationClassification stage01Validation =
            NativeStage03Stage01ValidationPolicy.Classify(
              stage01.Validation,
              NativeRuleCatalog.Current);
          foreach (string code in stage01Validation.TechnicalFatalCodes)
            technical.Add(code);
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

      string modelFileType = stage01?.Model?.GetValue(
        NativeStage01Keys.ModelFileType) ?? string.Empty;
      NativeStage03ChecklistGenerationResult generation =
        NativeStage03ChecklistGenerator.Generate(
          modelFileType,
          stage01?.Model?.Conditions,
          NativeReportingRuleCatalog.Current);
      if (!generation.Supported)
        technical.Add(NativeStage03Codes.ModelProfileNotImplemented);

      NativeWorkflowIdentity currentIdentity = null;
      if (stage01Current && generation.Supported && document != null)
      {
        try
        {
          NativeStoredInitialization stored = NativeStage01Storage.Read(document);
          currentIdentity = NativeWorkflowIdentityFactory.Create(
            uiApplication,
            modelFileType,
            stored.FileGuid,
            stage01.StorageDecision.ActualPayloadHash,
            NativeRuleCatalog.Current.Identity);
        }
        catch (Exception exception)
        {
          technical.Add("WORKFLOW_DOCUMENT_MISMATCH");
          messages.Add("无法建立 Stage03 当前 workflow identity："
            + exception.Message);
        }
      }

      NativeStage02RevitPreviewResult stage02 = null;
      if (stage01Current && generation.Supported && document != null)
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

      NativeStage02BReadResult stage02B = null;
      NativeStage02BStorageSnapshot stage02BSnapshot = null;
      if (stage01Current && generation.Supported && document != null)
      {
        try
        {
          stage02B = NativeStage02BRevitReadService.Read(uiApplication);
          stage02BSnapshot = NativeStage02BStorage.Read(document);
        }
        catch (Exception exception)
        {
          messages.Add("Stage02B 当前结果读取失败：" + exception.Message);
        }
      }

      NativeWorkflowResultEnvelope stage01Result = ReadWorkflowResult(
        document, "STAGE01", technical, messages);
      NativeWorkflowResultEnvelope stage02AResult = ReadWorkflowResult(
        document, "STAGE02A", technical, messages);
      NativeWorkflowResultEnvelope stage02BResult = ReadWorkflowResult(
        document, "STAGE02B", technical, messages);
      string stage01InputHash = stage01?.StorageDecision?.ActualPayloadHash
        ?? string.Empty;
      string stage02AInputHash = CurrentStage02AInputHash(stage02?.Preview);
      string stage02BInputHash = stage02BSnapshot?.SnapshotHash ?? string.Empty;
      var evidence = new NativeStage03SourceEvidenceBundle
      {
        ScanExecuted = true,
        CurrentIdentity = currentIdentity,
        Stage01 = stage01,
        Stage01CurrentInputSnapshotHash = stage01InputHash,
        Stage01Result = stage01Result,
        Stage02A = stage02?.Preview,
        Stage02ACurrentInputSnapshotHash = stage02AInputHash,
        Stage02AResult = stage02AResult,
        Stage02B = stage02B,
        Stage02BCurrentInputSnapshotHash = stage02BInputHash,
        Stage02BResult = stage02BResult,
        TechnicalPreflight = preflight,
        TechnicalFatalCodes = Freeze(technical)
      };
      var checklist = new List<NativeStage03ChecklistItem>(generation.Supported
        ? NativeStage03ChecklistEvaluator.Evaluate(
          generation.Definitions, evidence)
        : Array.Empty<NativeStage03ChecklistItem>());
      foreach (NativeStage03ChecklistItem item in checklist.Where(value =>
        value.Status == NativeStage03ChecklistStatus.Failed
        && IsTechnicalWorkflowCode(value.IssueCode)))
        technical.Add(item.IssueCode);

      var fields = new List<NativeStage03FieldEvidence>();
      var exportFields = new List<HifcFieldRequest>();
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      var legacyBusiness = new SortedSet<string>(StringComparer.Ordinal);
      if (stage02?.Preview != null && document != null)
      {
        foreach (NativeStage02ElementPlan elementPlan in stage02.Preview.Elements
          .OrderBy(value => value.Element.UniqueId, StringComparer.Ordinal))
        {
          if (elementPlan.RoleMatchStatus
            != NativeStage02RoleMatchStatus.Matched)
          {
            legacyBusiness.Add(Code(
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
              legacyBusiness);
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

      IReadOnlyList<NativeOfficialAcceptancePropertyReadback> readbacks =
        BuildOfficialReadbacks(
          document,
          generation.OfficialAcceptanceManifest,
          stage02?.Preview,
          stage02B,
          stage01Result,
          stage02AResult,
          stage02BResult,
          checklist);
      NativeStage03BlockerClassification blockers =
        NativeStage03BlockerPolicy.Classify(technical, checklist);

      NativeStage03GateDecision gate = NativeStage03GatePolicy.Evaluate(
        safeRequest.Mode,
        safeRequest.ForceReason,
        blockers.TechnicalFatalCodes,
        blockers.BusinessBlockers,
        exportFields.Count);
      RulePackageIdentity identity = catalog.Identity;
      string fingerprint = currentIdentity?.DocumentFingerprint
        ?? stage02?.Preview?.DocumentFingerprint
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
        DocumentPath = NormalizeDocumentPath(document?.PathName),
        ModelFileType = modelFileType,
        RevitVersion = uiApplication.Application.VersionNumber ?? string.Empty,
        NormalizedOutputDirectory = preflight.NormalizedOutputDirectory,
        PreflightHash = preflight.ProbeHash,
        Stage02ACurrentInputSnapshotHash = stage02AInputHash,
        Stage01PayloadSha256 = stage01Hash,
        Stage01WorkflowResult = stage01Result,
        Stage02AWorkflowResult = stage02AResult,
        Stage02BWorkflowResult = stage02BResult,
        PluginRuntime = CapturePluginRuntime(),
        OfficialAcceptanceManifest = generation.OfficialAcceptanceManifest,
        OfficialAcceptanceRevitReadbacks = readbacks,
        Checklist = new ReadOnlyCollection<NativeStage03ChecklistItem>(
          checklist.ToArray()),
        PassedCount = checklist.Count(value =>
          value.Status == NativeStage03ChecklistStatus.Passed),
        FailedCount = checklist.Count(value =>
          value.Status == NativeStage03ChecklistStatus.Failed),
        WarningCount = checklist.Count(value =>
          value.Status == NativeStage03ChecklistStatus.Warning),
        NotCheckedCount = checklist.Count(value =>
          value.Status == NativeStage03ChecklistStatus.NotChecked),
        TechnicalFatalCodes = blockers.TechnicalFatalCodes,
        BusinessBlockers = blockers.BusinessBlockers,
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

    private static NativeWorkflowResultEnvelope ReadWorkflowResult(
      Document document,
      string sourceFeature,
      ISet<string> technical,
      ICollection<string> messages)
    {
      if (document == null) return null;
      try
      {
        return NativeWorkflowResultStorage.Read(document, sourceFeature);
      }
      catch (Exception exception)
      {
        technical.Add("WORKFLOW_RESULT_HASH_MISMATCH");
        messages.Add(sourceFeature + " workflow result 读取失败："
          + exception.Message);
        return null;
      }
    }

    private static string CurrentStage02AInputHash(NativeStage02Preview preview)
    {
      if (preview == null) return string.Empty;
      return NativeStage02SemanticAssignmentCanonicalizer.Sha256(string.Join(
        "\u001f",
        (preview.Elements ?? Array.Empty<NativeStage02ElementPlan>())
        .Where(value => value?.Element != null)
        .OrderBy(value => value.Element.UniqueId, StringComparer.Ordinal)
        .Select(value => string.IsNullOrWhiteSpace(value.ElementSnapshotHash)
          ? NativeStage02ElementSnapshotCanonicalizer.Sha256(value.Element)
          : value.ElementSnapshotHash)));
    }

    private static bool IsTechnicalWorkflowCode(string code)
    {
      switch (code ?? string.Empty)
      {
        case "WORKFLOW_SCHEMA_MISMATCH":
        case "WORKFLOW_RESULT_HASH_MISMATCH":
        case "WORKFLOW_DOCUMENT_MISMATCH":
        case "WORKFLOW_MODEL_TYPE_MISMATCH":
        case "WORKFLOW_RULE_PACKAGE_MISMATCH":
          return true;
        default:
          return false;
      }
    }

    private static IReadOnlyList<NativeOfficialAcceptancePropertyReadback>
      BuildOfficialReadbacks(
        Document document,
        NativeOfficialAcceptanceManifest manifest,
        NativeStage02Preview stage02A,
        NativeStage02BReadResult stage02B,
        NativeWorkflowResultEnvelope stage01Result,
        NativeWorkflowResultEnvelope stage02AResult,
        NativeWorkflowResultEnvelope stage02BResult,
        ICollection<NativeStage03ChecklistItem> checklist)
    {
      var readbacks = new List<NativeOfficialAcceptancePropertyReadback>();
      foreach (NativeOfficialAcceptanceManifestEntry entry in
        (manifest?.Properties
          ?? Array.Empty<NativeOfficialAcceptanceManifestEntry>())
        .OrderBy(value => value.PropertyId, StringComparer.Ordinal))
      {
        NativeStage02PropertyDefinition property = null;
        NativeStage02RuleCatalog.Current.PropertiesById.TryGetValue(
          entry.PropertyId, out property);
        Element[] owners = ResolveReadbackOwners(
          document, entry, stage02A, stage02B);
        NativeOfficialAcceptanceOwnerReadback[] values = owners
          .Select(owner => ReadOwner(document, owner, property, entry))
          .GroupBy(value => (value.ExpectedIfcGlobalId ?? string.Empty) + "\n"
            + (value.RevitUniqueId ?? string.Empty), StringComparer.Ordinal)
          .Select(group => group.First())
          .OrderBy(value => value.ExpectedIfcGlobalId, StringComparer.Ordinal)
          .ThenBy(value => value.RevitUniqueId, StringComparer.Ordinal)
          .ToArray();
        string sourceHash = SourceResultHash(
          entry.SourceStage, stage01Result, stage02AResult, stage02BResult);
        readbacks.Add(new NativeOfficialAcceptancePropertyReadback
        {
          PropertyId = entry.PropertyId,
          SourceStage = entry.SourceStage,
          SourceResultHash = sourceHash,
          Values = new ReadOnlyCollection<
            NativeOfficialAcceptanceOwnerReadback>(values)
        });
        bool complete = property != null
          && !string.IsNullOrWhiteSpace(sourceHash)
          && values.Length > 0
          && values.All(value =>
            !string.IsNullOrWhiteSpace(value.ExpectedIfcGlobalId)
            && !string.IsNullOrWhiteSpace(value.RevitUniqueId)
            && !string.IsNullOrWhiteSpace(value.CanonicalValue));
        checklist.Add(new NativeStage03ChecklistItem
        {
          CheckId = "OFFICIAL.READBACK." + entry.PropertyId,
          DisplayName = entry.Identity,
          SourceStage = entry.SourceStage,
          CheckKind = NativeReportingCheckKind.System,
          PropertyId = entry.PropertyId,
          CurrentValue = complete
            ? string.Join(" | ", values.Select(value => value.CanonicalValue))
            : string.Empty,
          Status = complete
            ? NativeStage03ChecklistStatus.Passed
            : NativeStage03ChecklistStatus.Failed,
          IssueCode = complete ? string.Empty
            : OfficialReadbackFailureCode(entry, stage02B),
          IssueMessage = complete ? string.Empty
            : OfficialReadbackFailureCode(entry, stage02B),
          RemediationTarget = entry.SourceStage
            == NativeReportingSourceStage.Stage02B
              ? "OPEN_STAGE02B"
              : entry.SourceStage == NativeReportingSourceStage.Stage02A
                ? "OPEN_STAGE02A"
                : "OPEN_STAGE01",
          Elements = new ReadOnlyCollection<
            BIMBaoGui.RevitAddin.Issues.NativeIssueElementReference>(owners
            .Where(value => value != null)
            .GroupBy(value => value.UniqueId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(value => value.UniqueId, StringComparer.Ordinal)
            .Select(value => new BIMBaoGui.RevitAddin.Issues
              .NativeIssueElementReference
            {
              ElementId = value.Id.IntegerValue,
              UniqueId = value.UniqueId,
              ElementName = SafeElementName(value),
              CategoryName = value.Category?.Name ?? string.Empty
            }).ToArray()),
          InternalValidationPassed = complete,
          OfficialAcceptancePassed = false
        });
      }
      return new ReadOnlyCollection<NativeOfficialAcceptancePropertyReadback>(
        readbacks.ToArray());
    }

    private static Element[] ResolveReadbackOwners(
      Document document,
      NativeOfficialAcceptanceManifestEntry entry,
      NativeStage02Preview stage02A,
      NativeStage02BReadResult stage02B)
    {
      if (document == null || entry == null) return Array.Empty<Element>();
      if (entry.SourceStage == NativeReportingSourceStage.Stage01)
        return document.ProjectInformation == null
          ? Array.Empty<Element>()
          : new Element[] { document.ProjectInformation };
      if (entry.SourceStage == NativeReportingSourceStage.Stage02A)
      {
        return (stage02A?.Elements ?? Array.Empty<NativeStage02ElementPlan>())
          .Where(value => value?.Element != null
            && (value.Fields ?? Array.Empty<NativeStage02FieldPlan>())
              .Any(field => field?.Property != null
                && string.Equals(field.Property.PropertyId, entry.PropertyId,
                  StringComparison.Ordinal)))
          .Select(value => document.GetElement(value.Element.UniqueId))
          .Where(value => value != null)
          .GroupBy(value => value.UniqueId, StringComparer.Ordinal)
          .Select(group => group.First())
          .OrderBy(value => value.UniqueId, StringComparer.Ordinal)
          .ToArray();
      }
      if (entry.SourceStage != NativeReportingSourceStage.Stage02B)
        return Array.Empty<Element>();
      NativeStage02BMetricDefinition metric = NativeReportingRuleCatalog.Current
        .Stage02BMetrics.SingleOrDefault(value => string.Equals(
          value.PropertyId, entry.PropertyId, StringComparison.Ordinal));
      if (metric == null) return Array.Empty<Element>();
      if (string.Equals(metric.Property.IfcEntity, "IfcProject",
        StringComparison.Ordinal))
        return document.ProjectInformation == null
          ? Array.Empty<Element>()
          : new Element[] { document.ProjectInformation };
      NativeStage02BMetricRecord record = (stage02B?.Records
          ?? Array.Empty<NativeStage02BMetricRecord>())
        .SingleOrDefault(value => value != null && string.Equals(
          value.PropertyId, entry.PropertyId, StringComparison.Ordinal));
      if (record?.OfficialCarrierStatus
        != NativeOfficialCarrierEvidenceStatus.Verified
        || string.IsNullOrWhiteSpace(record.OfficialProjectionCarrierId))
        return Array.Empty<Element>();
      NativeOfficialProjectionCarrierDefinition carrier;
      try
      {
        carrier = NativeReportingRuleCatalog.Current.GetProjectionCarrier(
          record.OfficialProjectionCarrierId);
      }
      catch
      {
        return Array.Empty<Element>();
      }
      if (string.Equals(carrier.SelectorKind, "PROJECT_INFORMATION",
        StringComparison.Ordinal))
        return document.ProjectInformation == null
          ? Array.Empty<Element>()
          : new Element[] { document.ProjectInformation };
      return (stage02A?.Elements ?? Array.Empty<NativeStage02ElementPlan>())
        .Where(value => value?.Element != null
          && string.Equals(value.EffectiveRoleId.Length > 0
              ? value.EffectiveRoleId
              : value.RoleId,
            carrier.RoleId,
            StringComparison.Ordinal))
        .Select(value => document.GetElement(value.Element.UniqueId))
        .Where(value => value != null)
        .GroupBy(value => value.UniqueId, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(value => value.UniqueId, StringComparer.Ordinal)
        .ToArray();
    }

    private static NativeOfficialAcceptanceOwnerReadback ReadOwner(
      Document document,
      Element owner,
      NativeStage02PropertyDefinition property,
      NativeOfficialAcceptanceManifestEntry entry)
    {
      string uniqueId = owner?.UniqueId ?? string.Empty;
      string globalId = string.Empty;
      string currentValue = string.Empty;
      if (document != null && owner != null && property != null)
      {
        try
        {
          Guid exportId = ExportUtils.GetExportId(document, owner.Id);
          if (exportId != Guid.Empty) globalId = IfcGlobalId.Encode(exportId);
        }
        catch { }
        try
        {
          Element target = NativeStage02RevitService.ResolveTarget(
            document, owner, entry.BindingScope);
          Parameter parameter = target?.get_Parameter(property.ParameterGuid);
          currentValue = NativeStage02ValueCodec.Read(parameter, property);
        }
        catch { }
      }
      return new NativeOfficialAcceptanceOwnerReadback
      {
        RevitUniqueId = uniqueId,
        ExpectedIfcGlobalId = globalId,
        CanonicalValue = currentValue ?? string.Empty
      };
    }

    private static string OfficialReadbackFailureCode(
      NativeOfficialAcceptanceManifestEntry entry,
      NativeStage02BReadResult stage02B)
    {
      if (entry.SourceStage == NativeReportingSourceStage.Stage02B)
      {
        NativeStage02BMetricRecord record = (stage02B?.Records
            ?? Array.Empty<NativeStage02BMetricRecord>())
          .SingleOrDefault(value => value != null && string.Equals(
            value.PropertyId, entry.PropertyId, StringComparison.Ordinal));
        if (record?.OfficialCarrierStatus
          == NativeOfficialCarrierEvidenceStatus.PendingGoldenRvt)
          return "OFFICIAL_CARRIER_PENDING_GOLDEN_RVT";
      }
      return "READBACK_FAILED";
    }

    private static string SourceResultHash(
      NativeReportingSourceStage source,
      NativeWorkflowResultEnvelope stage01,
      NativeWorkflowResultEnvelope stage02A,
      NativeWorkflowResultEnvelope stage02B)
    {
      switch (source)
      {
        case NativeReportingSourceStage.Stage01:
          return stage01?.ResultHash ?? string.Empty;
        case NativeReportingSourceStage.Stage02A:
          return stage02A?.ResultHash ?? string.Empty;
        case NativeReportingSourceStage.Stage02B:
          return stage02B?.ResultHash ?? string.Empty;
        default:
          return string.Empty;
      }
    }

    private static NativePluginRuntimeIdentity CapturePluginRuntime()
    {
      Assembly assembly = Assembly.GetExecutingAssembly();
      string location = string.Empty;
      try
      {
        if (!string.IsNullOrWhiteSpace(assembly.Location))
          location = Path.GetFullPath(assembly.Location);
      }
      catch { }
      string informational = assembly.GetCustomAttribute<
        AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty;
      Match commit = Regex.Match(informational,
        @"(?:^|\.)sha\.([0-9a-fA-F]{40})(?:$|\.)",
        RegexOptions.CultureInvariant);
      FileVersionInfo file = null;
      try
      {
        if (location.Length > 0) file = FileVersionInfo.GetVersionInfo(location);
      }
      catch { }
      string dllSha = string.Empty;
      try
      {
        if (location.Length > 0 && File.Exists(location))
          dllSha = HifcCoreService.ComputeSha256(location);
      }
      catch { }
      return new NativePluginRuntimeIdentity
      {
        ProductVersion = file?.ProductVersion ?? string.Empty,
        AssemblyVersion = assembly.GetName().Version?.ToString() ?? string.Empty,
        InformationalVersion = informational,
        CommitSha = commit.Success
          ? commit.Groups[1].Value.ToLowerInvariant()
          : string.Empty,
        AddinDllPath = location,
        AddinDllSha256 = dllSha
      };
    }

    private static string NormalizeDocumentPath(string path)
    {
      if (string.IsNullOrWhiteSpace(path)) return string.Empty;
      try
      {
        return Path.GetFullPath(path);
      }
      catch
      {
        return path ?? string.Empty;
      }
    }

    private static string SafeElementName(Element element)
    {
      try { return element?.Name ?? string.Empty; }
      catch { return string.Empty; }
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
      using (SHA256 algorithm = SHA256.Create())
      {
        byte[] bytes = new UTF8Encoding(false).GetBytes(ToJson(result));
        return string.Concat(algorithm.ComputeHash(bytes).Select(value =>
          value.ToString("x2", CultureInfo.InvariantCulture)));
      }
    }

    internal static string ToJson(NativeStage03ScanResult result)
    {
      if (result == null) throw new ArgumentNullException(nameof(result));
      var builder = new StringBuilder(32768);
      builder.Append('{');
      Property(builder, "schema", "HBR_NATIVE_STAGE03_SCAN_V2", false);
      Property(builder, "mode", result.Mode.ToString(), true);
      Property(builder, "forceReason", result.ForceReason, true);
      Property(builder, "rulePackageId", result.RulePackageId, true);
      Property(builder, "rulePackageVersion", result.RulePackageVersion, true);
      Property(builder, "rulePackageSha256", result.RulePackageSha256, true);
      Property(builder, "documentFingerprint", result.DocumentFingerprint, true);
      Property(builder, "documentPath", result.DocumentPath, true);
      Property(builder, "modelFileType", result.ModelFileType, true);
      Property(builder, "revitVersion", result.RevitVersion, true);
      NativePluginRuntimeIdentity runtime = result.PluginRuntime
        ?? new NativePluginRuntimeIdentity();
      builder.Append(",\"pluginRuntime\":{");
      Property(builder, "productVersion", runtime.ProductVersion, false);
      Property(builder, "assemblyVersion", runtime.AssemblyVersion, true);
      Property(builder, "informationalVersion", runtime.InformationalVersion, true);
      Property(builder, "commitSha", runtime.CommitSha, true);
      Property(builder, "addinDllPath", runtime.AddinDllPath, true);
      Property(builder, "addinDllSha256", runtime.AddinDllSha256, true);
      builder.Append('}');
      Property(builder, "normalizedOutputDirectory",
        result.NormalizedOutputDirectory, true);
      Property(builder, "preflightHash", result.PreflightHash, true);
      Property(builder, "stage02ACurrentInputSnapshotHash",
        result.Stage02ACurrentInputSnapshotHash, true);
      Property(builder, "officialAcceptanceManifestSha256",
        result.OfficialAcceptanceManifest?.Sha256, true);
      Property(builder, "stage01ResultHash",
        result.Stage01WorkflowResult?.ResultHash, true);
      Property(builder, "stage02AResultHash",
        result.Stage02AWorkflowResult?.ResultHash, true);
      Property(builder, "stage02BResultHash",
        result.Stage02BWorkflowResult?.ResultHash, true);
      ArrayProperty(builder, "technical", result.TechnicalFatalCodes);
      builder.Append(",\"officialAcceptanceRevitReadbacks\":[");
      bool first = true;
      foreach (NativeOfficialAcceptancePropertyReadback readback in
        (result.OfficialAcceptanceRevitReadbacks
          ?? Array.Empty<NativeOfficialAcceptancePropertyReadback>())
        .OrderBy(value => value.PropertyId, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        first = false;
        builder.Append('{');
        Property(builder, "propertyId", readback.PropertyId, false);
        Property(builder, "sourceStage", readback.SourceStage.ToString(), true);
        Property(builder, "sourceResultHash", readback.SourceResultHash, true);
        builder.Append(",\"values\":[");
        bool firstValue = true;
        foreach (NativeOfficialAcceptanceOwnerReadback value in
          (readback.Values ?? Array.Empty<NativeOfficialAcceptanceOwnerReadback>())
          .OrderBy(item => item.ExpectedIfcGlobalId, StringComparer.Ordinal)
          .ThenBy(item => item.RevitUniqueId, StringComparer.Ordinal))
        {
          if (!firstValue) builder.Append(',');
          firstValue = false;
          builder.Append('{');
          Property(builder, "expectedIfcGlobalId",
            value.ExpectedIfcGlobalId, false);
          Property(builder, "revitUniqueId", value.RevitUniqueId, true);
          Property(builder, "canonicalValue", value.CanonicalValue, true);
          builder.Append('}');
        }
        builder.Append("]}");
      }
      builder.Append("],\"checklist\":[");
      first = true;
      foreach (NativeStage03ChecklistItem item in (result.Checklist
          ?? Array.Empty<NativeStage03ChecklistItem>())
        .OrderBy(value => value.CheckId, StringComparer.Ordinal))
      {
        if (!first) builder.Append(',');
        first = false;
        builder.Append('{');
        Property(builder, "checkId", item.CheckId, false);
        Property(builder, "kind", item.CheckKind.ToString(), true);
        Property(builder, "source", item.SourceStage.ToString(), true);
        Property(builder, "status", item.Status.ToString(), true);
        Property(builder, "fieldKey", item.FieldKey, true);
        Property(builder, "propertyId", item.PropertyId, true);
        Property(builder, "roleId", item.RoleId, true);
        Property(builder, "ruleText", item.RuleText, true);
        Property(builder, "targetKey", item.TargetKey, true);
        builder.Append(",\"elementUniqueIds\":[");
        bool firstElement = true;
        foreach (string uniqueId in (item.Elements
            ?? Array.Empty<BIMBaoGui.RevitAddin.Issues.NativeIssueElementReference>())
          .Where(value => value != null)
          .Select(value => value.UniqueId)
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .Distinct(StringComparer.Ordinal)
          .OrderBy(value => value, StringComparer.Ordinal))
        {
          if (!firstElement) builder.Append(',');
          firstElement = false;
          builder.Append(Quote(uniqueId));
        }
        builder.Append(']');
        Property(builder, "officialCarrierStatus",
          item.OfficialCarrierStatus.ToString(), true);
        Property(builder, "officialProjectionCarrierId",
          item.OfficialProjectionCarrierId, true);
        Property(builder, "officialCarrierProbeRef",
          item.OfficialCarrierProbeRef, true);
        Property(builder, "officialEvidenceRef", item.OfficialEvidenceRef, true);
        builder.Append('}');
      }
      builder.Append("]}");
      return builder.ToString();
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
