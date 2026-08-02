using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BIMBaoGui.Stage01.Rules;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrRulePackageLoaderTests
  {
    [Theory]
    [InlineData(new byte[] { (byte)'X', (byte)'B', (byte)'R', (byte)'P' })]
    [InlineData(new byte[] { (byte)'H', (byte)'B', (byte)'R' })]
    public void Load_rejects_wrong_or_short_magic(byte[] bytes)
    {
      InvalidDataException error = Assert.Throws<InvalidDataException>(
        () => HbrRulePackageLoader.Load(new MemoryStream(bytes)));

      Assert.Contains("magic", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Load_rejects_unsupported_format_version()
    {
      byte[] bytes = HbrRulePackTestFixture.ReadEmbeddedPackBytes();
      HbrRulePackTestFixture.WriteInt32BigEndian(bytes, 4, 2);

      InvalidDataException error = Assert.Throws<InvalidDataException>(
        () => HbrRulePackageLoader.Load(new MemoryStream(bytes)));

      Assert.Contains("version", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Load_rejects_negative_payload_length()
    {
      byte[] bytes = HbrRulePackTestFixture.ReadEmbeddedPackBytes();
      HbrRulePackTestFixture.WriteInt64BigEndian(bytes, 8, -1L);

      InvalidDataException error = Assert.Throws<InvalidDataException>(
        () => HbrRulePackageLoader.Load(new MemoryStream(bytes)));

      Assert.Contains("length", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Load_rejects_payload_length_larger_than_net48_array_limit()
    {
      byte[] bytes = HbrRulePackTestFixture.ReadEmbeddedPackBytes();
      HbrRulePackTestFixture.WriteInt64BigEndian(
        bytes,
        8,
        (long)int.MaxValue + 1L);

      InvalidDataException error = Assert.Throws<InvalidDataException>(
        () => HbrRulePackageLoader.Load(new MemoryStream(bytes)));

      Assert.Contains("length", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Load_rejects_declared_length_larger_than_available_payload()
    {
      byte[] bytes = HbrRulePackTestFixture.ReadEmbeddedPackBytes();
      long length = HbrRulePackTestFixture.ReadInt64BigEndian(bytes, 8);
      HbrRulePackTestFixture.WriteInt64BigEndian(bytes, 8, length + 1L);

      InvalidDataException error = Assert.Throws<InvalidDataException>(
        () => HbrRulePackageLoader.Load(new MemoryStream(bytes)));

      Assert.Contains("payload", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Load_rejects_trailing_bytes_after_declared_payload()
    {
      byte[] original = HbrRulePackTestFixture.ReadEmbeddedPackBytes();
      byte[] bytes = original.Concat(new byte[] { 0x00 }).ToArray();

      InvalidDataException error = Assert.Throws<InvalidDataException>(
        () => HbrRulePackageLoader.Load(new MemoryStream(bytes)));

      Assert.Contains("trailing", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Load_rejects_payload_hash_mismatch_before_text_parsing()
    {
      byte[] bytes = HbrRulePackTestFixture.ReadEmbeddedPackBytes();
      bytes[16] ^= 0xff;

      InvalidDataException error = Assert.Throws<InvalidDataException>(
        () => HbrRulePackageLoader.Load(new MemoryStream(bytes)));

      Assert.Contains("sha256", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Load_rejects_invalid_utf8_payload()
    {
      byte[] bytes = HbrRulePackTestFixture.BuildPack(
        new byte[] { 0xc3, 0x28 });

      InvalidDataException error = Assert.Throws<InvalidDataException>(
        () => HbrRulePackageLoader.Load(new MemoryStream(bytes)));

      Assert.Contains("utf-8", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Load_rejects_invalid_json_payload()
    {
      byte[] bytes = HbrRulePackTestFixture.BuildPack(
        Encoding.UTF8.GetBytes("{\"packageId\":"));

      InvalidDataException error = Assert.Throws<InvalidDataException>(
        () => HbrRulePackageLoader.Load(new MemoryStream(bytes)));

      Assert.Contains("json", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Load_reports_sha256_of_verified_raw_payload_and_payload_metadata()
    {
      byte[] embedded = HbrRulePackTestFixture.ReadEmbeddedPackBytes();
      byte[] originalPayload = HbrRulePackTestFixture.ExtractPayload(embedded);
      byte[] rawPayload = originalPayload
        .Concat(Encoding.UTF8.GetBytes("\r\n"))
        .ToArray();
      byte[] bytes = HbrRulePackTestFixture.BuildPack(rawPayload);

      HbrRulePackage package = HbrRulePackageLoader.Load(
        new MemoryStream(bytes));

      Assert.Equal(
        HbrRulePackTestFixture.ComputeSha256(rawPayload),
        package.RulePackageSha256);
      Assert.Equal("HBR-WUHAN-PLANNING", package.PackageId);
      Assert.Equal("1.0.0", package.PackageVersion);
      Assert.Equal(1, package.FormatVersion);
    }

    [Fact]
    public void Load_materializes_every_rule_source_section_into_concrete_models()
    {
      HbrRulePackage package = HbrRulePackageLoader.Load(
        new MemoryStream(HbrRulePackTestFixture.ReadEmbeddedPackBytes()));

      Assert.Equal("1.0.0", package.SchemaVersion);
      Assert.NotEqual(Guid.Empty, package.GuidNamespace);
      Assert.Equal(3, package.EvidenceSources.Count);
      Assert.Equal(359, package.Properties.Count);
      Assert.Equal(14, package.CarrierRoles.Count);
      Assert.Equal(3, package.ModelProfiles.Count);
      Assert.Equal(14, package.Conditions.Count);
      Assert.Equal(28, package.Tasks.Count);
      Assert.Equal(166, package.LegacyAliases.Count);
      Assert.All(
        package.Properties.Where(
          item => item.ContractKind == "HIFC_EXTENSION"),
        item => Assert.False(string.IsNullOrWhiteSpace(item.ExtensionReason)));

      HbrRuleProperty property = package.Properties[0];
      Assert.NotNull(property.Source);
      Assert.NotNull(property.Ifc);
      Assert.NotNull(property.Revit);
      Assert.NotNull(property.OfficialPlugin);
      Assert.NotNull(property.Requirement);
      Assert.NotNull(property.Suggestion);
      Assert.NotNull(property.IfcWrite);
      Assert.NotNull(property.CarrierRoleIds);
      Assert.NotNull(property.StageOwnership);
      Assert.NotNull(property.Ifc.AllowedRuntimeTypes);
      Assert.NotNull(property.Revit.LegacyNames);
      Assert.NotNull(property.Suggestion.Aliases);

      HbrCarrierRole role = package.CarrierRoles[0];
      Assert.NotNull(role.Cardinality);
      Assert.NotNull(role.ModelFileTypes);
      Assert.NotNull(role.RevitCategories);
      Assert.NotNull(role.AllowedElementKinds);
      Assert.NotNull(role.NameAliases);
      Assert.NotNull(role.FamilyAliases);
      Assert.NotNull(role.TypeAliases);

      Assert.All(package.ModelProfiles, profile =>
      {
        Assert.NotNull(profile.TaskIds);
        Assert.NotNull(profile.ActivationRuleIds);
      });
      Assert.All(package.Tasks, task =>
      {
        Assert.NotNull(task.AttributeRequirements);
        Assert.NotNull(task.Dependencies);
        Assert.NotNull(task.GeometryChecks);
        Assert.NotNull(task.PropertyChecks);
        Assert.NotNull(task.TargetComparisons);
      });
      Assert.Equal(102, package.Stage01.FieldRefs.Count);
      Assert.Equal(12, package.Stage01.InternalWorkflowFields.Count);
      Assert.Equal(
        9,
        package.Stage01.OfficialPluginCompatibility.EntityPolicies.Count);
      Assert.Equal(
        13,
        package.Stage01.OfficialPluginCompatibility.Exceptions.Count);
    }

    [Fact]
    public void Load_materializes_official_units_and_complete_stage01_projection_contract()
    {
      HbrRulePackage package = HbrRulePackageLoader.Load(
        new MemoryStream(HbrRulePackTestFixture.ReadEmbeddedPackBytes()));

      HbrRuleProperty[] official = package.Properties
        .Where(item => item.OfficialPlugin.InExtracted166)
        .ToArray();
      Assert.Equal(166, official.Length);
      Assert.All(official, item => Assert.NotNull(
        item.OfficialPlugin.LegacyProjection));

      HbrRuleProperty x = Assert.Single(
        official,
        item => item.PropertyId == "6b407894-09d4-529a-9f9f-a031219cdeaa");
      HbrRuleProperty y = Assert.Single(
        official,
        item => item.PropertyId == "1a64ef8d-e97c-5fa1-b53f-52b969b6198a");
      HbrRuleProperty elevation = Assert.Single(
        official,
        item => item.PropertyId == "50164757-c346-5005-a1b8-7b423c6b8de5");
      Assert.Null(x.Ifc.SourceUnit);
      Assert.Null(y.Ifc.SourceUnit);
      Assert.Equal("m", x.OfficialPlugin.LegacyProjection.OfficialUnit);
      Assert.Equal("m", y.OfficialPlugin.LegacyProjection.OfficialUnit);
      Assert.Equal("m", elevation.OfficialPlugin.LegacyProjection.OfficialUnit);

      Assert.Collection(
        package.Stage01.SpatialMappings,
        item =>
        {
          Assert.Equal("X", item.SourceName);
          Assert.Equal(
            "IfcProject|Pset_申报信息属性集|基点坐标 X",
            item.FieldKey);
          Assert.Equal("NorthSouth", item.TargetName);
          Assert.Equal("m", item.Unit);
        },
        item =>
        {
          Assert.Equal("Y", item.SourceName);
          Assert.Equal(
            "IfcProject|Pset_申报信息属性集|基点坐标 Y",
            item.FieldKey);
          Assert.Equal("EastWest", item.TargetName);
          Assert.Equal("m", item.Unit);
        },
        item =>
        {
          Assert.Equal("Elevation", item.SourceName);
          Assert.Equal(
            "IfcProject|Pset_申报信息属性集|基点高程",
            item.FieldKey);
          Assert.Equal("Elevation", item.TargetName);
          Assert.Equal("m", item.Unit);
        });

      var defaults = package.Stage01.InternalWorkflowFields
        .Select(item => new
        {
          item.FieldKey,
          item.Essential,
          item.DefaultStrategy,
          item.DefaultValue,
        })
        .Concat(package.Stage01.FieldRefs.Select(item => new
        {
          item.FieldKey,
          item.Essential,
          item.DefaultStrategy,
          item.DefaultValue,
        }))
        .ToArray();
      Assert.Equal(114, defaults.Length);
      Assert.Equal(27, defaults.Count(item => item.Essential));
      var fileGuid = Assert.Single(
        defaults,
        item => item.FieldKey == "HBR|FileIdentity|FileGuid");
      Assert.Equal("NEW_GUID", fileGuid.DefaultStrategy);
      Assert.Null(fileGuid.DefaultValue);
      var workflowVersion = Assert.Single(
        defaults,
        item => item.FieldKey == "HBR|Workflow|Version");
      Assert.Equal("STATIC", workflowVersion.DefaultStrategy);
      Assert.Equal("0.1.0", workflowVersion.DefaultValue);
      Assert.Equal("01_文件与项目身份", package.Stage01.DefaultActiveGroup);
      Assert.Equal(14, package.Conditions.Count);
      Assert.All(package.Conditions, condition => Assert.False(
        condition.DefaultActive));
    }
  }

  internal static class HbrRulePackTestFixture
  {
    internal const int HeaderLength = 48;
    internal const string ResourceName =
      "BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack";

    internal static byte[] ReadEmbeddedPackBytes()
    {
      Assembly assembly = typeof(HbrRulePackageLoaderTests).Assembly;
      using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
      {
        Assert.NotNull(stream);
        using (var buffer = new MemoryStream())
        {
          stream.CopyTo(buffer);
          return buffer.ToArray();
        }
      }
    }

    internal static byte[] ExtractPayload(byte[] pack)
    {
      long length = ReadInt64BigEndian(pack, 8);
      Assert.InRange(length, 0, int.MaxValue);
      Assert.Equal(HeaderLength + length, pack.LongLength);
      var payload = new byte[(int)length];
      Buffer.BlockCopy(pack, HeaderLength, payload, 0, payload.Length);
      return payload;
    }

    internal static byte[] BuildPack(byte[] payload)
    {
      var pack = new byte[HeaderLength + payload.Length];
      pack[0] = (byte)'H';
      pack[1] = (byte)'B';
      pack[2] = (byte)'R';
      pack[3] = (byte)'P';
      WriteInt32BigEndian(pack, 4, 1);
      WriteInt64BigEndian(pack, 8, payload.LongLength);
      byte[] hash;
      using (SHA256 algorithm = SHA256.Create())
        hash = algorithm.ComputeHash(payload);
      Buffer.BlockCopy(hash, 0, pack, 16, hash.Length);
      Buffer.BlockCopy(payload, 0, pack, HeaderLength, payload.Length);
      return pack;
    }

    internal static string ComputeSha256(byte[] bytes)
    {
      using (SHA256 algorithm = SHA256.Create())
        return string.Concat(
          algorithm.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }

    internal static long ReadInt64BigEndian(byte[] bytes, int offset)
    {
      ulong value = 0;
      for (int index = 0; index < 8; index++)
        value = (value << 8) | bytes[offset + index];
      return unchecked((long)value);
    }

    internal static void WriteInt32BigEndian(
      byte[] bytes,
      int offset,
      int value)
    {
      uint bits = unchecked((uint)value);
      for (int index = 3; index >= 0; index--)
      {
        bytes[offset + index] = (byte)(bits & 0xff);
        bits >>= 8;
      }
    }

    internal static void WriteInt64BigEndian(
      byte[] bytes,
      int offset,
      long value)
    {
      ulong bits = unchecked((ulong)value);
      for (int index = 7; index >= 0; index--)
      {
        bytes[offset + index] = (byte)(bits & 0xff);
        bits >>= 8;
      }
    }
  }
}
