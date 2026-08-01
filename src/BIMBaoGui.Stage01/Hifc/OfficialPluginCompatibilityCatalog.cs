using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace BIMBaoGui.Stage01.Hifc
{
  internal sealed class OfficialPluginEntityPolicy
  {
    public string IfcEntity { get; set; } = string.Empty;
    public string OfficialObjectMappingEvidence { get; set; } = string.Empty;
    public string RevitCarrier { get; set; } = string.Empty;
    public string WritePolicy { get; set; } = "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT";
    public bool OfficialExportVerified { get; set; }

    public bool IsBlocked => WritePolicy.StartsWith("BLOCK_", StringComparison.OrdinalIgnoreCase);
    public bool AllowsProjectInformationDefault =>
      string.Equals(IfcEntity, "IfcProject", StringComparison.Ordinal)
      || string.Equals(IfcEntity, "IfcBuilding", StringComparison.Ordinal);
  }

  internal sealed class OfficialPluginCompatibilityCatalog
  {
    private const string ResourceName =
      "BIMBaoGui.Stage01.Resources.official_plugin_compatibility_status.v1.json";
    private static readonly object InstanceLock = new object();
    private static OfficialPluginCompatibilityCatalog _instance;

    private readonly Dictionary<string, OfficialPluginEntityPolicy> _entityPolicies;
    private readonly HashSet<string> _stage01ProjectFieldExceptions;

    private OfficialPluginCompatibilityCatalog(
      IDictionary<string, OfficialPluginEntityPolicy> entityPolicies,
      IEnumerable<string> stage01ProjectFieldExceptions)
    {
      _entityPolicies = new Dictionary<string, OfficialPluginEntityPolicy>(
        entityPolicies ?? new Dictionary<string, OfficialPluginEntityPolicy>(),
        StringComparer.Ordinal);
      _stage01ProjectFieldExceptions = new HashSet<string>(
        stage01ProjectFieldExceptions ?? Array.Empty<string>(),
        StringComparer.Ordinal);
    }

    public static OfficialPluginCompatibilityCatalog Instance
    {
      get
      {
        if (_instance != null) return _instance;
        lock (InstanceLock)
        {
          if (_instance == null) _instance = Load();
          return _instance;
        }
      }
    }

    public OfficialPluginEntityPolicy GetEntityPolicy(string ifcEntity)
    {
      if (!string.IsNullOrWhiteSpace(ifcEntity)
        && _entityPolicies.TryGetValue(ifcEntity.Trim(), out OfficialPluginEntityPolicy policy))
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

    private static OfficialPluginCompatibilityCatalog Load()
    {
      Assembly assembly = typeof(OfficialPluginCompatibilityCatalog).Assembly;
      using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
      {
        if (stream == null)
        {
          string available = string.Join(", ", assembly.GetManifestResourceNames());
          throw new InvalidDataException(
            "缺少官方插件兼容状态资源："
            + ResourceName
            + "。当前资源："
            + available);
        }

        using (var reader = new StreamReader(stream))
        {
          var serializer = new JavaScriptSerializer
          {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 512
          };
          CompatibilityEnvelope envelope =
            serializer.Deserialize<CompatibilityEnvelope>(reader.ReadToEnd());
          if (envelope?.entities == null || envelope.entities.Count == 0)
            throw new InvalidDataException("官方插件兼容状态未定义任何 IFC 实体策略。");

          var policies = new Dictionary<string, OfficialPluginEntityPolicy>(StringComparer.Ordinal);
          foreach (KeyValuePair<string, EntityPolicyRecord> item in envelope.entities)
          {
            EntityPolicyRecord source = item.Value ?? new EntityPolicyRecord();
            policies[item.Key] = new OfficialPluginEntityPolicy
            {
              IfcEntity = item.Key,
              OfficialObjectMappingEvidence = source.officialObjectMappingEvidence ?? "UNVERIFIED",
              RevitCarrier = source.revitCarrier ?? string.Empty,
              WritePolicy = source.writePolicy ?? "BLOCK_PENDING_OFFICIAL_PLUGIN_CONTRACT",
              OfficialExportVerified = source.officialExportVerified
            };
          }

          return new OfficialPluginCompatibilityCatalog(
            policies,
            envelope.stage01ProjectFieldExceptions ?? Array.Empty<string>());
        }
      }
    }

    private sealed class CompatibilityEnvelope
    {
      public string[] stage01ProjectFieldExceptions { get; set; }
      public Dictionary<string, EntityPolicyRecord> entities { get; set; }
    }

    private sealed class EntityPolicyRecord
    {
      public string officialObjectMappingEvidence { get; set; }
      public string revitCarrier { get; set; }
      public string writePolicy { get; set; }
      public bool officialExportVerified { get; set; }
    }
  }
}
