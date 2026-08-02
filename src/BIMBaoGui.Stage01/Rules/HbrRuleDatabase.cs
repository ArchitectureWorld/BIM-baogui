using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Threading;

namespace BIMBaoGui.Stage01.Rules
{
  public sealed class HbrRuleDatabase
  {
    public const string ResourceName =
      "BIMBaoGui.Stage01.Resources.HBR_RulePack.hbrpack";

    private static readonly Lazy<HbrRuleDatabase> LazyCurrent =
      CreateLazy(LoadCurrent);

    private HbrRuleDatabase(HbrRulePackage package)
    {
      Package = package ?? throw new InvalidDataException(
        "HBRP package is null.");

      var propertiesById = new Dictionary<string, HbrRuleProperty>(
        StringComparer.Ordinal);
      var propertiesByIfcIdentity =
        new Dictionary<HbrIfcIdentity, HbrRuleProperty>();
      var propertiesByParameterGuid =
        new Dictionary<Guid, HbrRuleProperty>();
      foreach (HbrRuleProperty property in package.Properties)
      {
        AddUnique(
          propertiesById,
          property.PropertyId,
          property,
          "PropertiesById");
        AddUnique(
          propertiesByIfcIdentity,
          new HbrIfcIdentity(
            property.Ifc.Entity,
            property.Ifc.PropertySet,
            property.Ifc.Property),
          property,
          "PropertiesByIfcIdentity");
        AddUnique(
          propertiesByParameterGuid,
          property.Revit.ParameterGuid,
          property,
          "PropertiesByParameterGuid");
      }

      var carrierRolesById = new Dictionary<string, HbrCarrierRole>(
        StringComparer.Ordinal);
      foreach (HbrCarrierRole role in package.CarrierRoles)
        AddUnique(
          carrierRolesById,
          role.RoleId,
          role,
          "CarrierRolesById");

      var profilesByModelFileType =
        new Dictionary<string, HbrModelProfile>(StringComparer.Ordinal);
      foreach (HbrModelProfile profile in package.ModelProfiles)
        AddUnique(
          profilesByModelFileType,
          profile.ProfileId,
          profile,
          "ProfilesByModelFileType");

      var tasksById = new Dictionary<string, HbrTaskRule>(
        StringComparer.Ordinal);
      foreach (HbrTaskRule task in package.Tasks)
        AddUnique(tasksById, task.TaskId, task, "TasksById");

      PropertiesById = new ReadOnlyDictionary<string, HbrRuleProperty>(
        propertiesById);
      PropertiesByIfcIdentity =
        new ReadOnlyDictionary<HbrIfcIdentity, HbrRuleProperty>(
          propertiesByIfcIdentity);
      PropertiesByParameterGuid =
        new ReadOnlyDictionary<Guid, HbrRuleProperty>(
          propertiesByParameterGuid);
      CarrierRolesById = new ReadOnlyDictionary<string, HbrCarrierRole>(
        carrierRolesById);
      ProfilesByModelFileType =
        new ReadOnlyDictionary<string, HbrModelProfile>(
          profilesByModelFileType);
      TasksById = new ReadOnlyDictionary<string, HbrTaskRule>(tasksById);
    }

    public static HbrRuleDatabase Current => LazyCurrent.Value;

    public HbrRulePackage Package { get; }

    public IReadOnlyDictionary<string, HbrRuleProperty> PropertiesById
    {
      get;
    }

    public IReadOnlyDictionary<HbrIfcIdentity, HbrRuleProperty>
      PropertiesByIfcIdentity
    {
      get;
    }

    public IReadOnlyDictionary<Guid, HbrRuleProperty> PropertiesByParameterGuid
    {
      get;
    }

    public IReadOnlyDictionary<string, HbrCarrierRole> CarrierRolesById
    {
      get;
    }

    public IReadOnlyDictionary<string, HbrModelProfile> ProfilesByModelFileType
    {
      get;
    }

    public IReadOnlyDictionary<string, HbrTaskRule> TasksById { get; }

    public static HbrRuleDatabase Load(Stream stream)
    {
      return new HbrRuleDatabase(HbrRulePackageLoader.Load(stream));
    }

    internal static Lazy<HbrRuleDatabase> CreateLazy(
      Func<HbrRuleDatabase> factory)
    {
      return new Lazy<HbrRuleDatabase>(
        factory,
        LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static HbrRuleDatabase LoadCurrent()
    {
      Assembly assembly = typeof(HbrRuleDatabase).Assembly;
      using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
      {
        if (stream == null)
          throw new InvalidDataException(
            "Missing exact HBRP embedded resource: " + ResourceName + ".");
        return Load(stream);
      }
    }

    private static void AddUnique<TKey, TValue>(
      IDictionary<TKey, TValue> dictionary,
      TKey key,
      TValue value,
      string indexName)
    {
      if (dictionary.ContainsKey(key))
        throw new InvalidDataException(
          "HBRP duplicate key in " + indexName + ": " + key + ".");
      dictionary.Add(key, value);
    }
  }

  public sealed class HbrIfcIdentity : IEquatable<HbrIfcIdentity>
  {
    public HbrIfcIdentity(string entity, string propertySet, string property)
    {
      if (entity == null)
        throw new ArgumentNullException(nameof(entity));
      if (propertySet == null)
        throw new ArgumentNullException(nameof(propertySet));
      if (property == null)
        throw new ArgumentNullException(nameof(property));
      Entity = entity;
      PropertySet = propertySet;
      Property = property;
    }

    public string Entity { get; }
    public string PropertySet { get; }
    public string Property { get; }

    public bool Equals(HbrIfcIdentity other)
    {
      return !ReferenceEquals(other, null)
        && StringComparer.Ordinal.Equals(Entity, other.Entity)
        && StringComparer.Ordinal.Equals(PropertySet, other.PropertySet)
        && StringComparer.Ordinal.Equals(Property, other.Property);
    }

    public override bool Equals(object obj)
    {
      return Equals(obj as HbrIfcIdentity);
    }

    public override int GetHashCode()
    {
      unchecked
      {
        int hash = 17;
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Entity);
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(PropertySet);
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Property);
        return hash;
      }
    }

    public override string ToString()
    {
      return Entity + "|" + PropertySet + "|" + Property;
    }
  }
}
