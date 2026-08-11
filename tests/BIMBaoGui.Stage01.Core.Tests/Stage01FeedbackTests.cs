using System;
using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01FeedbackTests
  {
    private static readonly FieldDefinition ProjectNumber = new FieldDefinition
    {
      Key = Stage01Keys.ProjectNumber,
      Label = "项目编号",
      Group = "01_文件与项目身份",
      Kind = FieldKind.Text
    };

    [Fact]
    public void Build_ExplainsWhereEveryBlockingProblemMustBeFixed()
    {
      var validation = new ValidationResult(new[]
      {
        new ValidationMessage(ValidationSeverity.Error, Stage01Keys.ProjectNumber, "必填项尚未填写。"),
        new ValidationMessage(ValidationSeverity.Error, "HBR|Precheck|BlankProject", "必须确认当前文件尚未开始正式建模。")
      });

      var messages = Stage01Feedback.Build(
        validation,
        new[] { ProjectNumber },
        new[] { "请先保存当前 RVT 文件。" },
        8);

      Assert.Contains("文件环境：请先保存当前 RVT 文件。", messages);
      Assert.Contains("文件与项目身份 > 项目编号：必填项尚未填写。", messages);
      Assert.Contains("提交与校验：请勾选“确认当前文件尚未开始正式建模（允许 Revit 模板默认内容）”。", messages);
    }

    [Fact]
    public void Build_PrioritizesActualWriteFailureOverDerivedValidationNoise()
    {
      var validation = new ValidationResult(new[]
      {
        new ValidationMessage(
          ValidationSeverity.Error,
          Stage01Keys.InitializationStatus,
          "请选择下拉列表中的有效选项。")
      });

      var messages = Stage01Feedback.Build(
        validation,
        Array.Empty<FieldDefinition>(),
        Array.Empty<string>(),
        new[] { "初始化失败，事务已回滚：共享参数定义缺失：基点坐标X" },
        3);

      Assert.Equal(
        "最近写入：初始化失败，事务已回滚：共享参数定义缺失：基点坐标X",
        messages[0]);
    }

    [Fact]
    public void CountErrorsForGroup_ProvidesDirectoryBadges()
    {
      var validation = new ValidationResult(new[]
      {
        new ValidationMessage(ValidationSeverity.Error, Stage01Keys.ProjectNumber, "必填项尚未填写。"),
        new ValidationMessage(ValidationSeverity.Error, "HBR|Precheck|BlankProject", "必须确认。")
      });
      var definitions = new[] { ProjectNumber };

      Assert.Equal(1, Stage01Feedback.CountErrorsForGroup(validation, definitions, "01_文件与项目身份"));
      Assert.Equal(1, Stage01Feedback.CountErrorsForGroup(validation, definitions, "11_提交与校验"));
      Assert.Equal(0, Stage01Feedback.CountErrorsForGroup(validation, definitions, "10_项目条件"));
    }

    [Fact]
    public void FirstProblemGroup_ReturnsTheFirstDirectoryThatNeedsAttention()
    {
      var validation = new ValidationResult(new[]
      {
        new ValidationMessage(ValidationSeverity.Error, Stage01Keys.ProjectNumber, "必填项尚未填写。"),
        new ValidationMessage(ValidationSeverity.Error, "HBR|Precheck|BlankProject", "必须确认。")
      });

      string group = Stage01Feedback.FirstProblemGroup(validation, new[] { ProjectNumber });

      Assert.Equal("01_文件与项目身份", group);
    }
  }
}
