using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Rules;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrRuntimePackagingTests
  {
    private const string ProductionAssemblyFixtureName =
      "BIMBaoGui.Stage01.production.dll";
    private const int PackHeaderLength = 48;

    private static readonly string[] LegacyRuntimeResourceNames =
    {
      "BIMBaoGui.Stage01.Resources.stage01_file_initialization_registry_v0.1.json",
      "BIMBaoGui.Stage01.Resources.GH_HIFC_ParameterBindings.json",
      "BIMBaoGui.Stage01.Resources.GH_HIFC_SharedParameters.txt",
      "BIMBaoGui.Stage01.Resources.wuhan_planning_rules.v1.json",
      "BIMBaoGui.Stage01.Resources.official_plugin_compatibility_status.v1.json",
    };

    [Fact]
    public void Production_manifest_contains_exactly_one_runtime_pack()
    {
      Assembly productionAssembly = LoadProductionAssembly();
      AssertOnlyRuntimePack(productionAssembly);
      Assert.Equal(
        new[] { HbrRuleDatabase.ResourceName },
        productionAssembly.GetManifestResourceNames());
    }

    [Fact]
    public void Production_pack_identity_matches_current_manifest()
    {
      Assembly productionAssembly = LoadProductionAssembly();
      object database = GetStaticProperty(
        ProductionType(
          productionAssembly,
          "BIMBaoGui.Stage01.Rules.HbrRuleDatabase"),
        "Current");
      object package = GetInstanceProperty(database, "Package");

      Assert.Equal("HBR-WUHAN-PLANNING", GetStringProperty(package, "PackageId"));
      Assert.Equal("1.0.0", GetStringProperty(package, "PackageVersion"));
      string packageSha = GetStringProperty(package, "RulePackageSha256");
      Assert.Matches(new Regex("^[0-9a-f]{64}$"), packageSha);
      string payloadSha = ComputeSha256(ReadPayload(productionAssembly));
      Assert.Equal(payloadSha, packageSha);

      string manifestPath = FindRepositoryFile(
        Path.Combine("specs", "hbr-rules", "v1", "manifest.sha256.json"));
      var serializer = new JavaScriptSerializer();
      var manifest = serializer.Deserialize<Dictionary<string, object>>(
        File.ReadAllText(manifestPath));
      var rulePack = (Dictionary<string, object>)manifest["rulePack"];
      Assert.Equal(
        (string)rulePack["payloadSha256"],
        payloadSha);
    }

    [Fact]
    public void Test_and_production_packs_have_identical_payload_sha()
    {
      Assembly testAssembly = typeof(HbrRuntimePackagingTests).Assembly;
      Assembly productionAssembly = LoadProductionAssembly();

      AssertOnlyRuntimePack(testAssembly);
      AssertOnlyRuntimePack(productionAssembly);
      Assert.Equal(
        ComputeSha256(ReadPayload(testAssembly)),
        ComputeSha256(ReadPayload(productionAssembly)));
    }

    [Fact]
    public void All_catalogs_instantiate_from_embedded_pack_without_legacy_resources()
    {
      Assembly productionAssembly = LoadProductionAssembly();
      AssertOnlyRuntimePack(productionAssembly);

      object database = GetStaticProperty(
        ProductionType(
          productionAssembly,
          "BIMBaoGui.Stage01.Rules.HbrRuleDatabase"),
        "Current");
      AssertProductionObject(productionAssembly, database);

      object stage01 = InvokeFromDatabase(
        productionAssembly,
        "BIMBaoGui.Stage01.Infrastructure.Stage01RegistryProvider",
        database);
      AssertNonEmptyProperty(stage01, "Fields");

      object officialHifc = InvokeFromDatabase(
        productionAssembly,
        "BIMBaoGui.Stage01.Hifc.OfficialHifcMappingCatalog",
        database);
      AssertNonEmptyProperty(officialHifc, "Mappings");

      object compatibility = InvokeFromDatabase(
        productionAssembly,
        "BIMBaoGui.Stage01.Hifc.OfficialPluginCompatibilityCatalog",
        database);
      AssertNonEmptyProperty(compatibility, "EntityPolicies");

      object mvd = InvokeFromDatabase(
        productionAssembly,
        "BIMBaoGui.Stage01.Mvd.MvdIfcNormalizationCatalog",
        database);
      AssertNonEmptyProperty(mvd, "Rules");

      object taskRules = InvokeFromDatabase(
        productionAssembly,
        "BIMBaoGui.Stage01.TaskPlanning.TaskRuleCatalog",
        database);
      AssertProductionEnumerable(productionAssembly, taskRules);

      object activation = InvokeFromDatabase(
        productionAssembly,
        "BIMBaoGui.Stage01.Context.RuleActivationCatalog",
        database);
      AssertNonEmptyProperty(activation, "ConditionRules");

      Type serviceType = ProductionType(
        productionAssembly,
        "BIMBaoGui.Stage01.Revit.OfficialParameterProjectionService");
      MethodInfo createFromCurrent = serviceType.GetMethod(
        "CreateSharedParameterTextFromCurrentDatabase",
        BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(createFromCurrent);
      Assert.Empty(createFromCurrent.GetParameters());
      string first = Assert.IsType<string>(
        createFromCurrent.Invoke(null, Array.Empty<object>()));
      string second = Assert.IsType<string>(
        createFromCurrent.Invoke(null, Array.Empty<object>()));
      Assert.False(string.IsNullOrWhiteSpace(first));
      Assert.Equal(first, second);
      Assert.StartsWith("# This is a Revit shared parameter file.\r\n", first);

      Type textProjectionType = ProductionType(
        productionAssembly,
        "BIMBaoGui.Stage01.Hifc.HbrSharedParameterTextProjection");
      MethodInfo createFromDatabase = textProjectionType.GetMethod(
        "CreateText",
        BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(createFromDatabase);
      Assert.Equal(
        Assert.IsType<string>(createFromDatabase.Invoke(
          null,
          new[] { database })),
        first);
    }

    private static Assembly LoadProductionAssembly()
    {
      string path = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        ProductionAssemblyFixtureName);
      Assert.True(File.Exists(path), "生产程序集夹具不存在：" + path);
      Assembly assembly = Assembly.LoadFile(Path.GetFullPath(path));
      Assert.NotSame(typeof(HbrRuntimePackagingTests).Assembly, assembly);
      return assembly;
    }

    private static void AssertOnlyRuntimePack(Assembly assembly)
    {
      string[] manifestResources = assembly.GetManifestResourceNames();
      string[] hbrPacks = manifestResources
        .Where(name => name.EndsWith(".hbrpack", StringComparison.Ordinal))
        .ToArray();
      Assert.Equal(new[] { HbrRuleDatabase.ResourceName }, hbrPacks);
      string[] runtimeResources = manifestResources
        .Where(name => name.StartsWith(
          "BIMBaoGui.Stage01.Resources.",
          StringComparison.Ordinal))
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

      Assert.Equal(new[] { HbrRuleDatabase.ResourceName }, runtimeResources);
      Assert.All(
        LegacyRuntimeResourceNames,
        legacy => Assert.DoesNotContain(legacy, manifestResources));
    }

    private static byte[] ReadPayload(Assembly assembly)
    {
      byte[] pack = ReadPack(assembly);

      Assert.True(pack.Length >= PackHeaderLength);
      Assert.Equal((byte)'H', pack[0]);
      Assert.Equal((byte)'B', pack[1]);
      Assert.Equal((byte)'R', pack[2]);
      Assert.Equal((byte)'P', pack[3]);
      Assert.Equal(1, ReadInt32BigEndian(pack, 4));
      long payloadLength = ReadInt64BigEndian(pack, 8);
      Assert.InRange(payloadLength, 0, int.MaxValue);
      Assert.Equal(PackHeaderLength + payloadLength, pack.LongLength);

      var payload = new byte[(int)payloadLength];
      Buffer.BlockCopy(
        pack,
        PackHeaderLength,
        payload,
        0,
        payload.Length);
      byte[] headerSha = new byte[32];
      Buffer.BlockCopy(pack, 16, headerSha, 0, headerSha.Length);
      Assert.Equal(ToLowerHex(headerSha), ComputeSha256(payload));
      return payload;
    }

    private static byte[] ReadPack(Assembly assembly)
    {
      using (Stream stream = assembly.GetManifestResourceStream(
        HbrRuleDatabase.ResourceName))
      {
        Assert.NotNull(stream);
        using (var buffer = new MemoryStream())
        {
          stream.CopyTo(buffer);
          return buffer.ToArray();
        }
      }
    }

    private static int ReadInt32BigEndian(byte[] bytes, int offset)
    {
      uint value = 0;
      for (int index = 0; index < 4; index++)
        value = (value << 8) | bytes[offset + index];
      return unchecked((int)value);
    }

    private static long ReadInt64BigEndian(byte[] bytes, int offset)
    {
      ulong value = 0;
      for (int index = 0; index < 8; index++)
        value = (value << 8) | bytes[offset + index];
      return unchecked((long)value);
    }

    private static string ComputeSha256(byte[] bytes)
    {
      using (SHA256 algorithm = SHA256.Create())
        return ToLowerHex(algorithm.ComputeHash(bytes));
    }

    private static string ToLowerHex(byte[] bytes)
    {
      return string.Concat(bytes.Select(value => value.ToString("x2")));
    }

    private static Type ProductionType(Assembly assembly, string name)
    {
      Type type = assembly.GetType(name, false);
      Assert.NotNull(type);
      Assert.Same(assembly, type.Assembly);
      return type;
    }

    private static object GetStaticProperty(Type type, string name)
    {
      PropertyInfo property = type.GetProperty(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(property);
      object value = property.GetValue(null, null);
      Assert.NotNull(value);
      return value;
    }

    private static object GetInstanceProperty(object value, string name)
    {
      PropertyInfo property = value.GetType().GetProperty(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
      Assert.NotNull(property);
      object result = property.GetValue(value, null);
      Assert.NotNull(result);
      return result;
    }

    private static string GetStringProperty(object value, string name)
    {
      return Assert.IsType<string>(GetInstanceProperty(value, name));
    }

    private static string FindRepositoryFile(string relativePath)
    {
      DirectoryInfo directory = new DirectoryInfo(
        AppDomain.CurrentDomain.BaseDirectory);
      while (directory != null)
      {
        string candidate = Path.Combine(directory.FullName, relativePath);
        if (File.Exists(candidate)) return candidate;
        directory = directory.Parent;
      }
      throw new FileNotFoundException(
        "无法从测试输出目录定位仓库文件。",
        relativePath);
    }

    private static object InvokeFromDatabase(
      Assembly assembly,
      string typeName,
      object database)
    {
      Type type = ProductionType(assembly, typeName);
      MethodInfo method = type.GetMethod(
        "FromDatabase",
        BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      object result = method.Invoke(null, new[] { database });
      Assert.NotNull(result);
      if (!(result is IEnumerable))
        AssertProductionObject(assembly, result);
      return result;
    }

    private static void AssertNonEmptyProperty(object value, string name)
    {
      PropertyInfo property = value.GetType().GetProperty(
        name,
        BindingFlags.Public | BindingFlags.Instance);
      Assert.NotNull(property);
      Assert.NotEmpty(Assert.IsAssignableFrom<IEnumerable>(
        property.GetValue(value, null)).Cast<object>());
    }

    private static void AssertProductionEnumerable(
      Assembly assembly,
      object value)
    {
      object[] items = Assert.IsAssignableFrom<IEnumerable>(value)
        .Cast<object>()
        .ToArray();
      Assert.NotEmpty(items);
      Assert.All(items, item => AssertProductionObject(assembly, item));
    }

    private static void AssertProductionObject(Assembly assembly, object value)
    {
      Assert.NotNull(value);
      Assert.Same(assembly, value.GetType().Assembly);
    }
  }
}
