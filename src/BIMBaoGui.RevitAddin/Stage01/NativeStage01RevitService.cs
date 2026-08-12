using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01WriteRequest
  {
    internal NativeStage01Model Model { get; set; }
    internal bool ConfirmBlankProject { get; set; }
    internal bool AllowReinitialize { get; set; }
  }

  internal sealed class NativeStage01WriteResult
  {
    internal bool Success { get; set; }
    internal string Status { get; set; } = string.Empty;
    internal string PayloadJson { get; set; } = string.Empty;
    internal string PayloadHash { get; set; } = string.Empty;
    internal string FailureReportPath { get; set; } = string.Empty;
    internal IReadOnlyList<NativeStage01PreflightBlocker> Blockers { get; set; }
      = Array.Empty<NativeStage01PreflightBlocker>();
    internal IReadOnlyList<string> Messages { get; set; } =
      Array.Empty<string>();
  }

  internal static class NativeStage01RevitService
  {
    private const double CoordinateToleranceMeters = 0.0001;
    private const double AngleToleranceDegrees = 0.0001;

    internal static NativeStage01WriteResult Execute(
      UIApplication uiApplication,
      NativeStage01WriteRequest request)
    {
      if (uiApplication == null)
        throw new ArgumentNullException(nameof(uiApplication));
      if (request == null) throw new ArgumentNullException(nameof(request));
      if (request.Model == null)
        throw new ArgumentException("Stage01 写入请求缺少模型。", nameof(request));

      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01Model model = request.Model.Clone();
      string operationStage = "CAPTURE_DOCUMENT";
      Document document = uiApplication.ActiveUIDocument?.Document;
      if (document == null)
      {
        return new NativeStage01WriteResult
        {
          Success = false,
          Status = "初始化阻断",
          Blockers = new[]
          {
            new NativeStage01PreflightBlocker
            {
              Code = NativeStage01PreflightCodes.NoActiveDocument,
              Message = "Revit 当前没有活动项目文档。"
            }
          }
        };
      }

      NativeStoredInitialization stored = null;
      NativeStage01StorageDecision storageDecision = null;
      try
      {
        operationStage = "READ_STORAGE";
        stored = NativeStage01Storage.Read(document);
        storageDecision = NativeStage01StoragePolicy.Evaluate(
          stored,
          NativeStage01Canonicalizer.PayloadSchemaVersion);
      }
      catch (Exception exception)
      {
        storageDecision = new NativeStage01StorageDecision
        {
          State = NativeStage01StorageState.Corrupt,
          ErrorCode = NativeStage01StorageCodes.CorruptStorage,
          Message = exception.Message
        };
      }

      operationStage = "DOMAIN_VALIDATION";
      NativeStage01ValidationResult validation =
        NativeStage01Validator.Validate(model, catalog);
      var documentState = new NativeStage01DocumentState
      {
        HasDocument = true,
        RevitVersion = uiApplication.Application.VersionNumber ?? string.Empty,
        IsProjectDocument = !document.IsFamilyDocument,
        IsSaved = !string.IsNullOrWhiteSpace(document.PathName),
        IsReadOnly = document.IsReadOnly,
        StorageDecision = storageDecision,
        BlockingElements = Array.Empty<string>()
      };
      NativeStage01PreflightDecision preflight =
        NativeStage01WritePreflight.Evaluate(
          documentState,
          validation,
          request.ConfirmBlankProject,
          request.AllowReinitialize);
      if (!preflight.Accepted)
      {
        return new NativeStage01WriteResult
        {
          Success = false,
          Status = "初始化阻断",
          Blockers = preflight.Blockers,
          Messages = preflight.Blockers.Select(value => value.Message).ToArray()
        };
      }

      model.SetValue(
        NativeStage01Keys.WorkflowVersion,
        NativeStage01Canonicalizer.PayloadSchemaVersion);
      string fileGuid = model.GetValue(NativeStage01Keys.FileGuid);
      if (string.IsNullOrWhiteSpace(fileGuid))
      {
        fileGuid = stored != null && !string.IsNullOrWhiteSpace(stored.FileGuid)
          ? stored.FileGuid
          : Guid.NewGuid().ToString("D");
        model.SetValue(NativeStage01Keys.FileGuid, fileGuid);
      }
      string payloadJson = NativeStage01Canonicalizer.ToJson(model);
      string payloadHash = NativeStage01Canonicalizer.Sha256(payloadJson);
      var messages = new List<string>();
      bool transactionRolledBack = false;

      using (var group = new TransactionGroup(
        document,
        "湖北BIM报规｜原生文件初始化"))
      {
        try
        {
          operationStage = "TRANSACTION_GROUP_START";
          if (group.Start() != TransactionStatus.Started)
            throw new InvalidOperationException("无法启动 Stage01 事务组。" );

          using (var transaction = new Transaction(
            document,
            "写入原生 Stage01 文件初始化"))
          {
            operationStage = "TRANSACTION_START";
            if (transaction.Start() != TransactionStatus.Started)
              throw new InvalidOperationException("无法启动 Stage01 事务。" );

            operationStage = "APPLY_UNITS";
            ApplyUnits(document);
            operationStage = "PROJECT_POSITION";
            ApplyProjectPosition(document, model);
            operationStage = "PROJECT_INFORMATION";
            ApplyProjectInformation(document, model);
            operationStage = "INTERNAL_STORAGE";
            NativeStage01Storage.Write(document, new NativeStoredInitialization
            {
              HasRecord = true,
              PayloadJson = payloadJson,
              PayloadHash = payloadHash,
              FileGuid = fileGuid,
              WorkflowVersion =
                NativeStage01Canonicalizer.PayloadSchemaVersion,
              InitializedUtc = DateTimeOffset.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture)
            });
            operationStage = "PARAMETER_PROJECTION";
            messages.AddRange(
              NativeStage01ParameterProjectionService.WriteAndVerify(
                document,
                model,
                catalog));
            operationStage = "REGENERATE";
            document.Regenerate();
            operationStage = "TRANSACTION_COMMIT";
            if (transaction.Commit() != TransactionStatus.Committed)
              throw new InvalidOperationException("Stage01 事务未成功提交。" );
          }

          operationStage = "READBACK_VERIFICATION";
          IReadOnlyList<string> readbackErrors = VerifyReadback(
            document,
            model,
            payloadJson,
            payloadHash,
            catalog);
          if (readbackErrors.Count > 0)
          {
            transactionRolledBack =
              group.RollBack() == TransactionStatus.RolledBack;
            return new NativeStage01WriteResult
            {
              Success = false,
              Status = "初始化失败｜回读不一致",
              PayloadJson = payloadJson,
              PayloadHash = payloadHash,
              Messages = new[]
              {
                transactionRolledBack
                  ? "写入后回读不一致，事务组已整体回滚。"
                  : "写入后回读不一致，事务组回滚状态未确认。"
              }.Concat(readbackErrors).ToArray()
            };
          }

          operationStage = "TRANSACTION_GROUP_ASSIMILATE";
          if (group.Assimilate() != TransactionStatus.Committed)
            throw new InvalidOperationException(
              "Stage01 事务组未能合并为一次 Revit Undo。" );
          messages.Insert(
            0,
            storageDecision.RequiresMigration
              ? "旧版 Stage01 初始化已升级并完成回读。"
              : "原生 Stage01 初始化、参数投影和回读均已完成。" );
          return new NativeStage01WriteResult
          {
            Success = true,
            Status = "初始化通过",
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            Messages = new ReadOnlyCollection<string>(messages)
          };
        }
        catch (Exception exception)
        {
          try
          {
            transactionRolledBack =
              group.RollBack() == TransactionStatus.RolledBack;
          }
          catch
          {
          }

          Assembly assembly = typeof(NativeStage01RevitService).Assembly;
          NativeStage01FailureReportResult report =
            NativeStage01FailureReportWriter.TryWrite(
              new NativeStage01FailureReportContext
              {
                ProductVersion =
                  assembly.GetName().Version?.ToString() ?? string.Empty,
                RevitVersion =
                  uiApplication.Application.VersionNumber ?? string.Empty,
                DocumentTitle = document.Title ?? string.Empty,
                DocumentPath = document.PathName ?? string.Empty,
                FileGuid = fileGuid,
                PayloadHash = payloadHash,
                RulePackageId = catalog.Identity.PackageId,
                RulePackageVersion = catalog.Identity.PackageVersion,
                RulePackageSha256 = catalog.Identity.RulePackageSha256,
                OperationStage = operationStage,
                TransactionRolledBack = transactionRolledBack,
                Exception = exception,
                OccurredUtc = DateTimeOffset.UtcNow
              });
          string reportMessage = report.Success
            ? "失败报告：" + report.ReportPath
            : "失败报告写入失败：" + report.Error;
          return new NativeStage01WriteResult
          {
            Success = false,
            Status = "初始化技术失败",
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            FailureReportPath = report.ReportPath,
            Messages = new[]
            {
              operationStage + "：" + exception.Message,
              transactionRolledBack
                ? "事务组已回滚。"
                : "事务组回滚状态未确认。",
              reportMessage
            }
          };
        }
      }
    }

    private static void ApplyUnits(Document document)
    {
      Units units = document.GetUnits();
      SetDisplayUnits(units, UnitType.UT_Length, DisplayUnitType.DUT_METERS);
      SetDisplayUnits(
        units,
        UnitType.UT_Area,
        DisplayUnitType.DUT_SQUARE_METERS);
      SetDisplayUnits(
        units,
        UnitType.UT_Angle,
        DisplayUnitType.DUT_DECIMAL_DEGREES);
      document.SetUnits(units);
    }

    private static void SetDisplayUnits(
      Units units,
      UnitType unitType,
      DisplayUnitType displayUnitType)
    {
      FormatOptions options = units.GetFormatOptions(unitType);
      options.DisplayUnits = displayUnitType;
      units.SetFormatOptions(unitType, options);
    }

    private static void ApplyProjectPosition(
      Document document,
      NativeStage01Model model)
    {
      NativeProjectPositionPlan plan = NativeProjectPositionPlan.Create(
        ParseNumber(model, NativeStage01Keys.BaseX),
        ParseNumber(model, NativeStage01Keys.BaseY),
        ParseNumber(model, NativeStage01Keys.BaseElevation),
        ParseNumber(model, NativeStage01Keys.TrueNorthAngle));
      double northSouthInternal = UnitUtils.ConvertToInternalUnits(
        plan.NorthSouthMeters,
        DisplayUnitType.DUT_METERS);
      double eastWestInternal = UnitUtils.ConvertToInternalUnits(
        plan.EastWestMeters,
        DisplayUnitType.DUT_METERS);
      double elevationInternal = UnitUtils.ConvertToInternalUnits(
        plan.ElevationMeters,
        DisplayUnitType.DUT_METERS);
      var position = new ProjectPosition(
        eastWestInternal,
        northSouthInternal,
        elevationInternal,
        plan.AngleRadians);
      document.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, position);
    }

    private static void ApplyProjectInformation(
      Document document,
      NativeStage01Model model)
    {
      ProjectInfo information = document.ProjectInformation
        ?? throw new InvalidOperationException("当前文档缺少 ProjectInformation。" );
      information.Number = model.GetValue(NativeStage01Keys.ProjectNumber);
      information.Name = model.GetValue(NativeStage01Keys.ProjectName);
    }

    private static IReadOnlyList<string> VerifyReadback(
      Document document,
      NativeStage01Model model,
      string expectedPayload,
      string expectedHash,
      NativeRuleCatalog catalog)
    {
      var errors = new List<string>();
      Units units = document.GetUnits();
      if (units.GetFormatOptions(UnitType.UT_Length).DisplayUnits
        != DisplayUnitType.DUT_METERS)
        errors.Add("长度单位未回读为 m。" );
      if (units.GetFormatOptions(UnitType.UT_Area).DisplayUnits
        != DisplayUnitType.DUT_SQUARE_METERS)
        errors.Add("面积单位未回读为 m²。" );
      if (units.GetFormatOptions(UnitType.UT_Angle).DisplayUnits
        != DisplayUnitType.DUT_DECIMAL_DEGREES)
        errors.Add("角度单位未回读为 °。" );

      NativeProjectPositionPlan plan = NativeProjectPositionPlan.Create(
        ParseNumber(model, NativeStage01Keys.BaseX),
        ParseNumber(model, NativeStage01Keys.BaseY),
        ParseNumber(model, NativeStage01Keys.BaseElevation),
        ParseNumber(model, NativeStage01Keys.TrueNorthAngle));
      ProjectPosition position =
        document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
      Compare(
        errors,
        "基点坐标 X（南北）",
        plan.NorthSouthMeters,
        UnitUtils.ConvertFromInternalUnits(
          position.NorthSouth,
          DisplayUnitType.DUT_METERS),
        CoordinateToleranceMeters);
      Compare(
        errors,
        "基点坐标 Y（东西）",
        plan.EastWestMeters,
        UnitUtils.ConvertFromInternalUnits(
          position.EastWest,
          DisplayUnitType.DUT_METERS),
        CoordinateToleranceMeters);
      Compare(
        errors,
        "基点高程",
        plan.ElevationMeters,
        UnitUtils.ConvertFromInternalUnits(
          position.Elevation,
          DisplayUnitType.DUT_METERS),
        CoordinateToleranceMeters);
      Compare(
        errors,
        "真北角度",
        plan.AngleRadians * 180.0 / Math.PI,
        position.Angle * 180.0 / Math.PI,
        AngleToleranceDegrees);

      ProjectInfo information = document.ProjectInformation;
      if (information == null)
      {
        errors.Add("当前文档缺少 ProjectInformation。" );
      }
      else
      {
        if (!string.Equals(
          information.Number,
          model.GetValue(NativeStage01Keys.ProjectNumber),
          StringComparison.Ordinal))
          errors.Add("项目编号回读不一致。" );
        if (!string.Equals(
          information.Name,
          model.GetValue(NativeStage01Keys.ProjectName),
          StringComparison.Ordinal))
          errors.Add("项目名称回读不一致。" );
      }

      NativeStoredInitialization stored = NativeStage01Storage.Read(document);
      NativeStage01StorageDecision storageDecision =
        NativeStage01StoragePolicy.Evaluate(
          stored,
          NativeStage01Canonicalizer.PayloadSchemaVersion);
      if (storageDecision.State != NativeStage01StorageState.Current)
      {
        errors.Add("Stage01 Extensible Storage 回读未达到当前有效状态。" );
      }
      else
      {
        if (!string.Equals(
          stored.PayloadJson,
          expectedPayload,
          StringComparison.Ordinal))
          errors.Add("Stage01 Payload JSON 回读不一致。" );
        if (!string.Equals(
          stored.PayloadHash,
          expectedHash,
          StringComparison.OrdinalIgnoreCase))
          errors.Add("Stage01 Payload SHA-256 回读不一致。" );
      }
      errors.AddRange(
        NativeStage01ParameterProjectionService.Verify(
          document,
          model,
          catalog));
      return errors;
    }

    private static double ParseNumber(
      NativeStage01Model model,
      string key)
    {
      string value = model.GetValue(key);
      if (!double.TryParse(
        value,
        NumberStyles.Float | NumberStyles.AllowThousands,
        CultureInfo.InvariantCulture,
        out double number))
      {
        throw new InvalidOperationException("Stage01 数值字段无效：" + key);
      }
      return number;
    }

    private static void Compare(
      ICollection<string> errors,
      string label,
      double expected,
      double actual,
      double tolerance)
    {
      if (Math.Abs(expected - actual) <= tolerance) return;
      errors.Add(
        label
        + "回读不一致。预期="
        + expected.ToString("G17", CultureInfo.InvariantCulture)
        + "，实际="
        + actual.ToString("G17", CultureInfo.InvariantCulture));
    }
  }
}
