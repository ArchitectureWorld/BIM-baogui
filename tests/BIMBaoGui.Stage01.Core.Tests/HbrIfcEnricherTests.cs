using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BIMBaoGui.Stage01.Mvd;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class IfcGuidCodecTests
  {
    [Theory]
    [InlineData(
      "f70dd363-bfe3-495d-84a0-2c02dcb7d4d2",
      "3t3TDZl_D9NOIWB0BSjzJI")]
    [InlineData(
      "2ed6657d-e927-568b-95e1-2665a8aea6a2",
      "0krcLzwITMYvNX9cMehgQY")]
    [InlineData(
      "00000000-0000-0000-0000-000000000000",
      "0000000000000000000000")]
    [InlineData(
      "ffffffff-ffff-ffff-ffff-ffffffffffff",
      "3$$$$$$$$$$$$$$$$$$$$$")]
    public void Encode_matches_buildingSMART_and_cross_checked_vectors(
      string guidText,
      string globalId)
    {
      Assert.Equal(globalId, IfcGuidCodec.Encode(Guid.Parse(guidText)));
    }

    [Theory]
    [InlineData(
      "3t3TDZl_D9NOIWB0BSjzJI",
      "f70dd363-bfe3-495d-84a0-2c02dcb7d4d2")]
    [InlineData(
      "0krcLzwITMYvNX9cMehgQY",
      "2ed6657d-e927-568b-95e1-2665a8aea6a2")]
    [InlineData(
      "0000000000000000000000",
      "00000000-0000-0000-0000-000000000000")]
    [InlineData(
      "3$$$$$$$$$$$$$$$$$$$$$",
      "ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void Decode_matches_expected_rfc4122_guid(
      string globalId,
      string guidText)
    {
      Assert.Equal(Guid.Parse(guidText), IfcGuidCodec.Decode(globalId));
    }

    [Theory]
    [InlineData("000000000000000000000")]
    [InlineData("00000000000000000000000")]
    [InlineData("4000000000000000000000")]
    [InlineData("0!00000000000000000000")]
    [InlineData("")]
    public void Decode_rejects_noncanonical_global_id(string value)
    {
      Assert.False(IfcGuidCodec.IsValid(value));
      Assert.Throws<FormatException>(() => IfcGuidCodec.Decode(value));
    }

    [Fact]
    public void Guid_round_trip_preserves_rfc4122_byte_order()
    {
      Guid value = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

      string encoded = IfcGuidCodec.Encode(value);

      Assert.Equal(22, encoded.Length);
      Assert.InRange(encoded[0], '0', '3');
      Assert.Equal(value, IfcGuidCodec.Decode(encoded));
    }

    [Fact]
    public void Deterministic_global_id_is_stable_and_semantic_key_sensitive()
    {
      Guid namespaceId = Guid.Parse(
        "b3f9dc18-f6b4-5bd8-9d65-2ebed89f63c8");

      string first = IfcGuidCodec.CreateDeterministic(
        namespaceId,
        "pset|owner|Pset_申报信息属性集");
      string repeated = IfcGuidCodec.CreateDeterministic(
        namespaceId,
        "pset|owner|Pset_申报信息属性集");
      string different = IfcGuidCodec.CreateDeterministic(
        namespaceId,
        "relation|owner|Pset_申报信息属性集");

      Assert.Equal(first, repeated);
      Assert.NotEqual(first, different);
      Assert.True(IfcGuidCodec.IsValid(first));
      Assert.Equal(5, IfcGuidCodec.Decode(first).ToByteArray()[7] >> 4);
    }

    [Fact]
    public void Deterministic_global_id_rejects_empty_namespace_or_semantic_key()
    {
      Guid namespaceId = Guid.Parse(
        "b3f9dc18-f6b4-5bd8-9d65-2ebed89f63c8");

      Assert.Throws<ArgumentException>(() =>
        IfcGuidCodec.CreateDeterministic(Guid.Empty, "key"));
      Assert.Throws<ArgumentException>(() =>
        IfcGuidCodec.CreateDeterministic(namespaceId, " "));
    }

    [Fact]
    public void Deterministic_global_id_matches_rfc4122_v5_dns_vector()
    {
      Guid namespaceId = Guid.Parse(
        "6ba7b810-9dad-11d1-80b4-00c04fd430c8");

      string globalId = IfcGuidCodec.CreateDeterministic(
        namespaceId,
        "www.widgets.com");
      Guid value = IfcGuidCodec.Decode(globalId);
      byte[] bytes = value.ToByteArray();

      Assert.Equal(
        Guid.Parse("21f7f8de-8051-5b89-8680-0195ef798b6a"),
        value);
      Assert.Equal("0Xz$ZUW55RYOQ00PNlUOjg", globalId);
      Assert.Equal(0x50, bytes[7] & 0xf0);
      Assert.Equal(0x80, bytes[8] & 0xc0);
    }
  }

  public sealed class HbrIfcEnricherTests
  {
    private const string OwnerGlobalId = "3t3TDZl_D9NOIWB0BSjzJI";

    [Fact]
    public void Ifc4_catalogs_pin_official_schema_provenance()
    {
      Type provenanceType = typeof(HbrIfcEnricher).Assembly.GetType(
        "BIMBaoGui.Stage01.Mvd.HbrIfc4Add2Tc1SchemaProvenance");

      Assert.NotNull(provenanceType);
      Assert.Equal(
        "buildingSMART/IFC4.x-development",
        ReadStringConstant(provenanceType, "Repository"));
      Assert.Equal(
        "119bf71c8049cd0683df0109844605e975025db2",
        ReadStringConstant(provenanceType, "Commit"));
      Assert.Equal(
        "reference_schemas/IFC4_ADD2_TC1.exp",
        ReadStringConstant(provenanceType, "SchemaPath"));
      Assert.Equal(
        "cc49e47a9457bf8708a0db75c76308c19f0bd09b",
        ReadStringConstant(provenanceType, "GitBlobSha1"));
      Assert.Equal(
        "a2704ba20a1b3d0b7d9b61d6fd37d0baa3b4996ba3e90d968a1d2ca2819d1046",
        ReadStringConstant(provenanceType, "SchemaSha256"));
    }

    [Theory]
    [InlineData(
      "HbrIfcRootCarrierTypes",
      419,
      8154,
      "d52fa2b2eb63b1ec9056398527adba79822f716bb8f6a21bd9432682071a3f3b")]
    [InlineData(
      "HbrIfcRelatedObjectTypes",
      215,
      3798,
      "0165a8e7b0374fd80d5bac168179da9e498f155dfcc754370bbd7a891a8792dc")]
    [InlineData(
      "HbrIfcPropertySetDefinitionTypes",
      11,
      264,
      "86dc57904db65cf6e0cac41d1721dbfea84110155ad723100375d9f6177073e3")]
    [InlineData(
      "HbrIfcTypeObjectTypes",
      138,
      2816,
      "eae4da4ebedb404c1c72b7b18bacf510d53bce9e6422da9743a748256723aebd")]
    [InlineData(
      "HbrIfcPropertyTypes",
      9,
      191,
      "ab604fe18bfe3b47f92cc9cc35bd89467d12a22f69de4324aca36ef2f9cbb0be")]
    public void Ifc4_catalogs_match_pinned_schema_without_drift(
      string catalogTypeName,
      int expectedCount,
      int expectedUtf8ByteCount,
      string expectedSha256)
    {
      Type catalogType = typeof(HbrIfcEnricher).Assembly.GetType(
        "BIMBaoGui.Stage01.Mvd." + catalogTypeName);
      Assert.NotNull(catalogType);
      FieldInfo namesField = catalogType.GetField(
        "Names",
        BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(namesField);
      IEnumerable<string> names = Assert.IsAssignableFrom<IEnumerable<string>>(
        namesField.GetValue(null));
      string normalized = string.Join(
        "|",
        names.Select(name => name.ToUpperInvariant())
          .OrderBy(name => name, StringComparer.Ordinal));
      byte[] bytes = Encoding.UTF8.GetBytes(normalized);

      Assert.Equal(expectedCount, names.Count());
      Assert.Equal(expectedUtf8ByteCount, bytes.Length);
      using (SHA256 sha256 = SHA256.Create())
      {
        string actualSha256 = string.Concat(
          sha256.ComputeHash(bytes).Select(value =>
            value.ToString("x2", CultureInfo.InvariantCulture)));
        Assert.Equal(expectedSha256, actualSha256);
      }
    }

    [Fact]
    public void Apply_creates_property_pset_and_relationship_for_exact_owner()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "3353559.52");

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { value });

      Assert.True(result.Success);
      Assert.Equal(1, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      Assert.Equal(0, result.UpdatedProperties);
      Assert.Single(result.Fields);
      Assert.True(result.Fields[0].Success);
      Assert.True(result.Fields[0].ExactInspectionPassed);

      IfcStepDocument reparsed = IfcStepDocument.Parse(document.Serialize());
      IfcStepEntity property = Assert.Single(
        reparsed.OfType("IFCPROPERTYSINGLEVALUE"));
      Assert.Equal("原点坐标X", IfcStepSyntax.DecodeString(
        property.Arguments[0]));
      Assert.Equal("IFCREAL(3353559.52)", property.Arguments[2]);

      IfcStepEntity propertySet = Assert.Single(
        reparsed.OfType("IFCPROPERTYSET"));
      Assert.True(IfcGuidCodec.IsValid(IfcStepSyntax.DecodeString(
        propertySet.Arguments[0])));
      Assert.Equal("Pset_申报信息属性集", IfcStepSyntax.DecodeString(
        propertySet.Arguments[2]));
      Assert.Equal(
        new[] { property.Id },
        IfcStepSyntax.ParseReferenceList(propertySet.Arguments[4]));

      IfcStepEntity relationship = Assert.Single(
        reparsed.OfType("IFCRELDEFINESBYPROPERTIES"));
      Assert.True(IfcGuidCodec.IsValid(IfcStepSyntax.DecodeString(
        relationship.Arguments[0])));
      Assert.Equal(
        new[] { 7 },
        IfcStepSyntax.ParseReferenceList(relationship.Arguments[4]));
      Assert.Equal(
        propertySet.Id,
        IfcStepSyntax.ParseReference(relationship.Arguments[5]));
    }

    [Fact]
    public void Apply_adds_missing_property_to_existing_exact_pset()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('保留属性',$,IFCLABEL('keep'),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#21);\r\n"));

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3353559.52") });

      Assert.True(result.Success);
      Assert.Equal(1, result.CreatedProperties);
      Assert.Equal(0, result.CreatedPropertySets);
      Assert.Equal(0, result.CreatedRelationships);
      Assert.Equal(0, result.UpdatedProperties);
      Assert.Equal(21, result.Fields.Single().PropertySetId);
      Assert.Equal(22, result.Fields.Single().RelationshipId);

      IfcStepEntity propertySet = document.GetEntity(21);
      int[] references = IfcStepSyntax.ParseReferenceList(
        propertySet.Arguments[4]).ToArray();
      Assert.Equal(2, references.Length);
      Assert.Equal(20, references[0]);
      IfcStepEntity created = document.GetEntity(references[1]);
      Assert.Equal("原点坐标X", IfcStepSyntax.DecodeString(
        created.Arguments[0]));
      Assert.Equal("IFCLABEL('keep')", document.GetEntity(20).Arguments[2]);
      Assert.Single(document.OfType("IFCPROPERTYSET"));
      Assert.Single(document.OfType("IFCRELDEFINESBYPROPERTIES"));
    }

    [Fact]
    public void Apply_creates_isolated_pset_when_foreign_target_token_differs()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE("
        + "'原点坐标X',$,IFCREAL(1.0),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000000',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'3$$$$$$$$$$$$$$$$$$$$$',$,$,$,(#8),#21);\r\n"));
      string foreignPropertySetBefore = document.GetEntity(21).Serialize();
      string foreignRelationshipBefore = document.GetEntity(22).Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3353559.52") });

      HbrIfcEnrichmentFieldResult field = Assert.Single(result.Fields);
      Assert.True(
        result.Success,
        field.ErrorCode + ": " + field.Message);
      Assert.True(field.Success);
      Assert.True(field.ExactInspectionPassed);
      Assert.Equal(1, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      Assert.Equal(0, result.UpdatedProperties);
      Assert.NotEqual(20, field.PropertyId);
      Assert.NotEqual(21, field.PropertySetId);
      Assert.NotEqual(22, field.RelationshipId);
      Assert.Equal(foreignPropertySetBefore, document.GetEntity(21).Serialize());
      Assert.Equal(foreignRelationshipBefore, document.GetEntity(22).Serialize());
      Assert.Equal("IFCREAL(1.0)", document.GetEntity(20).Arguments[2]);
      Assert.Equal(
        new[] { 8 },
        IfcStepSyntax.ParseReferenceList(document.GetEntity(22).Arguments[4]));
      IfcStepEntity targetPropertySet = document.GetEntity(
        field.PropertySetId.Value);
      Assert.Equal(
        new[] { field.PropertyId.Value },
        IfcStepSyntax.ParseReferenceList(targetPropertySet.Arguments[4]));
      Assert.Equal(
        "IFCREAL(3353559.52)",
        document.GetEntity(field.PropertyId.Value).Arguments[2]);
      IfcStepEntity targetRelationship = document.GetEntity(
        field.RelationshipId.Value);
      Assert.Equal(
        new[] { 7 },
        IfcStepSyntax.ParseReferenceList(targetRelationship.Arguments[4]));
      Assert.Equal(
        targetPropertySet.Id,
        IfcStepSyntax.ParseReference(targetRelationship.Arguments[5]));
      Assert.Equal(2, document.OfType("IFCPROPERTYSET").Count());
      Assert.Equal(2, document.OfType("IFCRELDEFINESBYPROPERTIES").Count());
    }

    [Fact]
    public void Apply_reports_owner_not_found_for_exact_type_and_global_id()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('0krcLzwITMYvNX9cMehgQY',$,'Other',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('" + OwnerGlobalId + "',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.5") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcOwnerNotFound,
        before,
        document);
    }

    [Fact]
    public void Apply_reports_owner_conflict_for_duplicate_type_and_global_id()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project A',$,$,$,$,$,$);\r\n"
        + "#8=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project B',$,$,$,$,$,$);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.5") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcOwnerConflict,
        before,
        document);
    }

    [Theory]
    [InlineData("IfcProject")]
    [InlineData("IfcSite")]
    [InlineData("IfcBuilding")]
    public void Apply_supports_single_entity_by_type_for_verified_roots(
      string ownerType)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=" + ownerType.ToUpperInvariant()
        + "('" + OwnerGlobalId + "',$,'Owner',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "1.5");
      value.OwnerEntityType = ownerType;
      value.OwnerGlobalId = null;
      value.OwnerStrategy = HbrIfcOwnerStrategies.SingleEntityByType;

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { value });

      Assert.True(result.Success);
      Assert.Equal(7, result.Fields.Single().OwnerId);
    }

    [Theory]
    [InlineData("IfcOrganization")]
    [InlineData("IfcSpatialZone")]
    [InlineData("IfcWall")]
    public void Apply_rejects_unimplemented_single_entity_owner_without_fallback(
      string ownerType)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=" + ownerType.ToUpperInvariant()
        + "('0krcLzwITMYvNX9cMehgQY',$,'Other',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "1.5");
      value.OwnerEntityType = ownerType;
      value.OwnerGlobalId = null;
      value.OwnerStrategy = HbrIfcOwnerStrategies.SingleEntityByType;
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { value });

      AssertFailure(
        result,
        HbrIfcErrorCodes.RuleNotImplemented,
        before,
        document);
      Assert.Empty(document.OfType("IFCPROPERTYSET"));
    }

    [Fact]
    public void Inspector_rejects_unimplemented_single_entity_owner_before_reading_candidate_identity()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCORGANIZATION($,'Organization',$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "1.5");
      value.OwnerEntityType = "IfcOrganization";
      value.OwnerGlobalId = null;
      value.OwnerStrategy = HbrIfcOwnerStrategies.SingleEntityByType;

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(document, value);

      Assert.False(inspection.Success);
      Assert.Equal(HbrIfcErrorCodes.RuleNotImplemented, inspection.ErrorCode);
      Assert.Null(inspection.OwnerId);
    }

    [Theory]
    [InlineData("IFCPROPERTYSET")]
    [InlineData("IFCRELDEFINESBYPROPERTIES")]
    [InlineData("IFCORGANIZATION")]
    [InlineData("IFCWALLTYPE")]
    [InlineData("IFCBUILDINGELEMENT")]
    public void Apply_rejects_owner_type_not_allowed_by_related_objects_atomically(
      string ownerType)
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateInvalidRelatedObjectOwnerIfc(ownerType, false));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "1.5");
      value.OwnerEntityType = ownerType;
      value.OwnerGlobalId = OwnerGlobalId;
      string before = document.Serialize();
      int relationshipCount = document.OfType(
        "IFCRELDEFINESBYPROPERTIES").Count();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { value });

      AssertFailure(
        result,
        HbrIfcErrorCodes.InvalidValue,
        before,
        document);
      Assert.Equal(
        relationshipCount,
        document.OfType("IFCRELDEFINESBYPROPERTIES").Count());
    }

    [Theory]
    [InlineData("IFCPROPERTYSET")]
    [InlineData("IFCRELDEFINESBYPROPERTIES")]
    [InlineData("IFCORGANIZATION")]
    [InlineData("IFCWALLTYPE")]
    [InlineData("IFCBUILDINGELEMENT")]
    public void Inspector_rejects_owner_type_not_allowed_by_related_objects(
      string ownerType)
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateInvalidRelatedObjectOwnerIfc(ownerType, true));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "1.5");
      value.OwnerEntityType = ownerType;
      value.OwnerGlobalId = OwnerGlobalId;

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(document, value);

      Assert.False(inspection.Success);
      Assert.Equal(HbrIfcErrorCodes.InvalidValue, inspection.ErrorCode);
      Assert.Null(inspection.OwnerId);
    }

    [Theory]
    [InlineData("IFCWALLTYPE")]
    [InlineData("IFCPROPERTYSET")]
    [InlineData("IFCBUILDINGELEMENT")]
    public void Apply_rejects_foreign_relationship_with_invalid_related_object_owner_atomically(
      string foreignOwnerType)
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateForeignInvalidRelationshipOwnerIfc(foreignOwnerType, false));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.5") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcRelationshipConflict,
        before,
        document);
    }

    [Theory]
    [InlineData("IFCWALLTYPE")]
    [InlineData("IFCPROPERTYSET")]
    [InlineData("IFCBUILDINGELEMENT")]
    public void Inspector_rejects_foreign_relationship_with_invalid_related_object_owner(
      string foreignOwnerType)
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateForeignInvalidRelationshipOwnerIfc(foreignOwnerType, true));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "1.5");

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(document, value);

      Assert.False(inspection.Success);
      Assert.Equal(
        HbrIfcErrorCodes.IfcRelationshipConflict,
        inspection.ErrorCode);
    }

    [Fact]
    public void Apply_preserves_direct_element_quantity_relationship_and_creates_isolated_target_pset()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#19=IFCQUANTITYLENGTH('Length',$,$,1.0,$);\r\n"
        + "#20=IFCELEMENTQUANTITY('0000000000000000000008',$,"
        + "'BaseQuantities',$,$,(#19));\r\n"
        + "#21=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#7),#20);\r\n"));
      IfcStepEntity quantity = document.GetEntity(20);
      IfcStepEntity relationship = document.GetEntity(21);
      string[] quantityArguments = quantity.Arguments.ToArray();
      string[] relationshipArguments = relationship.Arguments.ToArray();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.5") });

      Assert.True(result.Success, result.Fields.Single().Message);
      Assert.Equal(1, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      Assert.NotEqual(20, result.Fields.Single().PropertySetId);
      Assert.Same(quantity, document.GetEntity(20));
      Assert.Same(relationship, document.GetEntity(21));
      Assert.Equal(quantityArguments, quantity.Arguments);
      Assert.Equal(relationshipArguments, relationship.Arguments);
    }

    [Fact]
    public void Apply_uses_pset_inside_typed_definition_set_and_preserves_quantity()
    {
      const string definitionSet =
        "IFCPROPERTYSETDEFINITIONSET((#20,#31))";
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#19=IFCQUANTITYLENGTH('Length',$,$,1.0,$);\r\n"
        + "#20=IFCELEMENTQUANTITY('0000000000000000000008',$,"
        + "'BaseQuantities',$,$,(#19));\r\n"
        + "#30=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.0),$);\r\n"
        + "#31=IFCPROPERTYSET('0000000000000000000006',$,"
        + "'Pset_申报信息属性集',$,(#30));\r\n"
        + "#32=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#7),"
        + definitionSet + ");\r\n"));
      IfcStepEntity quantity = document.GetEntity(20);
      IfcStepEntity relationship = document.GetEntity(32);
      string[] quantityArguments = quantity.Arguments.ToArray();

      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "2.0");
      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { value });

      Assert.True(result.Success, result.Fields.Single().Message);
      Assert.Equal(0, result.CreatedProperties);
      Assert.Equal(0, result.CreatedPropertySets);
      Assert.Equal(0, result.CreatedRelationships);
      Assert.Equal(1, result.UpdatedProperties);
      Assert.Equal(31, result.Fields.Single().PropertySetId);
      Assert.Equal("IFCREAL(2.0)", document.GetEntity(30).Arguments[2]);
      Assert.Equal(quantityArguments, quantity.Arguments);
      Assert.Equal(definitionSet, relationship.Arguments[5]);
      Assert.True(new HbrIfcFieldInspector().Inspect(document, value).Success);
    }

    [Fact]
    public void Inspector_accepts_target_pset_when_mixed_definition_set_contains_non_target_type_owned_pset()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.0),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000001',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#30=IFCPROPERTYSINGLEVALUE('Foreign',$,IFCLABEL('keep'),$);\r\n"
        + "#31=IFCPROPERTYSET('0000000000000000000002',$,"
        + "'Pset_Foreign',$,(#30));\r\n"
        + "#32=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000003',$,$,$,(#7),"
        + "IFCPROPERTYSETDEFINITIONSET((#21,#31)));\r\n"
        + "#40=IFCWALLTYPE('0000000000000000000004',$,'Wall Type',"
        + "$,$,(#31),$,$,$,.NOTDEFINED.);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "2.0");

      HbrIfcEnrichmentResult enrichment = new HbrIfcEnricher().Apply(
        document,
        new[] { value });
      string beforeInspection = document.Serialize();
      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(document, value);

      Assert.True(enrichment.Success, enrichment.Fields.Single().Message);
      Assert.True(inspection.Success, inspection.Message);
      Assert.Equal(21, inspection.PropertySetId);
      Assert.Equal(20, inspection.PropertyId);
      Assert.Equal(32, inspection.RelationshipId);
      Assert.Equal("IFCREAL(2.0)", inspection.TypedToken);
      Assert.Equal(beforeInspection, document.Serialize());
    }

    [Theory]
    [InlineData("IFCPROPERTYSETDEFINITIONSET(())")]
    [InlineData("IFCPROPERTYSETDEFINITIONSET((#31,#31))")]
    [InlineData("IFCPROPERTYSETDEFINITIONSET((#999))")]
    [InlineData("IFCPROPERTYSETDEFINITIONSET((#50))")]
    [InlineData("(#31)")]
    [InlineData("$")]
    public void Apply_rejects_invalid_property_set_definition_select_atomically(
      string definitionToken)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#30=IFCPROPERTYSINGLEVALUE('Existing',$,IFCLABEL('keep'),$);\r\n"
        + "#31=IFCPROPERTYSET('0000000000000000000006',$,"
        + "'Pset_Existing',$,(#30));\r\n"
        + "#50=IFCCARTESIANPOINT((0.0,0.0,0.0));\r\n"
        + "#60=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#7),"
        + definitionToken + ");\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.5") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcRelationshipConflict,
        before,
        document);
    }

    [Fact]
    public void Apply_rejects_relationship_with_invalid_property_set_definition_atomically()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCCARTESIANPOINT((0.0,0.0,0.0));\r\n"
        + "#21=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#7),#20);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.5") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcRelationshipConflict,
        before,
        document);
    }

    [Fact]
    public void Inspector_rejects_relationship_with_invalid_property_set_definition()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCCARTESIANPOINT((0.0,0.0,0.0));\r\n"
        + "#21=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#7),#20);\r\n"));

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(
          document,
          CreateValue("原点坐标X", "IfcReal", "1.5"));

      Assert.False(inspection.Success);
      Assert.Equal(
        HbrIfcErrorCodes.IfcRelationshipConflict,
        inspection.ErrorCode);
    }

    [Fact]
    public void Apply_does_not_adopt_or_modify_pset_owned_by_type_object()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.0),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000006',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#30=IFCWALLTYPE('0000000000000000000008',$,'Wall Type',"
        + "$,$,(#21),$,$,$,.NOTDEFINED.);\r\n"));
      string typeBefore = document.GetEntity(30).Serialize();
      string propertySetBefore = document.GetEntity(21).Serialize();
      string propertyBefore = document.GetEntity(20).Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      Assert.True(result.Success, result.Fields.Single().Message);
      Assert.Equal(1, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      Assert.Equal(0, result.UpdatedProperties);
      HbrIfcEnrichmentFieldResult field = Assert.Single(result.Fields);
      Assert.NotEqual(20, field.PropertyId);
      Assert.NotEqual(21, field.PropertySetId);
      Assert.Equal(typeBefore, document.GetEntity(30).Serialize());
      Assert.Equal(propertySetBefore, document.GetEntity(21).Serialize());
      Assert.Equal(propertyBefore, document.GetEntity(20).Serialize());
    }

    [Fact]
    public void Apply_rejects_target_occurrence_pset_also_owned_by_type_object_atomically()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.0),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000006',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#7),#21);\r\n"
        + "#30=IFCWALLTYPE('0000000000000000000008',$,'Wall Type',"
        + "$,$,(#21),$,$,$,.NOTDEFINED.);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertySetConflict,
        before,
        document);
    }

    [Fact]
    public void Inspector_rejects_target_occurrence_pset_also_owned_by_type_object()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(2.0),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000006',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#7),#21);\r\n"
        + "#30=IFCWALLTYPE('0000000000000000000008',$,'Wall Type',"
        + "$,$,(#21),$,$,$,.NOTDEFINED.);\r\n"));

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(
          document,
          CreateValue("原点坐标X", "IfcReal", "2.0"));

      Assert.False(inspection.Success);
      Assert.Equal(
        HbrIfcErrorCodes.IfcPropertySetConflict,
        inspection.ErrorCode);
    }

    [Theory]
    [InlineData("IFCCARTESIANPOINT((0.0,0.0,0.0))")]
    [InlineData("IFCPROJECT('0000000000000000000005',$,'Invalid',$,$,$,$,$,$)")]
    [InlineData("IFCSIMPLEPROPERTY('Invalid',$)")]
    public void Apply_rejects_non_concrete_ifc_property_reference_atomically(
      string invalidEntityBody)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.0),$);\r\n"
        + "#30=" + invalidEntityBody + ";\r\n"
        + "#31=IFCPROPERTYSET('0000000000000000000006',$,"
        + "'Pset_申报信息属性集',$,(#20,#30));\r\n"
        + "#32=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#7),#31);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertySetConflict,
        before,
        document);
    }

    [Theory]
    [InlineData("IFCCARTESIANPOINT((0.0,0.0,0.0))")]
    [InlineData("IFCPROJECT('0000000000000000000005',$,'Invalid',$,$,$,$,$,$)")]
    [InlineData("IFCSIMPLEPROPERTY('Invalid',$)")]
    public void Inspector_rejects_non_concrete_ifc_property_reference(
      string invalidEntityBody)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(2.0),$);\r\n"
        + "#30=" + invalidEntityBody + ";\r\n"
        + "#31=IFCPROPERTYSET('0000000000000000000006',$,"
        + "'Pset_申报信息属性集',$,(#20,#30));\r\n"
        + "#32=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#7),#31);\r\n"));

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(
          document,
          CreateValue("原点坐标X", "IfcReal", "2.0"));

      Assert.False(inspection.Success);
      Assert.Equal(
        HbrIfcErrorCodes.IfcPropertySetConflict,
        inspection.ErrorCode);
    }

    [Fact]
    public void Apply_rejects_duplicate_has_properties_reference_as_property_conflict()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.0),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000006',$,"
        + "'Pset_申报信息属性集',$,(#20,#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#7),#21);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertyConflict,
        before,
        document);
    }

    [Fact]
    public void Apply_preserves_legal_non_single_value_property_in_target_pset()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.0),$);\r\n"
        + "#30=IFCPROPERTYENUMERATEDVALUE('Other',$,(IFCLABEL('A')),$);\r\n"
        + "#31=IFCPROPERTYSET('0000000000000000000006',$,"
        + "'Pset_申报信息属性集',$,(#20,#30));\r\n"
        + "#32=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#7),#31);\r\n"));
      string nonSingleBefore = document.GetEntity(30).Serialize();
      string referencesBefore = document.GetEntity(31).Arguments[4];

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      Assert.True(result.Success, result.Fields.Single().Message);
      Assert.Equal(1, result.UpdatedProperties);
      Assert.Equal("IFCREAL(2.0)", document.GetEntity(20).Arguments[2]);
      Assert.Equal(nonSingleBefore, document.GetEntity(30).Serialize());
      Assert.Equal(referencesBefore, document.GetEntity(31).Arguments[4]);
    }

    [Fact]
    public void Apply_ignores_foreign_occurrence_pset_also_owned_by_type_object()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('Foreign',$,IFCLABEL('keep'),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000006',$,"
        + "'Pset_Foreign',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000009',$,$,$,(#8),#21);\r\n"
        + "#30=IFCWALLTYPE('0000000000000000000008',$,'Wall Type',"
        + "$,$,(#21),$,$,$,.NOTDEFINED.);\r\n"));
      int[] foreignIds = { 20, 21, 22, 30 };
      Dictionary<int, string> foreignBefore = foreignIds.ToDictionary(
        id => id,
        id => document.GetEntity(id).Serialize());

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      Assert.True(result.Success, result.Fields.Single().Message);
      Assert.Equal(1, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      HbrIfcEnrichmentFieldResult field = Assert.Single(result.Fields);
      Assert.NotEqual(21, field.PropertySetId);
      Assert.NotEqual(22, field.RelationshipId);
      foreach (int id in foreignIds)
        Assert.Equal(foreignBefore[id], document.GetEntity(id).Serialize());
    }

    [Fact]
    public void Single_owner_resolution_validates_all_candidate_global_ids_before_cardinality()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project A',$,$,$,$,$,$);\r\n"
        + "#8=IFCPROJECT('bad',$,'Project B',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "2.0");
      value.OwnerGlobalId = null;
      value.OwnerStrategy = HbrIfcOwnerStrategies.SingleEntityByType;
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { value });
      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(document, value);

      AssertFailure(
        result,
        HbrIfcErrorCodes.InvalidValue,
        before,
        document);
      Assert.False(inspection.Success);
      Assert.Equal(result.Fields.Single().ErrorCode, inspection.ErrorCode);
      Assert.Equal(HbrIfcErrorCodes.InvalidValue, inspection.ErrorCode);
    }

    [Fact]
    public void Apply_accepts_ifcwall_as_related_object_owner()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCWALL('" + OwnerGlobalId
        + "',$,'Wall',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "1.5");
      value.OwnerEntityType = "IfcWall";

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { value });

      Assert.True(result.Success, result.Fields.Single().Message);
      Assert.Equal(7, result.Fields.Single().OwnerId);
      IfcStepEntity relationship = Assert.Single(
        document.OfType("IFCRELDEFINESBYPROPERTIES"));
      Assert.Equal(
        new[] { 7 },
        IfcStepSyntax.ParseReferenceList(relationship.Arguments[4]));
    }

    [Fact]
    public void Apply_rejects_duplicate_target_properties_in_exact_pset()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#19=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.),$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(2.),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#19,#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#21);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertyConflict,
        before,
        document);
    }

    [Fact]
    public void Apply_rejects_multiple_exact_psets_for_same_owner()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#19=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.),$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(2.),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#19));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#21);\r\n"
        + "#23=IFCPROPERTYSET('3$$$$$$$$$$$$$$$$$$$$$',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#24=IFCRELDEFINESBYPROPERTIES("
        + "'0krcLzwITMYvNX9cMehgQZ',$,$,$,(#7),#23);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertySetConflict,
        before,
        document);
    }

    [Fact]
    public void Apply_rejects_multiple_relationships_for_exact_pset()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#21);\r\n"
        + "#23=IFCRELDEFINESBYPROPERTIES("
        + "'3$$$$$$$$$$$$$$$$$$$$$',$,$,$,(#7),#21);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcRelationshipConflict,
        before,
        document);
    }

    public static IEnumerable<object[]> MalformedGraphCases()
    {
      yield return new object[]
      {
        "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7));\r\n",
        HbrIfcErrorCodes.IfcRelationshipConflict
      };
      yield return new object[]
      {
        "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#999);\r\n",
        HbrIfcErrorCodes.IfcRelationshipConflict
      };
      yield return new object[]
      {
        "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7,#7),#21);\r\n",
        HbrIfcErrorCodes.IfcRelationshipConflict
      };
      yield return new object[]
      {
        "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$);\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#21);\r\n",
        HbrIfcErrorCodes.IfcPropertySetConflict
      };
      yield return new object[]
      {
        "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#20,#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#21);\r\n",
        HbrIfcErrorCodes.IfcPropertyConflict
      };
      yield return new object[]
      {
        "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#999));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#21);\r\n",
        HbrIfcErrorCodes.IfcPropertySetConflict
      };
      yield return new object[]
      {
        "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.));\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#21);\r\n",
        HbrIfcErrorCodes.IfcPropertyConflict
      };
      yield return new object[]
      {
        "#20=IFCPROPERTYSINGLEVALUE($,$,IFCREAL(1.),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#21);\r\n",
        HbrIfcErrorCodes.IfcPropertyConflict
      };
    }

    [Theory]
    [MemberData(nameof(MalformedGraphCases))]
    public void Apply_rejects_malformed_or_dangling_target_graph(
      string graph,
      string expectedErrorCode)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + graph));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3.0") });

      AssertFailure(result, expectedErrorCode, before, document);
    }

    [Fact]
    public void Apply_rejects_orphan_single_value_with_invalid_arity_without_mutation()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE("
        + "'Orphan',$,IFCLABEL('bad'));\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3353559.52") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertyConflict,
        before,
        document);
    }

    public static IEnumerable<object[]> InvalidExistingUpdateStructures()
    {
      foreach (string malformed in InvalidExistingTargetStructures(
        "IFCREAL(1.0)"))
        yield return new object[] { malformed };
    }

    public static IEnumerable<object[]> InvalidExistingNoopStructures()
    {
      foreach (string malformed in InvalidExistingTargetStructures(
        "IFCREAL(2.0)"))
        yield return new object[] { malformed };
    }

    [Theory]
    [MemberData(nameof(InvalidExistingUpdateStructures))]
    public void Apply_rejects_existing_property_update_when_sections_are_invalid(
      string malformed)
    {
      IfcStepDocument document = IfcStepDocument.Parse(malformed);
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcMutationFailed,
        before,
        document);
    }

    [Theory]
    [MemberData(nameof(InvalidExistingNoopStructures))]
    public void Apply_rejects_existing_property_noop_when_sections_are_invalid(
      string malformed)
    {
      IfcStepDocument document = IfcStepDocument.Parse(malformed);
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcMutationFailed,
        before,
        document);
    }

    [Theory]
    [InlineData("FOO();")]
    [InlineData("42;")]
    public void Apply_rejects_raw_non_entity_statement_inside_data_atomically(
      string rawStatement)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        rawStatement + "\r\n"
        + "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcMutationFailed,
        before,
        document);
    }

    [Fact]
    public void Apply_creates_missing_relationship_for_unique_unowned_pset()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE("
        + IfcStepSyntax.EncodeString("原点坐标X")
        + ",$,IFCREAL(3353559.52),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"));

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3353559.52") });

      Assert.True(result.Success);
      Assert.Equal(0, result.CreatedProperties);
      Assert.Equal(0, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      Assert.Equal(0, result.UpdatedProperties);
      Assert.Equal(20, result.Fields.Single().PropertyId);
      Assert.Equal(21, result.Fields.Single().PropertySetId);
      IfcStepEntity relationship = Assert.Single(
        document.OfType("IFCRELDEFINESBYPROPERTIES"));
      Assert.Equal(
        new[] { 7 },
        IfcStepSyntax.ParseReferenceList(relationship.Arguments[4]));
      Assert.Equal(
        21,
        IfcStepSyntax.ParseReference(relationship.Arguments[5]));
    }

    [Fact]
    public void Apply_reuses_adopted_unique_unowned_pset_for_multiple_batch_fields()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('保留属性',$,IFCLABEL('keep'),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"));

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[]
        {
          CreateValue("原点坐标X", "IfcReal", "3353559.52"),
          CreateValue("原点坐标Y", "IfcReal", "38589642.165")
        });

      Assert.True(result.Success);
      Assert.Equal(2, result.CreatedProperties);
      Assert.Equal(0, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      Assert.Equal(0, result.UpdatedProperties);
      Assert.Equal(2, result.Fields.Count);
      Assert.All(result.Fields, field =>
      {
        Assert.True(field.Success);
        Assert.True(field.ExactInspectionPassed);
      });
      Assert.Equal(
        new[] { 21 },
        result.Fields.Select(field => field.PropertySetId.Value)
          .Distinct()
          .ToArray());
      Assert.Single(
        result.Fields.Select(field => field.RelationshipId.Value).Distinct());
      Assert.Equal(
        3,
        IfcStepSyntax.ParseReferenceList(
          document.GetEntity(21).Arguments[4]).Count);
      Assert.Single(document.OfType("IFCPROPERTYSET"));
      Assert.Single(document.OfType("IFCRELDEFINESBYPROPERTIES"));
    }

    [Fact]
    public void Apply_ignores_multiple_foreign_same_name_psets_and_creates_isolated_target()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building A',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#9=IFCBUILDING('0000000000000000000005',$,'Building B',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('ForeignA',$,IFCLABEL('A'),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000001',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000002',$,$,$,(#8),#21);\r\n"
        + "#30=IFCPROPERTYSINGLEVALUE('ForeignB',$,IFCLABEL('B'),$);\r\n"
        + "#31=IFCPROPERTYSET('0000000000000000000003',$,"
        + "'Pset_申报信息属性集',$,(#30));\r\n"
        + "#32=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000004',$,$,$,(#9),#31);\r\n"));
      int[] foreignIds = { 20, 21, 22, 30, 31, 32 };
      Dictionary<int, string> foreignBefore = foreignIds.ToDictionary(
        id => id,
        id => document.GetEntity(id).Serialize());

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3353559.52") });

      Assert.True(result.Success);
      Assert.Equal(1, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      HbrIfcEnrichmentFieldResult field = Assert.Single(result.Fields);
      Assert.DoesNotContain(field.PropertySetId.Value, new[] { 21, 31 });
      Assert.DoesNotContain(field.RelationshipId.Value, new[] { 22, 32 });
      foreach (int foreignId in foreignIds)
        Assert.Equal(
          foreignBefore[foreignId],
          document.GetEntity(foreignId).Serialize());
      Assert.Equal(3, document.OfType("IFCPROPERTYSET").Count());
      Assert.Equal(3, document.OfType("IFCRELDEFINESBYPROPERTIES").Count());
    }

    [Fact]
    public void Apply_adopts_unique_unowned_pset_when_foreign_same_name_pset_exists()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('ForeignOnly',$,IFCLABEL('A'),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000001',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000002',$,$,$,(#8),#21);\r\n"
        + "#30=IFCPROPERTYSINGLEVALUE('原点坐标X',$,"
        + "IFCREAL(3353559.52),$);\r\n"
        + "#31=IFCPROPERTYSET('0000000000000000000003',$,"
        + "'Pset_申报信息属性集',$,(#30));\r\n"));
      string foreignPropertyBefore = document.GetEntity(20).Serialize();
      string foreignPsetBefore = document.GetEntity(21).Serialize();
      string foreignRelationshipBefore = document.GetEntity(22).Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3353559.52") });

      Assert.True(result.Success);
      Assert.Equal(0, result.CreatedProperties);
      Assert.Equal(0, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      HbrIfcEnrichmentFieldResult field = Assert.Single(result.Fields);
      Assert.Equal(30, field.PropertyId);
      Assert.Equal(31, field.PropertySetId);
      Assert.NotEqual(22, field.RelationshipId);
      Assert.Equal(foreignPropertyBefore, document.GetEntity(20).Serialize());
      Assert.Equal(foreignPsetBefore, document.GetEntity(21).Serialize());
      Assert.Equal(
        foreignRelationshipBefore,
        document.GetEntity(22).Serialize());
      IfcStepEntity targetRelationship = document.GetEntity(
        field.RelationshipId.Value);
      Assert.Equal(
        new[] { 7 },
        IfcStepSyntax.ParseReferenceList(targetRelationship.Arguments[4]));
      Assert.Equal(
        31,
        IfcStepSyntax.ParseReference(targetRelationship.Arguments[5]));
    }

    [Fact]
    public void Apply_rejects_multiple_unowned_same_name_psets_without_mutation()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('First',$,IFCLABEL('A'),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000001',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#30=IFCPROPERTYSINGLEVALUE('Second',$,IFCLABEL('B'),$);\r\n"
        + "#31=IFCPROPERTYSET('0000000000000000000003',$,"
        + "'Pset_申报信息属性集',$,(#30));\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3353559.52") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertySetConflict,
        before,
        document);
    }

    public static IEnumerable<object[]> SupportedTypedValues()
    {
      yield return new object[] { "IfcBoolean", ".T.", "IFCBOOLEAN(.T.)" };
      yield return new object[]
      {
        "IfcDate",
        "2026-08-04",
        "IFCDATE(" + IfcStepSyntax.EncodeString("2026-08-04") + ")"
      };
      yield return new object[]
      {
        "IfcDateTime",
        "2026-08-04T12:34:56+08:00",
        "IFCDATETIME("
        + IfcStepSyntax.EncodeString("2026-08-04T12:34:56+08:00")
        + ")"
      };
      yield return new object[] { "IfcInteger", "-42", "IFCINTEGER(-42)" };
      yield return new object[]
      {
        "IfcLabel",
        "  O'Brien 项目  ",
        "IFCLABEL(" + IfcStepSyntax.EncodeString("  O'Brien 项目  ") + ")"
      };
      yield return new object[] { "IfcReal", "-12.5", "IFCREAL(-12.5)" };
      yield return new object[]
      {
        "IfcText",
        " 第一行\r\n第二行 ",
        "IFCTEXT(" + IfcStepSyntax.EncodeString(" 第一行\r\n第二行 ") + ")"
      };
    }

    [Theory]
    [MemberData(nameof(SupportedTypedValues))]
    public void Apply_formats_each_supported_declared_type_exactly(
      string declaredType,
      string canonicalValue,
      string expectedToken)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("TypedValue", declaredType, canonicalValue) });

      Assert.True(result.Success);
      IfcStepEntity property = Assert.Single(
        document.OfType("IFCPROPERTYSINGLEVALUE"));
      Assert.Equal(expectedToken, property.Arguments[2]);
      Assert.True(IfcStepSyntax.TryParseTypedValue(
        property.Arguments[2],
        out string actualType,
        out _));
      Assert.Equal(declaredType.ToUpperInvariant(), actualType);
    }

    [Theory]
    [InlineData("1", "IFCREAL(1.0)")]
    [InlineData(".5", "IFCREAL(0.5)")]
    [InlineData("1e3", "IFCREAL(1000.0)")]
    public void Apply_normalizes_non_step_real_spellings_to_legal_equivalent_literal(
      string canonicalValue,
      string expectedToken)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("TypedValue", "IfcReal", canonicalValue) });

      Assert.True(result.Success, result.Fields.Single().Message);
      string token = Assert.Single(
        document.OfType("IFCPROPERTYSINGLEVALUE")).Arguments[2];
      Assert.Equal(expectedToken, token);
      Assert.Matches(
        @"^IFCREAL\([+-]?[0-9]+\.[0-9]*(E[+-]?[0-9]+)?\)$",
        token);
      string inner = token.Substring(8, token.Length - 9);
      Assert.Equal(
        double.Parse(
          canonicalValue,
          NumberStyles.Float,
          CultureInfo.InvariantCulture),
        double.Parse(inner, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    public static IEnumerable<object[]> InvalidTypedValues()
    {
      yield return new object[] { "IfcBoolean", "true" };
      yield return new object[] { "IfcBoolean", ".U." };
      yield return new object[] { "IfcDate", "2026-02-30" };
      yield return new object[] { "IfcDate", "2026/08/04" };
      yield return new object[] { "IfcDateTime", "2026-08-04T12:34:56" };
      yield return new object[] { "IfcDateTime", "2026-08-04 12:34:56Z" };
      yield return new object[] { "IfcInteger", "2147483648" };
      yield return new object[] { "IfcInteger", "1);#999=IFCPROJECT(" };
      yield return new object[] { "IfcLabel", "" };
      yield return new object[] { "IfcLabel", new string('x', 256) };
      yield return new object[] { "IfcReal", "NaN" };
      yield return new object[] { "IfcReal", "Infinity" };
      yield return new object[] { "IfcReal", "1,5" };
      yield return new object[] { "IfcReal", "1e309" };
      yield return new object[] { "IfcText", "" };
      yield return new object[] { "IfcIdentifier", "unsupported" };
    }

    [Theory]
    [MemberData(nameof(InvalidTypedValues))]
    public void Apply_rejects_invalid_or_unsupported_typed_value(
      string declaredType,
      string canonicalValue)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("TypedValue", declaredType, canonicalValue) });

      AssertFailure(result, HbrIfcErrorCodes.InvalidValue, before, document);
    }

    [Fact]
    public void Apply_is_byte_for_byte_idempotent_on_second_run()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "3353559.52");

      HbrIfcEnrichmentResult first = new HbrIfcEnricher().Apply(
        document,
        new[] { value });
      string firstOutput = document.Serialize();
      HbrIfcEnrichmentResult second = new HbrIfcEnricher().Apply(
        document,
        new[] { value });

      Assert.True(first.Success);
      Assert.True(second.Success);
      Assert.Equal(0, second.CreatedProperties);
      Assert.Equal(0, second.CreatedPropertySets);
      Assert.Equal(0, second.CreatedRelationships);
      Assert.Equal(0, second.UpdatedProperties);
      Assert.Equal(
        first.Fields.Single().PropertyId,
        second.Fields.Single().PropertyId);
      Assert.Equal(
        first.Fields.Single().PropertySetId,
        second.Fields.Single().PropertySetId);
      Assert.Equal(
        first.Fields.Single().RelationshipId,
        second.Fields.Single().RelationshipId);
      Assert.Equal(firstOutput, document.Serialize());
    }

    [Fact]
    public void Apply_coalesces_duplicate_batch_values_with_same_token()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "3353559.52");

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { value, value });

      Assert.True(result.Success);
      Assert.Equal(1, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      Assert.Equal(0, result.UpdatedProperties);
      Assert.Equal(2, result.Fields.Count);
      Assert.Equal(result.Fields[0].PropertyId, result.Fields[1].PropertyId);
      Assert.Single(document.OfType("IFCPROPERTYSINGLEVALUE"));
    }

    [Fact]
    public void Apply_rejects_duplicate_batch_values_with_different_tokens()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[]
        {
          CreateValue("原点坐标X", "IfcReal", "1.0"),
          CreateValue("原点坐标X", "IfcReal", "2.0")
        });

      Assert.False(result.Success);
      Assert.Equal(2, result.Fields.Count);
      Assert.Equal(
        HbrIfcErrorCodes.TransactionAborted,
        result.Fields[0].ErrorCode);
      Assert.Equal(
        HbrIfcErrorCodes.IfcPropertyConflict,
        result.Fields[1].ErrorCode);
      Assert.Equal(0, result.CreatedProperties);
      Assert.Equal(0, result.CreatedPropertySets);
      Assert.Equal(0, result.CreatedRelationships);
      Assert.Equal(0, result.UpdatedProperties);
      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void Apply_rejects_update_to_property_shared_by_another_pset()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.0),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000000',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'3$$$$$$$$$$$$$$$$$$$$$',$,$,$,(#7),#21);\r\n"
        + "#23=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQZ',$,"
        + "'Pset_Other',$,(#20));\r\n"
        + "#24=IFCRELDEFINESBYPROPERTIES("
        + "'0krcLzwITMYvNX9cMehgQX',$,$,$,(#8),#23);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertyConflict,
        before,
        document);
    }

    [Fact]
    public void Apply_rejects_update_to_pset_shared_by_another_owner()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.0),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000000',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'3$$$$$$$$$$$$$$$$$$$$$',$,$,$,(#7,#8),#21);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertySetConflict,
        before,
        document);
    }

    [Fact]
    public void Apply_allows_noop_for_shared_property_with_exact_token()
    {
      string encodedName = IfcStepSyntax.EncodeString("原点坐标X");
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE(" + encodedName
        + ",$,IFCREAL(1.0),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000000',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'3$$$$$$$$$$$$$$$$$$$$$',$,$,$,(#7),#21);\r\n"
        + "#23=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQZ',$,"
        + "'Pset_Other',$,(#20));\r\n"
        + "#24=IFCRELDEFINESBYPROPERTIES("
        + "'0krcLzwITMYvNX9cMehgQX',$,$,$,(#8),#23);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.0") });

      Assert.True(result.Success);
      Assert.Equal(0, result.UpdatedProperties);
      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void Apply_rejects_same_name_non_single_value_property()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYENUMERATEDVALUE("
        + "'原点坐标X',$,(IFCREAL(1.0)),$);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#21);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertyConflict,
        before,
        document);
    }

    [Theory]
    [InlineData("PropertySetName")]
    [InlineData("PropertyName")]
    [InlineData("PropertyIdentity")]
    [InlineData("SemanticKey")]
    public void Apply_rejects_missing_required_input_identity(string member)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "2.0");
      switch (member)
      {
        case "PropertySetName": value.PropertySetName = " "; break;
        case "PropertyName": value.PropertyName = null; break;
        case "PropertyIdentity": value.PropertyIdentity = string.Empty; break;
        case "SemanticKey": value.SemanticKey = " "; break;
      }
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { value });

      AssertFailure(result, HbrIfcErrorCodes.InvalidValue, before, document);
    }

    [Theory]
    [InlineData("PropertySetName")]
    [InlineData("PropertyName")]
    [InlineData("PropertyIdentity")]
    [InlineData("SemanticKey")]
    public void Apply_and_inspector_share_required_value_contract(string member)
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateExistingTargetIfc("IFCREAL(2.0)"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "2.0");
      switch (member)
      {
        case "PropertySetName": value.PropertySetName = " "; break;
        case "PropertyName": value.PropertyName = null; break;
        case "PropertyIdentity": value.PropertyIdentity = string.Empty; break;
        case "SemanticKey": value.SemanticKey = " "; break;
      }
      string before = document.Serialize();

      HbrIfcEnrichmentResult enrichment = new HbrIfcEnricher().Apply(
        document,
        new[] { value });
      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(document, value);

      Assert.False(enrichment.Success);
      Assert.False(inspection.Success);
      Assert.Equal(
        HbrIfcErrorCodes.InvalidValue,
        Assert.Single(enrichment.Fields).ErrorCode);
      Assert.Equal(
        Assert.Single(enrichment.Fields).ErrorCode,
        inspection.ErrorCode);
      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void Apply_and_inspector_resolve_owner_before_formatting_typed_value()
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateExistingTargetIfc("IFCREAL(2.0)"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "not-a-real");
      value.OwnerEntityType = "IFCORGANIZATION";
      value.OwnerGlobalId = null;
      value.OwnerStrategy = HbrIfcOwnerStrategies.SingleEntityByType;
      string before = document.Serialize();

      HbrIfcEnrichmentResult enrichment = new HbrIfcEnricher().Apply(
        document,
        new[] { value });
      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(document, value);

      Assert.False(enrichment.Success);
      Assert.False(inspection.Success);
      Assert.Equal(
        HbrIfcErrorCodes.RuleNotImplemented,
        Assert.Single(enrichment.Fields).ErrorCode);
      Assert.Equal(
        Assert.Single(enrichment.Fields).ErrorCode,
        inspection.ErrorCode);
      Assert.Equal(before, document.Serialize());
    }

    [Theory]
    [InlineData("IfcLabel", true)]
    [InlineData("IfcLabel", false)]
    [InlineData("IfcText", true)]
    [InlineData("IfcText", false)]
    public void Apply_maps_isolated_utf16_surrogate_to_invalid_value(
      string declaredType,
      bool highSurrogate)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "非法字符串",
        declaredType,
        new string(highSurrogate ? '\uD800' : '\uDC00', 1));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { value });

      Assert.False(result.Success);
      Assert.Equal(
        HbrIfcErrorCodes.InvalidValue,
        Assert.Single(result.Fields).ErrorCode);
      Assert.Equal(before, document.Serialize());
    }

    [Theory]
    [InlineData("IfcLabel", true)]
    [InlineData("IfcLabel", false)]
    [InlineData("IfcText", true)]
    [InlineData("IfcText", false)]
    public void Inspector_maps_isolated_utf16_surrogate_to_invalid_value_without_throwing(
      string declaredType,
      bool highSurrogate)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "非法字符串",
        declaredType,
        new string(highSurrogate ? '\uD800' : '\uDC00', 1));
      string before = document.Serialize();
      HbrIfcFieldInspectionResult inspection = null;

      Exception exception = Record.Exception(() =>
        inspection = new HbrIfcFieldInspector().Inspect(document, value));

      Assert.Null(exception);
      Assert.NotNull(inspection);
      Assert.False(inspection.Success);
      Assert.Equal(HbrIfcErrorCodes.InvalidValue, inspection.ErrorCode);
      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void Inspector_reads_back_exact_owner_pset_property_path()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "3353559.52");
      HbrIfcEnrichmentResult enrichment = new HbrIfcEnricher().Apply(
        document,
        new[] { value });

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(document, value);

      Assert.True(inspection.Success);
      Assert.Equal(string.Empty, inspection.ErrorCode);
      Assert.Equal(enrichment.Fields.Single().OwnerId, inspection.OwnerId);
      Assert.Equal(
        enrichment.Fields.Single().PropertySetId,
        inspection.PropertySetId);
      Assert.Equal(
        enrichment.Fields.Single().PropertyId,
        inspection.PropertyId);
      Assert.Equal(
        enrichment.Fields.Single().RelationshipId,
        inspection.RelationshipId);
      Assert.Equal("IFCREAL", inspection.ActualIfcType);
      Assert.Equal("IFCREAL(3353559.52)", inspection.TypedToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inspector_rejects_target_or_foreign_pset_with_invalid_global_id(
      bool foreign)
    {
      string invalidPropertySet = foreign
        ? "#30=IFCPROPERTYSINGLEVALUE('Foreign',$,IFCLABEL('keep'),$);\r\n"
          + "#31=IFCPROPERTYSET('bad',$,'Pset_Foreign',$,(#30));\r\n"
          + "#32=IFCRELDEFINESBYPROPERTIES("
          + "'0000000000000000000004',$,$,$,(#8),#31);\r\n"
        : string.Empty;
      string targetGlobalId = foreign
        ? "'0000000000000000000001'"
        : "'bad'";
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE("
        + "'原点坐标X',$,IFCREAL(1.5),$);\r\n"
        + "#21=IFCPROPERTYSET(" + targetGlobalId + ",$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000002',$,$,$,(#7),#21);\r\n"
        + invalidPropertySet));
      string before = document.Serialize();

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(
          document,
          CreateValue("原点坐标X", "IfcReal", "1.5"));

      Assert.False(inspection.Success);
      Assert.Equal(
        HbrIfcErrorCodes.IfcPropertySetConflict,
        inspection.ErrorCode);
      Assert.Equal(before, document.Serialize());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inspector_rejects_target_or_foreign_relationship_with_invalid_global_id(
      bool foreign)
    {
      string invalidRelationship = foreign
        ? "#30=IFCPROPERTYSINGLEVALUE('Foreign',$,IFCLABEL('keep'),$);\r\n"
          + "#31=IFCPROPERTYSET('0000000000000000000003',$,"
          + "'Pset_Foreign',$,(#30));\r\n"
          + "#32=IFCRELDEFINESBYPROPERTIES("
          + "'bad',$,$,$,(#8),#31);\r\n"
        : string.Empty;
      string targetGlobalId = foreign
        ? "'0000000000000000000002'"
        : "'bad'";
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE("
        + "'原点坐标X',$,IFCREAL(1.5),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000001',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + targetGlobalId + ",$,$,$,(#7),#21);\r\n"
        + invalidRelationship));
      string before = document.Serialize();

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(
          document,
          CreateValue("原点坐标X", "IfcReal", "1.5"));

      Assert.False(inspection.Success);
      Assert.Equal(
        HbrIfcErrorCodes.IfcRelationshipConflict,
        inspection.ErrorCode);
      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void Inspector_rejects_orphan_single_value_with_invalid_arity()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE("
        + "'原点坐标X',$,IFCREAL(1.5),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000001',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000002',$,$,$,(#7),#21);\r\n"
        + "#30=IFCPROPERTYSINGLEVALUE('Orphan',$,IFCLABEL('bad'));\r\n"));
      string before = document.Serialize();

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(
          document,
          CreateValue("原点坐标X", "IfcReal", "1.5"));

      Assert.False(inspection.Success);
      Assert.Equal(
        HbrIfcErrorCodes.IfcPropertyConflict,
        inspection.ErrorCode);
      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void Inspector_returns_failure_for_unsupported_schema()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateExistingTargetIfc(
        "IFCREAL(2.0)").Replace(
          "FILE_SCHEMA(('IFC4'))",
          "FILE_SCHEMA(('IFC2X3'))"));
      string before = document.Serialize();

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(
          document,
          CreateValue("原点坐标X", "IfcReal", "2.0"));

      Assert.False(inspection.Success);
      Assert.Equal(HbrIfcErrorCodes.InvalidValue, inspection.ErrorCode);
      Assert.Contains("IFC4", inspection.Message);
      Assert.Equal(before, document.Serialize());
    }

    [Theory]
    [InlineData("DUPLICATE_DATA")]
    [InlineData("MISSING_DATA_END")]
    public void Inspector_returns_failure_for_invalid_exchange_structure(
      string mutation)
    {
      string fixture = CreateExistingTargetIfc("IFCREAL(2.0)");
      string malformed = string.Equals(
        mutation,
        "DUPLICATE_DATA",
        StringComparison.Ordinal)
        ? fixture.Replace("DATA;\r\n", "DATA;\r\nDATA;\r\n")
        : fixture.Replace(
          "ENDSEC;\r\nEND-ISO-10303-21;",
          "END-ISO-10303-21;");
      IfcStepDocument document = IfcStepDocument.Parse(malformed);
      string before = document.Serialize();

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(
          document,
          CreateValue("原点坐标X", "IfcReal", "2.0"));

      Assert.False(inspection.Success);
      Assert.Equal(HbrIfcErrorCodes.InvalidValue, inspection.ErrorCode);
      Assert.Contains("结构", inspection.Message);
      Assert.Equal(before, document.Serialize());
    }

    [Theory]
    [InlineData(typeof(HbrIfcFieldInspectionResult))]
    [InlineData(typeof(HbrIfcBatchInspectionResult))]
    public void Inspection_result_dtos_do_not_expose_parameterless_constructors(
      Type resultType)
    {
      Assert.DoesNotContain(
        resultType.GetConstructors(
          BindingFlags.Public
          | BindingFlags.NonPublic
          | BindingFlags.Instance),
        constructor => constructor.GetParameters().Length == 0);
    }

    [Theory]
    [InlineData(typeof(HbrIfcFieldInspectionResult))]
    [InlineData(typeof(HbrIfcBatchInspectionResult))]
    public void Inspection_result_dto_properties_are_truly_get_only(
      Type resultType)
    {
      Assert.All(
        resultType.GetProperties(BindingFlags.Public | BindingFlags.Instance),
        property => Assert.Null(property.GetSetMethod(true)));
    }

    [Fact]
    public void Inspector_batch_fields_are_not_exposed_as_an_array()
    {
      HbrIfcBatchInspectionResult result = CreateSuccessfulInspectionBatch();

      Assert.False(result.Fields.GetType().IsArray);
    }

    [Fact]
    public void Inspector_batch_fields_reject_element_replacement()
    {
      HbrIfcBatchInspectionResult result = CreateSuccessfulInspectionBatch();
      IList<HbrIfcFieldInspectionResult> fields =
        Assert.IsAssignableFrom<IList<HbrIfcFieldInspectionResult>>(
          result.Fields);

      Assert.Throws<NotSupportedException>(() =>
        fields[0] = result.Fields[0]);
    }

    [Fact]
    public void Batch_inspection_result_defensively_copies_source_fields()
    {
      HbrIfcFieldInspectionResult original =
        CreateFieldInspectionResultForContract(
          "identity-original",
          true,
          string.Empty,
          "ok");
      HbrIfcFieldInspectionResult replacement =
        CreateFieldInspectionResultForContract(
          "identity-replacement",
          true,
          string.Empty,
          "ok");
      HbrIfcFieldInspectionResult[] source = { original };
      HbrIfcBatchInspectionResult result =
        CreateBatchInspectionResultForContract(
          true,
          string.Empty,
          "ok",
          source);

      source[0] = replacement;

      Assert.Same(original, Assert.Single(result.Fields));
    }

    [Theory]
    [InlineData("NULL_COLLECTION")]
    [InlineData("NULL_ELEMENT")]
    public void Batch_inspection_result_rejects_null_fields(string mutation)
    {
      IReadOnlyList<HbrIfcFieldInspectionResult> fields =
        string.Equals(mutation, "NULL_COLLECTION", StringComparison.Ordinal)
          ? null
          : new HbrIfcFieldInspectionResult[] { null };

      Assert.Throws<ArgumentNullException>(() =>
        CreateBatchInspectionResultForContract(
          true,
          string.Empty,
          "ok",
          fields));
    }

    [Theory]
    [InlineData("FIELD_ERROR")]
    [InlineData("FIELD_MESSAGE")]
    [InlineData("BATCH_ERROR")]
    [InlineData("BATCH_MESSAGE")]
    public void Inspection_result_dtos_reject_null_text_state(string mutation)
    {
      Assert.Throws<ArgumentNullException>(() =>
      {
        if (mutation.StartsWith("FIELD", StringComparison.Ordinal))
          CreateFieldInspectionResultForContract(
            "identity",
            true,
            string.Equals(mutation, "FIELD_ERROR", StringComparison.Ordinal)
              ? null
              : string.Empty,
            string.Equals(mutation, "FIELD_MESSAGE", StringComparison.Ordinal)
              ? null
              : "ok");
        else
          CreateBatchInspectionResultForContract(
            true,
            string.Equals(mutation, "BATCH_ERROR", StringComparison.Ordinal)
              ? null
              : string.Empty,
            string.Equals(mutation, "BATCH_MESSAGE", StringComparison.Ordinal)
              ? null
              : "ok",
            Array.Empty<HbrIfcFieldInspectionResult>());
      });
    }

    [Theory]
    [InlineData("FIELD_SUCCESS_WITH_ERROR")]
    [InlineData("FIELD_FAILURE_WITHOUT_ERROR")]
    [InlineData("BATCH_SUCCESS_WITH_ERROR")]
    [InlineData("BATCH_FAILURE_WITHOUT_ERROR")]
    [InlineData("BATCH_SUCCESS_WITH_FAILED_FIELD")]
    [InlineData("BATCH_FAILURE_WITH_SUCCESS_FIELD")]
    public void Inspection_result_dtos_reject_contradictory_success_state(
      string mutation)
    {
      Assert.Throws<ArgumentException>(() =>
      {
        if (string.Equals(
          mutation,
          "FIELD_SUCCESS_WITH_ERROR",
          StringComparison.Ordinal))
          CreateFieldInspectionResultForContract(
            "identity",
            true,
            HbrIfcErrorCodes.InvalidValue,
            "bad");
        else if (string.Equals(
          mutation,
          "FIELD_FAILURE_WITHOUT_ERROR",
          StringComparison.Ordinal))
          CreateFieldInspectionResultForContract(
            "identity",
            false,
            string.Empty,
            "bad");
        else
        {
          bool success = mutation.StartsWith(
            "BATCH_SUCCESS",
            StringComparison.Ordinal);
          string errorCode = mutation.EndsWith(
            "WITHOUT_ERROR",
            StringComparison.Ordinal)
              ? string.Empty
              : success
                ? string.Equals(
                  mutation,
                  "BATCH_SUCCESS_WITH_ERROR",
                  StringComparison.Ordinal)
                    ? HbrIfcErrorCodes.InvalidValue
                    : string.Empty
                : HbrIfcErrorCodes.InvalidValue;
          bool fieldSuccess = !string.Equals(
            mutation,
            "BATCH_SUCCESS_WITH_FAILED_FIELD",
            StringComparison.Ordinal);
          HbrIfcFieldInspectionResult field =
            CreateFieldInspectionResultForContract(
              "identity",
              fieldSuccess,
              fieldSuccess ? string.Empty : HbrIfcErrorCodes.InvalidValue,
              fieldSuccess ? "ok" : "bad");
          CreateBatchInspectionResultForContract(
            success,
            errorCode,
            "state",
            new[] { field });
        }
      });
    }

    [Fact]
    public void Field_inspection_result_has_one_identity_required_constructor_and_preserves_identity()
    {
      ConstructorInfo constructor = Assert.Single(
        typeof(HbrIfcFieldInspectionResult).GetConstructors(
          BindingFlags.Public
          | BindingFlags.NonPublic
          | BindingFlags.Instance));
      ParameterInfo identityParameter = Assert.Single(
        constructor.GetParameters(),
        parameter => string.Equals(
          parameter.Name,
          "propertyIdentity",
          StringComparison.Ordinal));
      Assert.False(identityParameter.IsOptional);

      var success = new HbrIfcFieldInspectionResult(
        "identity-success",
        true,
        string.Empty,
        "ok",
        ownerId: 7,
        propertyId: 20,
        propertySetId: 21,
        relationshipId: 22,
        actualIfcType: "IFCREAL",
        typedToken: "IFCREAL(2.0)");
      var failure = new HbrIfcFieldInspectionResult(
        "identity-failure",
        false,
        HbrIfcErrorCodes.IfcValueMismatch,
        "bad");

      Assert.Equal("identity-success", success.PropertyIdentity);
      Assert.Equal("identity-failure", failure.PropertyIdentity);
    }

    [Fact]
    public void Batch_inspection_result_has_one_explicit_constructor()
    {
      ConstructorInfo constructor = Assert.Single(
        typeof(HbrIfcBatchInspectionResult).GetConstructors(
          BindingFlags.Public
          | BindingFlags.NonPublic
          | BindingFlags.Instance));
      Assert.Equal(
        new[] { "success", "errorCode", "message", "fields" },
        constructor.GetParameters().Select(parameter => parameter.Name));
      var field = new HbrIfcFieldInspectionResult(
        "identity",
        true,
        string.Empty,
        "ok");

      var result = new HbrIfcBatchInspectionResult(
        true,
        string.Empty,
        "ok",
        new[] { field });

      Assert.True(result.Success);
      Assert.Same(field, Assert.Single(result.Fields));
    }

    [Fact]
    public void Inspector_batch_returns_success_for_valid_ifc4_empty_values()
    {
      string data = "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n";
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(data));
      var observer = new RecordingOperationObserver();

      HbrIfcBatchInspectionResult result =
        new HbrIfcFieldInspector().InspectMany(
          document,
          Array.Empty<HbrIfcEnrichmentValue>(),
          observer);

      Assert.True(result.Success, result.Message);
      Assert.Equal(string.Empty, result.ErrorCode);
      Assert.Empty(result.Fields);
      Assert.Equal(
        0,
        observer.EventCount(DocumentEntityEnumerationKind()));
      Assert.Equal(
        0,
        observer.EventCount(HbrIfcOperationKind.GraphValidation));
    }

    [Fact]
    public void Inspector_batch_returns_document_failure_for_ifc2x3_empty_values()
    {
      string data = "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n";
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateIfc(data).Replace(
          "FILE_SCHEMA(('IFC4'))",
          "FILE_SCHEMA(('IFC2X3'))"));

      HbrIfcBatchInspectionResult result =
        new HbrIfcFieldInspector().InspectMany(
          document,
          Array.Empty<HbrIfcEnrichmentValue>());

      Assert.False(result.Success);
      Assert.Equal(HbrIfcErrorCodes.InvalidValue, result.ErrorCode);
      Assert.Equal("HBR IFC inspection 仅支持 IFC4。", result.Message);
      Assert.Empty(result.Fields);
    }

    [Fact]
    public void Inspector_batch_returns_document_failure_without_throwing_for_malformed_ifc4_empty_values()
    {
      string data = "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n";
      string malformed = CreateIfc(data).Replace(
        "DATA;\r\n",
        "DATA;\r\nDATA;\r\n");
      IfcStepDocument document = IfcStepDocument.Parse(malformed);
      HbrIfcBatchInspectionResult result = null;

      Exception exception = Record.Exception(() =>
      {
        result = new HbrIfcFieldInspector().InspectMany(
          document,
          Array.Empty<HbrIfcEnrichmentValue>());
      });

      Assert.Null(exception);
      Assert.NotNull(result);
      Assert.False(result.Success);
      Assert.Equal(HbrIfcErrorCodes.InvalidValue, result.ErrorCode);
      Assert.Equal(
        "IFC inspection 文档结构无效："
          + "IFC STEP 必须是完整交换文件，且实体与 header record 位于正确区段。",
        result.Message);
      Assert.Empty(result.Fields);
    }

    [Fact]
    public void Inspector_result_carries_property_identity_for_single_success()
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateExistingTargetIfc("IFCREAL(2.0)"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "2.0");

      HbrIfcFieldInspectionResult result =
        new HbrIfcFieldInspector().Inspect(document, value);

      Assert.True(result.Success, result.Message);
      Assert.Equal(value.PropertyIdentity, result.PropertyIdentity);
    }

    [Fact]
    public void Inspector_batch_preserves_identity_for_success_null_mismatch_and_duplicate_path_in_input_order()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(2.0),$);\r\n"
        + "#30=IFCPROPERTYSINGLEVALUE('原点坐标Y',$,IFCREAL(4.0),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000001',$,"
        + "'Pset_申报信息属性集',$,(#20,#30));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000002',$,$,$,(#7),#21);\r\n"));
      HbrIfcEnrichmentValue success = CreateValue(
        "原点坐标X",
        "IfcReal",
        "2.0");
      success.PropertyIdentity = "identity-success";
      HbrIfcEnrichmentValue mismatch = CreateValue(
        "原点坐标Y",
        "IfcReal",
        "3.0");
      mismatch.PropertyIdentity = "identity-mismatch";
      HbrIfcEnrichmentValue duplicatePath = CreateValue(
        "原点坐标X",
        "IfcReal",
        "2.0");
      duplicatePath.PropertyIdentity = "identity-duplicate-path";
      HbrIfcEnrichmentValue[] values =
      {
        success,
        null,
        mismatch,
        duplicatePath
      };

      HbrIfcBatchInspectionResult result =
        new HbrIfcFieldInspector().InspectMany(document, values);

      Assert.False(result.Success);
      Assert.Equal(HbrIfcErrorCodes.InvalidValue, result.ErrorCode);
      Assert.Equal(
        new[]
        {
          "identity-success",
          null,
          "identity-mismatch",
          "identity-duplicate-path"
        },
        result.Fields.Select(field => field.PropertyIdentity));
      Assert.True(result.Fields[0].Success, result.Fields[0].Message);
      Assert.Equal(
        HbrIfcErrorCodes.InvalidValue,
        result.Fields[1].ErrorCode);
      Assert.Equal(
        HbrIfcErrorCodes.IfcValueMismatch,
        result.Fields[2].ErrorCode);
      Assert.True(result.Fields[3].Success, result.Fields[3].Message);
    }

    [Theory]
    [InlineData("SCHEMA", HbrIfcErrorCodes.InvalidValue)]
    [InlineData("STRUCTURE", HbrIfcErrorCodes.InvalidValue)]
    [InlineData("GRAPH", HbrIfcErrorCodes.IfcPropertyConflict)]
    public void Inspector_batch_preserves_identity_for_global_failures(
      string failureKind,
      string expectedErrorCode)
    {
      string data = "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n";
      if (string.Equals(failureKind, "GRAPH", StringComparison.Ordinal))
        data += "#30=IFCPROPERTYSINGLEVALUE('Orphan',$,IFCLABEL('bad'));\r\n";
      string fixture = CreateIfc(data);
      if (string.Equals(failureKind, "SCHEMA", StringComparison.Ordinal))
        fixture = fixture.Replace(
          "FILE_SCHEMA(('IFC4'))",
          "FILE_SCHEMA(('IFC2X3'))");
      else if (string.Equals(
        failureKind,
        "STRUCTURE",
        StringComparison.Ordinal))
        fixture = fixture.Replace("DATA;\r\n", "DATA;\r\nDATA;\r\n");
      IfcStepDocument document = IfcStepDocument.Parse(fixture);
      HbrIfcEnrichmentValue first = CreateValue(
        "第一字段",
        "IfcReal",
        "1.0");
      first.PropertyIdentity = "identity-first";
      HbrIfcEnrichmentValue last = CreateValue(
        "末尾字段",
        "IfcReal",
        "2.0");
      last.PropertyIdentity = "identity-last";

      HbrIfcBatchInspectionResult result =
        new HbrIfcFieldInspector().InspectMany(
          document,
          new[] { first, null, last });

      Assert.False(result.Success);
      Assert.Equal(expectedErrorCode, result.ErrorCode);
      Assert.Equal(
        new[] { "identity-first", null, "identity-last" },
        result.Fields.Select(field => field.PropertyIdentity));
      Assert.All(result.Fields, field => Assert.False(field.Success));
    }

    [Fact]
    public void Inspector_batch_preserves_identity_for_multi_owner_and_multi_pset_local_failures()
    {
      const string buildingGlobalId = "0krcLzwITMYvNX9cMehgQY";
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('" + buildingGlobalId
        + "',$,'Building',$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('ProjectValue',$,IFCREAL(1.0),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000001',$,"
        + "'Pset_Project',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000002',$,$,$,(#7),#21);\r\n"
        + "#30=IFCPROPERTYSINGLEVALUE('BuildingValue',$,IFCREAL(2.0),$);\r\n"
        + "#31=IFCPROPERTYSET('0000000000000000000003',$,"
        + "'Pset_Building',$,(#30));\r\n"
        + "#32=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000004',$,$,$,(#8),#31);\r\n"));
      HbrIfcEnrichmentValue projectSuccess = CreateValue(
        "ProjectValue",
        "IfcReal",
        "1.0");
      projectSuccess.PropertySetName = "Pset_Project";
      projectSuccess.PropertyIdentity = "identity-project-success";
      HbrIfcEnrichmentValue buildingMismatch = CreateValue(
        "BuildingValue",
        "IfcReal",
        "3.0");
      buildingMismatch.OwnerEntityType = "IfcBuilding";
      buildingMismatch.OwnerGlobalId = buildingGlobalId;
      buildingMismatch.PropertySetName = "Pset_Building";
      buildingMismatch.PropertyIdentity = "identity-building-mismatch";
      HbrIfcEnrichmentValue projectMissingPset = CreateValue(
        "MissingProjectValue",
        "IfcReal",
        "4.0");
      projectMissingPset.PropertySetName = "Pset_Missing";
      projectMissingPset.PropertyIdentity = "identity-project-missing-pset";
      HbrIfcEnrichmentValue buildingMissingProperty = CreateValue(
        "MissingBuildingValue",
        "IfcReal",
        "5.0");
      buildingMissingProperty.OwnerEntityType = "IfcBuilding";
      buildingMissingProperty.OwnerGlobalId = buildingGlobalId;
      buildingMissingProperty.PropertySetName = "Pset_Building";
      buildingMissingProperty.PropertyIdentity =
        "identity-building-missing-property";

      HbrIfcBatchInspectionResult result =
        new HbrIfcFieldInspector().InspectMany(
          document,
          new[]
          {
            projectSuccess,
            buildingMismatch,
            projectMissingPset,
            buildingMissingProperty
          });

      Assert.False(result.Success);
      Assert.Equal(
        new[]
        {
          "identity-project-success",
          "identity-building-mismatch",
          "identity-project-missing-pset",
          "identity-building-missing-property"
        },
        result.Fields.Select(field => field.PropertyIdentity));
      Assert.True(result.Fields[0].Success, result.Fields[0].Message);
      Assert.Equal(
        HbrIfcErrorCodes.IfcValueMismatch,
        result.Fields[1].ErrorCode);
      Assert.Equal(
        HbrIfcErrorCodes.IfcFieldNotFound,
        result.Fields[2].ErrorCode);
      Assert.Equal(
        HbrIfcErrorCodes.IfcFieldNotFound,
        result.Fields[3].ErrorCode);
    }

    [Theory]
    [InlineData("$")]
    [InlineData("'not-a-global-id'")]
    public void Inspector_rejects_malformed_global_id_on_any_same_type_owner(
      string malformedGlobalIdToken)
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCPROJECT(" + malformedGlobalIdToken
        + ",$,'Malformed',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE("
        + "'原点坐标X',$,IFCREAL(3353559.52),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000001',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000002',$,$,$,(#7),#21);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "3353559.52");

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(document, value);

      Assert.False(inspection.Success);
      Assert.Equal(HbrIfcErrorCodes.InvalidValue, inspection.ErrorCode);
      Assert.Contains("GlobalId", inspection.Message);
    }

    [Fact]
    public void Inspector_does_not_accept_global_same_name_decoy_property()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCTEXT('wrong'),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000000',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'3$$$$$$$$$$$$$$$$$$$$$',$,$,$,(#7),#21);\r\n"
        + "#30=IFCPROPERTYSINGLEVALUE("
        + "'原点坐标X',$,IFCREAL(3353559.52),$);\r\n"
        + "#31=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQZ',$,"
        + "'Pset_Decoy',$,(#30));\r\n"
        + "#32=IFCRELDEFINESBYPROPERTIES("
        + "'0krcLzwITMYvNX9cMehgQX',$,$,$,(#8),#31);\r\n"));
      HbrIfcEnrichmentValue value = CreateValue(
        "原点坐标X",
        "IfcReal",
        "3353559.52");

      HbrIfcFieldInspectionResult inspection =
        new HbrIfcFieldInspector().Inspect(document, value);

      Assert.False(inspection.Success);
      Assert.Equal(HbrIfcErrorCodes.IfcTypeMismatch, inspection.ErrorCode);
      Assert.Equal(20, inspection.PropertyId);
      Assert.Equal("IFCTEXT", inspection.ActualIfcType);
    }

    [Fact]
    public void Apply_rejects_deterministic_pset_global_id_collision()
    {
      string collision = IfcGuidCodec.CreateDeterministic(
        Guid.Parse("b3f9dc18-f6b4-5bd8-9d65-2ebed89f63c8"),
        "PSET|" + OwnerGlobalId + "|Pset_申报信息属性集");
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('Other',$,IFCLABEL('keep'),$);\r\n"
        + "#21=IFCPROPERTYSET('" + collision + "',$,'Pset_Other',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#8),#21);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertySetConflict,
        before,
        document);
    }

    [Theory]
    [InlineData("IFCSITE")]
    [InlineData("IFCWALL")]
    [InlineData("IFCTASK")]
    [InlineData("IFCGROUP")]
    public void Apply_rejects_pset_global_id_occupied_by_any_ifc4_root_type(
      string carrierType)
    {
      string collision = IfcGuidCodec.CreateDeterministic(
        Guid.Parse("b3f9dc18-f6b4-5bd8-9d65-2ebed89f63c8"),
        "PSET|" + OwnerGlobalId + "|Pset_申报信息属性集");
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=" + carrierType + "('" + collision + "');\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcPropertySetConflict,
        before,
        document);
    }

    [Fact]
    public void Apply_rejects_deterministic_relationship_global_id_collision()
    {
      string collision = IfcGuidCodec.CreateDeterministic(
        Guid.Parse("b3f9dc18-f6b4-5bd8-9d65-2ebed89f63c8"),
        "RELATIONSHIP|" + OwnerGlobalId + "|Pset_申报信息属性集");
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('Other',$,IFCLABEL('keep'),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000000',$,"
        + "'Pset_Other',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES('" + collision
        + "',$,$,$,(#8),#21);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcRelationshipConflict,
        before,
        document);
    }

    [Theory]
    [InlineData("PSET")]
    [InlineData("RELATIONSHIP")]
    public void Apply_does_not_treat_property_name_as_global_id_occupancy(
      string semanticKind)
    {
      Guid namespaceId = Guid.Parse(
        "b3f9dc18-f6b4-5bd8-9d65-2ebed89f63c8");
      string collision = IfcGuidCodec.CreateDeterministic(
        namespaceId,
        semanticKind + "|" + OwnerGlobalId + "|Pset_申报信息属性集");
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE('" + collision
        + "','keep-description',IFCLABEL('keep'),$);\r\n"));
      IfcStepEntity property = document.GetEntity(20);
      string propertyBefore = property.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.0") });

      Assert.True(result.Success);
      Assert.Equal(1, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      Assert.Same(property, document.GetEntity(20));
      Assert.Equal(propertyBefore, property.Serialize());
      Assert.Equal(IfcStepSyntax.EncodeString(collision), property.Arguments[0]);
      Assert.Equal("'keep-description'", property.Arguments[1]);
      Assert.Equal("IFCLABEL('keep')", property.Arguments[2]);
      Assert.Equal("$", property.Arguments[3]);
    }

    [Fact]
    public void Apply_reports_entity_id_exhaustion_without_partial_write()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#2147483647=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "1.0") });

      AssertFailure(
        result,
        HbrIfcErrorCodes.IfcMutationFailed,
        before,
        document);
    }

    [Fact]
    public void Apply_updates_only_unique_existing_target_property()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#19=IFCPROPERTYSINGLEVALUE('保留属性',$,IFCLABEL('keep'),$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE("
        + "'原点坐标X','keep-description',IFCTEXT('old'),#30);\r\n"
        + "#21=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
        + "'Pset_申报信息属性集',$,(#19,#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000000',$,$,$,(#7),#21);\r\n"
        + "#30=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);\r\n"));

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3353559.52") });

      Assert.True(result.Success);
      Assert.Equal(0, result.CreatedProperties);
      Assert.Equal(0, result.CreatedPropertySets);
      Assert.Equal(0, result.CreatedRelationships);
      Assert.Equal(1, result.UpdatedProperties);
      Assert.Equal(20, result.Fields.Single().PropertyId);
      Assert.Equal(
        new[] { 19, 20 },
        IfcStepSyntax.ParseReferenceList(document.GetEntity(21).Arguments[4]));
      Assert.Equal("'keep-description'", document.GetEntity(20).Arguments[1]);
      Assert.Equal("IFCREAL(3353559.52)", document.GetEntity(20).Arguments[2]);
      Assert.Equal("#30", document.GetEntity(20).Arguments[3]);
      Assert.Equal("IFCLABEL('keep')", document.GetEntity(19).Arguments[2]);
    }

    [Fact]
    public void Apply_update_preserves_existing_entity_identity_and_live_handles()
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateExistingTargetIfc("IFCREAL(1.0)"));
      int[] existingIds = { 7, 20, 21, 22 };
      Dictionary<int, IfcStepEntity> handles = existingIds.ToDictionary(
        id => id,
        id => document.GetEntity(id));

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      Assert.True(result.Success);
      Assert.Equal(1, result.UpdatedProperties);
      foreach (int id in existingIds)
        Assert.Same(handles[id], document.GetEntity(id));
      handles[20].SetArgument(2, "IFCREAL(3.0)");
      Assert.Contains("IFCREAL(3.0)", document.Serialize());
    }

    [Fact]
    public void Apply_create_preserves_existing_owner_identity_and_live_handle()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      IfcStepEntity ownerHandle = document.GetEntity(7);

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      Assert.True(result.Success);
      Assert.Same(ownerHandle, document.GetEntity(7));
      Assert.NotNull(document.GetEntity(result.Fields.Single().PropertyId.Value));
      ownerHandle.SetArgument(2, "'Renamed Project'");
      Assert.Contains("'Renamed Project'", document.Serialize());
    }

    [Fact]
    public void Apply_empty_batch_preserves_identity_and_skips_commit()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      IfcStepEntity ownerHandle = document.GetEntity(7);
      string before = document.Serialize();
      var observer = new RecordingOperationObserver();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        Array.Empty<HbrIfcEnrichmentValue>(),
        observer);

      Assert.True(result.Success);
      Assert.Empty(result.Fields);
      Assert.Same(ownerHandle, document.GetEntity(7));
      Assert.Equal(before, document.Serialize());
      Assert.Equal(0, observer.TotalEventCount);
      ownerHandle.SetArgument(2, "'After Empty Batch'");
      Assert.Contains("'After Empty Batch'", document.Serialize());
    }

    [Fact]
    public void Apply_exact_noop_preserves_all_existing_entity_identity()
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateExistingTargetIfc("IFCREAL(2.0)").Replace(
          "'原点坐标X'",
          IfcStepSyntax.EncodeString("原点坐标X")));
      int[] existingIds = { 7, 20, 21, 22 };
      Dictionary<int, IfcStepEntity> handles = existingIds.ToDictionary(
        id => id,
        id => document.GetEntity(id));
      string before = document.Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });

      Assert.True(result.Success);
      Assert.Equal(0, result.CreatedProperties);
      Assert.Equal(0, result.CreatedPropertySets);
      Assert.Equal(0, result.CreatedRelationships);
      Assert.Equal(0, result.UpdatedProperties);
      foreach (int id in existingIds)
        Assert.Same(handles[id], document.GetEntity(id));
      Assert.Equal(before, document.Serialize());
      handles[20].SetArgument(2, "IFCREAL(4.0)");
      Assert.Contains("IFCREAL(4.0)", document.Serialize());
    }

    [Theory]
    [InlineData(32, 2)]
    [InlineData(2000, 128)]
    public void Apply_uses_one_graph_index_and_batch_inspection_regardless_of_scale(
      int unrelatedEntityCount,
      int foreignPropertySetCount)
    {
      string fixture = CreatePerformanceIfc(
        unrelatedEntityCount,
        foreignPropertySetCount,
        out int[] foreignIds);
      IfcStepDocument document = IfcStepDocument.Parse(fixture);
      int initialEntityCount = document.Entities.Count();
      Dictionary<int, string> foreignBefore = foreignIds.ToDictionary(
        id => id,
        id => document.GetEntity(id).Serialize());
      HbrIfcEnrichmentValue[] values = Enumerable.Range(0, 359)
        .Select(index => CreateValue(
          "性能字段" + index.ToString("D3", CultureInfo.InvariantCulture),
          "IfcReal",
          (index + 1).ToString(CultureInfo.InvariantCulture)))
        .ToArray();
      var observer = new RecordingOperationObserver();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        values,
        observer);

      Assert.True(result.Success, result.Fields.FirstOrDefault()?.Message);
      Assert.Equal(359, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      Assert.Equal(0, result.UpdatedProperties);
      Assert.Equal(359, result.Fields.Count);
      Assert.All(result.Fields, field =>
      {
        Assert.True(field.Success);
        Assert.True(field.ExactInspectionPassed);
      });
      Assert.Equal(
        359,
        result.Fields.Select(field => field.PropertyId).Distinct().Count());
      Assert.Single(result.Fields.Select(field => field.PropertySetId).Distinct());
      Assert.Single(result.Fields.Select(field => field.RelationshipId).Distinct());
      foreach (KeyValuePair<int, string> pair in foreignBefore)
        Assert.Equal(pair.Value, document.GetEntity(pair.Key).Serialize());

      Assert.True(
        observer.MatchesExpectedProfile(initialEntityCount, 359),
        observer.DescribeProfile());
      Assert.Equal(1, observer.EventCount(HbrIfcOperationKind.CandidateClone));
      Assert.Equal(1, observer.EventCount(HbrIfcOperationKind.GraphIndexFullPass));
      Assert.Equal(
        initialEntityCount,
        observer.ItemCount(HbrIfcOperationKind.GraphIndexFullPass));
      Assert.Equal(1, observer.EventCount(HbrIfcOperationKind.GraphValidation));
      Assert.Equal(1, observer.EventCount(HbrIfcOperationKind.BatchInspection));
      Assert.Equal(
        359,
        observer.ItemCount(HbrIfcOperationKind.BatchInspection));
      Assert.Equal(
        359,
        observer.EventCount(HbrIfcOperationKind.IndexedFieldLookup));
      Assert.Equal(1, observer.EventCount(HbrIfcOperationKind.CommitTransfer));
      Assert.Equal(0, observer.EventCount(HbrIfcOperationKind.SectionBoundaryScan));
      Assert.Equal(0, observer.EventCount(HbrIfcOperationKind.MaximumIdScan));
      Assert.Equal(0, observer.EventCount(HbrIfcOperationKind.ForeignRelationshipRescan));
      Assert.Equal(0, observer.EventCount(HbrIfcOperationKind.CommitClone));
      Assert.Equal(
        1,
        observer.EventCount(DocumentEntityEnumerationKind()));
    }

    [Fact]
    public void Apply_uses_one_real_document_enumeration_with_many_irrelevant_owner_relationships()
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateOwnerRelationshipPerformanceIfc(512));
      int initialEntityCount = document.Entities.Count();
      HbrIfcEnrichmentValue[] values = Enumerable.Range(0, 359)
        .Select(index => CreateValue(
          "关系索引字段" + index.ToString("D3", CultureInfo.InvariantCulture),
          "IfcReal",
          (index + 1).ToString(CultureInfo.InvariantCulture)))
        .ToArray();
      var observer = new RecordingOperationObserver();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        values,
        observer);

      Assert.True(result.Success, result.Fields.FirstOrDefault()?.Message);
      Assert.Equal(359, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      Assert.Equal(359, result.Fields.Count);
      Assert.Equal(1, observer.EventCount(HbrIfcOperationKind.GraphIndexFullPass));
      Assert.Equal(
        initialEntityCount,
        observer.ItemCount(HbrIfcOperationKind.GraphIndexFullPass));
      Assert.Equal(
        1,
        observer.EventCount(DocumentEntityEnumerationKind()));
    }

    [Fact]
    public void Inspector_batch_uses_one_real_document_enumeration_and_matches_single_results()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      HbrIfcEnrichmentValue[] values = Enumerable.Range(0, 359)
        .Select(index => CreateValue(
          "批量检查字段" + index.ToString("D3", CultureInfo.InvariantCulture),
          "IfcReal",
          (index + 1).ToString(CultureInfo.InvariantCulture)))
        .ToArray();
      HbrIfcEnrichmentResult enrichment = new HbrIfcEnricher().Apply(
        document,
        values);
      Assert.True(enrichment.Success, enrichment.Fields.FirstOrDefault()?.Message);
      string before = document.Serialize();
      var observer = new RecordingOperationObserver();
      var inspector = new HbrIfcFieldInspector();

      HbrIfcBatchInspectionResult batchResult =
        inspector.InspectMany(document, values, observer);
      IReadOnlyList<HbrIfcFieldInspectionResult> batch = batchResult.Fields;
      HbrIfcFieldInspectionResult[] singles = values
        .Select(value => inspector.Inspect(document, value))
        .ToArray();

      Assert.True(batchResult.Success, batchResult.Message);
      Assert.Equal(359, batch.Count);
      Assert.All(batch, result => Assert.True(result.Success, result.Message));
      for (int index = 0; index < batch.Count; index++)
      {
        Assert.Equal(singles[index].Success, batch[index].Success);
        Assert.Equal(
          singles[index].PropertyIdentity,
          batch[index].PropertyIdentity);
        Assert.Equal(singles[index].ErrorCode, batch[index].ErrorCode);
        Assert.Equal(singles[index].OwnerId, batch[index].OwnerId);
        Assert.Equal(singles[index].PropertySetId, batch[index].PropertySetId);
        Assert.Equal(singles[index].PropertyId, batch[index].PropertyId);
        Assert.Equal(singles[index].RelationshipId, batch[index].RelationshipId);
        Assert.Equal(singles[index].ActualIfcType, batch[index].ActualIfcType);
        Assert.Equal(singles[index].TypedToken, batch[index].TypedToken);
      }
      Assert.Equal(
        1,
        observer.EventCount(DocumentEntityEnumerationKind()));
      Assert.Equal(
        1,
        observer.EventCount(HbrIfcOperationKind.GraphValidation));
      Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void Apply_merges_multiple_values_into_one_created_pset()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[]
        {
          CreateValue("原点坐标X", "IfcReal", "3353559.52"),
          CreateValue("原点坐标Y", "IfcReal", "38589642.165")
        });

      Assert.True(result.Success);
      Assert.Equal(2, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      Assert.Equal(2, result.Fields.Count);
      Assert.Equal(
        result.Fields[0].PropertySetId,
        result.Fields[1].PropertySetId);
      Assert.Equal(
        result.Fields[0].RelationshipId,
        result.Fields[1].RelationshipId);
      IfcStepEntity propertySet = Assert.Single(
        document.OfType("IFCPROPERTYSET"));
      Assert.Equal(
        2,
        IfcStepSyntax.ParseReferenceList(propertySet.Arguments[4]).Count);
    }

    [Fact]
    public void Apply_is_byte_deterministic_when_batch_order_is_reversed()
    {
      IfcStepDocument forwardDocument = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      IfcStepDocument reverseDocument = IfcStepDocument.Parse(
        forwardDocument.Serialize());
      HbrIfcEnrichmentValue x = CreateValue(
        "原点坐标X",
        "IfcReal",
        "3353559.52");
      HbrIfcEnrichmentValue y = CreateValue(
        "原点坐标Y",
        "IfcReal",
        "38589642.165");

      HbrIfcEnrichmentResult forward = new HbrIfcEnricher().Apply(
        forwardDocument,
        new[] { x, y });
      HbrIfcEnrichmentResult reverse = new HbrIfcEnricher().Apply(
        reverseDocument,
        new[] { y, x });

      Assert.True(forward.Success);
      Assert.True(reverse.Success);
      Assert.Equal(forwardDocument.Serialize(), reverseDocument.Serialize());
      Dictionary<string, HbrIfcEnrichmentFieldResult> forwardByIdentity =
        forward.Fields.ToDictionary(field => field.PropertyIdentity);
      Dictionary<string, HbrIfcEnrichmentFieldResult> reverseByIdentity =
        reverse.Fields.ToDictionary(field => field.PropertyIdentity);
      Assert.Equal(forwardByIdentity.Keys.OrderBy(key => key),
        reverseByIdentity.Keys.OrderBy(key => key));
      foreach (string identity in forwardByIdentity.Keys)
      {
        Assert.Equal(
          forwardByIdentity[identity].PropertyId,
          reverseByIdentity[identity].PropertyId);
        Assert.Equal(
          forwardByIdentity[identity].PropertySetId,
          reverseByIdentity[identity].PropertySetId);
        Assert.Equal(
          forwardByIdentity[identity].RelationshipId,
          reverseByIdentity[identity].RelationshipId);
      }
      IfcStepEntity forwardPset = Assert.Single(
        forwardDocument.OfType("IFCPROPERTYSET"));
      IfcStepEntity reversePset = Assert.Single(
        reverseDocument.OfType("IFCPROPERTYSET"));
      Assert.Equal(
        IfcStepSyntax.ParseReferenceList(forwardPset.Arguments[4]),
        IfcStepSyntax.ParseReferenceList(reversePset.Arguments[4]));
    }

    [Fact]
    public void Apply_maps_deterministic_failure_back_to_original_batch_order()
    {
      IfcStepDocument forwardDocument = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"));
      IfcStepDocument reverseDocument = IfcStepDocument.Parse(
        forwardDocument.Serialize());
      HbrIfcEnrichmentValue first = CreateValue(
        "原点坐标X",
        "IfcReal",
        "1.0");
      first.PropertyIdentity = "property-a";
      HbrIfcEnrichmentValue second = CreateValue(
        "原点坐标X",
        "IfcReal",
        "2.0");
      second.PropertyIdentity = "property-b";

      HbrIfcEnrichmentResult forward = new HbrIfcEnricher().Apply(
        forwardDocument,
        new[] { first, second });
      HbrIfcEnrichmentResult reverse = new HbrIfcEnricher().Apply(
        reverseDocument,
        new[] { second, first });

      Assert.False(forward.Success);
      Assert.False(reverse.Success);
      Assert.Equal(
        new[] { "property-a", "property-b" },
        forward.Fields.Select(field => field.PropertyIdentity));
      Assert.Equal(
        new[] { "property-b", "property-a" },
        reverse.Fields.Select(field => field.PropertyIdentity));
      Dictionary<string, string> forwardErrors = forward.Fields.ToDictionary(
        field => field.PropertyIdentity,
        field => field.ErrorCode);
      Dictionary<string, string> reverseErrors = reverse.Fields.ToDictionary(
        field => field.PropertyIdentity,
        field => field.ErrorCode);
      Assert.Equal(forwardErrors, reverseErrors);
      Assert.Equal(
        HbrIfcErrorCodes.TransactionAborted,
        forwardErrors["property-a"]);
      Assert.Equal(
        HbrIfcErrorCodes.IfcPropertyConflict,
        forwardErrors["property-b"]);
      Assert.Equal(forwardDocument.Serialize(), reverseDocument.Serialize());
    }

    [Fact]
    public void Apply_creates_isolated_pset_when_same_name_pset_belongs_to_foreign_owner()
    {
      IfcStepDocument document = IfcStepDocument.Parse(CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#8=IFCBUILDING('0krcLzwITMYvNX9cMehgQY',$,'Building',"
        + "$,$,$,$,$,$,$,$);\r\n"
        + "#19=IFCPROPERTYSINGLEVALUE("
        + "'ForeignOnly',$,IFCLABEL('private'),$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE("
        + "'原点坐标X',$,IFCREAL(3353559.52),$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000000',$,"
        + "'Pset_申报信息属性集',$,(#19,#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'3$$$$$$$$$$$$$$$$$$$$$',$,$,$,(#8),#21);\r\n"));
      string foreignPropertySetBefore = document.GetEntity(21).Serialize();
      string foreignRelationshipBefore = document.GetEntity(22).Serialize();

      HbrIfcEnrichmentResult result = new HbrIfcEnricher().Apply(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "3353559.52") });

      Assert.True(result.Success);
      Assert.Equal(1, result.CreatedProperties);
      Assert.Equal(1, result.CreatedPropertySets);
      Assert.Equal(1, result.CreatedRelationships);
      HbrIfcEnrichmentFieldResult field = Assert.Single(result.Fields);
      Assert.NotEqual(21, field.PropertySetId);
      Assert.NotEqual(22, field.RelationshipId);
      Assert.Equal(foreignPropertySetBefore, document.GetEntity(21).Serialize());
      Assert.Equal(foreignRelationshipBefore, document.GetEntity(22).Serialize());
      Assert.Equal(
        new[] { 8 },
        IfcStepSyntax.ParseReferenceList(document.GetEntity(22).Arguments[4]));
      IfcStepEntity targetPropertySet = document.GetEntity(
        field.PropertySetId.Value);
      int[] targetPropertyIds = IfcStepSyntax.ParseReferenceList(
        targetPropertySet.Arguments[4]).ToArray();
      int targetPropertyId = Assert.Single(targetPropertyIds);
      Assert.NotEqual(19, targetPropertyId);
      Assert.NotEqual(20, targetPropertyId);
      Assert.Equal(field.PropertyId, targetPropertyId);
      Assert.Equal(
        "原点坐标X",
        IfcStepSyntax.DecodeString(document.GetEntity(targetPropertyId).Arguments[0]));
      IfcStepEntity targetRelationship = document.GetEntity(
        field.RelationshipId.Value);
      Assert.Equal(
        new[] { 7 },
        IfcStepSyntax.ParseReferenceList(targetRelationship.Arguments[4]));
      Assert.Equal(
        targetPropertySet.Id,
        IfcStepSyntax.ParseReference(targetRelationship.Arguments[5]));
      Assert.Equal(2, document.OfType("IFCPROPERTYSET").Count());
      Assert.Equal(2, document.OfType("IFCRELDEFINESBYPROPERTIES").Count());
    }

    private static HbrIfcBatchInspectionResult CreateSuccessfulInspectionBatch()
    {
      IfcStepDocument document = IfcStepDocument.Parse(
        CreateExistingTargetIfc("IFCREAL(2.0)"));
      return new HbrIfcFieldInspector().InspectMany(
        document,
        new[] { CreateValue("原点坐标X", "IfcReal", "2.0") });
    }

    private static HbrIfcFieldInspectionResult
      CreateFieldInspectionResultForContract(
        string propertyIdentity,
        bool success,
        string errorCode,
        string message)
    {
      Type type = typeof(HbrIfcFieldInspectionResult);
      ConstructorInfo constructor = type.GetConstructors(
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .SingleOrDefault(candidate => candidate.GetParameters().Length != 0);
      if (constructor != null)
        return InvokeInspectionConstructor<HbrIfcFieldInspectionResult>(
          constructor,
          new object[]
          {
            propertyIdentity,
            success,
            errorCode,
            message,
            null,
            null,
            null,
            null,
            null,
            null
          });

      var legacy = (HbrIfcFieldInspectionResult)Activator.CreateInstance(
        type,
        true);
      type.GetProperty("PropertyIdentity").SetValue(legacy, propertyIdentity);
      type.GetProperty("Success").SetValue(legacy, success);
      type.GetProperty("ErrorCode").SetValue(legacy, errorCode);
      type.GetProperty("Message").SetValue(legacy, message);
      return legacy;
    }

    private static HbrIfcBatchInspectionResult
      CreateBatchInspectionResultForContract(
        bool success,
        string errorCode,
        string message,
        IReadOnlyList<HbrIfcFieldInspectionResult> fields)
    {
      Type type = typeof(HbrIfcBatchInspectionResult);
      ConstructorInfo constructor = type.GetConstructors(
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .SingleOrDefault(candidate => candidate.GetParameters().Length != 0);
      if (constructor != null)
        return InvokeInspectionConstructor<HbrIfcBatchInspectionResult>(
          constructor,
          new object[] { success, errorCode, message, fields });

      var legacy = (HbrIfcBatchInspectionResult)Activator.CreateInstance(
        type,
        true);
      type.GetProperty("Success").SetValue(legacy, success);
      type.GetProperty("ErrorCode").SetValue(legacy, errorCode);
      type.GetProperty("Message").SetValue(legacy, message);
      type.GetProperty("Fields").SetValue(legacy, fields);
      return legacy;
    }

    private static TResult InvokeInspectionConstructor<TResult>(
      ConstructorInfo constructor,
      object[] arguments)
    {
      try
      {
        return (TResult)constructor.Invoke(arguments);
      }
      catch (TargetInvocationException exception) when (
        exception.InnerException != null)
      {
        throw exception.InnerException;
      }
    }

    private static string ReadStringConstant(Type type, string fieldName)
    {
      FieldInfo field = type.GetField(
        fieldName,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(field);
      return Assert.IsType<string>(field.GetRawConstantValue());
    }

    private static HbrIfcEnrichmentValue CreateValue(
      string propertyName,
      string declaredType,
      string canonicalValue)
    {
      return new HbrIfcEnrichmentValue
      {
        OwnerEntityType = "IfcProject",
        OwnerGlobalId = OwnerGlobalId,
        OwnerStrategy = "GLOBAL_ID",
        PropertySetName = "Pset_申报信息属性集",
        PropertyName = propertyName,
        DeclaredIfcType = declaredType,
        CanonicalValue = canonicalValue,
        PropertyIdentity = "property-id|" + propertyName,
        SemanticKey = "IfcProject|Pset_申报信息属性集|" + propertyName
      };
    }

    private static string CreatePerformanceIfc(
      int unrelatedEntityCount,
      int foreignPropertySetCount,
      out int[] foreignIds)
    {
      var data = new StringBuilder();
      data.Append("#7=IFCPROJECT('")
        .Append(OwnerGlobalId)
        .Append("',$,'Project',$,$,$,$,$,$);\r\n");
      int nextId = 8;
      for (int index = 0; index < unrelatedEntityCount; index++)
      {
        data.Append('#').Append(nextId++)
          .Append("=IFCCARTESIANPOINT((0.,0.,0.));\r\n");
      }

      var protectedIds = new List<int>(foreignPropertySetCount * 4);
      string encodedPropertySetName =
        IfcStepSyntax.EncodeString("Pset_申报信息属性集");
      for (int index = 0; index < foreignPropertySetCount; index++)
      {
        int ownerId = nextId++;
        int propertyId = nextId++;
        int propertySetId = nextId++;
        int relationshipId = nextId++;
        protectedIds.Add(ownerId);
        protectedIds.Add(propertyId);
        protectedIds.Add(propertySetId);
        protectedIds.Add(relationshipId);
        data.Append('#').Append(ownerId).Append("=IFCBUILDING('")
          .Append(PerformanceGlobalId(index * 3 + 1))
          .Append("',$,'Foreign Building',$,$,$,$,$,$,$,$);\r\n");
        data.Append('#').Append(propertyId)
          .Append("=IFCPROPERTYSINGLEVALUE('Foreign")
          .Append(index.ToString("D3", CultureInfo.InvariantCulture))
          .Append("',$,IFCLABEL('keep'),$);\r\n");
        data.Append('#').Append(propertySetId).Append("=IFCPROPERTYSET('")
          .Append(PerformanceGlobalId(index * 3 + 2))
          .Append("',$,").Append(encodedPropertySetName)
          .Append(",$,(#").Append(propertyId).Append("));\r\n");
        data.Append('#').Append(relationshipId)
          .Append("=IFCRELDEFINESBYPROPERTIES('")
          .Append(PerformanceGlobalId(index * 3 + 3))
          .Append("',$,$,$,(#").Append(ownerId).Append("),#")
          .Append(propertySetId).Append(");\r\n");
      }
      foreignIds = protectedIds.ToArray();
      return CreateIfc(data.ToString());
    }

    private static string CreateOwnerRelationshipPerformanceIfc(
      int relationshipCount)
    {
      var data = new StringBuilder();
      data.Append("#7=IFCPROJECT('")
        .Append(OwnerGlobalId)
        .Append("',$,'Project',$,$,$,$,$,$);\r\n");
      int nextId = 8;
      for (int index = 0; index < relationshipCount; index++)
      {
        int propertyId = nextId++;
        int propertySetId = nextId++;
        int relationshipId = nextId++;
        data.Append('#').Append(propertyId)
          .Append("=IFCPROPERTYSINGLEVALUE('Irrelevant")
          .Append(index.ToString("D4", CultureInfo.InvariantCulture))
          .Append("',$,IFCLABEL('keep'),$);\r\n");
        data.Append('#').Append(propertySetId).Append("=IFCPROPERTYSET('")
          .Append(PerformanceGlobalId(10000 + index * 2))
          .Append("',$,'Pset_Irrelevant_")
          .Append(index.ToString("D4", CultureInfo.InvariantCulture))
          .Append("',$,(#").Append(propertyId).Append("));\r\n");
        data.Append('#').Append(relationshipId)
          .Append("=IFCRELDEFINESBYPROPERTIES('")
          .Append(PerformanceGlobalId(10001 + index * 2))
          .Append("',$,$,$,(#7),#")
          .Append(propertySetId).Append(");\r\n");
      }
      return CreateIfc(data.ToString());
    }

    private static HbrIfcOperationKind DocumentEntityEnumerationKind()
    {
      Assert.True(
        Enum.TryParse(
          "DocumentEntityEnumeration",
          out HbrIfcOperationKind kind),
        "生产枚举源必须公开 DocumentEntityEnumeration 诊断事件。");
      return kind;
    }

    private static string PerformanceGlobalId(int value)
    {
      byte[] bytes = new byte[16];
      Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, 0, 4);
      return IfcGuidCodec.Encode(new Guid(bytes));
    }

    private sealed class RecordingOperationObserver : IHbrIfcOperationObserver
    {
      private readonly List<HbrIfcOperationEvent> _events =
        new List<HbrIfcOperationEvent>();

      public void Observe(HbrIfcOperationEvent operation)
      {
        _events.Add(operation);
      }

      public int EventCount(HbrIfcOperationKind kind)
      {
        return _events.Count(operation => operation.Kind == kind);
      }

      public int TotalEventCount => _events.Count;

      public int ItemCount(HbrIfcOperationKind kind)
      {
        return _events.Where(operation => operation.Kind == kind)
          .Sum(operation => operation.ItemCount);
      }

      public bool MatchesExpectedProfile(int entityCount, int fieldCount)
      {
        return EventCount(HbrIfcOperationKind.CandidateClone) == 1
          && EventCount(HbrIfcOperationKind.GraphIndexFullPass) == 1
          && ItemCount(HbrIfcOperationKind.GraphIndexFullPass) == entityCount
          && EventCount(HbrIfcOperationKind.GraphValidation) == 1
          && EventCount(HbrIfcOperationKind.BatchInspection) == 1
          && ItemCount(HbrIfcOperationKind.BatchInspection) == fieldCount
          && EventCount(HbrIfcOperationKind.IndexedFieldLookup) == fieldCount
          && EventCount(HbrIfcOperationKind.CommitTransfer) == 1
          && EventCount(HbrIfcOperationKind.SectionBoundaryScan) == 0
          && EventCount(HbrIfcOperationKind.MaximumIdScan) == 0
          && EventCount(HbrIfcOperationKind.ForeignRelationshipRescan) == 0
          && EventCount(HbrIfcOperationKind.CommitClone) == 0;
      }

      public string DescribeProfile()
      {
        return string.Join(
          ", ",
          Enum.GetValues(typeof(HbrIfcOperationKind))
            .Cast<HbrIfcOperationKind>()
            .Select(kind => kind + "=" + EventCount(kind)
              + "/items:" + ItemCount(kind)));
      }
    }

    private static IEnumerable<string> InvalidExistingTargetStructures(
      string typedToken)
    {
      string valid = CreateExistingTargetIfc(typedToken);
      yield return valid.Replace("DATA;\r\n", string.Empty);
      yield return valid.Replace("DATA;\r\n", "DATA;\r\nDATA;\r\n");
      const string dataEnd = "ENDSEC;\r\nEND-ISO-10303-21;";
      yield return valid.Replace(dataEnd, "END-ISO-10303-21;");
      yield return valid.Replace(
        dataEnd,
        "ENDSEC;\r\nENDSEC;\r\nEND-ISO-10303-21;");
    }

    private static string CreateExistingTargetIfc(string typedToken)
    {
      return CreateIfc(
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + "#20=IFCPROPERTYSINGLEVALUE("
        + "'原点坐标X',$," + typedToken + ",$);\r\n"
        + "#21=IFCPROPERTYSET('0000000000000000000001',$,"
        + "'Pset_申报信息属性集',$,(#20));\r\n"
        + "#22=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000002',$,$,$,(#7),#21);\r\n");
    }

    private static string CreateInvalidRelatedObjectOwnerIfc(
      string ownerType,
      bool includeInspectionTarget)
    {
      string carrier;
      switch (ownerType)
      {
        case "IFCPROPERTYSET":
          carrier =
            "#30=IFCPROPERTYSINGLEVALUE('Carrier',$,IFCLABEL('keep'),$);\r\n"
            + "#7=IFCPROPERTYSET('" + OwnerGlobalId
            + "',$,'Pset_Carrier',$,(#30));\r\n";
          break;
        case "IFCRELDEFINESBYPROPERTIES":
          carrier =
            "#8=IFCPROJECT('0krcLzwITMYvNX9cMehgQY',$,'Carrier Owner',"
            + "$,$,$,$,$,$);\r\n"
            + "#30=IFCPROPERTYSINGLEVALUE('Carrier',$,IFCLABEL('keep'),$);\r\n"
            + "#31=IFCPROPERTYSET('0000000000000000000001',$,"
            + "'Pset_Carrier',$,(#30));\r\n"
            + "#7=IFCRELDEFINESBYPROPERTIES('" + OwnerGlobalId
            + "',$,$,$,(#8),#31);\r\n";
          break;
        case "IFCORGANIZATION":
          carrier = "#7=IFCORGANIZATION('" + OwnerGlobalId
            + "',$,'Organization',$,$);\r\n";
          break;
        case "IFCWALLTYPE":
          carrier = "#7=IFCWALLTYPE('" + OwnerGlobalId
            + "',$,'Wall Type',$,$,$,$,$,$,.NOTDEFINED.);\r\n";
          break;
        case "IFCBUILDINGELEMENT":
          carrier = "#7=IFCBUILDINGELEMENT('" + OwnerGlobalId
            + "',$,'Abstract Building Element',$,$,$,$,$);\r\n";
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(ownerType));
      }
      if (!includeInspectionTarget) return CreateIfc(carrier);
      return CreateIfc(
        carrier
        + "#40=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.5),$);\r\n"
        + "#41=IFCPROPERTYSET('0000000000000000000003',$,"
        + "'Pset_申报信息属性集',$,(#40));\r\n"
        + "#42=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000004',$,$,$,(#7),#41);\r\n");
    }

    private static string CreateForeignInvalidRelationshipOwnerIfc(
      string ownerType,
      bool includeInspectionTarget)
    {
      string invalidOwner;
      switch (ownerType)
      {
        case "IFCWALLTYPE":
          invalidOwner =
            "#8=IFCWALLTYPE('0krcLzwITMYvNX9cMehgQY',$,'Wall Type',"
            + "$,$,$,$,$,$,.NOTDEFINED.);\r\n";
          break;
        case "IFCPROPERTYSET":
          invalidOwner =
            "#30=IFCPROPERTYSINGLEVALUE('Carrier',$,IFCLABEL('keep'),$);\r\n"
            + "#8=IFCPROPERTYSET('0krcLzwITMYvNX9cMehgQY',$,"
            + "'Pset_InvalidOwner',$,(#30));\r\n";
          break;
        case "IFCBUILDINGELEMENT":
          invalidOwner =
            "#8=IFCBUILDINGELEMENT('0krcLzwITMYvNX9cMehgQY',$,"
            + "'Abstract Building Element',$,$,$,$,$);\r\n";
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(ownerType));
      }
      string data =
        "#7=IFCPROJECT('" + OwnerGlobalId
        + "',$,'Project',$,$,$,$,$,$);\r\n"
        + invalidOwner
        + "#31=IFCPROPERTYSINGLEVALUE('Foreign',$,IFCLABEL('keep'),$);\r\n"
        + "#32=IFCPROPERTYSET('0000000000000000000006',$,"
        + "'Pset_Foreign',$,(#31));\r\n"
        + "#33=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000007',$,$,$,(#8),#32);\r\n";
      if (!includeInspectionTarget) return CreateIfc(data);
      return CreateIfc(
        data
        + "#40=IFCPROPERTYSINGLEVALUE('原点坐标X',$,IFCREAL(1.5),$);\r\n"
        + "#41=IFCPROPERTYSET('0000000000000000000003',$,"
        + "'Pset_申报信息属性集',$,(#40));\r\n"
        + "#42=IFCRELDEFINESBYPROPERTIES("
        + "'0000000000000000000004',$,$,$,(#7),#41);\r\n");
    }

    private static void AssertFailure(
      HbrIfcEnrichmentResult result,
      string errorCode,
      string original,
      IfcStepDocument document)
    {
      Assert.False(result.Success);
      HbrIfcEnrichmentFieldResult field = Assert.Single(result.Fields);
      Assert.False(field.Success);
      Assert.Equal(errorCode, field.ErrorCode);
      Assert.Equal(0, result.CreatedProperties);
      Assert.Equal(0, result.CreatedPropertySets);
      Assert.Equal(0, result.CreatedRelationships);
      Assert.Equal(0, result.UpdatedProperties);
      Assert.Equal(original, document.Serialize());
    }

    private static string CreateIfc(string dataStatements)
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
  }
}
