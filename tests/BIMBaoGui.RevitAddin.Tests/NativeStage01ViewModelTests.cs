using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01ViewModelTests
  {
    [Fact]
    public void BuildsStableDirectoryAndStartsOnRequiredConditionsGroup()
    {
      var viewModel = new NativeStage01ViewModel(
        NativeRuleCatalog.Current);

      Assert.NotEmpty(viewModel.Groups);
      Assert.Equal(
        NativeStage01ViewModel.ConditionsGroup,
        viewModel.Groups.First());
      Assert.Equal(
        NativeStage01ViewModel.ConditionsGroup,
        viewModel.ActiveGroup);
      Assert.Contains("01_文件与项目身份", viewModel.Groups);
      Assert.NotEmpty(viewModel.Conditions);
      Assert.Equal(1, viewModel.GetMissingRequiredCount(
        NativeStage01ViewModel.ConditionsGroup));
    }

    [Fact]
    public void EditingAFieldMarksDirtyAndInvalidatesPreviousValidation()
    {
      var viewModel = new NativeStage01ViewModel(
        NativeRuleCatalog.Current);
      string fieldGroup = viewModel.Groups.First(group =>
        viewModel.FieldsForGroup(group).Any(value =>
          !value.ReadOnly && !value.Deferred));
      viewModel.SetActiveGroup(fieldGroup);
      NativeStage01FieldDefinition field = viewModel.ActiveFields
        .First(value => !value.ReadOnly && !value.Deferred);
      viewModel.Validate();
      Assert.NotNull(viewModel.Validation);

      viewModel.SetFieldValue(field, "新值");

      Assert.True(viewModel.IsDirty);
      Assert.Null(viewModel.Validation);
      Assert.Equal("新值", viewModel.GetFieldValue(field));
    }

    [Fact]
    public void ConditionEditingUsesMutualExclusionAndUpdatesRequiredCount()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      var viewModel = new NativeStage01ViewModel(catalog);
      string actualConditionId = catalog.Conditions.First().ConditionId;

      viewModel.SetNoConditions(true);
      Assert.True(viewModel.GetNoConditions());
      Assert.Equal(0, viewModel.GetMissingRequiredCount(
        NativeStage01ViewModel.ConditionsGroup));

      viewModel.SetCondition(actualConditionId, true);
      Assert.True(viewModel.GetCondition(actualConditionId));
      Assert.False(viewModel.GetNoConditions());

      viewModel.SetNoConditions(true);
      Assert.True(viewModel.GetNoConditions());
      Assert.False(viewModel.GetCondition(actualConditionId));
    }

    [Fact]
    public void RequiredCountAndGroupNavigationAreDeterministic()
    {
      var viewModel = new NativeStage01ViewModel(
        NativeRuleCatalog.Current);
      string group = viewModel.Groups.First(value =>
        viewModel.FieldsForGroup(value).Any(
          NativeStage01Validator.IsRequired));

      viewModel.SetActiveGroup(group);
      int before = viewModel.GetMissingRequiredCount(group);
      Assert.True(before > 0);
      NativeStage01FieldDefinition field = viewModel.ActiveFields.First(
        NativeStage01Validator.IsRequired);
      viewModel.SetFieldValue(field, ValidValue(field));
      int after = viewModel.GetMissingRequiredCount(group);

      Assert.True(after < before);
      Assert.Equal(group, viewModel.ActiveGroup);
    }

    [Fact]
    public void OptionalCountsFollowCurrentFieldValues()
    {
      var viewModel = new NativeStage01ViewModel(
        NativeRuleCatalog.Current);
      string group = viewModel.Groups.First(value =>
        viewModel.FieldsForGroup(value).Any(field =>
          !NativeStage01Validator.IsRequired(field)
          && !field.ReadOnly
          && !field.Deferred
          && string.IsNullOrWhiteSpace(viewModel.GetFieldValue(field))));
      NativeStage01FieldDefinition optionalField =
        viewModel.FieldsForGroup(group).First(field =>
          !NativeStage01Validator.IsRequired(field)
          && !field.ReadOnly
          && !field.Deferred
          && string.IsNullOrWhiteSpace(viewModel.GetFieldValue(field)));
      int expectedTotal = viewModel.FieldsForGroup(group).Count(field =>
        !NativeStage01Validator.IsRequired(field));
      int before = viewModel.FieldsForGroup(group).Count(field =>
        !NativeStage01Validator.IsRequired(field)
        && !string.IsNullOrWhiteSpace(viewModel.GetFieldValue(field)));

      Assert.Equal(expectedTotal, viewModel.GetOptionalFieldCount(group));
      Assert.Equal(before, viewModel.GetFilledOptionalFieldCount(group));

      viewModel.SetFieldValue(optionalField, ValidValue(optionalField));

      Assert.Equal(before + 1, viewModel.GetFilledOptionalFieldCount(group));
    }

    [Fact]
    public void OptionalValidationErrorsAreDetectedPerGroup()
    {
      var viewModel = new NativeStage01ViewModel(
        NativeRuleCatalog.Current);
      NativeStage01FieldDefinition optionalField =
        NativeRuleCatalog.Current.Stage01Fields.First(field =>
          !NativeStage01Validator.IsRequired(field)
          && !field.ReadOnly
          && !field.Deferred
          && InvalidValue(field) != null);

      viewModel.SetFieldValue(optionalField, InvalidValue(optionalField));
      viewModel.Validate();

      Assert.True(viewModel.HasOptionalValidationError(optionalField.UiGroup));
      Assert.Contains(
        viewModel.Validation.Messages,
        message => message.FieldKey == optionalField.FieldKey);
    }

    [Fact]
    public void LoadingStartsAtConditionsAndUsesIndependentModelCopies()
    {
      var viewModel = new NativeStage01ViewModel(
        NativeRuleCatalog.Current);
      NativeStage01Model source =
        NativeRuleCatalog.Current.CreateDefaultStage01Model();
      source.SetValue(NativeStage01Keys.ProjectName, "源项目");

      viewModel.LoadModel(source);
      viewModel.Model.SetValue(NativeStage01Keys.ProjectName, "界面项目");
      viewModel.AddOrganization();
      Assert.Equal(2, viewModel.Model.Organizations.Count);
      viewModel.RemoveCurrentOrganization();

      Assert.Equal(
        NativeStage01ViewModel.ConditionsGroup,
        viewModel.ActiveGroup);
      Assert.Equal("源项目", source.GetValue(NativeStage01Keys.ProjectName));
      Assert.Single(viewModel.Model.Organizations);
      Assert.True(viewModel.IsDirty);
    }

    private static string InvalidValue(NativeStage01FieldDefinition field)
    {
      string label = field.Label ?? string.Empty;
      if (label.Contains("邮政编码")) return "12";
      if (label.Contains("手机")
        || label.Contains("电话")
        || label.Contains("联系电话"))
        return "x";
      if (label.Contains("邮箱")) return "not-an-email";
      if (label.Contains("统一信用代码")
        || label.Contains("社会统一信用代码"))
        return "x";
      switch (field.Kind)
      {
        case NativeStage01FieldKind.Number: return "not-number";
        case NativeStage01FieldKind.Integer: return "1.5";
        case NativeStage01FieldKind.Boolean: return "maybe";
        case NativeStage01FieldKind.Guid: return "not-guid";
        case NativeStage01FieldKind.DateTime: return "not-date";
        case NativeStage01FieldKind.Enum: return "__invalid__";
        default: return null;
      }
    }

    private static string ValidValue(NativeStage01FieldDefinition field)
    {
      switch (field.Kind)
      {
        case NativeStage01FieldKind.Number: return "1.0";
        case NativeStage01FieldKind.Integer: return "1";
        case NativeStage01FieldKind.Boolean: return "true";
        case NativeStage01FieldKind.Guid:
          return Guid.NewGuid().ToString("D");
        case NativeStage01FieldKind.DateTime: return "2026-08-11";
        case NativeStage01FieldKind.Enum:
          return field.AllowedValues.FirstOrDefault() ?? "测试";
        default: return "测试";
      }
    }
  }
}
