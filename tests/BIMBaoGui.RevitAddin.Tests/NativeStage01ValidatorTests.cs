using System;
using System.Globalization;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01ValidatorTests
  {
    [Fact]
    public void DefaultModelIsBlockedUntilRequiredBusinessValuesAreFilled()
    {
      NativeStage01Model model =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();

      NativeStage01ValidationResult result =
        NativeStage01Validator.Validate(model, NativeRuleCatalog.Current);

      Assert.False(result.IsValid);
      Assert.Contains(result.Messages, value =>
        value.Code == NativeStage01ValidationCodes.RequiredValueMissing);
      Assert.Contains(result.Messages, value =>
        value.Code
          == NativeStage01ValidationCodes.ProjectConditionDeclarationMissing);
    }

    [Fact]
    public void FullyPopulatedRequiredModelPassesDomainValidation()
    {
      NativeStage01Model model = CreateValidModel();

      NativeStage01ValidationResult result =
        NativeStage01Validator.Validate(model, NativeRuleCatalog.Current);

      Assert.True(
        result.IsValid,
        string.Join(Environment.NewLine, result.Messages.Select(value =>
          value.Code + "｜" + value.FieldKey + "｜" + value.Message)));
    }

    [Fact]
    public void ActualConditionSelectionAlsoSatisfiesRequiredDeclaration()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01Model model = CreateValidModel();
      NativeProjectConditionDeclarationPolicy.SetNoConditions(
        model,
        catalog,
        false);
      NativeProjectConditionDeclarationPolicy.SetActualCondition(
        model,
        catalog,
        catalog.Conditions.First().ConditionId,
        true);

      NativeStage01ValidationResult result =
        NativeStage01Validator.Validate(model, catalog);

      Assert.DoesNotContain(result.Messages, value =>
        value.Code
          == NativeStage01ValidationCodes.ProjectConditionDeclarationMissing
        || value.Code
          == NativeStage01ValidationCodes.ProjectConditionDeclarationConflict);
    }

    [Fact]
    public void ConflictingConditionDeclarationIsRejected()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01Model model = CreateValidModel();
      model.SetCondition(catalog.Conditions.First().ConditionId, true);
      model.SetCondition(
        NativeProjectConditionDeclarationPolicy.NoneConditionId,
        true);

      NativeStage01ValidationResult result =
        NativeStage01Validator.Validate(model, catalog);

      Assert.Contains(result.Messages, value =>
        value.Code
          == NativeStage01ValidationCodes.ProjectConditionDeclarationConflict);
    }

    [Fact]
    public void RejectsWrongPayloadVersionAndMissingNoneDeclarationKey()
    {
      NativeStage01Model model = CreateValidModel();
      model.SetValue(NativeStage01Keys.WorkflowVersion, "0.9.0");
      model.Conditions.Remove(
        NativeProjectConditionDeclarationPolicy.NoneConditionId);

      NativeStage01ValidationResult result =
        NativeStage01Validator.Validate(model, NativeRuleCatalog.Current);

      Assert.Contains(result.Messages, value =>
        value.Code == NativeStage01ValidationCodes.PayloadVersionMismatch);
      Assert.Contains(result.Messages, value =>
        value.Code == NativeStage01ValidationCodes.ConditionMissing
        && value.FieldKey
          == NativeProjectConditionDeclarationPolicy.NoneConditionId);
    }

    [Fact]
    public void RejectsUnknownModelProfileAndOutOfRangeTrueNorth()
    {
      NativeStage01Model model = CreateValidModel();
      model.SetValue(NativeStage01Keys.ModelFileType, "不存在的模型类型");
      model.SetValue(NativeStage01Keys.TrueNorthAngle, "181");

      NativeStage01ValidationResult result =
        NativeStage01Validator.Validate(model, NativeRuleCatalog.Current);

      Assert.Contains(result.Messages, value =>
        value.Code == NativeStage01ValidationCodes.UnknownModelProfile);
      Assert.Contains(result.Messages, value =>
        value.Code == NativeStage01ValidationCodes.TrueNorthOutOfRange);
    }

    [Fact]
    public void RejectsMissingConditionKeysAndInvalidTypedValues()
    {
      NativeStage01Model model = CreateValidModel();
      string condition = NativeRuleCatalog.Current.Conditions.First().ConditionId;
      model.Conditions.Remove(condition);
      model.SetValue(NativeStage01Keys.BaseX, "not-a-number");

      NativeStage01ValidationResult result =
        NativeStage01Validator.Validate(model, NativeRuleCatalog.Current);

      Assert.Contains(result.Messages, value =>
        value.Code == NativeStage01ValidationCodes.ConditionMissing);
      Assert.Contains(result.Messages, value =>
        value.Code == NativeStage01ValidationCodes.InvalidNumber
        && value.FieldKey == NativeStage01Keys.BaseX);
    }

    private static NativeStage01Model CreateValidModel()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01Model model = catalog.CreateDefaultStage01Model();

      foreach (NativeStage01FieldDefinition field in catalog.Stage01Fields
        .Where(value => value.Essential && !value.Deferred))
      {
        string value = ValidValue(field);
        if (field.IsOrganization)
          model.SetOrganizationValue(0, field.FieldKey, value);
        else if (string.IsNullOrWhiteSpace(model.GetValue(field.FieldKey)))
          model.SetValue(field.FieldKey, value);
      }

      model.SetValue(NativeStage01Keys.ProjectNumber, "HB-2026-001");
      model.SetValue(NativeStage01Keys.ProjectName, "武汉市某建设项目");
      model.SetValue(NativeStage01Keys.SubitemCode, "SITE-01");
      model.SetValue(NativeStage01Keys.SubitemName, "总平面");
      model.SetValue(NativeStage01Keys.BaseX, "3373266.866");
      model.SetValue(NativeStage01Keys.BaseY, "38589642.165");
      model.SetValue(NativeStage01Keys.BaseElevation, "24.000");
      model.SetValue(NativeStage01Keys.CoordinateSystem, "CGCS2000");
      model.SetValue(NativeStage01Keys.ElevationSystem, "1985国家高程基准");
      model.SetValue(NativeStage01Keys.TrueNorthAngle, "0");
      NativeProjectConditionDeclarationPolicy.SetNoConditions(
        model,
        catalog,
        true);
      return model;
    }

    private static string ValidValue(NativeStage01FieldDefinition field)
    {
      string label = field.Label ?? string.Empty;
      if (label.Contains("统一信用代码"))
        return "91420100MA4K123456";
      if (label.Contains("邮箱")) return "name@example.com";
      if (label.Contains("手机") || label.Contains("电话"))
        return "13800138000";
      if (label.Contains("邮政编码")) return "430000";

      switch (field.Kind)
      {
        case NativeStage01FieldKind.Number:
          return 1.25.ToString(CultureInfo.InvariantCulture);
        case NativeStage01FieldKind.Integer:
          return "1";
        case NativeStage01FieldKind.Boolean:
          return "true";
        case NativeStage01FieldKind.Guid:
          return Guid.NewGuid().ToString("D");
        case NativeStage01FieldKind.DateTime:
          return "2026-08-11";
        case NativeStage01FieldKind.Enum:
          return field.AllowedValues.FirstOrDefault() ?? "测试";
        default:
          return "测试";
      }
    }
  }
}
