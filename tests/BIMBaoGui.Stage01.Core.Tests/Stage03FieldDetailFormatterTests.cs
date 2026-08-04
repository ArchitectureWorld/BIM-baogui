using System;
using System.Linq;
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
    public void Detail_contains_entity_owner_property_status_and_complete_ifc_evidence()
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

      Assert.Contains("实体=IfcProject", detail.Text);
      Assert.Contains("owner=uid-a", detail.Text);
      Assert.Contains("property=HBR.B", detail.Text);
      Assert.Contains("status=MISSING_PARAMETER", detail.Text);
      Assert.Contains(
        "RAW=PASS|#7|Pset_申报信息属性集.字段HBR.B|IFCLABEL|raw-HBR.B",
        detail.Text);
      Assert.Contains(
        "FINAL=IFC_VALUE_MISMATCH|#8|Pset_申报信息属性集.字段HBR.B|IFCLABEL|final-HBR.B",
        detail.Text);
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
        }
      };

      string[] first = Stage03FieldDetailFormatter.FormatAllBlockers(
        gate,
        new[] { "Z_CODE", "A_CODE", "A_CODE" },
        diagnostics,
        new[] { "z-message", "a-message", "a-message" }).ToArray();
      string[] second = Stage03FieldDetailFormatter.FormatAllBlockers(
        gate,
        new[] { "A_CODE", "Z_CODE" },
        diagnostics.Reverse(),
        new[] { "a-message", "z-message" }).ToArray();

      Assert.Equal(8, first.Length);
      Assert.Equal(first, second);
      Assert.StartsWith("业务阻断|IfcBuilding", first[0]);
      Assert.StartsWith("业务阻断|IfcProject", first[1]);
      Assert.Equal("技术致命|A_CODE", first[2]);
      Assert.Equal("技术致命|Z_CODE", first[3]);
      Assert.StartsWith("诊断|A|translate|ERROR|a-message", first[4]);
      Assert.StartsWith("诊断|Z|scan|ERROR|z-message", first[5]);
      Assert.Equal("消息|a-message", first[6]);
      Assert.Equal("消息|z-message", first[7]);
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
        Entity = entity,
        PropertySet = "Pset_申报信息属性集",
        IfcProperty = "字段" + propertyId,
        Role = role,
        ElementId = elementId,
        OwnerUniqueId = owner,
        Status = status,
        Active = true,
        IsBusinessBlocker = status != Stage03FieldStatus.Pass,
        RawIfcOwner = "#7",
        RawIfcPropertySet = "Pset_申报信息属性集",
        RawIfcProperty = "字段" + propertyId,
        RawIfcType = "IFCLABEL",
        RawIfcValue = "raw-" + propertyId,
        RawIfcStatus = Stage03FieldStatus.Pass,
        FinalIfcOwner = "#8",
        FinalIfcPropertySet = "Pset_申报信息属性集",
        FinalIfcProperty = "字段" + propertyId,
        FinalIfcType = "IFCLABEL",
        FinalIfcValue = "final-" + propertyId,
        FinalIfcStatus = Stage03FieldStatus.IfcValueMismatch
      };
    }
  }
}
