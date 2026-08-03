using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03ExportGatePolicyTests
  {
    [Theory]
    [InlineData(Stage03FieldStatus.MissingParameter)]
    [InlineData(Stage03FieldStatus.EmptyRequiredValue)]
    [InlineData(Stage03FieldStatus.UnclassifiedRequirement)]
    public void Strict_blocks_each_active_business_blocker(
      Stage03FieldStatus status)
    {
      Stage03GateDecision decision = Stage03ExportGatePolicy.Decide(
        Stage03GateMode.Strict,
        string.Empty,
        new[] { Field("property-b", status, true, "REQUIRED", true) },
        Array.Empty<string>());

      Assert.False(decision.AllowExport);
      Assert.False(decision.Forced);
      Stage03BusinessBlocker blocker = Assert.Single(
        decision.BusinessBlockers);
      Assert.Equal("property-b", blocker.PropertyId);
      Assert.Equal(status, blocker.Status);
      Assert.NotEmpty(decision.Messages);
    }

    [Fact]
    public void Strict_allows_pass_not_applicable_inactive_and_optional_nonblockers()
    {
      Stage03GateDecision decision = Stage03ExportGatePolicy.Decide(
        Stage03GateMode.Strict,
        "ignored",
        new[]
        {
          Field("pass", Stage03FieldStatus.Pass, true, "REQUIRED", false),
          Field(
            "na",
            Stage03FieldStatus.NotApplicable,
            false,
            "NOT_APPLICABLE",
            true),
          Field(
            "optional",
            Stage03FieldStatus.MissingParameter,
            true,
            "OPTIONAL",
            false),
          Field(
            "inactive-unclassified",
            Stage03FieldStatus.UnclassifiedRequirement,
            false,
            "UNCLASSIFIED",
            true)
        },
        new[] { "  ", null });

      Assert.True(decision.AllowExport);
      Assert.False(decision.Forced);
      Assert.Empty(decision.BusinessBlockers);
      Assert.Empty(decision.TechnicalFatalCodes);
    }

    [Fact]
    public void Strict_fail_closes_active_unclassified_even_if_upstream_did_not_mark_blocker()
    {
      Stage03GateDecision decision = Stage03ExportGatePolicy.Decide(
        Stage03GateMode.Strict,
        string.Empty,
        new[]
        {
          Field(
            "unclassified",
            Stage03FieldStatus.UnclassifiedRequirement,
            true,
            "UNCLASSIFIED",
            false)
        },
        Array.Empty<string>());

      Assert.False(decision.AllowExport);
      Assert.Equal(
        Stage03FieldStatus.UnclassifiedRequirement,
        Assert.Single(decision.BusinessBlockers).Status);
    }

    [Fact]
    public void Force_requires_a_trimmed_nonempty_reason_even_without_blockers()
    {
      foreach (string reason in new[] { null, string.Empty, " ", "\t\r\n" })
      {
        Stage03GateDecision decision = Stage03ExportGatePolicy.Decide(
          Stage03GateMode.Force,
          reason,
          Array.Empty<Stage03FieldResult>(),
          Array.Empty<string>());

        Assert.False(decision.AllowExport);
        Assert.False(decision.Forced);
        Assert.Contains(
          decision.Messages,
          message => message.Contains("原因"));
      }
    }

    [Fact]
    public void Valid_force_mode_is_audited_as_forced_even_without_business_blockers()
    {
      Stage03GateDecision decision = Stage03ExportGatePolicy.Decide(
        Stage03GateMode.Force,
        "验证测试放行模式",
        new[]
        {
          Field("pass", Stage03FieldStatus.Pass, true, "REQUIRED", false)
        },
        Array.Empty<string>());

      Assert.True(decision.AllowExport);
      Assert.True(decision.Forced);
      Assert.Empty(decision.BusinessBlockers);
    }

    [Fact]
    public void Force_with_reason_allows_business_blockers_and_preserves_sorted_unique_evidence()
    {
      Stage03GateDecision decision = Stage03ExportGatePolicy.Decide(
        Stage03GateMode.Force,
        "  仅用于测试放行  ",
        new[]
        {
          Field(
            "property-z",
            Stage03FieldStatus.InvalidValue,
            true,
            "REQUIRED",
            true),
          Field(
            "property-a",
            Stage03FieldStatus.MissingCarrier,
            true,
            "REQUIRED",
            true),
          Field(
            "property-a",
            Stage03FieldStatus.MissingCarrier,
            true,
            "REQUIRED",
            true)
        },
        Array.Empty<string>());

      Assert.True(decision.AllowExport);
      Assert.True(decision.Forced);
      Assert.Equal("仅用于测试放行", decision.Reason);
      Assert.Equal(
        new[] { "property-a", "property-z" },
        decision.BusinessBlockers.Select(item => item.PropertyId).ToArray());
      Assert.All(decision.BusinessBlockers, item =>
        Assert.False(string.IsNullOrWhiteSpace(item.Message)));
    }

    [Fact]
    public void Blocker_deduplication_preserves_same_property_on_distinct_owners()
    {
      Stage03FieldResult ownerZ = Field(
        "same-property",
        Stage03FieldStatus.InvalidValue,
        true,
        "REQUIRED",
        true);
      ownerZ.Entity = "IfcWall";
      ownerZ.OwnerUniqueId = "owner-z";
      ownerZ.Role = "building";
      Stage03FieldResult ownerA = Field(
        "same-property",
        Stage03FieldStatus.InvalidValue,
        true,
        "REQUIRED",
        true);
      ownerA.Entity = "IfcWall";
      ownerA.OwnerUniqueId = "owner-a";
      ownerA.Role = "building";

      Stage03GateDecision decision = Stage03ExportGatePolicy.Decide(
        Stage03GateMode.Strict,
        string.Empty,
        new[] { ownerZ, ownerA, ownerA },
        Array.Empty<string>());

      Assert.Equal(2, decision.BusinessBlockers.Count);
      Assert.Equal(
        new[] { "owner-a", "owner-z" },
        decision.BusinessBlockers.Select(item => item.OwnerUniqueId).ToArray());
      Assert.All(decision.BusinessBlockers, item =>
      {
        Assert.Equal("IfcWall", item.Entity);
        Assert.Equal("building", item.Role);
      });
    }

    [Fact]
    public void Blocker_deduplication_distinguishes_embedded_nul_from_field_boundary()
    {
      Stage03FieldResult embeddedInEntity = Field(
        "same-property",
        Stage03FieldStatus.InvalidValue,
        true,
        "REQUIRED",
        true);
      embeddedInEntity.Entity = "A\0B";
      embeddedInEntity.OwnerUniqueId = "C";
      Stage03FieldResult embeddedInOwner = Field(
        "same-property",
        Stage03FieldStatus.InvalidValue,
        true,
        "REQUIRED",
        true);
      embeddedInOwner.Entity = "A";
      embeddedInOwner.OwnerUniqueId = "B\0C";

      Stage03GateDecision forward = Stage03ExportGatePolicy.Decide(
        Stage03GateMode.Strict,
        string.Empty,
        new[] { embeddedInEntity, embeddedInOwner },
        Array.Empty<string>());
      Stage03GateDecision reverse = Stage03ExportGatePolicy.Decide(
        Stage03GateMode.Strict,
        string.Empty,
        new[] { embeddedInOwner, embeddedInEntity },
        Array.Empty<string>());

      Assert.Equal(2, forward.BusinessBlockers.Count);
      Assert.Equal(
        forward.BusinessBlockers.Select(BlockerEvidence),
        reverse.BusinessBlockers.Select(BlockerEvidence));
    }

    [Theory]
    [InlineData("WRONG_DOCUMENT")]
    [InlineData("UNSUPPORTED_REVIT")]
    [InlineData("DOCUMENT_UNAVAILABLE")]
    [InlineData("OUTPUT_EXISTS")]
    [InlineData("EXPORT_FAILED")]
    [InlineData("INVALID_IFC")]
    [InlineData("REPORT_FAILED")]
    [InlineData("FUTURE_FATAL_CODE")]
    public void Technical_fatal_always_fails_closed_in_force_mode(string code)
    {
      Stage03GateDecision decision = Stage03ExportGatePolicy.Decide(
        Stage03GateMode.Force,
        "测试原因",
        new[]
        {
          Field(
            "property-a",
            Stage03FieldStatus.MissingParameter,
            true,
            "REQUIRED",
            true)
        },
        new[] { code });

      Assert.False(decision.AllowExport);
      Assert.False(decision.Forced);
      Assert.Contains(code, decision.TechnicalFatalCodes);
      Assert.NotEmpty(decision.BusinessBlockers);
      Assert.Contains(
        decision.Messages,
        message => message.Contains(code));
    }

    [Fact]
    public void Technical_codes_are_trimmed_normalized_deduplicated_and_sorted()
    {
      Stage03GateDecision decision = Stage03ExportGatePolicy.Decide(
        Stage03GateMode.Strict,
        string.Empty,
        Array.Empty<Stage03FieldResult>(),
        new[]
        {
          " report_failed ",
          "OUTPUT_EXISTS",
          "REPORT_FAILED",
          null,
          " "
        });

      Assert.Equal(
        new[] { "OUTPUT_EXISTS", "REPORT_FAILED" },
        decision.TechnicalFatalCodes.ToArray());
      Assert.False(decision.AllowExport);
    }

    [Theory]
    [InlineData(Stage03GateMode.Strict, false)]
    [InlineData(Stage03GateMode.Strict, true)]
    [InlineData(Stage03GateMode.Force, false)]
    [InlineData(Stage03GateMode.Force, true)]
    public void Undefined_field_status_always_fails_closed(
      Stage03GateMode mode,
      bool isBusinessBlocker)
    {
      Stage03GateDecision decision = Stage03ExportGatePolicy.Decide(
        mode,
        "测试强制原因",
        new[]
        {
          Field(
            "undefined-status",
            (Stage03FieldStatus)999,
            true,
            "REQUIRED",
            isBusinessBlocker)
        },
        Array.Empty<string>());

      Assert.False(decision.AllowExport);
      Assert.False(decision.Forced);
      Assert.Contains("INVALID_FIELD_STATUS", decision.TechnicalFatalCodes);
    }

    [Theory]
    [MemberData(nameof(UndefinedStatusPositionsAndGateCombinations))]
    public void Undefined_status_in_any_field_status_position_always_fails_closed(
      string statusPosition,
      Stage03GateMode mode,
      bool active,
      bool isBusinessBlocker)
    {
      Stage03FieldResult field = Field(
        "undefined-substatus",
        Stage03FieldStatus.Pass,
        active,
        "REQUIRED",
        isBusinessBlocker);
      SetStatus(field, statusPosition, (Stage03FieldStatus)999);

      Stage03GateDecision decision = Stage03ExportGatePolicy.Decide(
        mode,
        "测试强制原因",
        new[] { field },
        Array.Empty<string>());

      Assert.False(decision.AllowExport);
      Assert.False(decision.Forced);
      Assert.Contains(
        Stage03TechnicalFatalCodes.InvalidFieldStatus,
        decision.TechnicalFatalCodes);
    }

    [Fact]
    public void Field_status_contract_contains_every_required_stage03_status()
    {
      string[] names = Enum.GetNames(typeof(Stage03FieldStatus));

      foreach (string required in new[]
      {
        "Pass",
        "NotApplicable",
        "MissingCarrier",
        "CarrierCategoryMismatch",
        "CarrierNameMismatch",
        "AmbiguousCarrier",
        "MissingParameter",
        "EmptyRequiredValue",
        "InvalidValue",
        "RuleNotImplemented",
        "UnclassifiedRequirement",
        "IfcOwnerNotFound",
        "IfcValueMismatch"
      })
      {
        Assert.Contains(required, names);
      }
    }

    private static Stage03FieldResult Field(
      string propertyId,
      Stage03FieldStatus status,
      bool active,
      string requirement,
      bool isBusinessBlocker)
    {
      return new Stage03FieldResult
      {
        PropertyId = propertyId,
        Status = status,
        Active = active,
        Requirement = requirement,
        IsBusinessBlocker = isBusinessBlocker,
        Messages = new[] { "字段检测消息" }
      };
    }

    public static IEnumerable<object[]>
      UndefinedStatusPositionsAndGateCombinations()
    {
      string[] positions =
      {
        nameof(Stage03FieldResult.Status),
        nameof(Stage03FieldResult.CarrierStatus),
        nameof(Stage03FieldResult.ParameterStatus),
        nameof(Stage03FieldResult.RevitStatus),
        nameof(Stage03FieldResult.RawIfcStatus),
        nameof(Stage03FieldResult.FinalIfcStatus)
      };
      foreach (string position in positions)
      {
        foreach (Stage03GateMode mode in new[]
        {
          Stage03GateMode.Strict,
          Stage03GateMode.Force
        })
        {
          foreach (bool active in new[] { false, true })
          {
            foreach (bool isBusinessBlocker in new[] { false, true })
            {
              yield return new object[]
              {
                position,
                mode,
                active,
                isBusinessBlocker
              };
            }
          }
        }
      }
    }

    private static void SetStatus(
      Stage03FieldResult field,
      string statusPosition,
      Stage03FieldStatus status)
    {
      switch (statusPosition)
      {
        case nameof(Stage03FieldResult.Status):
          field.Status = status;
          break;
        case nameof(Stage03FieldResult.CarrierStatus):
          field.CarrierStatus = status;
          break;
        case nameof(Stage03FieldResult.ParameterStatus):
          field.ParameterStatus = status;
          break;
        case nameof(Stage03FieldResult.RevitStatus):
          field.RevitStatus = status;
          break;
        case nameof(Stage03FieldResult.RawIfcStatus):
          field.RawIfcStatus = status;
          break;
        case nameof(Stage03FieldResult.FinalIfcStatus):
          field.FinalIfcStatus = status;
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(statusPosition));
      }
    }

    private static string BlockerEvidence(Stage03BusinessBlocker blocker)
    {
      return blocker.Entity + "|" + blocker.OwnerUniqueId;
    }
  }
}
