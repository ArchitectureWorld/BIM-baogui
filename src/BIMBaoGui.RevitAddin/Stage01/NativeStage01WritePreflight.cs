using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal static class NativeStage01PreflightCodes
  {
    internal const string NoActiveDocument = "NO_ACTIVE_DOCUMENT";
    internal const string UnsupportedRevit = "UNSUPPORTED_REVIT";
    internal const string FamilyDocument = "FAMILY_DOCUMENT";
    internal const string UnsavedDocument = "UNSAVED_DOCUMENT";
    internal const string ReadOnlyDocument = "READ_ONLY_DOCUMENT";
    internal const string ModelValidationFailed = "MODEL_VALIDATION_FAILED";
    internal const string CorruptStorage = "CORRUPT_STORAGE";
    internal const string UnsupportedFutureStorage =
      "UNSUPPORTED_FUTURE_STORAGE";
    internal const string BlankConfirmationRequired =
      "BLANK_CONFIRMATION_REQUIRED";
    internal const string ModelNotBlank = "MODEL_NOT_BLANK";
    internal const string ReinitializePermissionRequired =
      "REINITIALIZE_PERMISSION_REQUIRED";
  }

  internal sealed class NativeStage01DocumentState
  {
    internal bool HasDocument { get; set; }
    internal string RevitVersion { get; set; } = string.Empty;
    internal bool IsProjectDocument { get; set; }
    internal bool IsSaved { get; set; }
    internal bool IsReadOnly { get; set; }
    internal IReadOnlyList<string> BlockingElements { get; set; } =
      Array.Empty<string>();
    internal NativeStage01StorageDecision StorageDecision { get; set; }
  }

  internal sealed class NativeStage01PreflightBlocker
  {
    internal string Code { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal sealed class NativeStage01PreflightDecision
  {
    internal NativeStage01PreflightDecision(
      IEnumerable<NativeStage01PreflightBlocker> blockers)
    {
      Blockers = new ReadOnlyCollection<NativeStage01PreflightBlocker>(
        (blockers ?? Array.Empty<NativeStage01PreflightBlocker>()).ToArray());
    }

    internal IReadOnlyList<NativeStage01PreflightBlocker> Blockers { get; }
    internal bool Accepted => Blockers.Count == 0;
  }

  internal static class NativeStage01WritePreflight
  {
    internal static NativeStage01PreflightDecision Evaluate(
      NativeStage01DocumentState state,
      NativeStage01ValidationResult validation,
      bool confirmBlankProject,
      bool allowReinitialize)
    {
      var blockers = new List<NativeStage01PreflightBlocker>();
      if (state == null || !state.HasDocument)
      {
        Add(
          blockers,
          NativeStage01PreflightCodes.NoActiveDocument,
          "Revit 当前没有活动项目文档。" );
        return new NativeStage01PreflightDecision(blockers);
      }
      if (!string.Equals(
        state.RevitVersion,
        "2020",
        StringComparison.Ordinal))
      {
        Add(
          blockers,
          NativeStage01PreflightCodes.UnsupportedRevit,
          "当前原生插件仅支持 Revit 2020。" );
      }
      if (!state.IsProjectDocument)
      {
        Add(
          blockers,
          NativeStage01PreflightCodes.FamilyDocument,
          "族文档不能执行报规文件初始化。" );
      }
      if (!state.IsSaved)
      {
        Add(
          blockers,
          NativeStage01PreflightCodes.UnsavedDocument,
          "请先保存当前 RVT 文件。" );
      }
      if (state.IsReadOnly)
      {
        Add(
          blockers,
          NativeStage01PreflightCodes.ReadOnlyDocument,
          "当前 RVT 为只读状态。" );
      }
      if (validation == null || !validation.IsValid)
      {
        string details = validation == null
          ? "Stage01 领域校验未返回结果。"
          : string.Join(
            " ",
            validation.Messages.Select(value =>
              value.FieldKey + "：" + value.Message));
        Add(
          blockers,
          NativeStage01PreflightCodes.ModelValidationFailed,
          details);
      }

      NativeStage01StorageDecision storage = state.StorageDecision;
      if (storage == null)
      {
        Add(
          blockers,
          NativeStage01PreflightCodes.CorruptStorage,
          "Stage01 存储状态不可用。" );
      }
      else
      {
        switch (storage.State)
        {
          case NativeStage01StorageState.Corrupt:
            Add(
              blockers,
              NativeStage01PreflightCodes.CorruptStorage,
              string.IsNullOrWhiteSpace(storage.Message)
                ? "Stage01 初始化存储损坏。"
                : storage.Message);
            break;
          case NativeStage01StorageState.UnsupportedFuture:
            Add(
              blockers,
              NativeStage01PreflightCodes.UnsupportedFutureStorage,
              string.IsNullOrWhiteSpace(storage.Message)
                ? "RVT 中的 Stage01 数据来自未来版本。"
                : storage.Message);
            break;
          case NativeStage01StorageState.NoRecord:
            if (!confirmBlankProject)
            {
              Add(
                blockers,
                NativeStage01PreflightCodes.BlankConfirmationRequired,
                "首次初始化前必须明确确认当前文件尚未开始正式建模。" );
            }
            IReadOnlyList<string> elements = state.BlockingElements
              ?? Array.Empty<string>();
            if (elements.Count > 0)
            {
              Add(
                blockers,
                NativeStage01PreflightCodes.ModelNotBlank,
                "当前文件包含正式模型内容：" + string.Join("；", elements));
            }
            break;
          case NativeStage01StorageState.Current:
            if (!allowReinitialize)
            {
              Add(
                blockers,
                NativeStage01PreflightCodes.ReinitializePermissionRequired,
                "当前 RVT 已初始化；覆盖前必须明确启用允许重新初始化。" );
            }
            break;
          case NativeStage01StorageState.MigratableLegacy:
            break;
        }
      }

      return new NativeStage01PreflightDecision(blockers);
    }

    private static void Add(
      ICollection<NativeStage01PreflightBlocker> blockers,
      string code,
      string message)
    {
      blockers.Add(new NativeStage01PreflightBlocker
      {
        Code = code ?? string.Empty,
        Message = message ?? string.Empty
      });
    }
  }

  internal sealed class NativeProjectPositionPlan
  {
    private NativeProjectPositionPlan(
      double northSouthMeters,
      double eastWestMeters,
      double elevationMeters,
      double angleRadians)
    {
      NorthSouthMeters = northSouthMeters;
      EastWestMeters = eastWestMeters;
      ElevationMeters = elevationMeters;
      AngleRadians = angleRadians;
    }

    internal double NorthSouthMeters { get; }
    internal double EastWestMeters { get; }
    internal double ElevationMeters { get; }
    internal double AngleRadians { get; }

    internal static NativeProjectPositionPlan Create(
      double xNorthingMeters,
      double yEastingMeters,
      double elevationMeters,
      double trueNorthDegrees)
    {
      ValidateFinite(xNorthingMeters, nameof(xNorthingMeters));
      ValidateFinite(yEastingMeters, nameof(yEastingMeters));
      ValidateFinite(elevationMeters, nameof(elevationMeters));
      ValidateFinite(trueNorthDegrees, nameof(trueNorthDegrees));
      if (trueNorthDegrees < -180.0 || trueNorthDegrees > 180.0)
        throw new ArgumentOutOfRangeException(
          nameof(trueNorthDegrees),
          "真北角度必须位于 -180° 到 180°。" );
      return new NativeProjectPositionPlan(
        xNorthingMeters,
        yEastingMeters,
        elevationMeters,
        trueNorthDegrees * Math.PI / 180.0);
    }

    private static void ValidateFinite(double value, string name)
    {
      if (double.IsNaN(value) || double.IsInfinity(value))
        throw new ArgumentOutOfRangeException(name, "数值必须为有限数。" );
    }
  }
}
