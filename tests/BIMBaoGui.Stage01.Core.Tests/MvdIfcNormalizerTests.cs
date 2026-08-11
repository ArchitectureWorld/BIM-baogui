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

    [Theory]
    [InlineData(
      "IFCORGANIZATION",
      "组织通用属性集",
      "成立日期",
      "IFCTEXT('not-a-date')")]
    [InlineData(
      "IFCORGANIZATION",
      "组织通用属性集",
      "项目进场时间",
      "IFCTEXT('2026-02-30T25:61:00')")]
    [InlineData(
      "IFCBUILDINGSTOREY",
      "建筑楼层信息属性集",
      "是否设置避难层",
      "IFCLOGICAL(.U.)")]
    public void Normalize_rejects_invalid_typed_literals(
      string ownerType,
      string propertySet,
      string property,
      string value)
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateSinglePropertyFixture(ownerType, propertySet, property, value));

      Assert.Throws<InvalidDataException>(
        () => new MvdIfcNormalizer().Normalize(document));
    }

    [Theory]
    [InlineData(
      "IFCORGANIZATION",
      "组织通用属性集",
      "成立日期",
      "IFCTEXT('2026-08-02')",
      "IFCDATE('2026-08-02')")]
    [InlineData(
      "IFCORGANIZATION",
      "组织通用属性集",
      "项目进场时间",
      "IFCTEXT('2026-08-02T18:30:45')",
      "IFCDATETIME('2026-08-02T18:30:45')")]
    [InlineData(
      "IFCBUILDINGSTOREY",
      "建筑楼层信息属性集",
      "是否设置避难层",
      "IFCLOGICAL(.T.)",
      "IFCBOOLEAN(.T.)")]
    public void Normalize_accepts_valid_typed_literals(
      string ownerType,
      string propertySet,
      string property,
      string value,
      string expected)
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateSinglePropertyFixture(ownerType, propertySet, property, value));

      MvdIfcNormalizationResult result =
        new MvdIfcNormalizer().Normalize(document);

      Assert.True(result.Success);
      Assert.Contains(expected, document.Serialize());
    }

    [Fact]
    public void Normalize_official_factory_shape_changes_exactly_seven_labels()
    {
      const string fixture =
        "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n"
        + "#10=IFCPROJECT('project-guid',$,'P',$,$,$,$,(#11),#12);\n"
        + "#20=IFCBUILDING('building-guid',$,'1#',$,$,$,$,$,$,$,$,$);\n"
        + "#21=IFCBUILDINGSTOREY('storey-1-guid',$,'1F',$,$,$,$,$,$,$);\n"
        + "#22=IFCBUILDINGSTOREY('storey-2-guid',$,'RF',$,$,$,$,$,$,$);\n"
        + "#30=IFCPROPERTYSINGLEVALUE('建筑名称',$,IFCTEXT('1#'),$);\n"
        + "#31=IFCPROPERTYSINGLEVALUE('耐火等级',$,IFCTEXT('一级'),$);\n"
        + "#32=IFCPROPERTYSINGLEVALUE('安全等级',$,IFCTEXT('普通建筑'),$);\n"
        + "#33=IFCPROPERTYSINGLEVALUE('建筑类型名称',$,IFCTEXT('厂房'),$);\n"
        + "#34=IFCPROPERTYSINGLEVALUE('建筑用途名称',$,IFCTEXT('裙房'),$);\n"
        + "#35=IFCPROPERTYSET('building-pset-guid',$,'Pset_建筑技术信息属性集',$,(#30,#31,#32,#33,#34));\n"
        + "#36=IFCRELDEFINESBYPROPERTIES('building-rel-guid',$,$,$,(#20),#35);\n"
        + "#40=IFCPROPERTYSINGLEVALUE('楼层名称',$,IFCTEXT('1F'),$);\n"
        + "#41=IFCPROPERTYSET('storey-1-pset-guid',$,'Pset_建筑楼层信息属性集',$,(#40));\n"
        + "#42=IFCRELDEFINESBYPROPERTIES('storey-1-rel-guid',$,$,$,(#21),#41);\n"
        + "#50=IFCPROPERTYSINGLEVALUE('楼层名称',$,IFCTEXT('RF'),$);\n"
        + "#51=IFCPROPERTYSET('storey-2-pset-guid',$,'Pset_建筑楼层信息属性集',$,(#50));\n"
        + "#52=IFCRELDEFINESBYPROPERTIES('storey-2-rel-guid',$,$,$,(#22),#51);\n"
        + "ENDSEC;\nEND-ISO-10303-21;\n";
      IfcStepDocument document = IfcStepDocument.Parse(fixture);

      MvdIfcNormalizationResult result =
        new MvdIfcNormalizer().Normalize(document);
      string output = document.Serialize();

      Assert.Equal(7, result.MatchingPropertyCount);
      Assert.Equal(7, result.NormalizedValueTypeCount);
      Assert.Equal(7, CountOccurrences(output, "IFCLABEL("));
      Assert.DoesNotContain("IFCTEXT(", output);
    }

    [Fact]
    public void Normalize_counts_shared_property_once_across_multiple_owners()
    {
      const string fixture =
        "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n"
        + "#10=IFCPROJECT('project-guid',$,'P',$,$,$,$,(#11),#12);\n"
        + "#20=IFCBUILDING('building-1-guid',$,'1#',$,$,$,$,$,$,$,$,$);\n"
        + "#21=IFCBUILDING('building-2-guid',$,'2#',$,$,$,$,$,$,$,$,$);\n"
        + "#30=IFCPROPERTYSINGLEVALUE('建筑名称',$,IFCTEXT('共享名称'),$);\n"
        + "#31=IFCPROPERTYSET('building-pset-guid',$,'Pset_建筑技术信息属性集',$,(#30));\n"
        + "#32=IFCRELDEFINESBYPROPERTIES('building-rel-guid',$,$,$,(#20,#21),#31);\n"
        + "ENDSEC;\nEND-ISO-10303-21;\n";
      IfcStepDocument document = IfcStepDocument.Parse(fixture);
      var normalizer = new MvdIfcNormalizer();

      MvdIfcNormalizationResult result = normalizer.Normalize(document);
      MvdIfcValidationResult validation = normalizer.Validate(document);

      Assert.Equal(1, result.MatchingPropertyCount);
      Assert.Equal(1, validation.MatchingPropertyCount);
      Assert.True(validation.Success, string.Join(" | ", validation.Messages));
    }

    private static string CreateSinglePropertyFixture(
      string ownerType,
      string propertySet,
      string property,
      string value)
    {
      return "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n"
        + "#10=IFCPROJECT('project-guid',$,'P',$,$,$,$,(#11),#12);\n"
        + "#20=" + ownerType + "('owner-guid',$,'Owner',$,$,$,$,$,$,$,$,$);\n"
        + "#30=IFCPROPERTYSINGLEVALUE('" + property + "',$," + value + ",$);\n"
        + "#31=IFCPROPERTYSET('pset-guid',$,'Pset_" + propertySet + "',$,(#30));\n"
        + "#32=IFCRELDEFINESBYPROPERTIES('rel-guid',$,$,$,(#20),#31);\n"
        + "ENDSEC;\nEND-ISO-10303-21;\n";
    }

    private static int CountOccurrences(string value, string token)
    {
      int count = 0;
      int index = 0;
      while ((index = value.IndexOf(token, index, System.StringComparison.Ordinal)) >= 0)
      {
        count++;
        index += token.Length;
      }
      return count;
    }
  }
}
