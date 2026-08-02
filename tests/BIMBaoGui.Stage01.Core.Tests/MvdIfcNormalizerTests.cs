using System.IO;
using BIMBaoGui.Stage01.Mvd;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class MvdIfcNormalizerTests
  {
    private const string Fixture =
      "ISO-10303-21;\n"
      + "HEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n"
      + "#11=IFCPROJECT('project-guid',$,'项目信息',$,$,$,$,(#12),#13);\n"
      + "#23=IFCPROPERTYSINGLEVALUE('基点坐标Y',$,IFCREAL(38589642.165),$);\n"
      + "#34=IFCPROPERTYSINGLEVALUE('高程系名称',$,IFCTEXT('1985国家高程基准'),$);\n"
      + "#35=IFCPROPERTYSINGLEVALUE('坐标系名称',$,IFCTEXT('CGCS2000'),$);\n"
      + "#43=IFCPROPERTYSINGLEVALUE('基点高程',$,IFCREAL(24.),$);\n"
      + "#46=IFCPROPERTYSINGLEVALUE('基点坐标X',$,IFCREAL(3373266.866),$);\n"
      + "#47=IFCPROPERTYSINGLEVALUE('项目编号',$,IFCTEXT('HB-2026-001'),$);\n"
      + "#49=IFCPROPERTYSINGLEVALUE('项目名称',$,IFCTEXT('武汉市项目'),$);\n"
      + "#24=IFCPROPERTYSET('formal-guid',$,'申报信息属性集',$,(#23,#34,#35,#43,#46,#47,#49));\n"
      + "#63=IFCRELDEFINESBYPROPERTIES('formal-rel',$,$,$,(#11),#24);\n"
      + "#30=IFCPROPERTYSINGLEVALUE('HIFC.申报信息属性集.基点坐标Y',$,IFCTEXT('126606437.549213'),$);\n"
      + "#37=IFCPROPERTYSINGLEVALUE('HIFC.申报信息属性集.基点高程',$,IFCTEXT('78.7401574803149'),$);\n"
      + "#55=IFCPROPERTYSINGLEVALUE('HIFC.申报信息属性集.基点坐标X',$,IFCTEXT('11067148.5104987'),$);\n"
      + "#64=IFCPROPERTYSINGLEVALUE('第三方属性',$,IFCTEXT('保留'),$);\n"
      + "#28=IFCPROPERTYSET('data-guid',$,'数据',$,(#30,#37,#55,#64));\n"
      + "#65=IFCRELDEFINESBYPROPERTIES('data-rel',$,$,$,(#11),#28);\n"
      + "ENDSEC;\nEND-ISO-10303-21;\n";

    [Fact]
    public void Normalize_adds_Pset_prefix_without_changing_coordinate_values()
    {
      IfcStepDocument document = IfcStepDocument.Parse(Fixture);

      MvdIfcNormalizationResult result =
        new MvdIfcNormalizer().Normalize(document);
      string output = document.Serialize();

      Assert.True(result.Success);
      Assert.Contains(
        "IFCPROPERTYSET('formal-guid',$,"
        + IfcStepSyntax.EncodeString("Pset_申报信息属性集"),
        output);
      Assert.Contains(
        "IFCPROPERTYSINGLEVALUE('基点坐标X',$,IFCREAL(3373266.866),$)",
        output);
      Assert.Contains(
        "IFCPROPERTYSINGLEVALUE('基点坐标Y',$,IFCREAL(38589642.165),$)",
        output);
      Assert.Contains("IFCREAL(24.)", output);
    }

    [Fact]
    public void Normalize_converts_project_identity_to_IfcLabel()
    {
      IfcStepDocument document = IfcStepDocument.Parse(Fixture);

      new MvdIfcNormalizer().Normalize(document);
      string output = document.Serialize();

      Assert.Contains("IFCLABEL('HB-2026-001')", output);
      Assert.Contains("IFCLABEL('武汉市项目')", output);
    }

    [Fact]
    public void Normalize_removes_only_HIFC_duplicates_from_Data_pset()
    {
      IfcStepDocument document = IfcStepDocument.Parse(Fixture);

      MvdIfcNormalizationResult result =
        new MvdIfcNormalizer().Normalize(document);
      string output = document.Serialize();

      Assert.Equal(3, result.RemovedDuplicatePropertyCount);
      Assert.DoesNotContain("#30=IFCPROPERTYSINGLEVALUE", output);
      Assert.DoesNotContain("#37=IFCPROPERTYSINGLEVALUE", output);
      Assert.DoesNotContain("#55=IFCPROPERTYSINGLEVALUE", output);
      Assert.Contains("#64=IFCPROPERTYSINGLEVALUE", output);
      Assert.Contains("#28=IFCPROPERTYSET('data-guid',$,'数据',$,(#64));", output);
      Assert.Contains("#65=IFCRELDEFINESBYPROPERTIES", output);
    }

    [Fact]
    public void Validate_accepts_the_normalized_document()
    {
      IfcStepDocument document = IfcStepDocument.Parse(Fixture);
      var normalizer = new MvdIfcNormalizer();
      normalizer.Normalize(document);

      MvdIfcValidationResult validation = normalizer.Validate(document);

      Assert.True(validation.Success, string.Join(" | ", validation.Messages));
      Assert.True(validation.MatchingPropertyCount >= 7);
    }

    [Fact]
    public void Normalize_rejects_non_IFC4_documents()
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        Fixture.Replace("FILE_SCHEMA(('IFC4'))", "FILE_SCHEMA(('IFC2X3'))"));

      Assert.Throws<InvalidDataException>(
        () => new MvdIfcNormalizer().Normalize(document));
    }
  }
}
