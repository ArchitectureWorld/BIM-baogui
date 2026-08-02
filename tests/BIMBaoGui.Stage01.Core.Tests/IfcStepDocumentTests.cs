using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Mvd;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class IfcStepDocumentTests
  {
    private const string Fixture =
      "ISO-10303-21;\r\n"
      + "HEADER;\r\n"
      + "FILE_SCHEMA(('IFC4'));\r\n"
      + "ENDSEC;\r\n"
      + "DATA;\r\n"
      + "#11=IFCPROJECT('project-guid',$,'项目信息',$,$,$,$,(#12),#13);\r\n"
      + "#23=IFCPROPERTYSINGLEVALUE(\r\n"
      + "  '基点坐标Y',\r\n"
      + "  $,\r\n"
      + "  IFCREAL(38589642.165),\r\n"
      + "  $);\r\n"
      + "#24=IFCPROPERTYSET('pset-guid',$,'申报信息属性集',$,(#23));\r\n"
      + "#25=IFCCARTESIANPOINT((0.,0.,0.));\r\n"
      + "ENDSEC;\r\n"
      + "END-ISO-10303-21;\r\n";

    [Fact]
    public void Parse_indexes_multiline_entities_and_nested_arguments()
    {
      IfcStepDocument document = IfcStepDocument.Parse(Fixture);

      IfcStepEntity property = document.GetEntity(23);

      Assert.Equal("IFCPROPERTYSINGLEVALUE", property.Type);
      Assert.Equal("'基点坐标Y'", property.Arguments[0]);
      Assert.Equal("IFCREAL(38589642.165)", property.Arguments[2]);
      Assert.Equal(new[] { 23 }, document.OfType("IfcPropertySingleValue")
        .Select(entity => entity.Id)
        .ToArray());
    }

    [Fact]
    public void Serialize_preserves_unknown_statements_and_applies_targeted_changes()
    {
      IfcStepDocument document = IfcStepDocument.Parse(Fixture);
      document.GetEntity(23).SetArgument(
        0,
        IfcStepSyntax.EncodeString("基点坐标 Y"));

      string output = document.Serialize();
      string encodedName = IfcStepSyntax.EncodeString("基点坐标 Y");

      Assert.Contains(
        "#23=IFCPROPERTYSINGLEVALUE(" + encodedName
        + ",$,IFCREAL(38589642.165),$);",
        output);
      Assert.Contains("#25=IFCCARTESIANPOINT((0.,0.,0.));", output);
    }

    [Fact]
    public void Serialize_omits_deleted_entities()
    {
      IfcStepDocument document = IfcStepDocument.Parse(Fixture);
      document.GetEntity(25).Delete();

      string output = document.Serialize();

      Assert.DoesNotContain("#25=IFCCARTESIANPOINT", output);
      Assert.Contains("END-ISO-10303-21;", output);
    }

    [Fact]
    public void Parse_rejects_unbalanced_entity_arguments()
    {
      const string malformed =
        "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\n"
        + "DATA;\n#1=IFCPROPERTYSET('g',$,'P',$,(#2);\nENDSEC;\n"
        + "END-ISO-10303-21;\n";

      Assert.Throws<InvalidDataException>(
        () => IfcStepDocument.Parse(malformed));
    }

    [Theory]
    [InlineData("普通文本")]
    [InlineData("O'Brien")]
    [InlineData("HIFC.申报信息属性集.项目名称")]
    public void Step_string_round_trips(string value)
    {
      string encoded = IfcStepSyntax.EncodeString(value);

      Assert.Equal(value, IfcStepSyntax.DecodeString(encoded));
    }
  }
}
