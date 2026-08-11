using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Hifc;
using BIMBaoGui.Stage01.Rules;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  [Collection(ProcessCurrentDirectoryCollection.Name)]
  public sealed class HbrSharedParameterTextProjectionTests
  {
    [Fact]
    public void Projection_is_byte_deterministic_in_an_empty_directory()
    {
      string temporary = Path.Combine(
        Path.GetTempPath(),
        "HbrSharedParameterTextProjectionTests_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(temporary);
      string previous = Directory.GetCurrentDirectory();
      try
      {
        Directory.SetCurrentDirectory(temporary);
        string firstText = HbrSharedParameterTextProjection.CreateText(
          HbrRuleDatabase.Current);
        string secondText = HbrSharedParameterTextProjection.CreateText(
          HbrRuleDatabase.Current);
        byte[] firstBytes = HbrSharedParameterTextProjection.CreateUtf8Bytes(
          HbrRuleDatabase.Current);
        byte[] secondBytes = HbrSharedParameterTextProjection.CreateUtf8Bytes(
          HbrRuleDatabase.Current);

        Assert.Equal(firstText, secondText);
        Assert.Equal(firstBytes, secondBytes);
        Assert.Equal(new UTF8Encoding(false).GetBytes(firstText), firstBytes);
        Assert.False(firstBytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.EndsWith("\r\n", firstText);
        Assert.DoesNotContain("\n", firstText.Replace("\r\n", string.Empty));
        Assert.Empty(Directory.GetFileSystemEntries(temporary));
      }
      finally
      {
        Directory.SetCurrentDirectory(previous);
        Directory.Delete(temporary, true);
      }
    }

    [Fact]
    public void Projection_tests_isolate_process_current_directory_mutation()
    {
      const string expectedCollection = "ProcessCurrentDirectory";
      CustomAttributeData collection = typeof(
        HbrSharedParameterTextProjectionTests)
        .CustomAttributes
        .SingleOrDefault(attribute =>
          attribute.AttributeType == typeof(CollectionAttribute));
      Assert.NotNull(collection);
      Assert.Equal(
        expectedCollection,
        (string)collection.ConstructorArguments.Single().Value);

      CustomAttributeData definition = typeof(
        HbrSharedParameterTextProjectionTests)
        .Assembly
        .GetTypes()
        .SelectMany(type => type.CustomAttributes)
        .SingleOrDefault(attribute =>
          attribute.AttributeType == typeof(CollectionDefinitionAttribute)
          && string.Equals(
            (string)attribute.ConstructorArguments.Single().Value,
            expectedCollection,
            StringComparison.Ordinal));
      Assert.NotNull(definition);
      CustomAttributeNamedArgument disableParallelization = definition
        .NamedArguments
        .Single(argument =>
          string.Equals(
            argument.MemberName,
            "DisableParallelization",
            StringComparison.Ordinal));
      Assert.True((bool)disableParallelization.TypedValue.Value);
    }

    [Fact]
    public void Projection_matches_frozen_canonical_and_official_alias_definitions()
    {
      CanonicalSnapshot canonical = ReadSnapshot<CanonicalSnapshot>(
        "shared-parameters-canonical.v1.json");
      OfficialSnapshot official = ReadSnapshot<OfficialSnapshot>(
        "official-hifc-mappings.v1.json");
      OfficialMappingSnapshot[] aliases = official.mappings
        .OrderBy(mapping => mapping.propertySet, StringComparer.Ordinal)
        .ThenBy(mapping => mapping.officialSourceParameterName, StringComparer.Ordinal)
        .ToArray();
      var expectedLines = new List<string>();
      expectedLines.AddRange(canonical.preamble);
      expectedLines.AddRange(canonical.groups.Select(GroupLine));

      var aliasGroups = aliases
        .GroupBy(mapping => mapping.propertySet, StringComparer.Ordinal)
        .Select((group, index) => new
        {
          id = (1000 + index).ToString(),
          propertySet = group.Key,
          mappings = group.ToArray()
        })
        .ToArray();
      expectedLines.AddRange(aliasGroups.Select(group =>
        "GROUP\t"
        + group.id
        + "\tGH_HIFC_官方源_"
        + Sanitize(group.propertySet)));
      expectedLines.Add(
        "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");
      expectedLines.AddRange(canonical.parameters.Select(ParameterLine));
      foreach (var group in aliasGroups)
      {
        expectedLines.AddRange(group.mappings.Select(mapping => string.Join(
          "\t",
          "PARAM",
          mapping.officialSourceParameterGuid,
          Sanitize(mapping.officialSourceParameterName),
          mapping.officialSourceParameterType,
          string.Empty,
          group.id,
          "1",
          "Official exact source alias | "
            + Sanitize(mapping.ifcEntity)
            + " | "
            + Sanitize(mapping.propertySet)
            + " | "
            + Sanitize(mapping.ifcProperty),
          "1",
          "0")));
      }

      string expected = string.Join("\r\n", expectedLines) + "\r\n";
      string actual = HbrSharedParameterTextProjection.CreateText(
        HbrRuleDatabase.Current);
      Assert.Equal(141, canonical.parameters.Length);
      Assert.Equal(16, aliasGroups.Length);
      Assert.Equal(166, aliases.Length);
      Assert.Equal(expected, actual);
    }

    [Fact]
    public void Alias_identity_duplicates_keep_the_first_mapping_deterministically()
    {
      Guid duplicateGuid = Guid.Parse(
        "11111111-1111-5111-8111-111111111111");
      OfficialHifcMapping first = CreateAlias(
        duplicateGuid,
        "建筑属性集",
        "建筑名称",
        "TEXT");
      OfficialHifcMapping duplicate = CreateAlias(
        duplicateGuid,
        "建筑属性集",
        "建筑名称",
        "TEXT");
      OfficialHifcMapping other = CreateAlias(
        Guid.Parse("22222222-2222-5222-8222-222222222222"),
        "场地属性集",
        "场地名称",
        "TEXT");

      OfficialHifcMapping[] actual =
        HbrSharedParameterTextProjection.DistinctOfficialAliases(
          new[] { first, duplicate, other });

      Assert.Equal(2, actual.Length);
      Assert.Same(first, actual[0]);
      Assert.Same(other, actual[1]);
    }

    [Theory]
    [InlineData("PropertySet", "建筑属性集", "冲突属性集")]
    [InlineData("OfficialSourceParameterName", "建筑名称", "冲突名称")]
    [InlineData("OfficialSourceParameterType", "TEXT", "INTEGER")]
    public void Alias_guid_identity_conflicts_report_guid_field_and_values(
      string field,
      string originalValue,
      string conflictingValue)
    {
      Guid guid = Guid.Parse("33333333-3333-5333-8333-333333333333");
      OfficialHifcMapping first = CreateAlias(
        guid,
        "建筑属性集",
        "建筑名称",
        "TEXT");
      OfficialHifcMapping conflict = CreateAlias(
        guid,
        "建筑属性集",
        "建筑名称",
        "TEXT");
      switch (field)
      {
        case "PropertySet":
          conflict.PropertySet = conflictingValue;
          break;
        case "OfficialSourceParameterName":
          conflict.OfficialSourceParameterName = conflictingValue;
          break;
        case "OfficialSourceParameterType":
          conflict.OfficialSourceParameterType = conflictingValue;
          break;
        default:
          throw new InvalidOperationException("Unknown alias field: " + field);
      }

      InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
        HbrSharedParameterTextProjection.DistinctOfficialAliases(
          new[] { first, conflict }));

      Assert.Contains(guid.ToString("D"), error.Message);
      Assert.Contains(field, error.Message);
      Assert.Contains(originalValue, error.Message);
      Assert.Contains(conflictingValue, error.Message);
    }

    [Fact]
    public void Revit_service_uses_pure_text_but_keeps_unicode_file_boundary()
    {
      string projectDirectory = Path.GetFullPath(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        @"..\..\.."));
      string root = Path.GetFullPath(Path.Combine(projectDirectory, @"..\.."));
      string source = File.ReadAllText(Path.Combine(
        root,
        @"src\BIMBaoGui.Stage01\Revit\OfficialParameterProjectionService.cs"));

      Assert.Contains(
        "HbrSharedParameterTextProjection.CreateText(HbrRuleDatabase.Current)",
        source);
      Assert.DoesNotContain("ReadEmbeddedText", source);
      Assert.DoesNotContain("GetManifestResourceStream", source);
      Assert.DoesNotContain("GH_HIFC_SharedParameters.txt", source);
      Assert.Contains("Encoding.Unicode", source);
      Assert.Contains("application.SharedParametersFilename = previous", source);
      Assert.Contains("File.Delete(temporary)", source);
    }

    private static OfficialHifcMapping CreateAlias(
      Guid guid,
      string propertySet,
      string name,
      string parameterType)
    {
      return new OfficialHifcMapping
      {
        OfficialSourceParameterGuid = guid,
        PropertySet = propertySet,
        OfficialSourceParameterName = name,
        OfficialSourceParameterType = parameterType
      };
    }

    private static T ReadSnapshot<T>(string fileName)
    {
      Assembly assembly = typeof(HbrSharedParameterTextProjectionTests).Assembly;
      string resourceName = assembly.GetManifestResourceNames().Single(name =>
        name.EndsWith("Snapshots." + fileName, StringComparison.Ordinal));
      using (Stream stream = assembly.GetManifestResourceStream(resourceName))
      using (var reader = new StreamReader(
        stream,
        new UTF8Encoding(false, true),
        true))
      {
        return new JavaScriptSerializer
        {
          MaxJsonLength = int.MaxValue,
          RecursionLimit = 512
        }.Deserialize<T>(reader.ReadToEnd());
      }
    }

    private static string GroupLine(SharedParameterGroup group)
    {
      return string.Join("\t", group.record, group.id, group.name);
    }

    private static string ParameterLine(SharedParameterRecord parameter)
    {
      return string.Join(
        "\t",
        parameter.record,
        parameter.guid,
        parameter.name,
        parameter.dataType,
        parameter.dataCategory,
        parameter.group,
        parameter.visible,
        parameter.description,
        parameter.userModifiable,
        parameter.hideWhenNoValue);
    }

    private static string Sanitize(string value)
    {
      return (value ?? string.Empty)
        .Replace('\t', ' ')
        .Replace('\r', ' ')
        .Replace('\n', ' ');
    }

    private sealed class CanonicalSnapshot
    {
      public string[] preamble { get; set; }
      public SharedParameterGroup[] groups { get; set; }
      public SharedParameterRecord[] parameters { get; set; }
    }

    private sealed class SharedParameterGroup
    {
      public string record { get; set; }
      public string id { get; set; }
      public string name { get; set; }
    }

    private sealed class SharedParameterRecord
    {
      public string record { get; set; }
      public string guid { get; set; }
      public string name { get; set; }
      public string dataType { get; set; }
      public string dataCategory { get; set; }
      public string group { get; set; }
      public string visible { get; set; }
      public string description { get; set; }
      public string userModifiable { get; set; }
      public string hideWhenNoValue { get; set; }
    }

    private sealed class OfficialSnapshot
    {
      public OfficialMappingSnapshot[] mappings { get; set; }
    }

    private sealed class OfficialMappingSnapshot
    {
      public string ifcEntity { get; set; }
      public string propertySet { get; set; }
      public string ifcProperty { get; set; }
      public string officialSourceParameterName { get; set; }
      public string officialSourceParameterType { get; set; }
      public string officialSourceParameterGuid { get; set; }
    }
  }
}
