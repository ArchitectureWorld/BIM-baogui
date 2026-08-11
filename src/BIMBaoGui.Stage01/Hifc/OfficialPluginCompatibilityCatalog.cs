using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Hifc
{
  internal sealed class OfficialPluginEntityPolicy
  {
    public string IfcEntity { get; set; } = string.Empty;
    public string OfficialObjectMappingEvidence { get; set; } = string.Empty;
    public string RevitCarrier { get; set; } = string.Empty;
    public string WritePolicy { get; set; } =
      "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT";
    public bool OfficialExportVerified { get; set; }

    public bool IsBlocked => WritePolicy.StartsWith(
      "BLOCK_",
      StringComparison.OrdinalIgnoreCase);

    public bool AllowsProjectInformationDefault =>
      string.Equals(IfcEntity, "IfcProject", StringComparison.Ordinal)
      || string.Equals(IfcEntity, "IfcBuilding", StringComparison.Ordinal);
  }

  internal sealed class OfficialPluginException
  {
    internal OfficialPluginException(string fieldKey, string reason)
    {
      FieldKey = fieldKey;
      Reason = reason;
    }

    public string FieldKey { get; }
    public string Reason { get; }
  }

  internal sealed class OfficialPluginCompatibilityCatalog
  {
    private static readonly Lazy<OfficialPluginCompatibilityCatalog> LazyInstance =
      new Lazy<OfficialPluginCompatibilityCatalog>(() =>
        FromDatabase(HbrRuleDatabase.Current));

    private readonly Dictionary<string, OfficialPluginEntityPolicy>
      _entityPoliciesByIfcEntity;
    private readonly HashSet<string> _stage01ProjectFieldExceptions;

    private OfficialPluginCompatibilityCatalog(
      IReadOnlyList<OfficialPluginEntityPolicy> entityPolicies,
      IReadOnlyList<OfficialPluginException> exceptions)
    {
      EntityPolicies = entityPolicies
        ?? throw new ArgumentNullException(nameof(entityPolicies));
      Exceptions = exceptions ?? throw new ArgumentNullException(nameof(exceptions));
      _entityPoliciesByIfcEntity =
        new Dictionary<string, OfficialPluginEntityPolicy>(StringComparer.Ordinal);
      foreach (OfficialPluginEntityPolicy policy in entityPolicies)
      {
        if (policy == null || string.IsNullOrWhiteSpace(policy.IfcEntity))
          throw new InvalidDataException(
            "Official plugin entity policy is incomplete.");
        if (_entityPoliciesByIfcEntity.ContainsKey(policy.IfcEntity))
          throw new InvalidDataException(
            "Official plugin entity policy is duplicated: "
            + policy.IfcEntity);
        _entityPoliciesByIfcEntity.Add(policy.IfcEntity, policy);
      }

      _stage01ProjectFieldExceptions = new HashSet<string>(StringComparer.Ordinal);
      foreach (OfficialPluginException exception in exceptions)
      {
        if (exception == null
          || string.IsNullOrWhiteSpace(exception.FieldKey)
          || string.IsNullOrWhiteSpace(exception.Reason))
          throw new InvalidDataException(
            "Official plugin exception is incomplete.");
        if (!_stage01ProjectFieldExceptions.Add(exception.FieldKey))
          throw new InvalidDataException(
            "Official plugin exception is duplicated: "
            + exception.FieldKey);
      }
    }

    public static OfficialPluginCompatibilityCatalog Instance =>
      LazyInstance.Value;

    public IReadOnlyList<OfficialPluginEntityPolicy> EntityPolicies { get; }
    public IReadOnlyList<OfficialPluginException> Exceptions { get; }

    internal static OfficialPluginCompatibilityCatalog FromDatabase(
      HbrRuleDatabase database)
    {
      if (database == null) throw new ArgumentNullException(nameof(database));
      HbrOfficialPluginCompatibility source =
        database.Package.Stage01.OfficialPluginCompatibility;
      OfficialPluginEntityPolicy[] policies = source.EntityPolicies
        .Select(policy => new OfficialPluginEntityPolicy
        {
          IfcEntity = policy.IfcEntity,
          OfficialObjectMappingEvidence = policy.OfficialObjectMappingEvidence,
          RevitCarrier = policy.RevitCarrier,
          WritePolicy = policy.WritePolicy,
          OfficialExportVerified = policy.OfficialExportVerified
        })
        .ToArray();
      OfficialPluginException[] exceptions = source.Exceptions
        .Select(exception => new OfficialPluginException(
          exception.FieldKey,
          exception.Reason))
        .ToArray();
      return new OfficialPluginCompatibilityCatalog(policies, exceptions);
    }

    public OfficialPluginEntityPolicy GetEntityPolicy(string ifcEntity)
    {
      if (!string.IsNullOrWhiteSpace(ifcEntity)
        && _entityPoliciesByIfcEntity.TryGetValue(
          ifcEntity.Trim(),
          out OfficialPluginEntityPolicy policy))
        return policy;

      return new OfficialPluginEntityPolicy
      {
        IfcEntity = ifcEntity ?? string.Empty,
        OfficialObjectMappingEvidence = "UNVERIFIED",
        WritePolicy = "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT",
        OfficialExportVerified = false
      };
    }

    public bool IsStage01ProjectFieldException(string fieldKey)
    {
      return !string.IsNullOrWhiteSpace(fieldKey)
        && _stage01ProjectFieldExceptions.Contains(fieldKey.Trim());
    }
  }
}
