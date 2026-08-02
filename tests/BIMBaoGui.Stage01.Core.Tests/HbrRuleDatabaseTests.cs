using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Rules;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrRuleDatabaseTests
  {
    private const string ProductionAssemblyFixtureName =
      "BIMBaoGui.Stage01.production.dll";
    private const string ExpectedRulePackResourceName =
      "BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack";

    [Fact]
    public void Production_assembly_manifest_and_Current_use_its_embedded_pack()
    {
      string productionAssemblyPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        ProductionAssemblyFixtureName);
      Assert.True(
        File.Exists(productionAssemblyPath),
        "Production assembly fixture is missing: " + productionAssemblyPath);

      Assembly productionAssembly = Assembly.LoadFile(
        Path.GetFullPath(productionAssemblyPath));
      Assert.NotSame(typeof(HbrRuleDatabase).Assembly, productionAssembly);
      Assert.Single(
        productionAssembly.GetManifestResourceNames(),
        name => name == ExpectedRulePackResourceName);

      Type databaseType = productionAssembly.GetType(
        "BIMBaoGui.Stage01.Rules.HbrRuleDatabase",
        true);
      PropertyInfo currentProperty = databaseType.GetProperty(
        "Current",
        BindingFlags.Public | BindingFlags.Static);
      Assert.NotNull(currentProperty);

      object current = currentProperty.GetValue(null, null);
      Assert.NotNull(current);
      Assert.Same(productionAssembly, current.GetType().Assembly);
      object package = databaseType.GetProperty("Package").GetValue(
        current,
        null);
      string payloadSha256 = (string)package.GetType()
        .GetProperty("RulePackageSha256")
        .GetValue(package, null);
      Assert.Matches("^[0-9a-f]{64}$", payloadSha256);
    }

    [Fact]
    public void Load_exposes_verified_counts_and_all_six_indexes()
    {
      HbrRuleDatabase database = LoadEmbeddedDatabase();

      Assert.Equal(359, database.PropertiesById.Count);
      Assert.Equal(359, database.PropertiesByIfcIdentity.Count);
      Assert.Equal(359, database.PropertiesByParameterGuid.Count);
      Assert.Equal(14, database.CarrierRolesById.Count);
      Assert.Equal(3, database.ProfilesByModelFileType.Count);
      Assert.Equal(28, database.TasksById.Count);
      Assert.Equal(
        356,
        database.Package.Properties.Count(
          property => property.ContractKind == "MVD"));
      Assert.Equal(
        3,
        database.Package.Properties.Count(
          property => property.ContractKind == "HIFC_EXTENSION"));

      HbrRuleProperty property = database.Package.Properties[0];
      Assert.Same(property, database.PropertiesById[property.PropertyId]);
      Assert.Same(
        property,
        database.PropertiesByIfcIdentity[new HbrIfcIdentity(
          property.Ifc.Entity,
          property.Ifc.PropertySet,
          property.Ifc.Property)]);
      Assert.False(database.PropertiesByIfcIdentity.ContainsKey(
        new HbrIfcIdentity(
          property.Ifc.Entity.ToUpperInvariant(),
          property.Ifc.PropertySet,
          property.Ifc.Property)));
      Assert.Same(
        property,
        database.PropertiesByParameterGuid[property.Revit.ParameterGuid]);
      Assert.Same(
        database.Package.CarrierRoles[0],
        database.CarrierRolesById[
          database.Package.CarrierRoles[0].RoleId]);
      Assert.Same(
        database.Package.ModelProfiles[0],
        database.ProfilesByModelFileType[
          database.Package.ModelProfiles[0].ProfileId]);
      Assert.Same(
        database.Package.Tasks[0],
        database.TasksById[database.Package.Tasks[0].TaskId]);
    }

    [Fact]
    public void Load_rejects_duplicate_property_id()
    {
      AssertDuplicateRejected(root =>
      {
        IList properties = List(root, "properties");
        Object(properties[1])["propertyId"] =
          Object(properties[0])["propertyId"];
      }, "PropertiesById");
    }

    [Fact]
    public void Load_rejects_duplicate_ordinal_ifc_identity()
    {
      AssertDuplicateRejected(root =>
      {
        IList properties = List(root, "properties");
        IDictionary<string, object> first = Object(
          Object(properties[0])["ifc"]);
        IDictionary<string, object> second = Object(
          Object(properties[1])["ifc"]);
        second["entity"] = first["entity"];
        second["propertySet"] = first["propertySet"];
        second["property"] = first["property"];
      }, "PropertiesByIfcIdentity");
    }

    [Fact]
    public void Load_rejects_duplicate_parameter_guid_even_with_different_case()
    {
      AssertDuplicateRejected(root =>
      {
        IList properties = List(root, "properties");
        IDictionary<string, object> first = Object(
          Object(properties[0])["revit"]);
        IDictionary<string, object> second = Object(
          Object(properties[1])["revit"]);
        second["parameterGuid"] = first["parameterGuid"]
          .ToString()
          .ToUpperInvariant();
      }, "PropertiesByParameterGuid");
    }

    [Fact]
    public void Load_rejects_duplicate_carrier_role_id()
    {
      AssertDuplicateRejected(root =>
      {
        IList roles = List(root, "carrierRoles");
        Object(roles[1])["roleId"] = Object(roles[0])["roleId"];
      }, "CarrierRolesById");
    }

    [Fact]
    public void Load_rejects_duplicate_model_profile_id()
    {
      AssertDuplicateRejected(root =>
      {
        IList profiles = List(root, "modelProfiles");
        Object(profiles[1])["profileId"] = Object(profiles[0])["profileId"];
      }, "ProfilesByModelFileType");
    }

    [Fact]
    public void Load_rejects_duplicate_task_id()
    {
      AssertDuplicateRejected(root =>
      {
        IList tasks = List(root, "tasks");
        Object(tasks[1])["taskId"] = Object(tasks[0])["taskId"];
      }, "TasksById");
    }

    [Fact]
    public void Package_and_indexes_are_deeply_read_only_and_remain_unchanged()
    {
      HbrRuleDatabase database = LoadEmbeddedDatabase();
      HbrRulePackage package = database.Package;
      HbrRuleProperty property = package.Properties[0];
      HbrCarrierRole role = package.CarrierRoles[0];
      HbrModelProfile profile = package.ModelProfiles[0];
      HbrTaskRule task = package.Tasks[0];

      AssertReadOnly(package.EvidenceSources, package.EvidenceSources[0]);
      AssertReadOnly(package.Properties, property);
      AssertReadOnly(package.CarrierRoles, role);
      AssertReadOnly(package.ModelProfiles, profile);
      AssertReadOnly(package.Conditions, package.Conditions[0]);
      AssertReadOnly(package.Tasks, task);
      AssertReadOnly(package.LegacyAliases, package.LegacyAliases[0]);
      AssertReadOnly(property.CarrierRoleIds, "MUTATION");
      AssertReadOnly(property.StageOwnership, "MUTATION");
      AssertReadOnly(property.Ifc.AllowedRuntimeTypes, "MUTATION");
      AssertReadOnly(property.Revit.LegacyNames, "MUTATION");
      AssertReadOnly(property.Suggestion.Aliases, "MUTATION");
      AssertReadOnly(role.ModelFileTypes, "MUTATION");
      AssertReadOnly(role.RevitCategories, "MUTATION");
      AssertReadOnly(role.AllowedElementKinds, "MUTATION");
      AssertReadOnly(role.NameAliases, "MUTATION");
      AssertReadOnly(role.FamilyAliases, "MUTATION");
      AssertReadOnly(role.TypeAliases, "MUTATION");
      AssertReadOnly(profile.TaskIds, "MUTATION");
      AssertReadOnly(profile.ActivationRuleIds, "MUTATION");
      AssertReadOnly(task.AttributeRequirements, "MUTATION");
      AssertReadOnly(task.Dependencies, "MUTATION");
      AssertReadOnly(task.GeometryChecks, "MUTATION");
      AssertReadOnly(task.PropertyChecks, "MUTATION");
      AssertReadOnly(task.TargetComparisons, "MUTATION");
      AssertReadOnly(
        package.Stage01.FieldRefs,
        package.Stage01.FieldRefs[0]);
      AssertReadOnly(
        package.Stage01.InternalWorkflowFields,
        package.Stage01.InternalWorkflowFields[0]);
      AssertReadOnly(
        package.Stage01.SpatialMappings,
        package.Stage01.SpatialMappings[0]);
      AssertReadOnly(
        package.Stage01.InternalWorkflowFields[0].AllowedValues,
        "MUTATION");
      AssertReadOnly(
        package.Stage01.OfficialPluginCompatibility.EntityPolicies,
        package.Stage01.OfficialPluginCompatibility.EntityPolicies[0]);
      AssertReadOnly(
        package.Stage01.OfficialPluginCompatibility.Exceptions,
        package.Stage01.OfficialPluginCompatibility.Exceptions[0]);

      AssertReadOnlyDictionary(
        database.PropertiesById,
        "MUTATION",
        property);
      AssertReadOnlyDictionary(
        database.PropertiesByIfcIdentity,
        new HbrIfcIdentity("IfcMutation", "Pset_Mutation", "Mutation"),
        property);
      AssertReadOnlyDictionary(
        database.PropertiesByParameterGuid,
        Guid.NewGuid(),
        property);
      AssertReadOnlyDictionary(
        database.CarrierRolesById,
        "MUTATION",
        role);
      AssertReadOnlyDictionary(
        database.ProfilesByModelFileType,
        "MUTATION",
        profile);
      AssertReadOnlyDictionary(
        database.TasksById,
        "MUTATION",
        task);

      Type[] domainTypes = typeof(HbrRulePackage).Assembly
        .GetTypes()
        .Where(type => type.IsPublic
          && type.Namespace == "BIMBaoGui.Stage01.Rules")
        .ToArray();
      Assert.All(domainTypes, type => Assert.DoesNotContain(
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
        propertyInfo => propertyInfo.SetMethod != null
          && propertyInfo.SetMethod.IsPublic));
      Assert.All(domainTypes, type => Assert.DoesNotContain(
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
        propertyInfo => propertyInfo.PropertyType.IsArray));
      Assert.Equal(359, package.Properties.Count);
      Assert.Same(property, database.PropertiesById[property.PropertyId]);
    }

    [Fact]
    public void Current_uses_execution_and_publication_to_return_one_instance()
    {
      var results = new HbrRuleDatabase[64];

      Parallel.For(
        0,
        results.Length,
        index => results[index] = HbrRuleDatabase.Current);

      Assert.All(results, result => Assert.Same(results[0], result));
      PropertyInfo current = typeof(HbrRuleDatabase).GetProperty(
        "Current",
        BindingFlags.Public | BindingFlags.Static);
      Assert.NotNull(current);
      Assert.Null(current.SetMethod);
    }

    [Fact]
    public async Task CreateLazy_is_deferred_and_executes_factory_once_under_contention()
    {
      const int workerCount = 8;
      int factoryCalls = 0;
      int accessAttempts = 0;
      using (var accessBarrier = new Barrier(workerCount + 1))
      using (var factoryEntered = new ManualResetEventSlim(false))
      using (var releaseFactory = new ManualResetEventSlim(false))
      {
        Lazy<HbrRuleDatabase> lazy = HbrRuleDatabase.CreateLazy(() =>
        {
          Interlocked.Increment(ref factoryCalls);
          factoryEntered.Set();
          releaseFactory.Wait();
          return LoadEmbeddedDatabase();
        });

        Assert.False(lazy.IsValueCreated);
        Assert.Equal(0, Volatile.Read(ref factoryCalls));

        Task<HbrRuleDatabase>[] accesses = Enumerable.Range(0, workerCount)
          .Select(_ => Task.Factory.StartNew(
            () =>
            {
              accessBarrier.SignalAndWait();
              Interlocked.Increment(ref accessAttempts);
              return lazy.Value;
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default))
          .ToArray();

        try
        {
          Assert.True(accessBarrier.SignalAndWait(TimeSpan.FromSeconds(5)));
          Assert.True(SpinWait.SpinUntil(
            () => Volatile.Read(ref accessAttempts) == workerCount,
            TimeSpan.FromSeconds(5)));
          Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(5)));

          SpinWait.SpinUntil(
            () => Volatile.Read(ref factoryCalls) > 1,
            TimeSpan.FromSeconds(1));
          releaseFactory.Set();

          Task<HbrRuleDatabase[]> allAccesses = Task.WhenAll(accesses);
          Task completed = await Task.WhenAny(
            allAccesses,
            Task.Delay(TimeSpan.FromSeconds(10)));
          Assert.Same(allAccesses, completed);
          HbrRuleDatabase[] results = await allAccesses;
          Assert.True(lazy.IsValueCreated);
          Assert.Equal(1, Volatile.Read(ref factoryCalls));
          Assert.All(results, result => Assert.Same(results[0], result));
        }
        finally
        {
          releaseFactory.Set();
        }
      }
    }

    [Fact]
    public void Embedded_pack_manifest_and_header_hash_match_independent_hash()
    {
      Assembly assembly = typeof(HbrRuleDatabaseTests).Assembly;
      Assert.Single(
        assembly.GetManifestResourceNames(),
        name => name == HbrRulePackTestFixture.ResourceName);
      byte[] pack = HbrRulePackTestFixture.ReadEmbeddedPackBytes();
      byte[] payload = HbrRulePackTestFixture.ExtractPayload(pack);
      byte[] headerHash = new byte[32];
      Buffer.BlockCopy(pack, 16, headerHash, 0, headerHash.Length);

      string expected = string.Concat(
        headerHash.Select(value => value.ToString("x2")));
      string actual;
      using (SHA256 algorithm = SHA256.Create())
        actual = string.Concat(
          algorithm.ComputeHash(payload)
            .Select(value => value.ToString("x2")));

      Assert.Equal(expected, actual);
      Assert.Equal(actual, HbrRuleDatabase.Current.Package.RulePackageSha256);
    }

    private static HbrRuleDatabase LoadEmbeddedDatabase()
    {
      return HbrRuleDatabase.Load(new MemoryStream(
        HbrRulePackTestFixture.ReadEmbeddedPackBytes()));
    }

    private static void AssertDuplicateRejected(
      Action<IDictionary<string, object>> mutation,
      string indexName)
    {
      byte[] pack = MutatePayload(mutation);

      InvalidDataException error = Assert.Throws<InvalidDataException>(
        () => HbrRuleDatabase.Load(new MemoryStream(pack)));

      Assert.Contains(indexName, error.Message);
    }

    private static byte[] MutatePayload(
      Action<IDictionary<string, object>> mutation)
    {
      byte[] original = HbrRulePackTestFixture.ReadEmbeddedPackBytes();
      string json = Encoding.UTF8.GetString(
        HbrRulePackTestFixture.ExtractPayload(original));
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512,
      };
      IDictionary<string, object> root = Object(
        serializer.DeserializeObject(json));
      mutation(root);
      return HbrRulePackTestFixture.BuildPack(
        Encoding.UTF8.GetBytes(serializer.Serialize(root)));
    }

    private static IDictionary<string, object> Object(object value)
    {
      return Assert.IsAssignableFrom<IDictionary<string, object>>(value);
    }

    private static IList List(
      IDictionary<string, object> value,
      string key)
    {
      return Assert.IsAssignableFrom<IList>(value[key]);
    }

    private static void AssertReadOnly<T>(
      IReadOnlyList<T> value,
      T mutation)
    {
      T[] before = value.ToArray();
      IList<T> list = Assert.IsAssignableFrom<IList<T>>(value);
      Assert.True(list.IsReadOnly);
      Assert.Throws<NotSupportedException>(() => list.Add(mutation));
      Assert.Equal(before, value.ToArray());
    }

    private static void AssertReadOnlyDictionary<TKey, TValue>(
      IReadOnlyDictionary<TKey, TValue> value,
      TKey key,
      TValue mutation)
    {
      int before = value.Count;
      IDictionary<TKey, TValue> dictionary =
        Assert.IsAssignableFrom<IDictionary<TKey, TValue>>(value);
      Assert.True(dictionary.IsReadOnly);
      Assert.Throws<NotSupportedException>(
        () => dictionary.Add(key, mutation));
      Assert.Equal(before, value.Count);
    }
  }
}
