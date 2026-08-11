using System;
using System.Collections.Generic;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01WritePreflightTests
  {
    [Theory]
    [InlineData(false, "2020", true, true, false, NativeStage01PreflightCodes.NoActiveDocument)]
    [InlineData(true, "2024", true, true, false, NativeStage01PreflightCodes.UnsupportedRevit)]
    [InlineData(true, "2020", false, true, false, NativeStage01PreflightCodes.FamilyDocument)]
    [InlineData(true, "2020", true, false, false, NativeStage01PreflightCodes.UnsavedDocument)]
    [InlineData(true, "2020", true, true, true, NativeStage01PreflightCodes.ReadOnlyDocument)]
    public void BlocksUnsupportedHostAndDocumentStates(
      bool hasDocument,
      string revitVersion,
      bool isProject,
      bool isSaved,
      bool isReadOnly,
      string expectedCode)
    {
      NativeStage01DocumentState state = CurrentState();
      state.HasDocument = hasDocument;
      state.RevitVersion = revitVersion;
      state.IsProjectDocument = isProject;
      state.IsSaved = isSaved;
      state.IsReadOnly = isReadOnly;

      NativeStage01PreflightDecision decision =
        NativeStage01WritePreflight.Evaluate(
          state,
          ValidValidation(),
          confirmBlankProject: true,
          allowReinitialize: true);

      Assert.False(decision.Accepted);
      Assert.Contains(decision.Blockers, value => value.Code == expectedCode);
    }

    [Fact]
    public void BlocksDomainValidationAndCorruptOrFutureStorage()
    {
      NativeStage01DocumentState state = CurrentState();
      NativeStage01ValidationResult invalid =
        new NativeStage01ValidationResult(new[]
        {
          new NativeStage01ValidationMessage
          {
            Code = NativeStage01ValidationCodes.RequiredValueMissing,
            FieldKey = NativeStage01Keys.ProjectName,
            Message = "项目名称缺失。"
          }
        });
      NativeStage01PreflightDecision validationDecision =
        NativeStage01WritePreflight.Evaluate(
          state,
          invalid,
          true,
          true);
      Assert.Contains(validationDecision.Blockers, value =>
        value.Code == NativeStage01PreflightCodes.ModelValidationFailed);

      state.StorageDecision = new NativeStage01StorageDecision
      {
        State = NativeStage01StorageState.Corrupt,
        ErrorCode = NativeStage01StorageCodes.CorruptStorage,
        Message = "存储损坏"
      };
      NativeStage01PreflightDecision corruptDecision =
        NativeStage01WritePreflight.Evaluate(
          state,
          ValidValidation(),
          true,
          true);
      Assert.Contains(corruptDecision.Blockers, value =>
        value.Code == NativeStage01PreflightCodes.CorruptStorage);

      state.StorageDecision = new NativeStage01StorageDecision
      {
        State = NativeStage01StorageState.UnsupportedFuture,
        ErrorCode = NativeStage01StorageCodes.UnsupportedFutureVersion,
        Message = "未来版本"
      };
      NativeStage01PreflightDecision futureDecision =
        NativeStage01WritePreflight.Evaluate(
          state,
          ValidValidation(),
          true,
          true);
      Assert.Contains(futureDecision.Blockers, value =>
        value.Code == NativeStage01PreflightCodes.UnsupportedFutureStorage);
    }

    [Fact]
    public void FirstInitializationRequiresBlankConfirmationAndNoModelBlockers()
    {
      NativeStage01DocumentState state = CurrentState();
      state.StorageDecision = new NativeStage01StorageDecision
      {
        State = NativeStage01StorageState.NoRecord
      };

      NativeStage01PreflightDecision noConfirmation =
        NativeStage01WritePreflight.Evaluate(
          state,
          ValidValidation(),
          confirmBlankProject: false,
          allowReinitialize: false);
      Assert.Contains(noConfirmation.Blockers, value =>
        value.Code == NativeStage01PreflightCodes.BlankConfirmationRequired);

      state.BlockingElements = new[] { "墙 / Id=42" };
      NativeStage01PreflightDecision notBlank =
        NativeStage01WritePreflight.Evaluate(
          state,
          ValidValidation(),
          confirmBlankProject: true,
          allowReinitialize: false);
      Assert.Contains(notBlank.Blockers, value =>
        value.Code == NativeStage01PreflightCodes.ModelNotBlank);

      state.BlockingElements = Array.Empty<string>();
      NativeStage01PreflightDecision accepted =
        NativeStage01WritePreflight.Evaluate(
          state,
          ValidValidation(),
          confirmBlankProject: true,
          allowReinitialize: false);
      Assert.True(accepted.Accepted);
    }

    [Fact]
    public void CurrentInitializationRequiresPermissionButLegacyMigrationDoesNot()
    {
      NativeStage01DocumentState state = CurrentState();
      NativeStage01PreflightDecision blocked =
        NativeStage01WritePreflight.Evaluate(
          state,
          ValidValidation(),
          confirmBlankProject: false,
          allowReinitialize: false);
      Assert.Contains(blocked.Blockers, value =>
        value.Code == NativeStage01PreflightCodes.ReinitializePermissionRequired);

      NativeStage01PreflightDecision allowed =
        NativeStage01WritePreflight.Evaluate(
          state,
          ValidValidation(),
          false,
          true);
      Assert.True(allowed.Accepted);

      state.StorageDecision = new NativeStage01StorageDecision
      {
        State = NativeStage01StorageState.MigratableLegacy
      };
      NativeStage01PreflightDecision migration =
        NativeStage01WritePreflight.Evaluate(
          state,
          ValidValidation(),
          false,
          false);
      Assert.True(migration.Accepted);
    }

    [Fact]
    public void ProjectPositionPlanNeverSwapsXAndY()
    {
      NativeProjectPositionPlan plan = NativeProjectPositionPlan.Create(
        xNorthingMeters: 3373266.866,
        yEastingMeters: 38589642.165,
        elevationMeters: 24.0,
        trueNorthDegrees: 90.0);

      Assert.Equal(3373266.866, plan.NorthSouthMeters, 6);
      Assert.Equal(38589642.165, plan.EastWestMeters, 6);
      Assert.Equal(24.0, plan.ElevationMeters, 6);
      Assert.Equal(Math.PI / 2.0, plan.AngleRadians, 12);
    }

    private static NativeStage01DocumentState CurrentState()
    {
      return new NativeStage01DocumentState
      {
        HasDocument = true,
        RevitVersion = "2020",
        IsProjectDocument = true,
        IsSaved = true,
        IsReadOnly = false,
        BlockingElements = Array.Empty<string>(),
        StorageDecision = new NativeStage01StorageDecision
        {
          State = NativeStage01StorageState.Current
        }
      };
    }

    private static NativeStage01ValidationResult ValidValidation()
    {
      return new NativeStage01ValidationResult(
        Array.Empty<NativeStage01ValidationMessage>());
    }
  }
}
