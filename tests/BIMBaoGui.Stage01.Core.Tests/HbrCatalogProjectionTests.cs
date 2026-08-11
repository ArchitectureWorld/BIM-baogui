using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Hifc;
using BIMBaoGui.Stage01.Infrastructure;
using BIMBaoGui.Stage01.Mvd;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.TaskPlanning;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrCatalogProjectionTests
  {
    [Fact]
    public void Stage01_catalog_projects_the_complete_frozen_behavior()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      Stage01RegistryProvider registry =
        Stage01RegistryProvider.FromDatabase(database);
      Stage01Model model = registry.CreateDefaultModel();

      Assert.Equal(114, registry.Fields.Count);
      Assert.Equal(
        database.Package.Stage01.InternalWorkflowFields.Select(field => field.FieldKey),
        registry.Fields.Take(12).Select(field => field.Key));
      Assert.Equal(
        database.Package.Stage01.FieldRefs.Select(field => field.FieldKey),
        registry.Fields.Skip(12).Select(field => field.Key));
      Assert.Equal(27, registry.Fields.Count(field => field.Essential));
      Assert.Equal(14, registry.Conditions.Count);
      Assert.Equal(12, model.Values.Count(pair =>
        !string.IsNullOrWhiteSpace(pair.Value)));
      Assert.True(Guid.TryParse(model.GetValue(Stage01Keys.FileGuid), out Guid fileGuid));
      Assert.NotEqual(Guid.Empty, fileGuid);
      Assert.All(registry.Conditions, condition =>
        Assert.False(model.GetCondition(condition.Key)));

      AssertSnapshot("stage01-registry.v1.json", new
      {
        schemaVersion = "1.0",
        frozenFromCommit =
          "91a2db05ed57ae2335e63ff532b4bf5fe6109dfb",
        fields = registry.Fields.Select(field => new
        {
          key = field.Key,
          label = field.Label,
          group = field.Group,
          kind = field.Kind.ToString(),
          readOnly = field.ReadOnly,
          essential = field.Essential,
          deferred = field.Deferred,
          source = field.Source,
          entity = field.Entity,
          pset = field.Pset,
          allowedValues = field.AllowedValues.ToArray()
        }).ToArray(),
        groups = registry.Groups.ToArray(),
        conditions = registry.Conditions.Select(condition => new
        {
          key = condition.Key,
          label = condition.Label,
          group = condition.Group
        }).ToArray(),
        defaults = new
        {
          values = registry.Fields
            .Where(field => !string.IsNullOrWhiteSpace(model.GetValue(field.Key)))
            .Select(field => new
            {
              key = field.Key,
              value = string.Equals(
                field.Key,
                Stage01Keys.FileGuid,
                StringComparison.Ordinal)
                ? "<GUID>"
                : model.GetValue(field.Key)
            }).ToArray(),
          activeGroup = model.ActiveGroup,
          conditions = registry.Conditions.Select(condition => new
          {
            key = condition.Key,
            value = model.GetCondition(condition.Key)
          }).ToArray()
        }
      });
    }

    [Fact]
    public void Stage01_spatial_mapping_preserves_axis_meaning()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      var x = database.Package.Stage01.SpatialMappings.Single(item =>
        string.Equals(item.SourceName, "X", StringComparison.Ordinal));
      var y = database.Package.Stage01.SpatialMappings.Single(item =>
        string.Equals(item.SourceName, "Y", StringComparison.Ordinal));
      var elevation = database.Package.Stage01.SpatialMappings.Single(item =>
        string.Equals(item.SourceName, "Elevation", StringComparison.Ordinal));

      Assert.Equal("NorthSouth", x.TargetName);
      Assert.Equal("EastWest", y.TargetName);
      Assert.Equal("Elevation", elevation.TargetName);
      Assert.Equal("m", x.Unit);
      Assert.Equal("m", y.Unit);
      Assert.Equal("m", elevation.Unit);
    }

    [Fact]
    public void Official_catalog_projects_complete_frozen_mapping_and_resolvers()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      Stage01RegistryProvider registry =
        Stage01RegistryProvider.FromDatabase(database);
      OfficialHifcMappingCatalog catalog =
        OfficialHifcMappingCatalog.FromDatabase(database);
      OfficialHifcMapping[] mappings = catalog.Mappings.ToArray();

      Assert.Equal(166, mappings.Length);
      Assert.Equal(25, mappings.Count(mapping =>
        string.IsNullOrWhiteSpace(mapping.Category)));
      Assert.Equal(164, mappings.Count(mapping => string.Equals(
        mapping.OfficialSourceParameterGroup,
        "材质和装饰",
        StringComparison.Ordinal)));
      Assert.Equal(2, mappings.Count(mapping => string.Equals(
        mapping.OfficialSourceParameterGroup,
        "阶段化",
        StringComparison.Ordinal)));
      foreach (OfficialHifcMapping mapping in mappings)
      {
        AssertResolved(catalog, mapping.PropertyId, mapping);
        AssertResolved(catalog, mapping.ParameterGuid.ToString("D"), mapping);
        AssertResolved(catalog, mapping.ParameterName, mapping);
        AssertResolved(
          catalog,
          "  " + mapping.PropertyId.ToUpperInvariant() + "  ",
          mapping);
      }

      AssertSnapshot("official-hifc-mappings.v1.json", new
      {
        schemaVersion = "1.0",
        frozenFromCommit =
          "91a2db05ed57ae2335e63ff532b4bf5fe6109dfb",
        mappings = mappings.Select(mapping => new
        {
          propertyId = mapping.PropertyId,
          parameterGuid = mapping.ParameterGuid.ToString("D"),
          parameterName = mapping.ParameterName,
          bindingScope = mapping.BindingScope,
          category = mapping.Category,
          carrier = mapping.Carrier,
          persistenceMode = mapping.PersistenceMode,
          ifcEntity = mapping.IfcEntity,
          propertySet = mapping.PropertySet,
          ifcProperty = mapping.IfcProperty,
          ifcDataType = mapping.IfcDataType,
          sharedParameterType = mapping.SharedParameterType,
          unit = mapping.Unit,
          sourceParameterOverride = mapping.SourceParameterOverride,
          officialSourceParameterName = mapping.OfficialSourceParameterName,
          officialSourceParameterGroup = mapping.OfficialSourceParameterGroup,
          officialSourceParameterType = mapping.OfficialSourceParameterType,
          officialSourceParameterGuid = mapping.OfficialSourceParameterGuid.ToString("D"),
          legacyOfficialSourceParameterGuid = mapping.LegacyOfficialSourceParameterGuid.ToString("D"),
          isTypeBinding = mapping.IsTypeBinding,
          hasDistinctOfficialSourceAlias = mapping.HasDistinctOfficialSourceAlias
        }).ToArray(),
        aliasResolvers = mappings.SelectMany(mapping => new[]
        {
          ResolveAlias(catalog, mapping.PropertyId),
          ResolveAlias(catalog, mapping.ParameterGuid.ToString("D")),
          ResolveAlias(catalog, mapping.ParameterName),
          ResolveAlias(catalog, "  " + mapping.PropertyId.ToUpperInvariant() + "  ")
        }).ToArray(),
        stage01FieldResolvers = registry.Fields
          .Where(field => field.Key.IndexOf('|') >= 0)
          .Select(field => ResolveStage01Field(catalog, field.Key))
          .ToArray(),
        officialSourceParameterGroups = mappings
          .GroupBy(
            mapping => mapping.OfficialSourceParameterGroup,
            StringComparer.Ordinal)
          .Select(group => new { name = group.Key, count = group.Count() })
          .ToArray(),
        categorylessCount = mappings.Count(mapping =>
          string.IsNullOrWhiteSpace(mapping.Category))
      });
    }

    [Fact]
    public void Official_spatial_mappings_keep_legacy_alias_and_unspaced_identity()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      OfficialHifcMappingCatalog catalog =
        OfficialHifcMappingCatalog.FromDatabase(database);
      foreach (string sourceName in new[] { "X", "Y", "Elevation" })
      {
        HbrSpatialMapping spatial =
          database.Package.Stage01.SpatialMappings.Single(item =>
            string.Equals(item.SourceName, sourceName, StringComparison.Ordinal));
        Assert.True(catalog.TryResolveStage01FieldKey(
          spatial.FieldKey,
          out OfficialHifcMapping mapping));
        Assert.Equal("m", mapping.Unit);
        Assert.DoesNotContain(" ", spatial.FieldKey);
        Assert.StartsWith("HIFC.", mapping.ParameterName);
        Assert.DoesNotContain(" ", mapping.IfcProperty);
      }
    }

    [Fact]
    public void Stage01_xy_coordinates_use_the_same_length_contract_as_projection()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      OfficialHifcMappingCatalog catalog =
        OfficialHifcMappingCatalog.FromDatabase(database);
      foreach (string sourceName in new[] { "X", "Y" })
      {
        HbrSpatialMapping spatial = database.Package.Stage01.SpatialMappings
          .Single(item => item.SourceName == sourceName);
        Assert.True(catalog.TryResolveStage01FieldKey(
          spatial.FieldKey,
          out OfficialHifcMapping mapping));
        HbrRuleProperty property = database.PropertiesById[mapping.PropertyId];
        Assert.Equal("LENGTH", mapping.SharedParameterType);
        Assert.Equal("Length", property.Revit.ParameterType);
      }
    }

    [Fact]
    public void Plugin_compatibility_projects_policies_exceptions_and_fallback()
    {
      OfficialPluginCompatibilityCatalog catalog =
        OfficialPluginCompatibilityCatalog.FromDatabase(
          HbrRuleDatabase.Current);
      OfficialPluginEntityPolicy fallback = catalog.GetEntityPolicy(
        " UnknownIfcEntity ");

      Assert.Equal(9, catalog.EntityPolicies.Count);
      Assert.Equal(13, catalog.Exceptions.Count);
      Assert.All(catalog.Exceptions, exception =>
        Assert.False(string.IsNullOrWhiteSpace(exception.Reason)));
      Assert.Equal(" UnknownIfcEntity ", fallback.IfcEntity);
      Assert.Equal("UNVERIFIED", fallback.OfficialObjectMappingEvidence);
      Assert.Equal("BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT", fallback.WritePolicy);
      Assert.False(fallback.OfficialExportVerified);
      Assert.True(fallback.IsBlocked);

      AssertSnapshot("official-plugin-compatibility.v1.json", new
      {
        schemaVersion = "1.0",
        frozenFromCommit =
          "91a2db05ed57ae2335e63ff532b4bf5fe6109dfb",
        entityPolicies = catalog.EntityPolicies.Select(policy => new
        {
          ifcEntity = policy.IfcEntity,
          officialObjectMappingEvidence = policy.OfficialObjectMappingEvidence,
          revitCarrier = policy.RevitCarrier,
          writePolicy = policy.WritePolicy,
          officialExportVerified = policy.OfficialExportVerified,
          isBlocked = policy.IsBlocked,
          allowsProjectInformationDefault = policy.AllowsProjectInformationDefault
        }).ToArray(),
        exceptions = catalog.Exceptions.Select(exception => new
        {
          fieldKey = exception.FieldKey,
          reason = exception.Reason,
          recognized = catalog.IsStage01ProjectFieldException(exception.FieldKey)
        }).ToArray(),
        unknownEntityFallback = new
        {
          input = " UnknownIfcEntity ",
          ifcEntity = fallback.IfcEntity,
          officialObjectMappingEvidence = fallback.OfficialObjectMappingEvidence,
          revitCarrier = fallback.RevitCarrier,
          writePolicy = fallback.WritePolicy,
          officialExportVerified = fallback.OfficialExportVerified,
          isBlocked = fallback.IsBlocked,
          allowsProjectInformationDefault = fallback.AllowsProjectInformationDefault
        }
      });
    }

    [Fact]
    public void Mvd_catalog_projects_complete_rules_and_alias_resolvers()
    {
      MvdIfcNormalizationCatalog catalog =
        MvdIfcNormalizationCatalog.FromDatabase(HbrRuleDatabase.Current);
      MvdIfcNormalizationRule[] rules = catalog.Rules.ToArray();

      Assert.Equal(179, rules.Length);
      foreach (MvdIfcNormalizationRule rule in rules)
      {
        foreach (string propertySet in rule.PropertySetAliases)
        foreach (string property in rule.PropertyAliases)
        {
          Assert.True(catalog.TryResolve(
            rule.Entity,
            propertySet,
            property,
            out MvdIfcNormalizationRule resolved));
          Assert.Same(rule, resolved);
        }
      }

      AssertSnapshot("mvd-ifc-normalization.v1.json", new
      {
        schemaVersion = "1.0",
        frozenFromCommit =
          "91a2db05ed57ae2335e63ff532b4bf5fe6109dfb",
        rules = rules.Select(rule => new
        {
          entity = rule.Entity,
          canonicalPropertySet = rule.CanonicalPropertySet,
          propertySetAliases = rule.PropertySetAliases.ToArray(),
          canonicalProperty = rule.CanonicalProperty,
          propertyAliases = rule.PropertyAliases.ToArray(),
          targetType = rule.TargetType,
          unit = rule.Unit,
          internalAliases = rule.InternalAliases.ToArray()
        }).ToArray()
      });
    }

    [Fact]
    public void Mvd_axis_aliases_resolve_with_and_without_property_whitespace()
    {
      HbrRuleDatabase database = HbrRuleDatabase.Current;
      MvdIfcNormalizationCatalog catalog =
        MvdIfcNormalizationCatalog.FromDatabase(database);
      foreach (string sourceName in new[] { "X", "Y" })
      {
        HbrSpatialMapping spatial =
          database.Package.Stage01.SpatialMappings.Single(item =>
            string.Equals(item.SourceName, sourceName, StringComparison.Ordinal));
        string[] parts = spatial.FieldKey.Split(new[] { '|' }, 3);
        string withoutWhitespace = new string(parts[2]
          .Where(character => !char.IsWhiteSpace(character))
          .ToArray());
        Assert.True(catalog.TryResolve(
          parts[0],
          parts[1],
          parts[2],
          out MvdIfcNormalizationRule spaced));
        Assert.True(catalog.TryResolve(
          parts[0],
          parts[1],
          withoutWhitespace,
          out MvdIfcNormalizationRule compact));
        Assert.Same(spaced, compact);
        Assert.Equal("m", spaced.Unit);
      }
    }

    [Fact]
    public void Task_catalog_projects_all_rules_and_legacy_partitions()
    {
      IReadOnlyList<TaskRuleDefinition> rules =
        TaskRuleCatalog.FromDatabase(HbrRuleDatabase.Current);
      string[] models =
      {
        PlanningTargetRequirementPolicy.SiteModel,
        PlanningTargetRequirementPolicy.AboveGroundModel,
        PlanningTargetRequirementPolicy.UndergroundModel
      };
      var partitions = models.Select(model => new
      {
        modelFileType = model,
        rules = rules
          .Where(rule => string.Equals(
            rule.ModelFileType,
            model,
            StringComparison.Ordinal))
          .OrderBy(rule => rule.Item.Sequence)
          .ThenBy(rule => rule.Item.TaskId, StringComparer.Ordinal)
          .Select(rule => new
          {
            modelFileType = rule.ModelFileType,
            taskId = rule.Item.TaskId,
            name = rule.Item.Name,
            objectCode = rule.Item.ObjectCode,
            requirement = rule.Item.Requirement.ToString(),
            conditionKey = rule.Item.ConditionKey,
            sequence = rule.Item.Sequence,
            skeletonTask = rule.Item.SkeletonTask,
            attributeRequirements = rule.Item.AttributeRequirements.ToArray(),
            dependencies = rule.Item.Dependencies.ToArray(),
            geometryChecks = rule.Item.GeometryChecks.ToArray(),
            propertyChecks = rule.Item.PropertyChecks.ToArray(),
            targetComparisons = rule.Item.TargetComparisons.ToArray()
          }).ToArray()
      }).ToArray();

      Assert.Equal(28, rules.Count);
      Assert.Equal(15, partitions[0].rules.Length);
      Assert.Equal(7, partitions[1].rules.Length);
      Assert.Equal(6, partitions[2].rules.Length);
      Assert.Empty(TaskRuleCatalog.ForModelType(null));
      Assert.Empty(TaskRuleCatalog.ForModelType("UNKNOWN_MODEL"));
      Assert.All(partitions, partition => Assert.Equal(
        partition.rules.Select(item => item.taskId),
        partition.rules
          .OrderBy(item => item.sequence)
          .ThenBy(item => item.taskId, StringComparer.Ordinal)
          .Select(item => item.taskId)));

      AssertSnapshot("task-rules.v1.json", new
      {
        schemaVersion = "1.0",
        frozenFromCommit =
          "91a2db05ed57ae2335e63ff532b4bf5fe6109dfb",
        partitions,
        nullModelCount = TaskRuleCatalog.ForModelType(null).Count,
        unknownModelCount = TaskRuleCatalog.ForModelType("UNKNOWN_MODEL").Count
      });
    }

    [Fact]
    public void Activation_catalog_projects_the_complete_fixed_matrix()
    {
      RuleActivationProjection projection =
        RuleActivationCatalog.FromDatabase(HbrRuleDatabase.Current);
      string[] conditionKeys = projection.ConditionRules.Keys.ToArray();
      string[] models =
      {
        PlanningTargetRequirementPolicy.SiteModel,
        PlanningTargetRequirementPolicy.AboveGroundModel,
        PlanningTargetRequirementPolicy.UndergroundModel,
        "UNKNOWN_MODEL"
      };
      var cases = new List<object>();
      foreach (string model in models)
      {
        cases.Add(ActivationCase(
          projection,
          model,
          "none",
          new Dictionary<string, bool>()));
        cases.Add(ActivationCase(
          projection,
          model,
          "all",
          conditionKeys.ToDictionary(
            key => key,
            key => true,
            StringComparer.Ordinal)));
        foreach (string conditionKey in conditionKeys)
        {
          cases.Add(ActivationCase(
            projection,
            model,
            "only:" + conditionKey,
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
              [conditionKey] = true
            }));
        }
      }

      Assert.Equal(10, projection.ConditionRules.Count);
      Assert.Equal(48, cases.Count);
      RuleActivationResult unknown = projection.Compile(
        "UNKNOWN_MODEL",
        new Dictionary<string, bool>());
      Assert.Equal(new[]
      {
        "HBR.TARGET.BUILDING_DENSITY",
        "HBR.TARGET.FLOOR_AREA_RATIO",
        "HBR.TARGET.GREEN_RATE"
      }, unknown.Activated);

      AssertSnapshot("rule-activation.v1.json", new
      {
        schemaVersion = "1.0",
        frozenFromCommit =
          "91a2db05ed57ae2335e63ff532b4bf5fe6109dfb",
        conditionRules = projection.ConditionRules.Select(rule => new
        {
          conditionKey = rule.Key,
          activationRuleId = rule.Value
        }).ToArray(),
        cases = cases.ToArray()
      });
    }

    [Fact]
    public void Test_runtime_does_not_embed_legacy_catalog_resources()
    {
      string[] resourceNames = typeof(HbrCatalogProjectionTests).Assembly
        .GetManifestResourceNames();
      foreach (string legacyName in new[]
      {
        "stage01_file_initialization_registry_v0.1.json",
        "GH_HIFC_ParameterBindings.json",
        "wuhan_planning_rules.v1.json",
        "official_plugin_compatibility_status.v1.json"
      })
        Assert.DoesNotContain(resourceNames, name =>
          name.EndsWith(legacyName, StringComparison.Ordinal));
      Assert.Contains(resourceNames, name =>
        name.EndsWith("HBR_RulePack.hbrpack", StringComparison.Ordinal));
      Assert.Equal(8, resourceNames.Count(name =>
        name.IndexOf(".Snapshots.", StringComparison.Ordinal) >= 0));
    }

    [Fact]
    public void Catalog_sources_have_no_legacy_resource_or_default_fallback_path()
    {
      string projectDirectory = Path.GetFullPath(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        @"..\..\.."));
      string root = Path.GetFullPath(Path.Combine(projectDirectory, @"..\.."));
      string[] relativePaths =
      {
        @"src\BIMBaoGui.Stage01\Infrastructure\Stage01RegistryProvider.cs",
        @"src\BIMBaoGui.Stage01\Hifc\OfficialHifcMappingCatalog.cs",
        @"src\BIMBaoGui.Stage01\Hifc\OfficialPluginCompatibilityCatalog.cs",
        @"src\BIMBaoGui.Stage01\Mvd\MvdIfcNormalizationCatalog.cs",
        @"src\BIMBaoGui.Stage01\TaskPlanning\TaskRuleCatalog.cs",
        @"src\BIMBaoGui.Stage01\Context\RuleActivationCatalog.cs"
      };
      foreach (string relativePath in relativePaths)
      {
        string source = File.ReadAllText(Path.Combine(root, relativePath));
        Assert.DoesNotContain("GetManifestResourceStream", source);
        Assert.DoesNotContain("ReadEmbeddedText", source);
        Assert.DoesNotContain("stage01_file_initialization_registry", source);
        Assert.DoesNotContain("GH_HIFC_ParameterBindings", source);
        Assert.DoesNotContain("wuhan_planning_rules", source);
        Assert.DoesNotContain("official_plugin_compatibility_status", source);
        Assert.DoesNotContain("catch (", source);
      }
    }

    private static object ActivationCase(
      RuleActivationProjection projection,
      string modelFileType,
      string state,
      IDictionary<string, bool> conditions)
    {
      RuleActivationResult result = projection.Compile(
        modelFileType,
        conditions);
      return new
      {
        modelFileType,
        state,
        conditions = conditions
          .OrderBy(pair => pair.Key, StringComparer.Ordinal)
          .Select(pair => new { key = pair.Key, value = pair.Value })
          .ToArray(),
        activated = result.Activated.ToArray(),
        notApplicable = result.NotApplicable.ToArray()
      };
    }

    private static void AssertResolved(
      OfficialHifcMappingCatalog catalog,
      string alias,
      OfficialHifcMapping expected)
    {
      Assert.True(catalog.TryResolve(alias, out OfficialHifcMapping actual));
      Assert.Same(expected, actual);
    }

    private static object ResolveAlias(
      OfficialHifcMappingCatalog catalog,
      string alias)
    {
      bool resolved = catalog.TryResolve(alias, out OfficialHifcMapping mapping);
      return new
      {
        alias,
        resolved,
        propertyId = resolved ? mapping.PropertyId : null
      };
    }

    private static object ResolveStage01Field(
      OfficialHifcMappingCatalog catalog,
      string fieldKey)
    {
      bool resolved = catalog.TryResolveStage01FieldKey(
        fieldKey,
        out OfficialHifcMapping mapping);
      return new
      {
        fieldKey,
        resolved,
        propertyId = resolved ? mapping.PropertyId : null
      };
    }

    private static void AssertSnapshot(string fileName, object actual)
    {
      Assembly assembly = typeof(HbrCatalogProjectionTests).Assembly;
      string resourceName = assembly.GetManifestResourceNames().Single(name =>
        name.EndsWith("Snapshots." + fileName, StringComparison.Ordinal));
      string expected;
      using (Stream stream = assembly.GetManifestResourceStream(resourceName))
      using (var reader = new StreamReader(
        stream,
        new UTF8Encoding(false, true),
        true))
      {
        expected = reader.ReadToEnd();
      }
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
      };
      Assert.Equal(
        serializer.Serialize(serializer.DeserializeObject(expected)),
        serializer.Serialize(actual));
    }
  }
}
