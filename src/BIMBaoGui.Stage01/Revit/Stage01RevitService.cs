using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Diagnostics;
using BIMBaoGui.Stage01.Infrastructure;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class Stage01RevitService
  {
    private const double CoordinateToleranceMeters = 0.0001;
    private const double AngleToleranceDegrees = 0.0001;
    private const string CorruptStorageMessage =
      "检测到损坏或不完整的 Stage01 初始化存储记录。为防止覆盖现有数据，已阻止读取和写入；请先修复 Revit DataStorage。";

    public static RevitDocumentSnapshot ReadSnapshot(Stage01Model model)
    {
      if (RevitHost.RunReadInHostContext(
        () => ReadSnapshotCore(model),
        out RevitDocumentSnapshot snapshot,
        out string error))
        return snapshot;

      return new RevitDocumentSnapshot
      {
        Status = error,
        Messages = new[] { error }
      };
    }

    private static RevitDocumentSnapshot ReadSnapshotCore(Stage01Model model)
    {
      var snapshot = new RevitDocumentSnapshot();
      var messages = new List<string>();
      if (!RevitHost.TryGetContext(
        out UIApplication uiapp,
        out _,
        out Document document,
        out string hostError))
      {
        snapshot.Status = hostError;
        snapshot.Messages = new[] { hostError };
        return snapshot;
      }

      snapshot.HostAvailable = true;
      snapshot.RevitVersion = uiapp.Application.VersionNumber ?? string.Empty;
      snapshot.IsRevit2020 = string.Equals(
        snapshot.RevitVersion,
        "2020",
        StringComparison.Ordinal);
      snapshot.IsProjectDocument = !document.IsFamilyDocument;
      snapshot.IsSaved = !string.IsNullOrWhiteSpace(document.PathName);
      snapshot.IsReadOnly = document.IsReadOnly;
      snapshot.DocumentTitle = document.Title ?? string.Empty;
      snapshot.DocumentPath = document.PathName ?? string.Empty;
      snapshot.BlockingElements = BlankFileGate.FindBlockingElements(document);
      snapshot.IsBlank = snapshot.BlockingElements.Count == 0;

      StoredInitialization stored = Stage01Storage.Read(document);
      Stage01StorageDecision storageDecision = EvaluateStorage(stored);
      snapshot.StorageDecision = storageDecision;
      snapshot.IsInitialized = storageDecision.IsInitialized;
      snapshot.StoredPayloadHash = stored?.PayloadHash ?? string.Empty;
      snapshot.StoredPayloadJson = stored?.PayloadJson ?? string.Empty;
      snapshot.StoredWorkflowVersion = stored?.WorkflowVersion ?? string.Empty;
      snapshot.RequiresWorkflowMigration = storageDecision.RequiresWorkflowMigration;
      string currentHash = CanonicalPayload.Sha256(CanonicalPayload.Build(model));
      snapshot.PayloadMatches = snapshot.IsInitialized
        && !snapshot.RequiresWorkflowMigration
        && string.Equals(
          currentHash,
          snapshot.StoredPayloadHash,
          StringComparison.OrdinalIgnoreCase);

      if (!snapshot.IsRevit2020)
        messages.Add(
          "当前 Revit 版本为 " + snapshot.RevitVersion + "，本组件仅支持 Revit 2020。");
      if (!snapshot.IsProjectDocument)
        messages.Add("当前文档是族文件，不支持初始化。");
      if (!snapshot.IsSaved)
        messages.Add("请先保存当前 RVT 文件。");
      if (snapshot.IsReadOnly)
        messages.Add("当前文档为只读状态。");
      if (storageDecision.State == Stage01StorageState.CorruptInitialization)
        messages.Add(CorruptStorageMessage);
      if (!snapshot.IsBlank && storageDecision.RequiresBlankModelGate)
        messages.Add(
          "当前文件已存在正式建模内容或外部链接，不符合“尚未开始正式建模”的初始化条件。");

      if (storageDecision.State == Stage01StorageState.CorruptInitialization)
        snapshot.Status = "初始化存储损坏";
      else if (snapshot.RequiresWorkflowMigration)
        snapshot.Status = "旧版初始化待升级";
      else if (snapshot.IsInitialized)
        snapshot.Status = snapshot.PayloadMatches
          ? "初始化通过"
          : "已修改待重新提交";
      else if (messages.Count > 0)
        snapshot.Status = "环境检查未通过";
      else
        snapshot.Status = "待填写并提交";
      snapshot.Messages = messages;
      return snapshot;
    }

    public static IReadOnlyList<string> PopulateModelFromDocument(Stage01Model model)
    {
      if (RevitHost.RunReadInHostContext(
        () => PopulateModelFromDocumentCore(model),
        out IReadOnlyList<string> messages,
        out string error))
        return messages;
      return new[] { error };
    }

    private static IReadOnlyList<string> PopulateModelFromDocumentCore(Stage01Model model)
    {
      var messages = new List<string>();
      if (!RevitHost.TryGetContext(
        out UIApplication uiapp,
        out _,
        out Document document,
        out string hostError))
        return new[] { hostError };

      StoredInitialization stored = Stage01Storage.Read(document);
      Stage01StorageDecision storageDecision = EvaluateStorage(stored);
      if (storageDecision.State == Stage01StorageState.CorruptInitialization)
        return new[] { CorruptStorageMessage };
      if (storageDecision.IsInitialized)
      {
        if (Stage01PayloadCodec.TryApply(stored.PayloadJson, model, out string payloadError))
        {
          messages.Add("已读取当前 Revit 文件中的初始化记录。");
          if (storageDecision.RequiresWorkflowMigration)
            messages.Add(
              "检测到旧版初始化 "
              + (stored.WorkflowVersion ?? string.Empty)
              + "；再次提交将自动升级，无需启用“允许重新初始化”。" );
        }
        else
          messages.Add(payloadError);
      }

      if (string.IsNullOrWhiteSpace(model.GetValue(Stage01Keys.ProjectNumber)))
        model.SetValue(
          Stage01Keys.ProjectNumber,
          document.ProjectInformation?.Number ?? string.Empty);
      if (string.IsNullOrWhiteSpace(model.GetValue(Stage01Keys.ProjectName)))
        model.SetValue(
          Stage01Keys.ProjectName,
          document.ProjectInformation?.Name ?? string.Empty);

      try
      {
        ProjectPosition position = document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
        model.SetValue(
          Stage01Keys.BaseX,
          Format(UnitUtils.ConvertFromInternalUnits(
            position.NorthSouth,
            DisplayUnitType.DUT_METERS)));
        model.SetValue(
          Stage01Keys.BaseY,
          Format(UnitUtils.ConvertFromInternalUnits(
            position.EastWest,
            DisplayUnitType.DUT_METERS)));
        model.SetValue(
          Stage01Keys.BaseElevation,
          Format(UnitUtils.ConvertFromInternalUnits(
            position.Elevation,
            DisplayUnitType.DUT_METERS)));
        model.SetValue(
          Stage01Keys.TrueNorthAngle,
          Format(position.Angle * 180.0 / Math.PI));
        messages.Add("已读取基点坐标 X（南北）、Y（东西）、高程和真北角度。");
      }
      catch (Exception exception)
      {
        messages.Add("读取项目位置失败：" + exception.Message);
      }

      model.SetValue(Stage01Keys.LengthUnit, "m");
      model.SetValue(Stage01Keys.AreaUnit, "m²");
      model.SetValue(Stage01Keys.AngUnit, "°");
      model.SetValue(Stage01Keys.WorkflowVersion, HBRContextVersions.FileContextSchema);
      if (string.IsNullOrWhiteSpace(model.GetValue(Stage01Keys.FileGuid)))
        model.SetValue(
          Stage01Keys.FileGuid,
          stored?.FileGuid ?? Guid.NewGuid().ToString("D"));
      messages.Add(
        "当前宿主：Revit " + uiapp.Application.VersionNumber + " / " + document.Title);
      return messages;
    }

    public static bool EnqueueCommit(
      Stage01Model model,
      Action<CommitResult> completed,
      out string error)
    {
      Stage01Model snapshotModel = model.Clone();
      return RevitHost.EnqueueAction(uiapp =>
      {
        CommitResult result;
        try
        {
          result = Commit(uiapp, snapshotModel);
        }
        catch (Exception exception)
        {
          result = new CommitResult
          {
            Success = false,
            Status = "初始化失败",
            Messages = new[] { exception.Message }
          };
        }
        completed?.Invoke(result);
      }, out error);
    }

    private static CommitResult Commit(UIApplication uiapp, Stage01Model model)
    {
      Document document = uiapp.ActiveUIDocument?.Document;
      if (document == null)
        return Failure("Revit 当前没有活动项目文档。");
      if (uiapp.Application.VersionNumber != "2020")
        return Failure("本组件仅支持 Revit 2020。当前版本：" + uiapp.Application.VersionNumber);
      if (document.IsFamilyDocument)
        return Failure("族文件不能执行报规文件初始化。");
      if (document.IsReadOnly)
        return Failure("当前文档为只读状态。");
      if (string.IsNullOrWhiteSpace(document.PathName))
        return Failure("请先保存当前 RVT 文件。");

      string operationStage = "VALIDATION";
      StoredInitialization existing = Stage01Storage.Read(document);
      Stage01StorageDecision storageDecision = EvaluateStorage(existing);
      if (storageDecision.State == Stage01StorageState.CorruptInitialization)
        return Failure(CorruptStorageMessage);
      ValidationResult validation = Stage01Validator.Validate(
        model,
        Stage01RegistryProvider.Instance.Fields,
        storageDecision.ValidationMode);
      if (!validation.IsValid)
        return Failure(
          validation.Messages
            .Where(item => item.Severity == ValidationSeverity.Error)
            .Select(item => item.Message)
            .ToArray());

      bool requiresMigration = storageDecision.RequiresWorkflowMigration;
      if (storageDecision.RequiresReinitializePermission && !model.AllowReinitialize)
        return Failure("当前文件已经初始化。如确需覆盖，请先启用“允许重新初始化”。");

      IReadOnlyList<string> blockers = BlankFileGate.FindBlockingElements(document);
      if (blockers.Count > 0 && storageDecision.RequiresBlankModelGate)
        return Failure(new[] { "实质模型门禁未通过。" }.Concat(blockers).ToArray());

      string fileGuid = model.GetValue(Stage01Keys.FileGuid);
      if (string.IsNullOrWhiteSpace(fileGuid))
      {
        fileGuid = Guid.NewGuid().ToString("D");
        model.SetValue(Stage01Keys.FileGuid, fileGuid);
      }
      model.SetValue(Stage01Keys.WorkflowVersion, HBRContextVersions.FileContextSchema);
      string payloadJson = CanonicalPayload.Build(model);
      string payloadHash = CanonicalPayload.Sha256(payloadJson);
      var commitMessages = new List<string>();
      if (requiresMigration)
      {
        commitMessages.Add(
          "旧版初始化将从 "
          + (existing.WorkflowVersion ?? string.Empty)
          + " 自动升级到 "
          + HBRContextVersions.FileContextSchema
          + "，无需启用“允许重新初始化”。" );
      }

      using (var group = new TransactionGroup(document, "湖北BIM报规｜文件初始化"))
      {
        if (group.Start() != TransactionStatus.Started)
          return Failure("无法启动 Revit 事务组。");
        try
        {
          using (var transaction = new Transaction(
            document,
            "写入文件初始化与官方插件源参数"))
          {
            if (transaction.Start() != TransactionStatus.Started)
              throw new InvalidOperationException("无法启动 Revit 事务。");

            operationStage = "APPLY_UNITS";
            ApplyUnits(document);
            operationStage = "PROJECT_POSITION";
            ApplyProjectPosition(document, model);
            operationStage = "PROJECT_INFORMATION";
            ApplyProjectInformation(document, model);
            operationStage = "INTERNAL_STORAGE";
            Stage01Storage.Write(document, new StoredInitialization
            {
              PayloadJson = payloadJson,
              PayloadHash = payloadHash,
              FileGuid = fileGuid,
              WorkflowVersion = model.GetValue(Stage01Keys.WorkflowVersion),
              InitializedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            });
            operationStage = "OFFICIAL_PROJECTION";
            commitMessages.AddRange(
              Stage01OfficialHifcProjectionService.WriteAndVerify(
                document,
                payloadJson));

            operationStage = "TRANSACTION_COMMIT";
            document.Regenerate();
            if (transaction.Commit() != TransactionStatus.Committed)
              throw new InvalidOperationException("Revit 事务未成功提交。");
          }

          operationStage = "READBACK_VERIFICATION";
          IReadOnlyList<string> readbackErrors = VerifyReadback(
            document,
            model,
            payloadHash);
          if (readbackErrors.Count > 0)
          {
            group.RollBack();
            return Failure(
              new[] { "写入后回读验证失败，已整体回滚。" }
                .Concat(readbackErrors)
                .ToArray());
          }

          if (group.Assimilate() != TransactionStatus.Committed)
            return Failure("事务组未能合并为一次可撤销操作。");

          bool hasOfficialProtocolBlocker = commitMessages.Any(message =>
            message.StartsWith("BLOCK_", StringComparison.Ordinal));
          var resultMessages = new List<string>
          {
            "文件初始化、内部唯一参数、官方精确源参数写入与 Revit 回读均已完成。",
            "必须使用官方 H-IFC 插件重新导出 IFC；旧 IFC 不会自动更新。"
          };
          resultMessages.AddRange(commitMessages);

          string status;
          if (hasOfficialProtocolBlocker)
            status = requiresMigration
              ? "初始化升级完成｜官方导出协议存在阻断"
              : "初始化完成｜官方导出协议存在阻断";
          else
            status = requiresMigration
              ? "旧版初始化已升级｜待官方重新导出验收"
              : "初始化完成｜待官方重新导出验收";

          return new CommitResult
          {
            Success = true,
            Status = status,
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            Messages = resultMessages
          };
        }
        catch (Exception exception)
        {
          bool transactionRolledBack = false;
          try
          {
            transactionRolledBack = group.RollBack() == TransactionStatus.RolledBack;
          }
          catch
          {
          }

          var pluginAssembly = typeof(Stage01RevitService).Assembly;
          DateTimeOffset occurredUtc = DateTimeOffset.UtcNow;
          Stage01FailureReportWriteResult reportResult =
            Stage01FailureReportWriter.TryWrite(new Stage01FailureReportContext
            {
              AssemblyPath = pluginAssembly.Location,
              PluginName = pluginAssembly.GetName().Name,
              PluginVersion = pluginAssembly.GetName().Version?.ToString() ?? string.Empty,
              RevitVersionNumber = uiapp.Application.VersionNumber ?? string.Empty,
              RevitVersionName = uiapp.Application.VersionName ?? string.Empty,
              RevitBuild = uiapp.Application.VersionBuild ?? string.Empty,
              ProcessArchitecture = Environment.Is64BitProcess ? "x64" : "x86",
              DocumentTitle = document.Title ?? string.Empty,
              DocumentPath = document.PathName ?? string.Empty,
              DocumentIsReadOnly = document.IsReadOnly,
              DocumentIsFamilyDocument = document.IsFamilyDocument,
              DocumentIsWorkshared = document.IsWorkshared,
              OperationStage = operationStage,
              TransactionRolledBack = transactionRolledBack,
              Exception = exception,
              OccurredUtc = occurredUtc,
              OccurredLocal = DateTimeOffset.Now
            });

          string rollbackSummary = transactionRolledBack
            ? "事务已回滚"
            : "事务回滚状态未确认";
          if (reportResult.Success)
          {
            return Failure(
              "DIAG_STAGE01_COMMIT_FAILED：初始化失败，"
              + rollbackSummary
              + "；异常类型="
              + exception.GetType().FullName
              + "；错误报告="
              + reportResult.ReportPath);
          }

          return Failure(
            "DIAG_STAGE01_COMMIT_FAILED：REPORT_WRITE_FAILED；初始化失败，"
            + rollbackSummary
            + "；原始异常="
            + reportResult.OriginalExceptionSummary
            + "；报告写入异常="
            + reportResult.ReportWriteErrorSummary);
        }
      }
    }

    private static Stage01StorageDecision EvaluateStorage(StoredInitialization stored)
    {
      return Stage01StorageStatePolicy.Evaluate(
        stored != null,
        stored?.PayloadJson,
        stored?.PayloadHash,
        stored?.FileGuid,
        stored?.WorkflowVersion,
        HBRContextVersions.FileContextSchema);
    }

    private static void ApplyUnits(Document document)
    {
      Units units = document.GetUnits();
      SetDisplayUnits(units, UnitType.UT_Length, DisplayUnitType.DUT_METERS);
      SetDisplayUnits(units, UnitType.UT_Area, DisplayUnitType.DUT_SQUARE_METERS);
      SetDisplayUnits(units, UnitType.UT_Angle, DisplayUnitType.DUT_DECIMAL_DEGREES);
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

    private static void ApplyProjectPosition(Document document, Stage01Model model)
    {
      double northMeters = ParseRequiredNumber(model, Stage01Keys.BaseX);
      double eastMeters = ParseRequiredNumber(model, Stage01Keys.BaseY);
      double elevationMeters = ParseRequiredNumber(model, Stage01Keys.BaseElevation);
      double angleDegrees = ParseRequiredNumber(model, Stage01Keys.TrueNorthAngle);
      double northFeet = UnitUtils.ConvertToInternalUnits(
        northMeters,
        DisplayUnitType.DUT_METERS);
      double eastFeet = UnitUtils.ConvertToInternalUnits(
        eastMeters,
        DisplayUnitType.DUT_METERS);
      double elevationFeet = UnitUtils.ConvertToInternalUnits(
        elevationMeters,
        DisplayUnitType.DUT_METERS);
      double angleRadians = angleDegrees * Math.PI / 180.0;
      var position = new ProjectPosition(
        eastFeet,
        northFeet,
        elevationFeet,
        angleRadians);
      document.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, position);
    }

    private static void ApplyProjectInformation(Document document, Stage01Model model)
    {
      ProjectInfo information = document.ProjectInformation;
      information.Number = model.GetValue(Stage01Keys.ProjectNumber);
      information.Name = model.GetValue(Stage01Keys.ProjectName);
    }

    private static IReadOnlyList<string> VerifyReadback(
      Document document,
      Stage01Model model,
      string payloadHash)
    {
      var errors = new List<string>();
      Units units = document.GetUnits();
      if (units.GetFormatOptions(UnitType.UT_Length).DisplayUnits
        != DisplayUnitType.DUT_METERS)
        errors.Add("长度单位未回读为 m。");
      if (units.GetFormatOptions(UnitType.UT_Area).DisplayUnits
        != DisplayUnitType.DUT_SQUARE_METERS)
        errors.Add("面积单位未回读为 m²。");
      if (units.GetFormatOptions(UnitType.UT_Angle).DisplayUnits
        != DisplayUnitType.DUT_DECIMAL_DEGREES)
        errors.Add("角度单位未回读为 °。");

      ProjectPosition position = document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
      double north = UnitUtils.ConvertFromInternalUnits(
        position.NorthSouth,
        DisplayUnitType.DUT_METERS);
      double east = UnitUtils.ConvertFromInternalUnits(
        position.EastWest,
        DisplayUnitType.DUT_METERS);
      double elevation = UnitUtils.ConvertFromInternalUnits(
        position.Elevation,
        DisplayUnitType.DUT_METERS);
      double angle = position.Angle * 180.0 / Math.PI;
      CompareNumber(
        errors,
        "基点坐标 X（南北）",
        ParseRequiredNumber(model, Stage01Keys.BaseX),
        north,
        CoordinateToleranceMeters);
      CompareNumber(
        errors,
        "基点坐标 Y（东西）",
        ParseRequiredNumber(model, Stage01Keys.BaseY),
        east,
        CoordinateToleranceMeters);
      CompareNumber(
        errors,
        "基点高程",
        ParseRequiredNumber(model, Stage01Keys.BaseElevation),
        elevation,
        CoordinateToleranceMeters);
      CompareNumber(
        errors,
        "真北角度",
        ParseRequiredNumber(model, Stage01Keys.TrueNorthAngle),
        angle,
        AngleToleranceDegrees);

      if (!string.Equals(
        document.ProjectInformation.Number,
        model.GetValue(Stage01Keys.ProjectNumber),
        StringComparison.Ordinal))
        errors.Add("项目编号回读不一致。");
      if (!string.Equals(
        document.ProjectInformation.Name,
        model.GetValue(Stage01Keys.ProjectName),
        StringComparison.Ordinal))
        errors.Add("项目名称回读不一致。");

      StoredInitialization stored = Stage01Storage.Read(document);
      Stage01StorageDecision storageDecision = EvaluateStorage(stored);
      if (!storageDecision.IsInitialized)
        errors.Add("初始化记录未写入 Revit DataStorage。");
      else
      {
        if (!string.Equals(
          stored.PayloadHash,
          payloadHash,
          StringComparison.OrdinalIgnoreCase))
          errors.Add("初始化载荷哈希回读不一致。");
        if (!string.Equals(
          stored.WorkflowVersion,
          HBRContextVersions.FileContextSchema,
          StringComparison.Ordinal))
          errors.Add("初始化工作流版本未升级到当前版本。");
      }
      return errors;
    }

    private static double ParseRequiredNumber(Stage01Model model, string key)
    {
      if (!Stage01Validator.TryDouble(model.GetValue(key), out double value))
        throw new InvalidOperationException("数值字段无效：" + key);
      return value;
    }

    private static void CompareNumber(
      ICollection<string> errors,
      string label,
      double expected,
      double actual,
      double tolerance)
    {
      if (Math.Abs(expected - actual) > tolerance)
        errors.Add(
          label
          + "回读不一致。预期="
          + expected.ToString("G17", CultureInfo.InvariantCulture)
          + "，实际="
          + actual.ToString("G17", CultureInfo.InvariantCulture));
    }

    private static string Format(double value)
    {
      return value.ToString("0.###############", CultureInfo.InvariantCulture);
    }

    private static CommitResult Failure(params string[] messages)
    {
      return new CommitResult
      {
        Success = false,
        Status = "初始化失败",
        Messages = messages ?? Array.Empty<string>()
      };
    }
  }
}
