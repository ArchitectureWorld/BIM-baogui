using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02ManualReviewStorage
  {
    internal static readonly Guid SchemaGuid = new Guid(
      "ea41c42d-d6d7-4ce3-8793-4d5827303f11");
    internal const string SchemaVersion = "HBR_NATIVE_GEOMETRY_REVIEW_V1";
    internal const string StorageName = "HBR_BIMBAOGUI_STAGE02_GEOMETRY_REVIEWS";

    internal static IReadOnlyList<NativeStage02ManualReviewRecord> Read(
      Document document)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      DataStorage storage = FindStorage(document);
      if (storage == null) return Array.Empty<NativeStage02ManualReviewRecord>();
      Schema schema = Schema.Lookup(SchemaGuid);
      if (schema == null) return Array.Empty<NativeStage02ManualReviewRecord>();
      EnsureSchema(schema);
      Entity entity = storage.GetEntity(schema);
      if (!entity.IsValid()) return Array.Empty<NativeStage02ManualReviewRecord>();
      string version = Get(entity, schema, "SchemaVersion");
      if (!string.Equals(version, SchemaVersion, StringComparison.Ordinal))
        throw new InvalidDataException("Stage02 geometry review schema version 无效。");
      string json = Get(entity, schema, "RecordsJson");
      ReviewDto[] values;
      try
      {
        values = new JavaScriptSerializer
        {
          MaxJsonLength = 8 * 1024 * 1024,
          RecursionLimit = 128
        }.Deserialize<ReviewDto[]>(json) ?? Array.Empty<ReviewDto>();
      }
      catch (Exception exception)
      {
        throw new InvalidDataException(
          "Stage02 geometry review records 无法解析。",
          exception);
      }
      var records = new List<NativeStage02ManualReviewRecord>();
      foreach (ReviewDto value in values.Where(item => item != null))
      {
        NativeStage02ManualReviewRecord raw = FromDto(value);
        NativeStage02ManualReviewRecord sealedRecord =
          NativeStage02ManualReviewPolicy.Seal(raw);
        if (!string.Equals(
          raw.RecordHash,
          sealedRecord.RecordHash,
          StringComparison.Ordinal))
          throw new InvalidDataException("Stage02 geometry review record hash 无效。");
        records.Add(sealedRecord);
      }
      return records
        .GroupBy(value => value.CheckId, StringComparer.Ordinal)
        .Select(group => group.OrderByDescending(
          value => value.ReviewedUtc,
          StringComparer.Ordinal).First())
        .OrderBy(value => value.CheckId, StringComparer.Ordinal)
        .ToArray();
    }

    internal static NativeStage02ManualReviewRecord Write(
      Document document,
      NativeStage02ManualReviewRecord draft)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (!document.IsModifiable)
        throw new InvalidOperationException(
          "NativeStage02ManualReviewStorage.Write 必须在短 Revit transaction 中执行。");
      NativeStage02ManualReviewRecord sealedRecord =
        NativeStage02ManualReviewPolicy.Seal(draft);
      var records = Read(document)
        .Where(value => !string.Equals(
          value.CheckId,
          sealedRecord.CheckId,
          StringComparison.Ordinal))
        .Concat(new[] { sealedRecord })
        .OrderBy(value => value.CheckId, StringComparer.Ordinal)
        .Select(ToDto)
        .ToArray();
      string json = new JavaScriptSerializer
      {
        MaxJsonLength = 8 * 1024 * 1024,
        RecursionLimit = 128
      }.Serialize(records);
      Schema schema = GetOrCreateSchema();
      DataStorage storage = FindStorage(document) ?? DataStorage.Create(document);
      storage.Name = StorageName;
      var entity = new Entity(schema);
      Set(entity, schema, "SchemaVersion", SchemaVersion);
      Set(entity, schema, "RecordsJson", json);
      storage.SetEntity(entity);

      NativeStage02ManualReviewRecord readback = Read(document)
        .Single(value => value.CheckId == sealedRecord.CheckId);
      if (!string.Equals(
        readback.RecordHash,
        sealedRecord.RecordHash,
        StringComparison.Ordinal))
        throw new InvalidDataException("Stage02 geometry review readback 失败。");
      return readback;
    }

    private static DataStorage FindStorage(Document document)
    {
      DataStorage[] matches = new FilteredElementCollector(document)
        .OfClass(typeof(DataStorage))
        .Cast<DataStorage>()
        .Where(value => string.Equals(
          value.Name,
          StorageName,
          StringComparison.Ordinal))
        .ToArray();
      if (matches.Length > 1)
        throw new InvalidDataException("RVT 中存在多个 Stage02 geometry review storage。");
      return matches.SingleOrDefault();
    }

    private static Schema GetOrCreateSchema()
    {
      Schema schema = Schema.Lookup(SchemaGuid);
      if (schema != null)
      {
        EnsureSchema(schema);
        return schema;
      }
      var builder = new SchemaBuilder(SchemaGuid);
      builder.SetSchemaName("HBR_BIMBaoGui_Stage02_Geometry_Reviews");
      builder.SetDocumentation("湖北省 BIM 报规 Stage02A 当前快照几何人工复核");
      builder.SetReadAccessLevel(AccessLevel.Public);
      builder.SetWriteAccessLevel(AccessLevel.Public);
      builder.AddSimpleField("SchemaVersion", typeof(string));
      builder.AddSimpleField("RecordsJson", typeof(string));
      return builder.Finish();
    }

    private static void EnsureSchema(Schema schema)
    {
      foreach (string name in new[] { "SchemaVersion", "RecordsJson" })
      {
        Field field = schema.GetField(name);
        if (field == null || field.ValueType != typeof(string))
          throw new InvalidDataException(
            "Stage02 geometry review schema field 冲突：" + name);
      }
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
      entity.Set<string>(schema.GetField(name), value ?? string.Empty);
    }

    private static ReviewDto ToDto(NativeStage02ManualReviewRecord value)
    {
      return new ReviewDto
      {
        schemaVersion = value.SchemaVersion,
        checkId = value.CheckId,
        ruleText = value.RuleText,
        documentFingerprint = value.DocumentFingerprint,
        rulePackageSha256 = value.RulePackageSha256,
        elementUniqueIds = value.ElementUniqueIds.ToArray(),
        elementSnapshotHashes = value.ElementSnapshotHashes.ToArray(),
        geometryEvidenceHashes = value.GeometryEvidenceHashes.ToArray(),
        decision = value.Decision,
        reviewer = value.Reviewer,
        basis = value.Basis,
        reviewedUtc = value.ReviewedUtc,
        recordHash = value.RecordHash
      };
    }

    private static NativeStage02ManualReviewRecord FromDto(ReviewDto value)
    {
      return new NativeStage02ManualReviewRecord
      {
        SchemaVersion = value.schemaVersion ?? string.Empty,
        CheckId = value.checkId ?? string.Empty,
        RuleText = value.ruleText ?? string.Empty,
        DocumentFingerprint = value.documentFingerprint ?? string.Empty,
        RulePackageSha256 = value.rulePackageSha256 ?? string.Empty,
        ElementUniqueIds = value.elementUniqueIds ?? Array.Empty<string>(),
        ElementSnapshotHashes = value.elementSnapshotHashes ?? Array.Empty<string>(),
        GeometryEvidenceHashes = value.geometryEvidenceHashes ?? Array.Empty<string>(),
        Decision = value.decision ?? string.Empty,
        Reviewer = value.reviewer ?? string.Empty,
        Basis = value.basis ?? string.Empty,
        ReviewedUtc = value.reviewedUtc ?? string.Empty,
        RecordHash = value.recordHash ?? string.Empty
      };
    }

    private sealed class ReviewDto
    {
      public string schemaVersion { get; set; }
      public string checkId { get; set; }
      public string ruleText { get; set; }
      public string documentFingerprint { get; set; }
      public string rulePackageSha256 { get; set; }
      public string[] elementUniqueIds { get; set; }
      public string[] elementSnapshotHashes { get; set; }
      public string[] geometryEvidenceHashes { get; set; }
      public string decision { get; set; }
      public string reviewer { get; set; }
      public string basis { get; set; }
      public string reviewedUtc { get; set; }
      public string recordHash { get; set; }
    }
  }
}
