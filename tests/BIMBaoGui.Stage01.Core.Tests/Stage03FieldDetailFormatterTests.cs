using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03FieldDetailFormatterTests
  {
    [Fact]
    public void All_fields_include_failures_and_have_stable_carrier_field_order()
    {
      Stage03FieldResult projectB = Field(
        "IfcProject",
        "PROJECT",
        1,
        "uid-a",
        "HBR.B",
        Stage03FieldStatus.MissingParameter);
      Stage03FieldResult buildingC = Field(
        "IfcBuilding",
        "BUILDING",
        2,
        "uid-b",
        "HBR.C",
        Stage03FieldStatus.Pass);
      Stage03FieldResult projectA = Field(
        "IfcProject",
        "PROJECT",
        1,
        "uid-a",
        "HBR.A",
        Stage03FieldStatus.Pass);

      Stage03FieldDetail[] forward = Stage03FieldDetailFormatter.Format(
        new[] { projectB, buildingC, projectA }).ToArray();
      Stage03FieldDetail[] reverse = Stage03FieldDetailFormatter.Format(
        new[] { projectA, buildingC, projectB }).ToArray();

      Assert.Equal(3, forward.Length);
      Assert.Equal(
        new[] { "HBR.C", "HBR.A", "HBR.B" },
        forward.Select(detail => detail.PropertyId));
      Assert.Equal(new[] { 0, 1, 1 }, forward.Select(detail => detail.CarrierIndex));
      Assert.Equal(new[] { 0, 0, 1 }, forward.Select(detail => detail.FieldIndex));
      Assert.Contains(forward, detail =>
        detail.PropertyId == "HBR.B"
        && detail.Status == Stage03FieldStatus.MissingParameter);
      Assert.Equal(
        forward.Select(detail => detail.Text),
        reverse.Select(detail => detail.Text));
      Assert.Equal(
        forward.Select(detail => detail.CarrierIndex),
        reverse.Select(detail => detail.CarrierIndex));
      Assert.Equal(
        forward.Select(detail => detail.FieldIndex),
        reverse.Select(detail => detail.FieldIndex));
    }

    [Fact]
    public void Detail_contains_complete_field_evidence_with_stably_sorted_messages()
    {
      Stage03FieldDetail detail = Assert.Single(
        Stage03FieldDetailFormatter.Format(new[]
        {
          Field(
            "IfcProject",
            "PROJECT",
            1,
            "uid-a",
            "HBR.B",
            Stage03FieldStatus.MissingParameter)
        }));

      Dictionary<string, object> evidence = ParseRecord(detail.Text);
      Assert.Equal(38, evidence.Count);
      Assert.Equal("HBR.B", evidence["propertyId"]);
      Assert.Equal("OFFICIAL", evidence["contractKind"]);
      Assert.Equal("REQUIRED", evidence["requirement"]);
      Assert.Equal("ACTIVE", evidence["applicability"]);
      Assert.Equal("NOT_IMPLEMENTED", evidence["runtimeStatus"]);
      Assert.Equal(
        "OWNER_STRATEGY_NOT_IMPLEMENTED",
        evidence["runtimeBlockCode"]);
      Assert.Equal(
        "当前 IFC owner strategy 尚未实现：CANONICAL_SPATIAL_ZONE_RECORD。",
        evidence["runtimeBlockReason"]);
      Assert.Equal("IfcProject", evidence["entity"]);
      Assert.Equal("Pset_申报信息属性集", evidence["propertySet"]);
      Assert.Equal("字段HBR.B", evidence["ifcProperty"]);
      Assert.Equal("PROJECT", evidence["role"]);
      Assert.Equal(1, evidence["elementId"]);
      Assert.Equal("uid-a", evidence["ownerUniqueId"]);
      Assert.Equal(
        "11111111-2222-3333-4444-555555555555",
        evidence["parameterGuid"]);
      Assert.Equal("参数-HBR.B", evidence["parameterName"]);
      Assert.Equal("INSTANCE", evidence["parameterScope"]);
      Assert.Equal("AMBIGUOUS_CARRIER", evidence["carrierStatus"]);
      Assert.Equal("EMPTY_REQUIRED_VALUE", evidence["parameterStatus"]);
      Assert.Equal("INVALID_VALUE", evidence["revitStatus"]);
      Assert.Equal(" raw-HBR.B ", evidence["revitRawValue"]);
      Assert.Equal("normalized-HBR.B", evidence["revitNormalizedValue"]);
      Assert.Equal("INSTANCE_PARAMETER", evidence["revitValueSource"]);
      Assert.Equal("#7", evidence["rawIfcOwner"]);
      Assert.Equal("Pset_申报信息属性集", evidence["rawIfcPropertySet"]);
      Assert.Equal("字段HBR.B", evidence["rawIfcProperty"]);
      Assert.Equal("IFCLABEL", evidence["rawIfcType"]);
      Assert.Equal("raw-HBR.B", evidence["rawIfcValue"]);
      Assert.Equal("IFC_OWNER_NOT_FOUND", evidence["rawIfcStatus"]);
      Assert.Equal("#8", evidence["finalIfcOwner"]);
      Assert.Equal("Pset_申报信息属性集", evidence["finalIfcPropertySet"]);
      Assert.Equal("字段HBR.B", evidence["finalIfcProperty"]);
      Assert.Equal("IFCLABEL", evidence["finalIfcType"]);
      Assert.Equal("final-HBR.B", evidence["finalIfcValue"]);
      Assert.Equal("IFC_VALUE_MISMATCH", evidence["finalIfcStatus"]);
      Assert.Equal("MISSING_PARAMETER", evidence["status"]);
      Assert.True(Assert.IsType<bool>(evidence["active"]));
      Assert.True(Assert.IsType<bool>(evidence["isBusinessBlocker"]));
      Assert.Equal(
        new[] { "a｜message-HBR.B", "z|message-HBR.B" },
        Messages(evidence));
    }

    [Fact]
    public void All_blockers_are_complete_grouped_and_stably_sorted()
    {
      var gate = new Stage03GateDecision(
        Stage03GateMode.Strict,
        false,
        false,
        string.Empty,
        new[]
        {
          new Stage03BusinessBlocker(
            "IfcProject",
            "uid-b",
            "PROJECT",
            2,
            "HBR.B",
            Stage03FieldStatus.MissingParameter,
            "REQUIRED",
            "missing-b"),
          new Stage03BusinessBlocker(
            "IfcBuilding",
            "uid-a",
            "BUILDING",
            1,
            "HBR.A",
            Stage03FieldStatus.InvalidValue,
            "REQUIRED",
            "invalid-a")
        },
        Array.Empty<string>(),
        Array.Empty<string>());
      var diagnostics = new[]
      {
        new Stage03Diagnostic
        {
          Code = "B",
          Stage = "scan",
          Severity = "INFO",
          Message = "b-info"
        },
        new Stage03Diagnostic
        {
          Code = "Z",
          Stage = "scan",
          Severity = "ERROR",
          Message = "z-message"
        },
        new Stage03Diagnostic
        {
          Code = "A",
          Stage = "translate",
          Severity = "ERROR",
          Message = "a-message"
        },
        new Stage03Diagnostic
        {
          Code = "Y",
          Stage = "translate",
          Severity = "WARNING",
          Message = "y-warning"
        }
      };

      string[] first = Stage03FieldDetailFormatter.FormatAllBlockers(
        gate,
        new[] { "Z_CODE", "A_CODE", "A_CODE" },
        diagnostics).ToArray();
      string[] second = Stage03FieldDetailFormatter.FormatAllBlockers(
        gate,
        new[] { "A_CODE", "Z_CODE" },
        diagnostics.Reverse()).ToArray();

      Assert.Equal(6, first.Length);
      Assert.Equal(first, second);
      Dictionary<string, object>[] records = first
        .Select(ParseRecord)
        .ToArray();
      Assert.Equal("业务阻断", records[0]["kind"]);
      Assert.Equal("IfcBuilding", records[0]["entity"]);
      Assert.Equal("业务阻断", records[1]["kind"]);
      Assert.Equal("IfcProject", records[1]["entity"]);
      Assert.Equal("技术致命", records[2]["kind"]);
      Assert.Equal("A_CODE", records[2]["code"]);
      Assert.Equal("技术致命", records[3]["kind"]);
      Assert.Equal("Z_CODE", records[3]["code"]);
      Assert.Equal("阻断级诊断", records[4]["kind"]);
      Assert.Equal("A", records[4]["code"]);
      Assert.Equal("ERROR", records[4]["severity"]);
      Assert.Equal("阻断级诊断", records[5]["kind"]);
      Assert.Equal("Z", records[5]["code"]);
      Assert.DoesNotContain(
        records,
        record => string.Equals(
          record["kind"] as string,
          "消息",
          StringComparison.Ordinal));
    }

    [Fact]
    public void Clean_success_with_info_and_warning_diagnostics_has_no_blockers()
    {
      var gate = new Stage03GateDecision(
        Stage03GateMode.Strict,
        true,
        false,
        string.Empty,
        Array.Empty<Stage03BusinessBlocker>(),
        Array.Empty<string>(),
        new[] { "Stage03 三件套已生成。" });
      var diagnostics = new[]
      {
        Diagnostic("INFO_CODE", "INFO"),
        Diagnostic("WARNING_CODE", "WARNING")
      };

      Assert.Empty(Stage03FieldDetailFormatter.FormatAllBlockers(
        gate,
        Array.Empty<string>(),
        diagnostics));
    }

    [Fact]
    public void Force_empty_reason_formats_one_stable_typed_business_blocker()
    {
      string baseline = null;
      foreach (string reason in new[] { null, string.Empty, " ", "\t\r\n" })
      {
        Stage03GateDecision gate = Stage03ExportGatePolicy.Decide(
          Stage03GateMode.Force,
          reason,
          new[]
          {
            new Stage03FieldResult
            {
              PropertyId = "HBR.PASS",
              Active = true,
              Status = Stage03FieldStatus.Pass
            }
          },
          Array.Empty<string>());

        string encoded = Assert.Single(
          Stage03FieldDetailFormatter.FormatAllBlockers(
            gate,
            Array.Empty<string>(),
            new[]
            {
              Diagnostic("INFO_CODE", "INFO"),
              Diagnostic("WARNING_CODE", "WARNING")
            }));
        Dictionary<string, object> blocker = ParseRecord(encoded);
        Assert.Equal("业务阻断", blocker["kind"]);
        Assert.Equal("FORCE_REASON_REQUIRED", blocker["status"]);
        Assert.Equal(
          "Force 模式必须提供非空强制原因。",
          blocker["message"]);
        if (baseline == null)
          baseline = encoded;
        else
          Assert.Equal(baseline, encoded);
      }
    }

    [Theory]
    [InlineData("ERROR", "INFO_CODE", true)]
    [InlineData("FATAL", "INFO_CODE", true)]
    [InlineData("CRITICAL", "INFO_CODE", true)]
    [InlineData(" error ", "INFO_CODE", true)]
    [InlineData(" fatal ", "INFO_CODE", true)]
    [InlineData(" critical ", "INFO_CODE", true)]
    [InlineData("INFO", Stage03TechnicalFatalCodes.WrongDocument, true)]
    [InlineData("INFO", Stage03TechnicalFatalCodes.UnsupportedRevit, true)]
    [InlineData("INFO", Stage03TechnicalFatalCodes.DocumentUnavailable, true)]
    [InlineData("INFO", Stage03TechnicalFatalCodes.OutputExists, true)]
    [InlineData("INFO", Stage03TechnicalFatalCodes.ExportFailed, true)]
    [InlineData("INFO", Stage03TechnicalFatalCodes.InvalidIfc, true)]
    [InlineData("INFO", Stage03TechnicalFatalCodes.ReportFailed, true)]
    [InlineData("INFO", Stage03TechnicalFatalCodes.InvalidFieldStatus, true)]
    [InlineData("INFO", " INVALID_IFC ", true)]
    [InlineData("INFO", "translator_fatal", true)]
    [InlineData("INFO", "invalid_ifc", false)]
    [InlineData("INFO", "INFO_CODE", false)]
    [InlineData("WARNING", "WARNING_CODE", false)]
    [InlineData(null, "INFO_CODE", false)]
    [InlineData("INFO", null, false)]
    public void Blocking_diagnostic_policy_is_typed_and_severity_aware(
      string severity,
      string code,
      bool expected)
    {
      Assert.Equal(
        expected,
        Stage03BlockingDiagnosticPolicy.IsBlocking(
          Diagnostic(code, severity)));
    }

    [Fact]
    public void Null_diagnostic_is_fail_closed()
    {
      Assert.True(Stage03BlockingDiagnosticPolicy.IsBlocking(null));
    }

    [Fact]
    public void Force_allow_export_retains_business_blockers()
    {
      var gate = new Stage03GateDecision(
        Stage03GateMode.Force,
        true,
        true,
        "仅用于测试",
        new[]
        {
          new Stage03BusinessBlocker(
            "IfcProject",
            "uid-a",
            "PROJECT",
            1,
            "HBR.A",
            Stage03FieldStatus.InvalidValue,
            "REQUIRED",
            "invalid-a")
        },
        Array.Empty<string>(),
        new[] { "Force 模式已放行。" });

      Dictionary<string, object> blocker = ParseRecord(Assert.Single(
        Stage03FieldDetailFormatter.FormatAllBlockers(
          gate,
          Array.Empty<string>(),
          Array.Empty<Stage03Diagnostic>())));

      Assert.Equal("业务阻断", blocker["kind"]);
      Assert.Equal("IfcProject", blocker["entity"]);
    }

    [Fact]
    public void Json_encoding_distinguishes_and_preserves_delimiter_values()
    {
      Stage03FieldResult singleMessage = Field(
        "IfcProject",
        "PROJECT",
        1,
        "uid-a",
        "HBR.A",
        Stage03FieldStatus.InvalidValue);
      singleMessage.Messages = new[] { "a|b" };
      Stage03FieldResult splitMessages = Field(
        "IfcProject",
        "PROJECT",
        1,
        "uid-a",
        "HBR.A",
        Stage03FieldStatus.InvalidValue);
      splitMessages.Messages = new[] { "a", "b" };

      string singleText = Assert.Single(
        Stage03FieldDetailFormatter.Format(new[] { singleMessage })).Text;
      string splitText = Assert.Single(
        Stage03FieldDetailFormatter.Format(new[] { splitMessages })).Text;

      Assert.NotEqual(singleText, splitText);
      Assert.Equal(new[] { "a|b" }, Messages(ParseRecord(singleText)));
      Assert.Equal(new[] { "a", "b" }, Messages(ParseRecord(splitText)));

      var gate = new Stage03GateDecision(
        Stage03GateMode.Force,
        true,
        true,
        "test",
        new[]
        {
          new Stage03BusinessBlocker(
            "Ifc|Project",
            "uid｜a",
            "PROJECT",
            1,
            "HBR|A",
            Stage03FieldStatus.InvalidValue,
            "REQUIRED",
            "bad|value｜one")
        },
        Array.Empty<string>(),
        Array.Empty<string>());
      var diagnostics = new[]
      {
        new Stage03Diagnostic
        {
          Code = "DIAG|CODE",
          Stage = "stage｜one",
          Severity = "ERROR",
          Message = "diagnostic|message｜one"
        }
      };

      Dictionary<string, object>[] blockers =
        Stage03FieldDetailFormatter.FormatAllBlockers(
          gate,
          new[] { "TECH|CODE｜ONE" },
          diagnostics)
        .Select(ParseRecord)
        .ToArray();

      Assert.Equal("Ifc|Project", blockers[0]["entity"]);
      Assert.Equal("uid｜a", blockers[0]["ownerUniqueId"]);
      Assert.Equal("bad|value｜one", blockers[0]["message"]);
      Assert.Equal("TECH|CODE｜ONE", blockers[1]["code"]);
      Assert.Equal("DIAG|CODE", blockers[2]["code"]);
      Assert.Equal("stage｜one", blockers[2]["stage"]);
      Assert.Equal("diagnostic|message｜one", blockers[2]["message"]);
    }

    [Fact]
    public void Component_failures_are_complete_json_records_with_unambiguous_messages()
    {
      Dictionary<string, object> preflight = ParseRecord(Assert.Single(
        Stage03FieldDetailFormatter.FormatComponentFailure(
          "COMPONENT_PREFLIGHT",
          "COMPONENT_PREFLIGHT",
          "missing|context｜line-one\r\nline-two")));

      Assert.Equal("阻断级诊断", preflight["kind"]);
      Assert.Equal("COMPONENT_PREFLIGHT", preflight["code"]);
      Assert.Equal("COMPONENT_PREFLIGHT", preflight["stage"]);
      Assert.Equal("ERROR", preflight["severity"]);
      Assert.Equal(
        "missing|context｜line-one\r\nline-two",
        preflight["message"]);

      Dictionary<string, object>[] workflowFailure =
        Stage03FieldDetailFormatter.FormatComponentFailure(
          Stage03TechnicalFatalCodes.InvalidIfc,
          "COMPONENT",
          "workflow|failed｜without result\nnext",
          new[] { Stage03TechnicalFatalCodes.InvalidIfc })
        .Select(ParseRecord)
        .ToArray();

      Assert.Equal(2, workflowFailure.Length);
      Assert.Equal("技术致命", workflowFailure[0]["kind"]);
      Assert.Equal(
        Stage03TechnicalFatalCodes.InvalidIfc,
        workflowFailure[0]["code"]);
      Assert.Equal("阻断级诊断", workflowFailure[1]["kind"]);
      Assert.Equal("COMPONENT", workflowFailure[1]["stage"]);
      Assert.Equal(
        "workflow|failed｜without result\nnext",
        workflowFailure[1]["message"]);
    }

    private static Stage03Diagnostic Diagnostic(
      string code,
      string severity)
    {
      return new Stage03Diagnostic
      {
        Code = code,
        Stage = "stage",
        Severity = severity,
        Message = code + "-message"
      };
    }

    private static Dictionary<string, object> ParseRecord(string text)
    {
      return Assert.IsType<Dictionary<string, object>>(
        new JavaScriptSerializer().DeserializeObject(text));
    }

    private static string[] Messages(Dictionary<string, object> record)
    {
      return Assert.IsType<object[]>(record["messages"])
        .Select(value => Assert.IsType<string>(value))
        .ToArray();
    }

    private static Stage03FieldResult Field(
      string entity,
      string role,
      int elementId,
      string owner,
      string propertyId,
      Stage03FieldStatus status)
    {
      return new Stage03FieldResult
      {
        PropertyId = propertyId,
        ContractKind = "OFFICIAL",
        Requirement = "REQUIRED",
        Applicability = "ACTIVE",
        RuntimeStatus = "NOT_IMPLEMENTED",
        RuntimeBlockCode = "OWNER_STRATEGY_NOT_IMPLEMENTED",
        RuntimeBlockReason =
          "当前 IFC owner strategy 尚未实现：CANONICAL_SPATIAL_ZONE_RECORD。",
        Entity = entity,
        PropertySet = "Pset_申报信息属性集",
        IfcProperty = "字段" + propertyId,
        Role = role,
        ElementId = elementId,
        OwnerUniqueId = owner,
        ParameterGuid = "11111111-2222-3333-4444-555555555555",
        ParameterName = "参数-" + propertyId,
        ParameterScope = "INSTANCE",
        CarrierStatus = Stage03FieldStatus.AmbiguousCarrier,
        ParameterStatus = Stage03FieldStatus.EmptyRequiredValue,
        RevitStatus = Stage03FieldStatus.InvalidValue,
        RevitRawValue = " raw-" + propertyId + " ",
        RevitNormalizedValue = "normalized-" + propertyId,
        RevitValueSource = "INSTANCE_PARAMETER",
        Status = status,
        Active = true,
        IsBusinessBlocker = status != Stage03FieldStatus.Pass,
        RawIfcOwner = "#7",
        RawIfcPropertySet = "Pset_申报信息属性集",
        RawIfcProperty = "字段" + propertyId,
        RawIfcType = "IFCLABEL",
        RawIfcValue = "raw-" + propertyId,
        RawIfcStatus = Stage03FieldStatus.IfcOwnerNotFound,
        FinalIfcOwner = "#8",
        FinalIfcPropertySet = "Pset_申报信息属性集",
        FinalIfcProperty = "字段" + propertyId,
        FinalIfcType = "IFCLABEL",
        FinalIfcValue = "final-" + propertyId,
        FinalIfcStatus = Stage03FieldStatus.IfcValueMismatch,
        Messages = new[]
        {
          "z|message-" + propertyId,
          "a｜message-" + propertyId
        }
      };
    }
  }
}
