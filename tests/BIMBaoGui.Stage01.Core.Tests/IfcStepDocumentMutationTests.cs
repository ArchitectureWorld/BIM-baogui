using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Mvd;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class IfcStepDocumentMutationTests
  {
    [Fact]
    public void AddEntity_inserts_canonical_entity_before_data_endsec()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n"));

      IfcStepEntity added = document.AddEntity(
        "IfcPropertySingleValue",
        new[] { "'Name'", "$", "IFCLABEL('Value')", "$" });

      string output = document.Serialize();
      int dataIndex = output.IndexOf("DATA;", System.StringComparison.Ordinal);
      int entityIndex = output.IndexOf(
        "#8=IFCPROPERTYSINGLEVALUE('Name',$,IFCLABEL('Value'),$);",
        System.StringComparison.Ordinal);
      int dataEndIndex = output.IndexOf(
        "ENDSEC;",
        dataIndex,
        System.StringComparison.Ordinal);

      Assert.Equal(8, added.Id);
      Assert.True(entityIndex > dataIndex);
      Assert.True(entityIndex < dataEndIndex);
    }

    [Fact]
    public void AddEntity_uses_highest_allocated_id_even_when_entity_was_deleted()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#2=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n"
        + "#41=IFCCARTESIANPOINT((0.,0.,0.));\r\n"));
      document.GetEntity(41).Delete();

      IfcStepEntity added = document.AddEntity(
        "IFCCARTESIANPOINT",
        new[] { "(1.,2.,3.)" });

      Assert.Equal(42, added.Id);
      Assert.DoesNotContain("#41=", document.Serialize());
    }

    [Fact]
    public void AddEntity_round_trips_and_occurs_exactly_once()
    {
      string fixture = CreateFixture(
        "/* VENDOR_TOKEN; */\r\n"
        + "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n");
      IfcStepDocument document = IfcStepDocument.Parse(fixture);

      document.AddEntity("IFCCARTESIANPOINT", new[] { "(1.,2.,3.)" });
      string output = document.Serialize();
      IfcStepDocument reparsed = IfcStepDocument.Parse(output);

      Assert.Equal("IFCCARTESIANPOINT", reparsed.GetEntity(8).Type);
      Assert.Equal("(1.,2.,3.)", reparsed.GetEntity(8).Arguments.Single());
      Assert.Equal(1, CountOccurrences(output, "#8=IFCCARTESIANPOINT"));
      Assert.Contains("VENDOR_TOKEN;", output);
      Assert.StartsWith("ISO-10303-21;\r\nHEADER;", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddEntity_rejects_missing_or_duplicate_data_boundary(bool duplicate)
    {
      string fixture = CreateFixture(
        "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n");
      fixture = duplicate
        ? fixture.Replace("DATA;\r\n", "DATA;\r\nDATA;\r\n")
        : fixture.Replace("DATA;\r\n", string.Empty);
      IfcStepDocument document = IfcStepDocument.Parse(fixture);
      string before = document.Serialize();

      Assert.Throws<InvalidDataException>(() => document.AddEntity(
        "IFCCARTESIANPOINT",
        new[] { "(1.,2.,3.)" }));
      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void AddEntity_rejects_exhausted_entity_id_without_mutation()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#2147483647=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n"));
      string before = document.Serialize();

      Assert.Throws<InvalidDataException>(() => document.AddEntity(
        "IFCCARTESIANPOINT",
        new[] { "(1.,2.,3.)" }));
      Assert.Equal(before, document.Serialize());
    }

    [Theory]
    [InlineData("IFCPROPERTYSET);#99=IFCPROJECT")]
    [InlineData("IFC PROPERTY SET")]
    [InlineData("")]
    public void AddEntity_rejects_invalid_entity_type_without_mutation(
      string type)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n"));
      string before = document.Serialize();

      Assert.Throws<ArgumentException>(() => document.AddEntity(
        type,
        new[] { "(1.,2.,3.)" }));
      Assert.Equal(before, document.Serialize());
    }

    [Theory]
    [InlineData("IFCLABEL('safe'));#99=IFCPROJECT('injected'")]
    [InlineData("'unterminated")]
    [InlineData("#1,#2")]
    [InlineData("")]
    public void AddEntity_rejects_invalid_argument_without_mutation(
      string argument)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n"));
      string before = document.Serialize();

      Assert.Throws<ArgumentException>(() => document.AddEntity(
        "IFCCARTESIANPOINT",
        new[] { argument }));
      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void AddEntity_ignores_step_boundaries_and_entities_inside_comments()
    {
      const string fixture =
        "ISO-10303-21;\r\n"
        + "HEADER;\r\n"
        + "/* vendor; DATA; ENDSEC; #999=IFCPROJECT('fake'); */\r\n"
        + "FILE_SCHEMA(('IFC4'));\r\n"
        + "ENDSEC;\r\n"
        + "DATA;\r\n"
        + "/* before entity; DATA; */\r\n"
        + "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n"
        + "/* before data end; ENDSEC; */\r\n"
        + "ENDSEC;\r\n"
        + "END-ISO-10303-21;\r\n";
      IfcStepDocument document = IfcStepDocument.Parse(fixture);

      IfcStepEntity added = document.AddEntity(
        "IFCCARTESIANPOINT",
        new[] { "(1.,2.,3.)" });
      string output = document.Serialize();
      IfcStepDocument reparsed = IfcStepDocument.Parse(output);

      Assert.Equal(8, added.Id);
      Assert.False(reparsed.TryGetEntity(999, out _));
      Assert.Equal("IFCPROJECT", reparsed.GetEntity(7).Type);
      Assert.Equal("IFCCARTESIANPOINT", reparsed.GetEntity(8).Type);
      Assert.Contains(
        "/* vendor; DATA; ENDSEC; #999=IFCPROJECT('fake'); */",
        output);
      Assert.Contains("/* before entity; DATA; */", output);
      Assert.Contains("/* before data end; ENDSEC; */", output);
      Assert.Equal(1, CountOccurrences(output, "#8=IFCCARTESIANPOINT"));
    }

    [Fact]
    public void SplitTopLevelArguments_ignores_comments_but_keeps_comment_text_in_strings()
    {
      IReadOnlyList<string> arguments = IfcStepSyntax.SplitTopLevelArguments(
        "'Name'/* vendor,) ; */,/* between,); */$,"
        + "IFCTEXT('literal /* not comment */ ,);'),$");

      Assert.Equal(
        new[]
        {
          "'Name'",
          "$",
          "IFCTEXT('literal /* not comment */ ,);')",
          "$"
        },
        arguments);
    }

    [Fact]
    public void SplitTopLevelArguments_rejects_unclosed_comment()
    {
      Assert.Throws<InvalidDataException>(() =>
        IfcStepSyntax.SplitTopLevelArguments(
          "'Name',/* vendor never closes"));
    }

    [Fact]
    public void Parse_preserves_unmodified_entity_with_trailing_comment_exactly()
    {
      string fixture = CreateFixture(
        "#7=IFCPROPERTYSINGLEVALUE('Name',$,"
        + "IFCTEXT('literal /* not comment */'),$)"
        + "/* vendor trailing ,) ; */;\r\n");

      IfcStepDocument document = IfcStepDocument.Parse(fixture);

      Assert.Equal(4, document.GetEntity(7).Arguments.Count);
      Assert.Equal(fixture, document.Serialize());
    }

    [Fact]
    public void AddEntity_accepts_comments_as_trivia_without_allowing_statement_tokens()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n"));

      IfcStepEntity added = document.AddEntity(
        "IFCPROPERTYSINGLEVALUE",
        new[]
        {
          "'Name'/* vendor,) ; = */",
          "/* description */$",
          "IFCTEXT('literal /* not comment */ ;=')",
          "$"
        });

      Assert.Equal(
        new[]
        {
          "'Name'",
          "$",
          "IFCTEXT('literal /* not comment */ ;=')",
          "$"
        },
        added.Arguments);
    }

    [Fact]
    public void Parse_and_AddEntity_accept_inline_comments_in_markers_and_schema()
    {
      const string fixture =
        "ISO-10303-21;\r\n"
        + "HEADER/* marker vendor */;\r\n"
        + "FILE_SCHEMA/* schema vendor */((/* list */'IFC4'));\r\n"
        + "ENDSEC/* header end vendor */;\r\n"
        + "DATA/* data vendor */;\r\n"
        + "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n"
        + "ENDSEC/* data end vendor */;\r\n"
        + "END-ISO-10303-21;\r\n";
      IfcStepDocument document = IfcStepDocument.Parse(fixture);

      IfcStepEntity added = document.AddEntity(
        "IFCCARTESIANPOINT",
        new[] { "(1.,2.,3.)" });

      Assert.Equal("IFC4", document.Schema);
      Assert.Equal(8, added.Id);
      Assert.Contains("HEADER/* marker vendor */;", document.Serialize());
      Assert.Contains(
        "FILE_SCHEMA/* schema vendor */((/* list */'IFC4'));",
        document.Serialize());
    }

    [Fact]
    public void AddEntity_rejects_commented_header_record_inside_DATA()
    {
      string fixture = CreateFixture(
        "FILE_DESCRIPTION/* vendor */(('outside header'),'2;1');\r\n"
        + "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n");
      IfcStepDocument document = IfcStepDocument.Parse(fixture);
      string before = document.Serialize();

      Assert.Throws<InvalidDataException>(() => document.AddEntity(
        "IFCCARTESIANPOINT",
        new[] { "(1.,2.,3.)" }));

      Assert.Equal(before, document.Serialize());
    }

    [Theory]
    [InlineData("FOO();")]
    [InlineData("42;")]
    public void AddEntity_rejects_raw_non_entity_statement_inside_data_without_mutation(
      string rawStatement)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        rawStatement + "\r\n"
        + "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n"));
      string before = document.Serialize();

      Assert.Throws<InvalidDataException>(() => document.AddEntity(
        "IFCCARTESIANPOINT",
        new[] { "(1.,2.,3.)" }));

      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void Structure_comment_normalization_does_not_strip_string_content()
    {
      const string literal = "literal /* not a comment */ DATA/*fake*/;";
      string fixture =
        "ISO-10303-21;\r\n"
        + "HEADER;\r\n"
        + "FILE_DESCRIPTION(('" + literal + "'),'2;1');\r\n"
        + "FILE_SCHEMA(('IFC4'));\r\n"
        + "ENDSEC;\r\n"
        + "DATA;\r\n"
        + "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n"
        + "ENDSEC;\r\n"
        + "END-ISO-10303-21;\r\n";
      IfcStepDocument document = IfcStepDocument.Parse(fixture);

      document.AddEntity("IFCCARTESIANPOINT", new[] { "(1.,2.,3.)" });

      Assert.Contains("'" + literal + "'", document.Serialize());
    }

    [Fact]
    public void Updating_one_argument_keeps_other_commented_arguments_semantically_valid()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROPERTYSINGLEVALUE("
        + "'Name'/* vendor,) ; */,"
        + "/* description */'Description',"
        + "IFCREAL(1.0),/* unit */$);\r\n"));

      document.GetEntity(7).SetArgument(2, "IFCREAL(2.0)");
      IfcStepDocument reparsed = IfcStepDocument.Parse(document.Serialize());
      IfcStepEntity property = reparsed.GetEntity(7);

      Assert.Equal("Name", IfcStepSyntax.DecodeString(property.Arguments[0]));
      Assert.Equal(
        "Description",
        IfcStepSyntax.DecodeString(property.Arguments[1]));
      Assert.Equal("IFCREAL(2.0)", property.Arguments[2]);
      Assert.Equal("$", property.Arguments[3]);
    }

    [Fact]
    public void Entity_arguments_expose_an_IReadOnlyList_contract()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROPERTYSINGLEVALUE('Name',$,IFCREAL(1.0),$);\r\n"));
      IfcStepEntity entity = document.GetEntity(7);

      IReadOnlyList<string> arguments = entity.Arguments;
      System.Reflection.PropertyInfo property = typeof(IfcStepEntity)
        .GetProperty(nameof(IfcStepEntity.Arguments));

      Assert.NotNull(property);
      Assert.Equal(typeof(IReadOnlyList<string>), property.PropertyType);
      Assert.Equal("IFCREAL(1.0)", arguments[2]);
    }

    [Fact]
    public void Entity_arguments_runtime_collection_cannot_bypass_SetArgument()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROPERTYSINGLEVALUE('Name',$,IFCREAL(1.0),$);\r\n"));
      IfcStepEntity entity = document.GetEntity(7);
      string before = document.Serialize();
      IList<string> runtimeList = Assert.IsAssignableFrom<IList<string>>(
        entity.Arguments);

      Assert.Throws<NotSupportedException>(() =>
        runtimeList[2] = "IFCREAL(2.0)");

      Assert.Equal("IFCREAL(1.0)", entity.Arguments[2]);
      Assert.Equal(before, document.Serialize());
    }

    [Theory]
    [InlineData("IFCLABEL('safe'));#99=IFCPROJECT('injected')")]
    [InlineData("#1,#2")]
    [InlineData("/* never closed")]
    public void SetArgument_rejects_invalid_single_argument_without_mutation(
      string invalidArgument)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROPERTYSINGLEVALUE('Name',$,IFCREAL(1.0),$);\r\n"));
      IfcStepEntity entity = document.GetEntity(7);
      string before = document.Serialize();

      Assert.Throws<ArgumentException>(() =>
        entity.SetArgument(2, invalidArgument));

      Assert.Equal("IFCREAL(1.0)", entity.Arguments[2]);
      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void SetArgument_uses_the_same_comment_aware_normalization_as_AddEntity()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROPERTYSINGLEVALUE('Name',$,IFCREAL(1.0),$);\r\n"));
      IfcStepEntity entity = document.GetEntity(7);

      entity.SetArgument(
        2,
        "/* leading */ IFCREAL(2.0) /* trailing ,);= */");

      Assert.Equal("IFCREAL(2.0)", entity.Arguments[2]);
      Assert.Contains(
        "#7=IFCPROPERTYSINGLEVALUE('Name',$,IFCREAL(2.0),$);",
        document.Serialize());
    }

    [Fact]
    public void EncodeString_uses_x2_for_bmp_and_x4_for_non_bmp_characters()
    {
      string emoji = char.ConvertFromUtf32(0x1F600);

      Assert.Equal("'\\X2\\4E2D\\X0\\'", IfcStepSyntax.EncodeString("中"));
      Assert.Equal(
        "'\\X4\\0001F600\\X0\\'",
        IfcStepSyntax.EncodeString(emoji));
    }

    [Fact]
    public void Step_string_x2_x4_round_trips_mixed_unicode()
    {
      string value = "ASCII-中-" + char.ConvertFromUtf32(0x1F600) + "-终";

      string encoded = IfcStepSyntax.EncodeString(value);

      Assert.Contains("\\X2\\4E2D\\X0\\", encoded);
      Assert.Contains("\\X4\\0001F600\\X0\\", encoded);
      Assert.Equal(value, IfcStepSyntax.DecodeString(encoded));
    }

    [Theory]
    [InlineData('\uD83D')]
    [InlineData('\uDE00')]
    public void EncodeString_rejects_isolated_surrogate(char surrogate)
    {
      Assert.Throws<InvalidDataException>(() =>
        IfcStepSyntax.EncodeString(new string(surrogate, 1)));
    }

    [Fact]
    public void DecodeString_decodes_valid_x4_code_point()
    {
      Assert.Equal(
        char.ConvertFromUtf32(0x1F600),
        IfcStepSyntax.DecodeString("'\\X4\\0001F600\\X0\\'"));
    }

    [Fact]
    public void DecodeString_decodes_valid_x2_bmp_code_unit()
    {
      Assert.Equal("中", IfcStepSyntax.DecodeString("'\\X2\\4E2D\\X0\\'"));
    }

    [Theory]
    [InlineData("'\\X2\\D800\\X0\\'")]
    [InlineData("'\\X2\\DC00\\X0\\'")]
    [InlineData("'\\X2\\D83DDE00\\X0\\'")]
    public void DecodeString_rejects_surrogate_code_units_in_x2(string token)
    {
      Assert.Throws<InvalidDataException>(() =>
        IfcStepSyntax.DecodeString(token));
    }

    [Theory]
    [InlineData("'\\X4\\\\X0\\'")]
    [InlineData("'\\X4\\0001F60\\X0\\'")]
    [InlineData("'\\X4\\0001F60G\\X0\\'")]
    [InlineData("'\\X4\\00110000\\X0\\'")]
    [InlineData("'\\X4\\0000D800\\X0\\'")]
    public void DecodeString_rejects_invalid_x4_payload(string token)
    {
      Assert.Throws<InvalidDataException>(() =>
        IfcStepSyntax.DecodeString(token));
    }

    public static IEnumerable<object[]> InvalidSectionMarkerFixtures()
    {
      const string prefix = "ISO-10303-21;\r\n";
      const string schema = "FILE_SCHEMA(('IFC4'));\r\n";
      const string entity =
        "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n";
      const string suffix = "END-ISO-10303-21;\r\n";
      yield return new object[]
      {
        prefix + "DATA;\r\nHEADER;\r\n" + schema + entity
        + "ENDSEC;\r\n" + suffix
      };
      yield return new object[]
      {
        prefix + "HEADER;\r\n" + schema
        + "ENDSEC;\r\nENDSEC;\r\nDATA;\r\n" + entity
        + "ENDSEC;\r\n" + suffix
      };
      yield return new object[]
      {
        prefix + schema + "DATA;\r\n" + entity + "ENDSEC;\r\n" + suffix
      };
      yield return new object[]
      {
        prefix + "HEADER;\r\nHEADER;\r\n" + schema
        + "ENDSEC;\r\nDATA;\r\n" + entity + "ENDSEC;\r\n" + suffix
      };
      yield return new object[]
      {
        prefix + "HEADER;\r\n" + schema + "DATA;\r\n" + entity
        + "ENDSEC;\r\n" + suffix
      };
      yield return new object[]
      {
        prefix + "HEADER;\r\n" + schema + "ENDSEC;\r\n"
        + "DATA;\r\nDATA;\r\n" + entity + "ENDSEC;\r\n" + suffix
      };
      yield return new object[]
      {
        prefix + "HEADER;\r\n" + schema + "ENDSEC;\r\n"
        + "DATA;\r\n" + entity + suffix
      };
    }

    [Theory]
    [MemberData(nameof(InvalidSectionMarkerFixtures))]
    public void AddEntity_rejects_invalid_header_data_marker_state_without_mutation(
      string fixture)
    {
      IfcStepDocument document = IfcStepDocument.Parse(fixture);
      string before = document.Serialize();

      Assert.Throws<InvalidDataException>(() => document.AddEntity(
        "IFCCARTESIANPOINT",
        new[] { "(1.,2.,3.)" }));
      Assert.Equal(before, document.Serialize());
    }

    public static IEnumerable<object[]> InvalidExchangeStructureFixtures()
    {
      string valid = CreateFixture(
        "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n");
      yield return new object[]
      {
        valid.Replace("ISO-10303-21;\r\n", string.Empty)
      };
      yield return new object[]
      {
        valid.Replace(
          "ISO-10303-21;\r\n",
          "ISO-10303-21;\r\nISO-10303-21;\r\n")
      };
      yield return new object[]
      {
        valid.Replace("END-ISO-10303-21;\r\n", string.Empty)
      };
      yield return new object[]
      {
        valid.Replace(
          "END-ISO-10303-21;\r\n",
          "END-ISO-10303-21;\r\nEND-ISO-10303-21;\r\n")
      };
      yield return new object[]
      {
        "ISO-10303-21;\r\n"
        + "#7=IFCPROJECT('outside',$,'Outside',$,$,$,$,$,$);\r\n"
        + "HEADER;\r\nFILE_SCHEMA(('IFC4'));\r\nENDSEC;\r\n"
        + "DATA;\r\n#8=IFCCARTESIANPOINT((0.,0.,0.));\r\n"
        + "ENDSEC;\r\nEND-ISO-10303-21;\r\n"
      };
      yield return new object[]
      {
        "ISO-10303-21;\r\nHEADER;\r\n"
        + "#7=IFCPROJECT('header',$,'Header',$,$,$,$,$,$);\r\n"
        + "FILE_SCHEMA(('IFC4'));\r\nENDSEC;\r\n"
        + "DATA;\r\n#8=IFCCARTESIANPOINT((0.,0.,0.));\r\n"
        + "ENDSEC;\r\nEND-ISO-10303-21;\r\n"
      };
      yield return new object[]
      {
        "ISO-10303-21;\r\nHEADER;\r\n"
        + "FILE_SCHEMA(('IFC4'));\r\nENDSEC;\r\n"
        + "DATA;\r\n#7=IFCPROJECT('data',$,'Data',$,$,$,$,$,$);\r\n"
        + "ENDSEC;\r\n"
        + "#8=IFCCARTESIANPOINT((0.,0.,0.));\r\n"
        + "END-ISO-10303-21;\r\n"
      };
      yield return new object[]
      {
        "ISO-10303-21;\r\nHEADER;\r\n"
        + "FILE_SCHEMA(('IFC4'));\r\nENDSEC;\r\n"
        + "DATA;\r\n"
        + "FILE_DESCRIPTION(('outside header'),'2;1');\r\n"
        + "#7=IFCPROJECT('data',$,'Data',$,$,$,$,$,$);\r\n"
        + "ENDSEC;\r\nEND-ISO-10303-21;\r\n"
      };
    }

    [Theory]
    [MemberData(nameof(InvalidExchangeStructureFixtures))]
    public void AddEntity_rejects_invalid_exchange_structure_without_mutation(
      string fixture)
    {
      IfcStepDocument document = IfcStepDocument.Parse(fixture);
      string before = document.Serialize();

      Assert.Throws<InvalidDataException>(() => document.AddEntity(
        "IFCCARTESIANPOINT",
        new[] { "(1.,2.,3.)" }));
      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void Parse_rejects_unclosed_step_comment()
    {
      string malformed = CreateFixture(
        "#7=IFCPROJECT('owner',$,'Project',$,$,$,$,$,$);\r\n")
        .Replace("ENDSEC;\r\nEND-ISO", "/* never closed\r\nENDSEC;\r\nEND-ISO");

      Assert.Throws<InvalidDataException>(() =>
        IfcStepDocument.Parse(malformed));
    }

    [Fact]
    public void ReplaceWith_rejects_candidate_that_drops_an_existing_entity_atomically()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROJECT('owner',$,'Original',$,$,$,$,$,$);\r\n"
        + "#8=IFCCARTESIANPOINT((0.,0.,0.));\r\n"));
      IfcStepDocument candidate = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROJECT('owner',$,'Changed',$,$,$,$,$,$);\r\n"));
      string before = document.Serialize();
      IfcStepEntity projectHandle = document.GetEntity(7);

      Assert.Throws<InvalidOperationException>(() =>
        document.ReplaceWith(candidate));

      Assert.Equal(before, document.Serialize());
      Assert.Same(projectHandle, document.GetEntity(7));
      Assert.Equal("'Original'", projectHandle.Arguments[2]);
      Assert.Equal("IFCCARTESIANPOINT", document.GetEntity(8).Type);
    }

    [Fact]
    public void ReplaceWith_independent_parse_transfers_source_value_to_live_handle_and_serialization()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROJECT('owner',$,'Original',$,$,$,$,$,$);\r\n"));
      IfcStepDocument source = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROJECT('owner',$,'Source',$,$,$,$,$,$);\r\n"));
      IfcStepEntity projectHandle = document.GetEntity(7);

      document.ReplaceWith(source);

      Assert.Same(projectHandle, document.GetEntity(7));
      Assert.Equal("'Source'", projectHandle.Arguments[2]);
      string serialized = document.Serialize();
      Assert.Contains("'Source'", serialized);
      Assert.DoesNotContain("'Original'", serialized);
      IfcStepDocument reparsed = IfcStepDocument.Parse(serialized);
      Assert.Equal("'Source'", reparsed.GetEntity(7).Arguments[2]);
    }

    [Fact]
    public void ReplaceWith_rejects_independent_source_with_invalid_sections_atomically()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROJECT('owner',$,'Original',$,$,$,$,$,$);\r\n"));
      string invalidSource = CreateFixture(
        "#7=IFCPROJECT('owner',$,'Source',$,$,$,$,$,$);\r\n")
        .Replace("DATA;\r\n", "DATA;\r\nDATA;\r\n");
      IfcStepDocument source = IfcStepDocument.Parse(invalidSource);
      IfcStepEntity projectHandle = document.GetEntity(7);
      byte[] before = System.Text.Encoding.UTF8.GetBytes(document.Serialize());
      string[] argumentsBefore = projectHandle.Arguments.ToArray();

      Assert.Throws<InvalidDataException>(() => document.ReplaceWith(source));

      Assert.Equal(
        before,
        System.Text.Encoding.UTF8.GetBytes(document.Serialize()));
      Assert.Same(projectHandle, document.GetEntity(7));
      Assert.Equal(argumentsBefore, projectHandle.Arguments);
    }

    [Fact]
    public void ReplaceWith_rejects_deleted_existing_entity_before_copying_any_state()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateFixture(
        "#7=IFCPROJECT('owner',$,'Original',$,$,$,$,$,$);\r\n"
        + "#8=IFCCARTESIANPOINT((0.,0.,0.));\r\n"));
      IfcStepEntity projectHandle = document.GetEntity(7);
      IfcStepEntity pointHandle = document.GetEntity(8);
      IfcStepDocument candidate = document.Clone();
      candidate.GetEntity(7).SetArgument(2, "'Changed'");
      candidate.GetEntity(8).Delete();
      byte[] before = System.Text.Encoding.UTF8.GetBytes(
        document.Serialize());

      Assert.Throws<InvalidOperationException>(() =>
        document.ReplaceWith(candidate));

      Assert.Equal(
        before,
        System.Text.Encoding.UTF8.GetBytes(document.Serialize()));
      Assert.Same(projectHandle, document.GetEntity(7));
      Assert.Same(pointHandle, document.GetEntity(8));
      Assert.Equal("'Original'", projectHandle.Arguments[2]);
      Assert.False(pointHandle.IsDeleted);
      Assert.True(document.TryGetEntity(8, out IfcStepEntity livePoint));
      Assert.Same(pointHandle, livePoint);
    }

    private static string CreateFixture(string dataStatements)
    {
      return "ISO-10303-21;\r\n"
        + "HEADER;\r\n"
        + "FILE_SCHEMA(('IFC4'));\r\n"
        + "ENDSEC;\r\n"
        + "DATA;\r\n"
        + dataStatements
        + "ENDSEC;\r\n"
        + "END-ISO-10303-21;\r\n";
    }

    private static int CountOccurrences(string value, string search)
    {
      int count = 0;
      int index = 0;
      while ((index = value.IndexOf(
        search,
        index,
        StringComparison.Ordinal)) >= 0)
      {
        count++;
        index += search.Length;
      }
      return count;
    }
  }
}
