using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01ReadResult
  {
    internal bool Success { get; set; }
    internal string Status { get; set; } = string.Empty;
    internal NativeStage01Model Model { get; set; }
    internal NativeStage01StorageDecision StorageDecision { get; set; }
    internal NativeStage01ValidationResult Validation { get; set; }
    internal IReadOnlyList<string> Messages { get; set; } =
      Array.Empty<string>();
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

      NativeStage01Model model = storageDecision.Payload?.Model?.Clone()
        ?? catalog.CreateDefaultStage01Model();
      if (storageDecision.State == NativeStage01StorageState.Current)
        messages.Add("已读取当前 RVT 的 Stage01 初始化记录。" );
      else if (storageDecision.State
        == NativeStage01StorageState.MigratableLegacy)
        messages.Add("已读取旧版 Stage01 记录；再次提交时自动升级。" );
      else if (storageDecision.State == NativeStage01StorageState.Corrupt
        || storageDecision.State
          == NativeStage01StorageState.UnsupportedFuture)
        messages.Add(storageDecision.Message);

      NativeStage01ConditionSchemaReconciliation conditionSchema =
        NativeStage01ConditionSchemaPolicy.Reconcile(model, catalog);
      if (conditionSchema.Changed)
      {
        messages.Add(
          "已补齐当前规则库新增的项目条件键："
          + string.Join("、", conditionSchema.AddedConditionIds)
          + "；新增键仅设为未勾选，未替用户选择或声明项目条件。" );
      }

      PopulateMissingDocumentValues(document, model, messages);
      NativeStage01ValidationResult validation =
        NativeStage01Validator.Validate(model, catalog);
      bool environmentReady = string.Equals(
          application.Application.VersionNumber,
          "2020",
          StringComparison.Ordinal)
        && !document.IsFamilyDocument
        && !document.IsReadOnly
        && !string.IsNullOrWhiteSpace(document.PathName);
      bool storageUsable = storageDecision.State
        != NativeStage01StorageState.Corrupt
        && storageDecision.State
          != NativeStage01StorageState.UnsupportedFuture;
      return new NativeStage01ReadResult
      {
        Success = environmentReady && storageUsable,
        Status = !environmentReady
          ? "当前文档环境不可写"
          : storageUsable ? "Stage01 已读取" : "Stage01 存储阻断",
        Model = model,
        StorageDecision = storageDecision,
        Validation = validation,
        Messages = messages
      };
    }

    private static void PopulateMissingDocumentValues(
      Document document,
      NativeStage01Model model,
      ICollection<string> messages)
    {
      ProjectInfo information = document.ProjectInformation;
      if (information != null)
      {
        SetIfBlank(
          model,
          NativeStage01Keys.ProjectNumber,
          information.Number);
        SetIfBlank(
          model,
          NativeStage01Keys.ProjectName,
          information.Name);
      }
      try
      {
        ProjectPosition position =
          document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
        SetIfBlank(
          model,
          NativeStage01Keys.BaseX,
          Format(UnitUtils.ConvertFromInternalUnits(
            position.NorthSouth,
            DisplayUnitType.DUT_METERS)));
        SetIfBlank(
          model,
          NativeStage01Keys.BaseY,
          Format(UnitUtils.ConvertFromInternalUnits(
            position.EastWest,
            DisplayUnitType.DUT_METERS)));
        SetIfBlank(
          model,
          NativeStage01Keys.BaseElevation,
          Format(UnitUtils.ConvertFromInternalUnits(
            position.Elevation,
            DisplayUnitType.DUT_METERS)));
        SetIfBlank(
          model,
          NativeStage01Keys.TrueNorthAngle,
          Format(position.Angle * 180.0 / Math.PI));
        messages.Add("已读取 X（南北）、Y（东西）、高程和真北。" );
      }
      catch (Exception exception)
      {
        messages.Add("读取项目位置失败：" + exception.Message);
      }
      SetIfBlank(model, NativeStage01Keys.LengthUnit, "m");
      SetIfBlank(model, NativeStage01Keys.AreaUnit, "m²");
      SetIfBlank(model, NativeStage01Keys.AngleUnit, "°");
      if (string.IsNullOrWhiteSpace(model.GetValue(NativeStage01Keys.FileGuid)))
        model.SetValue(
          NativeStage01Keys.FileGuid,
          Guid.NewGuid().ToString("D"));
      if (string.IsNullOrWhiteSpace(
        model.GetValue(NativeStage01Keys.WorkflowVersion)))
      {
        model.SetValue(
          NativeStage01Keys.WorkflowVersion,
          NativeStage01Canonicalizer.PayloadSchemaVersion);
      }
    }

    private static void SetIfBlank(
      NativeStage01Model model,
      string key,
      string value)
    {
      if (string.IsNullOrWhiteSpace(model.GetValue(key)))
        model.SetValue(key, value ?? string.Empty);
    }

    private static string Format(double value)
    {
      return value.ToString("G17", CultureInfo.InvariantCulture);
    }
  }
}
