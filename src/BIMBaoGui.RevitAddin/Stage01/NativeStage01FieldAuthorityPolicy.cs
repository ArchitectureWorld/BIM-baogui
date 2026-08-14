using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01Drift
  {
    internal NativeStage01Drift(
      string fieldKey,
      string label,
      string storedValue,
      string liveValue,
      string authoritySource)
    {
      FieldKey = fieldKey ?? string.Empty;
      Label = label ?? string.Empty;
      StoredValue = storedValue ?? string.Empty;
      LiveValue = liveValue ?? string.Empty;
      AuthoritySource = authoritySource ?? string.Empty;
      StoredAuthority = NativeStage01FieldAuthorityPolicy.PayloadConfirmedAuthority;
      Code = NativeStage01FieldAuthorityPolicy.LiveValueChangedCode;
      Message = Label
        + "的当前 RVT 值与上次确认值不一致；未自动覆盖任一侧。";
    }

    internal string Code { get; }
    internal string FieldKey { get; }
    internal string Label { get; }
    internal string StoredValue { get; }
    internal string LiveValue { get; }
    internal string StoredAuthority { get; }
    internal string AuthoritySource { get; }
    internal string Message { get; }
    internal bool IsDifferent => true;
  }

  internal static class NativeStage01FieldAuthorityPolicy
  {
    internal const string RevitLiveAuthority = "REVIT_LIVE";
    internal const string PayloadConfirmedAuthority = "PAYLOAD_CONFIRMED";
    internal const string LiveValueChangedCode = "LIVE_VALUE_CHANGED";
    private const double CoordinateTolerance = 0.0001;
    private const double AngleTolerance = 0.0001;

    private sealed class Contract
    {
      internal string FieldKey { get; set; }
      internal string Label { get; set; }
      internal string AuthoritySource { get; set; }
      internal bool ApplyAsInitial { get; set; }
      internal Func<NativeStage01LiveEvidence, bool> IsAvailable { get; set; }
      internal Func<NativeStage01LiveEvidence, string> ReadLive { get; set; }
      internal double? NumericTolerance { get; set; }
    }

    private static readonly Contract[] Contracts =
    {
      new Contract
      {
        FieldKey = NativeStage01Keys.ProjectName,
        ApplyAsInitial = true,
        Label = "项目名称",
        AuthoritySource = "REVIT_PROJECT_INFORMATION",
        IsAvailable = value => value.ProjectInformationAvailable,
        ReadLive = value => value.ProjectName
      },
      new Contract
      {
        FieldKey = NativeStage01Keys.ProjectNumber,
        ApplyAsInitial = true,
        Label = "项目编号",
        AuthoritySource = "REVIT_PROJECT_INFORMATION",
        IsAvailable = value => value.ProjectInformationAvailable,
        ReadLive = value => value.ProjectNumber
      },
      new Contract
      {
        FieldKey = NativeStage01Keys.BaseX,
        ApplyAsInitial = true,
        Label = "基点坐标 X（南北）",
        AuthoritySource = "REVIT_PROJECT_POSITION",
        IsAvailable = value => value.ProjectPositionAvailable,
        ReadLive = value => value.BaseX,
        NumericTolerance = CoordinateTolerance
      },
      new Contract
      {
        FieldKey = NativeStage01Keys.BaseY,
        ApplyAsInitial = true,
        Label = "基点坐标 Y（东西）",
        AuthoritySource = "REVIT_PROJECT_POSITION",
        IsAvailable = value => value.ProjectPositionAvailable,
        ReadLive = value => value.BaseY,
        NumericTolerance = CoordinateTolerance
      },
      new Contract
      {
        FieldKey = NativeStage01Keys.BaseElevation,
        ApplyAsInitial = true,
        Label = "基点高程",
        AuthoritySource = "REVIT_PROJECT_POSITION",
        IsAvailable = value => value.ProjectPositionAvailable,
        ReadLive = value => value.BaseElevation,
        NumericTolerance = CoordinateTolerance
      },
      new Contract
      {
        FieldKey = NativeStage01Keys.TrueNorthAngle,
        ApplyAsInitial = true,
        Label = "真北角度",
        AuthoritySource = "REVIT_PROJECT_POSITION",
        IsAvailable = value => value.ProjectPositionAvailable,
        ReadLive = value => value.TrueNorthAngle,
        NumericTolerance = AngleTolerance
      },
      new Contract
      {
        FieldKey = NativeStage01Keys.LengthUnit,
        ApplyAsInitial = false,
        Label = "长度单位",
        AuthoritySource = "REVIT_UNITS",
        IsAvailable = value => value.UnitsAvailable,
        ReadLive = value => value.LengthUnit
      },
      new Contract
      {
        FieldKey = NativeStage01Keys.AreaUnit,
        ApplyAsInitial = false,
        Label = "面积单位",
        AuthoritySource = "REVIT_UNITS",
        IsAvailable = value => value.UnitsAvailable,
        ReadLive = value => value.AreaUnit
      },
      new Contract
      {
        FieldKey = NativeStage01Keys.AngleUnit,
        ApplyAsInitial = false,
        Label = "角度单位",
        AuthoritySource = "REVIT_UNITS",
        IsAvailable = value => value.UnitsAvailable,
        ReadLive = value => value.AngleUnit
      }
    };

    internal static void ApplyInitialValues(
      NativeStage01Model model,
      NativeStage01LiveEvidence evidence)
    {
      if (model == null) throw new ArgumentNullException(nameof(model));
      if (evidence == null) throw new ArgumentNullException(nameof(evidence));
      foreach (Contract contract in Contracts.Where(value =>
        value.ApplyAsInitial && value.IsAvailable(evidence)))
      {
        model.SetValue(
          contract.FieldKey,
          contract.ReadLive(evidence) ?? string.Empty);
      }
    }

    internal static IReadOnlyList<NativeStage01Drift> Compare(
      NativeStage01Model model,
      NativeStage01LiveEvidence evidence)
    {
      if (model == null) throw new ArgumentNullException(nameof(model));
      if (evidence == null) throw new ArgumentNullException(nameof(evidence));
      NativeStage01Drift[] drifts = Contracts
        .Where(contract => contract.IsAvailable(evidence))
        .Select(contract => new
        {
          Contract = contract,
          Stored = model.GetValue(contract.FieldKey),
          Live = contract.ReadLive(evidence) ?? string.Empty
        })
        .Where(value => !AreEquivalent(
          value.Stored,
          value.Live,
          value.Contract.NumericTolerance))
        .Select(value => new NativeStage01Drift(
          value.Contract.FieldKey,
          value.Contract.Label,
          value.Stored,
          value.Live,
          value.Contract.AuthoritySource))
        .ToArray();
      return new ReadOnlyCollection<NativeStage01Drift>(drifts);
    }

    internal static bool NumericEquivalent(string left, string right)
    {
      return AreEquivalent(left, right, 0.0);
    }

    private static bool AreEquivalent(
      string stored,
      string live,
      double? numericTolerance)
    {
      string left = stored ?? string.Empty;
      string right = live ?? string.Empty;
      if (!numericTolerance.HasValue)
        return string.Equals(left, right, StringComparison.Ordinal);
      if (double.TryParse(
          left,
          NumberStyles.Float | NumberStyles.AllowThousands,
          CultureInfo.InvariantCulture,
          out double leftNumber)
        && double.TryParse(
          right,
          NumberStyles.Float | NumberStyles.AllowThousands,
          CultureInfo.InvariantCulture,
          out double rightNumber)
        && !double.IsNaN(leftNumber)
        && !double.IsNaN(rightNumber)
        && !double.IsInfinity(leftNumber)
        && !double.IsInfinity(rightNumber))
      {
        return Math.Abs(leftNumber - rightNumber) <= numericTolerance.Value;
      }
      return string.Equals(left, right, StringComparison.Ordinal);
    }
  }
}
