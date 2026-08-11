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
    public void BuildsStableDirectoryAndStartsOnDatabaseDefaultGroup()
    {
      var viewModel = new NativeStage01ViewModel(
        NativeRuleCatalog.Current);

      Assert.NotEmpty(viewModel.Groups);
      Assert.Equal(
        NativeRuleCatalog.Current.DefaultActiveGroup,
        viewModel.ActiveGroup);
      Assert.Contains("01_文件与项目身份", viewModel.Groups);
      Assert.Contains(NativeStage01ViewModel.ConditionsGroup, viewModel.Groups);
      Assert.NotEmpty(viewModel.ActiveFields);
    }

    [Fact]
    public void EditingAFieldMarksDirtyAndInvalidatesPreviousValidation()
    {
      var viewModel = new NativeStage01ViewModel(
        NativeRuleCatalog.Current);
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
    public void LoadingAndOrganizationEditingUseIndependentModelCopies()
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

      Assert.Equal("源项目", source.GetValue(NativeStage01Keys.ProjectName));
      Assert.Equal(1, viewModel.Model.Organizations.Count);
      Assert.False(viewModel.IsDirty);
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
