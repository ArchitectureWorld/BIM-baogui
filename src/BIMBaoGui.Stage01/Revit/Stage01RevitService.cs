using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Infrastructure;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class Stage01RevitService
  {
    private const double CoordinateToleranceMeters = 0.0001;
    private const double AngleToleranceDegrees = 0.0001;

    public static RevitDocumentSnapshot ReadSnapshot(Stage01Model model)
    {
      if (RevitHost.RunReadInHostContext(() => ReadSnapshotCore(model), out RevitDocumentSnapshot snapshot, out string error))
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
      if (!RevitHost.TryGetContext(out UIApplication uiapp, out _, out Document document, out string hostError))
      {
        snapshot.Status = hostError;
        snapshot.Messages = new[] { hostError };
        return snapshot;
      }

      snapshot.HostAvailable = true;
      snapshot.RevitVersion = uiapp.Application.VersionNumber ?? string.Empty;
      snapshot.IsRevit2020 = string.Equals(snapshot.RevitVersion, "2020", StringComparison.Ordinal);
      snapshot.IsProjectDocument = !document.IsFamilyDocument;
      snapshot.IsSaved = !string.IsNullOrWhiteSpace(document.PathName);
      snapshot.IsReadOnly = document.IsReadOnly;
      snapshot.DocumentTitle = document.Title ?? string.Empty;
      snapshot.DocumentPath = document.PathName ?? string.Empty;
      snapshot.BlockingElements = BlankFileGate.FindBlockingElements(document);
      snapshot.IsBlank = snapshot.BlockingElements.Count == 0;

      StoredInitialization stored = Stage01Storage.Read(document);
      snapshot.IsInitialized = stored != null && !string.IsNullOrWhiteSpace(stored.PayloadHash);
      snapshot.StoredPayloadHash = stored?.PayloadHash ?? string.Empty;
      snapshot.StoredPayloadJson = stored?.PayloadJson ?? string.Empty;
      string currentHash = CanonicalPayload.Sha256(CanonicalPayload.Build(model));
      snapshot.PayloadMatches = snapshot.IsInitialized && string.Equals(currentHash, snapshot.StoredPayloadHash, StringComparison.OrdinalIgnoreCase);

      if (!snapshot.IsRevit2020) messages.Add("当前 Revit 版本为 " + snapshot.RevitVersion + "，本组件仅支持 Revit 2020。");
      if (!snapshot.IsProjectDocument) messages.Add("当前文档是族文件，不支持初始化。");
      if (!snapshot.IsSaved) messages.Add("请先保存当前 RVT 文件。");
      if (snapshot.IsReadOnly) messages.Add("当前文档为只读状态。");
      if (!snapshot.IsBlank && !snapshot.IsInitialized) messages.Add("当前文件已存在正式建模内容或外部链接，不符合“尚未开始正式建模”的初始化条件。");

      if (snapshot.IsInitialized)
        snapshot.Status = snapshot.PayloadMatches ? "初始化通过" : "已修改待重新提交";
      else if (messages.Count > 0)
        snapshot.Status = "环境检查未通过";
      else
        snapshot.Status = "待填写并提交";
      snapshot.Messages = messages;
      return snapshot;
    }

    public static IReadOnlyList<string> PopulateModelFromDocument(Stage01Model model)
    {
      if (RevitHost.RunReadInHostContext(() => PopulateModelFromDocumentCore(model), out IReadOnlyList<string> messages, out string error))
        return messages;
      return new[] { error };
    }

    private static IReadOnlyList<string> PopulateModelFromDocumentCore(Stage01Model model)
    {
      var messages = new List<string>();
      if (!RevitHost.TryGetContext(out UIApplication uiapp, out _, out Document document, out string hostError))
        return new[] { hostError };

      StoredInitialization stored = Stage01Storage.Read(document);
      if (stored != null && !string.IsNullOrWhiteSpace(stored.PayloadJson))
      {
        if (Stage01PayloadCodec.TryApply(stored.PayloadJson, model, out string payloadError))
          messages.Add("已读取当前 Revit 文件中的初始化记录。");
        else
          messages.Add(payloadError);
      }

      if (string.IsNullOrWhiteSpace(model.GetValue(Stage01Keys.ProjectNumber)))
        model.SetValue(Stage01Keys.ProjectNumber, document.ProjectInformation?.Number ?? string.Empty);
      if (string.IsNullOrWhiteSpace(model.GetValue(Stage01Keys.ProjectName)))
        model.SetValue(Stage01Keys.ProjectName, document.ProjectInformation?.Name ?? string.Empty);

      try
      {
        ProjectPosition position = document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
        model.SetValue(Stage01Keys.BaseX, Format(UnitUtils.ConvertFromInternalUnits(position.EastWest, DisplayUnitType.DUT_METERS)));
        model.SetValue(Stage01Keys.BaseY, Format(UnitUtils.ConvertFromInternalUnits(position.NorthSouth, DisplayUnitType.DUT_METERS)));
        model.SetValue(Stage01Keys.BaseElevation, Format(UnitUtils.ConvertFromInternalUnits(position.Elevation, DisplayUnitType.DUT_METERS)));
        model.SetValue(Stage01Keys.TrueNorthAngle, Format(position.Angle * 180.0 / Math.PI));
        messages.Add("已读取当前项目位置、坐标、高程和真北角度。");
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
        model.SetValue(Stage01Keys.FileGuid, stored?.FileGuid ?? Guid.NewGuid().ToString("D"));
      messages.Add("当前宿主：Revit " + uiapp.Application.VersionNumber + " / " + document.Title);
      return messages;
    }

    public static bool EnqueueCommit(Stage01Model model, Action<CommitResult> completed, out string error)
    {
      Stage01Model snapshotModel = model.Clone();
      return RevitHost.EnqueueAction(uiapp =>
      {
        CommitResult result;
        try { result = Commit(uiapp, snapshotModel); }
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

      ValidationResult validation = Stage01Validator.Validate(model, Stage01RegistryProvider.Instance.Fields);
      if (!validation.IsValid)
        return Failure(validation.Messages.Where(x => x.Severity == ValidationSeverity.Error).Select(x => x.Message).ToArray());

      StoredInitialization existing = Stage01Storage.Read(document);
      if (existing != null && !model.AllowReinitialize)
        return Failure("当前文件已经初始化。如确需覆盖，请先启用“允许重新初始化”。");

      IReadOnlyList<string> blockers = BlankFileGate.FindBlockingElements(document);
      if (blockers.Count > 0 && existing == null)
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

      using (var group = new TransactionGroup(document, "湖北BIM报规｜文件初始化"))
      {
        if (group.Start() != TransactionStatus.Started)
          return Failure("无法启动 Revit 事务组。");
        try
        {
          using (var transaction = new Transaction(document, "写入文件初始化配置"))
          {
            if (transaction.Start() != TransactionStatus.Started)
              throw new InvalidOperationException("无法启动 Revit 事务。");
            ApplyUnits(document);
            ApplyProjectPosition(document, model);
            ApplyProjectInformation(document, model);
            Stage01Storage.Write(document, new StoredInitialization
            {
              PayloadJson = payloadJson,
              PayloadHash = payloadHash,
              FileGuid = fileGuid,
              WorkflowVersion = model.GetValue(Stage01Keys.WorkflowVersion),
              InitializedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            });
            document.Regenerate();
            if (transaction.Commit() != TransactionStatus.Committed)
              throw new InvalidOperationException("Revit 事务未成功提交。");
          }

          IReadOnlyList<string> readbackErrors = VerifyReadback(document, model, payloadHash);
          if (readbackErrors.Count > 0)
          {
            group.RollBack();
            return Failure(new[] { "写入后回读验证失败，已整体回滚。" }.Concat(readbackErrors).ToArray());
          }

          if (group.Assimilate() != TransactionStatus.Committed)
            return Failure("事务组未能合并为一次可撤销操作。");
          return new CommitResult
          {
            Success = true,
            Status = "初始化通过",
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            Messages = new[] { "文件初始化写入与回读验证均通过。" }
          };
        }
        catch (Exception exception)
        {
          try { group.RollBack(); } catch { }
          return Failure("初始化失败，事务已回滚：" + exception.Message);
        }
      }
    }

    private static void ApplyUnits(Document document)
    {
      Units units = document.GetUnits();
      SetDisplayUnits(units, UnitType.UT_Length, DisplayUnitType.DUT_METERS);
      SetDisplayUnits(units, UnitType.UT_Area, DisplayUnitType.DUT_SQUARE_METERS);
      SetDisplayUnits(units, UnitType.UT_Angle, DisplayUnitType.DUT_DECIMAL_DEGREES);
      document.SetUnits(units);
    }

    private static void SetDisplayUnits(Units units, UnitType unitType, DisplayUnitType displayUnitType)
    {
      FormatOptions options = units.GetFormatOptions(unitType);
      options.DisplayUnits = displayUnitType;
      units.SetFormatOptions(unitType, options);
    }

    private static void ApplyProjectPosition(Document document, Stage01Model model)
    {
      double eastMeters = ParseRequiredNumber(model, Stage01Keys.BaseX);
      double northMeters = ParseRequiredNumber(model, Stage01Keys.BaseY);
      double elevationMeters = ParseRequiredNumber(model, Stage01Keys.BaseElevation);
      double angleDegrees = ParseRequiredNumber(model, Stage01Keys.TrueNorthAngle);
      double eastFeet = UnitUtils.ConvertToInternalUnits(eastMeters, DisplayUnitType.DUT_METERS);
      double northFeet = UnitUtils.ConvertToInternalUnits(northMeters, DisplayUnitType.DUT_METERS);
      double elevationFeet = UnitUtils.ConvertToInternalUnits(elevationMeters, DisplayUnitType.DUT_METERS);
      double angleRadians = angleDegrees * Math.PI / 180.0;
      var position = new ProjectPosition(eastFeet, northFeet, elevationFeet, angleRadians);
      document.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, position);
    }

    private static void ApplyProjectInformation(Document document, Stage01Model model)
    {
      ProjectInfo information = document.ProjectInformation;
      information.Number = model.GetValue(Stage01Keys.ProjectNumber);
      information.Name = model.GetValue(Stage01Keys.ProjectName);
    }

    private static IReadOnlyList<string> VerifyReadback(Document document, Stage01Model model, string payloadHash)
    {
      var errors = new List<string>();
      Units units = document.GetUnits();
      if (units.GetFormatOptions(UnitType.UT_Length).DisplayUnits != DisplayUnitType.DUT_METERS)
        errors.Add("长度单位未回读为 m。");
      if (units.GetFormatOptions(UnitType.UT_Area).DisplayUnits != DisplayUnitType.DUT_SQUARE_METERS)
        errors.Add("面积单位未回读为 m²。");
      if (units.GetFormatOptions(UnitType.UT_Angle).DisplayUnits != DisplayUnitType.DUT_DECIMAL_DEGREES)
        errors.Add("角度单位未回读为 °。");

      ProjectPosition position = document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
      double east = UnitUtils.ConvertFromInternalUnits(position.EastWest, DisplayUnitType.DUT_METERS);
      double north = UnitUtils.ConvertFromInternalUnits(position.NorthSouth, DisplayUnitType.DUT_METERS);
      double elevation = UnitUtils.ConvertFromInternalUnits(position.Elevation, DisplayUnitType.DUT_METERS);
      double angle = position.Angle * 180.0 / Math.PI;
      CompareNumber(errors, "基点坐标 X", ParseRequiredNumber(model, Stage01Keys.BaseX), east, CoordinateToleranceMeters);
      CompareNumber(errors, "基点坐标 Y", ParseRequiredNumber(model, Stage01Keys.BaseY), north, CoordinateToleranceMeters);
      CompareNumber(errors, "基点高程", ParseRequiredNumber(model, Stage01Keys.BaseElevation), elevation, CoordinateToleranceMeters);
      CompareNumber(errors, "真北角度", ParseRequiredNumber(model, Stage01Keys.TrueNorthAngle), angle, AngleToleranceDegrees);

      if (!string.Equals(document.ProjectInformation.Number, model.GetValue(Stage01Keys.ProjectNumber), StringComparison.Ordinal))
        errors.Add("项目编号回读不一致。");
      if (!string.Equals(document.ProjectInformation.Name, model.GetValue(Stage01Keys.ProjectName), StringComparison.Ordinal))
        errors.Add("项目名称回读不一致。");

      StoredInitialization stored = Stage01Storage.Read(document);
      if (stored == null)
        errors.Add("初始化记录未写入 Revit DataStorage。");
      else if (!string.Equals(stored.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
        errors.Add("初始化载荷哈希回读不一致。");
      return errors;
    }

    private static double ParseRequiredNumber(Stage01Model model, string key)
    {
      if (!Stage01Validator.TryDouble(model.GetValue(key), out double value))
        throw new InvalidOperationException("数值字段无效：" + key);
      return value;
    }

    private static void CompareNumber(ICollection<string> errors, string label, double expected, double actual, double tolerance)
    {
      if (Math.Abs(expected - actual) > tolerance)
        errors.Add(label + "回读不一致。预期=" + expected.ToString("G17", CultureInfo.InvariantCulture)
          + "，实际=" + actual.ToString("G17", CultureInfo.InvariantCulture));
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
