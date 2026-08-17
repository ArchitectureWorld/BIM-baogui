using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01ReadResult
  {
    internal bool Success { get; set; }
    internal string Status { get; set; } = string.Empty;
    internal NativeStage01Model Model { get; set; }
    internal NativeStage01StorageDecision StorageDecision { get; set; }
    internal NativeStage01ValidationResult Validation { get; set; }
    internal NativeStage01LiveEvidence LiveEvidence { get; set; } =
      new NativeStage01LiveEvidence();
    internal IReadOnlyList<NativeStage01Drift> Drifts { get; set; } =
      Array.Empty<NativeStage01Drift>();
    internal bool RequiresMigrationConfirmation { get; set; }
    internal string SourcePayloadVersion { get; set; } = string.Empty;
    internal IReadOnlyList<string> Messages { get; set; } =
      Array.Empty<string>();
    internal NativeWorkflowResultEnvelope WorkflowResult { get; set; }
    internal NativeWorkflowResultEnvelope Stage02BResult { get; set; }
  }

  internal static class NativeStage01RevitReadService
  {
    internal static NativeStage01ReadResult Read(UIApplication application)
    {
      if (application == null) throw new ArgumentNullException(nameof(application));
      Document document = application.ActiveUIDocument?.Document;
      if (document == null)
      {
        return new NativeStage01ReadResult
        {
          Success = false,
          Status = "等待项目文档",
          Model = NativeRuleCatalog.Current.CreateDefaultStage01Model(),
          Messages = new[] { "Revit 当前没有活动项目文档。" }
        };
      }

      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      var messages = new List<string>();
      NativeStoredInitialization stored;
      NativeStage01StorageDecision storageDecision;
      try
      {
        stored = NativeStage01Storage.Read(document);
        storageDecision = NativeStage01StoragePolicy.Evaluate(
          stored,
          NativeStage01Canonicalizer.PayloadSchemaVersion);
      }
      catch (Exception exception)
      {
        stored = new NativeStoredInitialization { HasRecord = true };
        storageDecision = new NativeStage01StorageDecision
        {
          State = NativeStage01StorageState.Corrupt,
          ErrorCode = NativeStage01StorageCodes.CorruptStorage,
          Message = exception.Message
        };
      }

      NativeStage01LiveEvidence liveEvidence = CaptureLiveEvidence(
        document,
        messages);
      NativeStage01Model model;
      IReadOnlyList<NativeStage01Drift> drifts =
        Array.Empty<NativeStage01Drift>();
      bool requiresMigrationConfirmation = false;
      bool migrationFailed = false;
      string sourcePayloadVersion = storageDecision.Payload?.SchemaVersion
        ?? stored?.WorkflowVersion
        ?? string.Empty;

      switch (storageDecision.State)
      {
        case NativeStage01StorageState.NoRecord:
          model = catalog.CreateDefaultStage01Model();
          NativeStage01FieldAuthorityPolicy.ApplyInitialValues(
            model,
            liveEvidence);
          messages.Add(
            "当前 RVT 没有 Stage01 记录；已将 Revit 项目信息、经纬度、X（南北）、Y（东西）、高程和真北作为新表单初值；单位保持工作流目标 m / m² / °，当前 RVT 单位仅用于差异对账。" );
          break;

        case NativeStage01StorageState.Current:
          model = storageDecision.Payload.Model.Clone();
          drifts = NativeStage01FieldAuthorityPolicy.Compare(
            model,
            liveEvidence);
          messages.Add("已读取当前 RVT 的 Stage01 0.9.1 初始化记录。" );
          AddDriftMessage(drifts, messages);
          break;

        case NativeStage01StorageState.MigratableLegacy:
          NativeStage01MigrationResult migration =
            NativeStage01MigrationService.Migrate(
              storageDecision.Payload,
              catalog,
              NativeStage01Canonicalizer.PayloadSchemaVersion);
          if (migration.Success)
          {
            model = migration.Model;
            requiresMigrationConfirmation = true;
            messages.AddRange(migration.Messages);
            messages.Add(
              "已生成 0.9.1 内存迁移候选；等待用户确认迁移，读取动作未改写原 Storage。" );
            drifts = NativeStage01FieldAuthorityPolicy.Compare(
              model,
              liveEvidence);
            AddDriftMessage(drifts, messages);
          }
          else
          {
            model = storageDecision.Payload?.Model?.Clone()
              ?? CreateBlockedModel(catalog);
            migrationFailed = true;
            messages.AddRange(migration.Messages);
            messages.Add("Stage01 旧版记录无法生成安全迁移候选，已阻断写入。" );
          }
          break;

        case NativeStage01StorageState.Corrupt:
        case NativeStage01StorageState.UnsupportedFuture:
        default:
          model = storageDecision.Payload?.Model?.Clone()
            ?? CreateBlockedModel(catalog);
          messages.Add(storageDecision.Message);
          break;
      }

      NativeStage01ValidationResult validation =
        NativeStage01Validator.Validate(model, catalog);
      NativeWorkflowResultEnvelope workflowResult = ReadWorkflowResult(
        document,
        "STAGE01",
        messages);
      NativeWorkflowResultEnvelope stage02BResult = ReadWorkflowResult(
        document,
        "STAGE02B",
        messages);
      FilterCurrentWorkflowResults(
        application,
        catalog,
        model,
        stored,
        storageDecision,
        messages,
        ref workflowResult,
        ref stage02BResult);
      bool environmentReady = string.Equals(
          application.Application.VersionNumber,
          "2020",
          StringComparison.Ordinal)
        && !document.IsFamilyDocument
        && !document.IsReadOnly
        && !string.IsNullOrWhiteSpace(document.PathName);
      bool storageUsable = !migrationFailed
        && storageDecision.State != NativeStage01StorageState.Corrupt
        && storageDecision.State
          != NativeStage01StorageState.UnsupportedFuture;
      string status;
      if (!environmentReady)
        status = "当前文档环境不可写";
      else if (!storageUsable)
        status = "Stage01 存储阻断";
      else if (requiresMigrationConfirmation)
        status = "Stage01 等待迁移确认";
      else
        status = "Stage01 已读取";
      return new NativeStage01ReadResult
      {
        Success = environmentReady && storageUsable,
        Status = status,
        Model = model,
        StorageDecision = storageDecision,
        Validation = validation,
        LiveEvidence = liveEvidence,
        Drifts = drifts,
        RequiresMigrationConfirmation = requiresMigrationConfirmation,
        SourcePayloadVersion = sourcePayloadVersion,
        WorkflowResult = workflowResult,
        Stage02BResult = stage02BResult,
        Messages = new ReadOnlyCollection<string>(messages)
      };
    }

    private static void AddDriftMessage(
      IReadOnlyList<NativeStage01Drift> drifts,
      ICollection<string> messages)
    {
      if (drifts == null || drifts.Count == 0)
      {
        messages.Add("Stage01 上次确认的 Revit 原生字段与当前 RVT 一致。" );
        return;
      }
      messages.Add(
        "检测到 "
        + drifts.Count
        + " 项 Revit 现场值变化；仅显示 drift，未静默覆盖 Stage01 Payload。" );
    }

    private static NativeStage01LiveEvidence CaptureLiveEvidence(
      Document document,
      ICollection<string> messages)
    {
      var evidence = new NativeStage01LiveEvidence();
      ProjectInfo information = document.ProjectInformation;
      if (information != null)
      {
        evidence.ProjectInformationAvailable = true;
        evidence.ProjectNumber = information.Number ?? string.Empty;
        evidence.ProjectName = information.Name ?? string.Empty;
      }
      try
      {
        ProjectPosition position =
          document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
        evidence.BaseX = Format(UnitUtils.ConvertFromInternalUnits(
          position.NorthSouth,
          DisplayUnitType.DUT_METERS));
        evidence.BaseY = Format(UnitUtils.ConvertFromInternalUnits(
          position.EastWest,
          DisplayUnitType.DUT_METERS));
        evidence.BaseElevation = Format(UnitUtils.ConvertFromInternalUnits(
          position.Elevation,
          DisplayUnitType.DUT_METERS));
        evidence.TrueNorthAngle = Format(position.Angle * 180.0 / Math.PI);
        evidence.ProjectPositionAvailable = true;
        messages.Add("已读取 X（南北）、Y（东西）、高程和真北现场值。" );
      }
      catch (Exception exception)
      {
        messages.Add("读取项目位置现场证据失败：" + exception.Message);
      }
      try
      {
        SiteLocation site = document.SiteLocation;
        evidence.Longitude = NativeStage01GeoLocationPolicy.FormatDegrees(
          site.Longitude);
        evidence.Latitude = NativeStage01GeoLocationPolicy.FormatDegrees(
          site.Latitude);
        evidence.GeoLocationAvailable = true;
        messages.Add("已读取 Revit SiteLocation 经纬度现场值。" );
      }
      catch (Exception exception)
      {
        messages.Add("读取 SiteLocation 经纬度现场证据失败：" + exception.Message);
      }
      try
      {
        Units units = document.GetUnits();
        evidence.LengthUnit = DescribeUnit(
          units.GetFormatOptions(UnitType.UT_Length).DisplayUnits,
          DisplayUnitType.DUT_METERS,
          "m");
        evidence.AreaUnit = DescribeUnit(
          units.GetFormatOptions(UnitType.UT_Area).DisplayUnits,
          DisplayUnitType.DUT_SQUARE_METERS,
          "m²");
        evidence.AngleUnit = DescribeUnit(
          units.GetFormatOptions(UnitType.UT_Angle).DisplayUnits,
          DisplayUnitType.DUT_DECIMAL_DEGREES,
          "°");
        evidence.UnitsAvailable = true;
      }
      catch (Exception exception)
      {
        messages.Add("读取项目单位现场证据失败：" + exception.Message);
      }
      return evidence;
    }

    private static NativeWorkflowResultEnvelope ReadWorkflowResult(
      Document document,
      string sourceFeature,
      ICollection<string> messages)
    {
      try
      {
        return NativeWorkflowResultStorage.Read(document, sourceFeature);
      }
      catch (Exception exception)
      {
        messages.Add(
          "读取 " + sourceFeature + " workflow result 失败：" + exception.Message);
        return null;
      }
    }

    private static void FilterCurrentWorkflowResults(
      UIApplication application,
      NativeRuleCatalog catalog,
      NativeStage01Model model,
      NativeStoredInitialization stored,
      NativeStage01StorageDecision storageDecision,
      ICollection<string> messages,
      ref NativeWorkflowResultEnvelope stage01Result,
      ref NativeWorkflowResultEnvelope stage02BResult)
    {
      if (storageDecision?.State != NativeStage01StorageState.Current
        || stored == null
        || string.IsNullOrWhiteSpace(stored.FileGuid)
        || string.IsNullOrWhiteSpace(storageDecision.ActualPayloadHash)
        || string.IsNullOrWhiteSpace(
          model.GetValue(NativeStage01Keys.ModelFileType)))
      {
        stage01Result = null;
        stage02BResult = null;
        return;
      }
      NativeWorkflowIdentity identity;
      try
      {
        identity = NativeWorkflowIdentityFactory.Create(
          application,
          model.GetValue(NativeStage01Keys.ModelFileType),
          stored.FileGuid,
          storageDecision.ActualPayloadHash,
          catalog.Identity);
      }
      catch (Exception exception)
      {
        messages.Add("无法建立当前 Stage01 workflow identity：" + exception.Message);
        stage01Result = null;
        stage02BResult = null;
        return;
      }
      stage01Result = KeepCurrent(
        stage01Result,
        identity,
        storageDecision.ActualPayloadHash,
        "STAGE01",
        messages);
      stage02BResult = KeepCurrent(
        stage02BResult,
        identity,
        stage02BResult?.InputSnapshotHash,
        "STAGE02B",
        messages);
    }

    private static NativeWorkflowResultEnvelope KeepCurrent(
      NativeWorkflowResultEnvelope result,
      NativeWorkflowIdentity identity,
      string currentInputHash,
      string sourceFeature,
      ICollection<string> messages)
    {
      if (result == null) return null;
      NativeWorkflowFreshnessDecision decision =
        NativeWorkflowFreshnessPolicy.Evaluate(
          result,
          identity,
          currentInputHash ?? string.Empty);
      if (decision.State == NativeWorkflowFreshnessState.Current) return result;
      messages.Add(
        sourceFeature + " workflow result 已拒绝作为当前值：" + decision.Code);
      return null;
    }

    private static NativeStage01Model CreateBlockedModel(
      NativeRuleCatalog catalog)
    {
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));
      return new NativeStage01Model
      {
        ActiveGroup = catalog.DefaultActiveGroup
      };
    }

    private static string DescribeUnit(
      DisplayUnitType actual,
      DisplayUnitType canonical,
      string canonicalText)
    {
      return actual == canonical ? canonicalText : actual.ToString();
    }

    private static string Format(double value)
    {
      return value.ToString("G17", CultureInfo.InvariantCulture);
    }
  }
}
