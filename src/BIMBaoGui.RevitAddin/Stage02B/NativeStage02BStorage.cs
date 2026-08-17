using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal static class NativeStage02BStorage
  {
    internal static readonly Guid SchemaGuid = new Guid(
      "420ba043-1d47-4f29-a97e-f33c75e18385");
    internal const string SchemaName = "HBR_NATIVE_STAGE02B_METRICS_V1";
    internal const string StorageName = "HBR Native Stage02B Metrics";
    private const string SchemaVersion = "HBR_NATIVE_STAGE02B_METRICS_V1";

    internal static NativeStage02BStorageSnapshot Read(Document document)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      DataStorage storage = FindStorage(document);
      if (storage == null)
        return NativeStage02BCanonicalizer.SealSnapshot(
          Array.Empty<NativeStage02BMetricRecord>());
      Schema schema = Schema.Lookup(SchemaGuid)
        ?? throw new InvalidDataException(
          "Stage02B DataStorage exists without its fixed schema.");
      EnsureSchema(schema);
      Entity entity = storage.GetEntity(schema);
      if (!entity.IsValid())
        throw new InvalidDataException("Stage02B DataStorage entity is invalid.");
      string version = Get(entity, schema, "SchemaVersion");
      string canonical = Get(entity, schema, "CanonicalJson");
      if (!string.Equals(version, SchemaVersion, StringComparison.Ordinal)
        || string.IsNullOrWhiteSpace(canonical))
        throw new InvalidDataException("Stage02B storage schema version is invalid.");
      NativeStage02BStorageSnapshot snapshot = ParseCanonical(canonical);
      if (!string.Equals(snapshot.CanonicalJson, canonical,
        StringComparison.Ordinal))
        throw new InvalidDataException("Stage02B storage JSON is not canonical.");
      return snapshot;
    }

    internal static void Write(
      Document document,
      NativeStage02BStorageSnapshot snapshot)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
      if (!document.IsModifiable)
        throw new InvalidOperationException(
          "NativeStage02BStorage.Write requires an active transaction.");
      NativeStage02BStorageSnapshot sealedSnapshot =
        NativeStage02BCanonicalizer.SealSnapshot(snapshot.Records);
      if (!string.Equals(snapshot.CanonicalJson, sealedSnapshot.CanonicalJson,
          StringComparison.Ordinal)
        || !string.Equals(snapshot.SnapshotHash, sealedSnapshot.SnapshotHash,
          StringComparison.Ordinal))
        throw new InvalidDataException("Stage02B snapshot hash mismatch.");

      Schema schema = GetOrCreateSchema();
      DataStorage storage = FindStorage(document) ?? DataStorage.Create(document);
      storage.Name = StorageName;
      var entity = new Entity(schema);
      Set(entity, schema, "SchemaVersion", SchemaVersion);
      Set(entity, schema, "CanonicalJson", sealedSnapshot.CanonicalJson);
      storage.SetEntity(entity);
    }

    internal static void WriteMetric(
      Document document,
      NativeStage02BMetricRecord record)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (record == null) throw new ArgumentNullException(nameof(record));
      if (!document.IsModifiable)
        throw new InvalidOperationException(
          "NativeStage02BStorage.WriteMetric requires an active transaction.");
      NativeStage02BStorageSnapshot current = Read(document);
      Write(document, NativeStage02BStoragePolicy.Merge(current, record));
    }

    private static NativeStage02BStorageSnapshot ParseCanonical(string json)
    {
      Dictionary<string, object> root;
      try
      {
        root = new JavaScriptSerializer
        {
          MaxJsonLength = 8 * 1024 * 1024,
          RecursionLimit = 128
        }.Deserialize<Dictionary<string, object>>(json);
      }
      catch (Exception exception)
      {
        throw new InvalidDataException(
          "Stage02B canonical JSON cannot be parsed.", exception);
      }
      if (root == null || GetString(root, "schemaVersion") != SchemaVersion)
        throw new InvalidDataException("Stage02B canonical JSON schema is invalid.");
      object rawRecords;
      if (!root.TryGetValue("records", out rawRecords)
        || !(rawRecords is IEnumerable enumerable)
        || rawRecords is string)
        throw new InvalidDataException("Stage02B canonical JSON records are invalid.");
      var records = new List<NativeStage02BMetricRecord>();
      foreach (object raw in enumerable)
      {
        Dictionary<string, object> wrapper = Object(raw, "records[]");
        Dictionary<string, object> value = Object(
          Required(wrapper, "record"), "record");
        Dictionary<string, object> identity = Object(
          Required(value, "identityContext"), "identityContext");
        if (!Enum.TryParse(GetString(value, "officialCarrierStatus"),
          out NativeOfficialCarrierEvidenceStatus status))
          throw new InvalidDataException(
            "Stage02B official carrier status is invalid.");
        var record = new NativeStage02BMetricRecord
        {
          PropertyId = GetString(value, "propertyId"),
          Identity = GetString(value, "identity"),
          Unit = GetString(value, "unit"),
          Source = GetString(value, "source"),
          RequestedCanonicalValue = GetString(
            value, "requestedCanonicalValue"),
          LastSuccessfulCanonicalValue = GetString(
            value, "lastSuccessfulCanonicalValue"),
          LastAttemptRunId = GetString(value, "lastAttemptRunId"),
          LastSuccessfulRunId = GetString(value, "lastSuccessfulRunId"),
          WriteStatus = GetString(value, "writeStatus"),
          ReadbackStatus = GetString(value, "readbackStatus"),
          ProjectionStatus = GetString(value, "projectionStatus"),
          OfficialCarrierStatus = status,
          OfficialProjectionCarrierId = GetString(
            value, "officialProjectionCarrierId"),
          OfficialCarrierProbeRef = GetString(
            value, "officialCarrierProbeRef"),
          OfficialEvidenceRef = GetString(value, "officialEvidenceRef"),
          IdentityContext = new NativeWorkflowIdentity
          {
            DocumentFingerprint = GetString(
              identity, "documentFingerprint"),
            ModelFileType = GetString(identity, "modelFileType"),
            RulePackageId = GetString(identity, "rulePackageId"),
            RulePackageVersion = GetString(identity, "rulePackageVersion"),
            RulePackageSha256 = GetString(identity, "rulePackageSha256")
          },
          UpdatedUtc = GetString(value, "updatedUtc"),
          ErrorCode = GetString(value, "errorCode"),
          ResultHash = GetString(wrapper, "resultHash")
        };
        if (!NativeStage02BCanonicalizer.VerifyRecord(record))
          throw new InvalidDataException("Stage02B record hash mismatch.");
        records.Add(record);
      }
      NativeStage02BStorageSnapshot snapshot = NativeStage02BCanonicalizer
        .SealSnapshot(records);
      if (!string.Equals(snapshot.CanonicalJson, json, StringComparison.Ordinal))
        throw new InvalidDataException("Stage02B storage JSON is not canonical.");
      return snapshot;
    }

    private static DataStorage FindStorage(Document document)
    {
      DataStorage[] matches = new FilteredElementCollector(document)
        .OfClass(typeof(DataStorage)).Cast<DataStorage>()
        .Where(value => string.Equals(
          value.Name, StorageName, StringComparison.Ordinal)).ToArray();
      if (matches.Length > 1)
        throw new InvalidDataException(
          "Multiple Stage02B DataStorage elements are present.");
      return matches.SingleOrDefault();
    }

    private static Schema GetOrCreateSchema()
    {
      Schema existing = Schema.Lookup(SchemaGuid);
      if (existing != null)
      {
        EnsureSchema(existing);
        return existing;
      }
      var builder = new SchemaBuilder(SchemaGuid);
      builder.SetSchemaName(SchemaName);
      builder.SetDocumentation("湖北省 BIM 报规 Stage02B 项目实际指标");
      builder.SetReadAccessLevel(AccessLevel.Public);
      builder.SetWriteAccessLevel(AccessLevel.Public);
      builder.AddSimpleField("SchemaVersion", typeof(string));
      builder.AddSimpleField("CanonicalJson", typeof(string));
      return builder.Finish();
    }

    private static void EnsureSchema(Schema schema)
    {
      if (!string.Equals(schema.SchemaName, SchemaName, StringComparison.Ordinal))
        throw new InvalidDataException("Stage02B schema name mismatch.");
      foreach (string name in new[] { "SchemaVersion", "CanonicalJson" })
      {
        Field field = schema.GetField(name);
        if (field == null || field.ValueType != typeof(string))
          throw new InvalidDataException(
            "Stage02B schema field mismatch: " + name);
      }
    }

    private static object Required(
      IDictionary<string, object> value,
      string key)
    {
      if (!value.TryGetValue(key, out object raw))
        throw new InvalidDataException("Stage02B JSON member missing: " + key);
      return raw;
    }

    private static Dictionary<string, object> Object(object value, string key)
    {
      if (!(value is Dictionary<string, object> dictionary))
        throw new InvalidDataException(
          "Stage02B JSON member is not an object: " + key);
      return dictionary;
    }

    private static string GetString(
      IDictionary<string, object> value,
      string key)
    {
      object raw = Required(value, key);
      if (!(raw is string text))
        throw new InvalidDataException(
          "Stage02B JSON member is not a string: " + key);
      return text;
    }

    private static string Get(Entity entity, Schema schema, string name)
    {
      return entity.Get<string>(schema.GetField(name)) ?? string.Empty;
    }

    private static void Set(
      Entity entity,
      Schema schema,
      string name,
      string value)
    {
      entity.Set(schema.GetField(name), value ?? string.Empty);
    }
  }
}
