using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02SemanticAssignmentStorage
  {
    internal static readonly Guid SchemaGuid = new Guid(
      "6f0ab4a7-0e0f-46d9-a31e-1f7615a4f2e3");
    internal const string StorageName = "HBR_BIMBAOGUI_STAGE02_ASSIGNMENTS";

    internal static NativeStage02SemanticAssignmentStorageSnapshot Read(
      Document document)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      DataStorage storage = FindStorage(document);
      if (storage == null) return null;
      Schema schema = Schema.Lookup(SchemaGuid);
      if (schema == null)
      {
        return new NativeStage02SemanticAssignmentStorageSnapshot
        {
          SchemaVersion = string.Empty
        };
      }
      EnsureSchemaFields(schema);
      Entity entity = storage.GetEntity(schema);
      if (!entity.IsValid())
      {
        return new NativeStage02SemanticAssignmentStorageSnapshot
        {
          SchemaVersion = string.Empty
        };
      }

      string canonicalJson = GetString(entity, schema, "CanonicalJson");
      return new NativeStage02SemanticAssignmentStorageSnapshot
      {
        SchemaVersion = GetString(entity, schema, "SchemaVersion"),
        RulePackageId = GetString(entity, schema, "RulePackageId"),
        RulePackageVersion = GetString(entity, schema, "RulePackageVersion"),
        CanonicalJson = canonicalJson,
        PayloadSha256 = GetString(entity, schema, "PayloadSha256"),
        UpdatedUtc = GetString(entity, schema, "UpdatedUtc"),
        Payload = NativeStage02SemanticAssignmentCodec.Parse(canonicalJson)
      };
    }

    internal static void Write(
      Document document,
      NativeStage02SemanticAssignmentStorageSnapshot snapshot)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
      if (string.IsNullOrWhiteSpace(snapshot.SchemaVersion)
        || string.IsNullOrWhiteSpace(snapshot.RulePackageId)
        || string.IsNullOrWhiteSpace(snapshot.RulePackageVersion)
        || string.IsNullOrWhiteSpace(snapshot.CanonicalJson)
        || string.IsNullOrWhiteSpace(snapshot.PayloadSha256)
        || string.IsNullOrWhiteSpace(snapshot.UpdatedUtc))
      {
        throw new InvalidDataException(
          "不能写入不完整的 Stage02 语义角色存储记录。" );
      }

      Schema schema = GetOrCreateSchema();
      DataStorage storage = FindStorage(document) ?? DataStorage.Create(document);
      storage.Name = StorageName;
      var entity = new Entity(schema);
      SetString(entity, schema, "SchemaVersion", snapshot.SchemaVersion);
      SetString(entity, schema, "RulePackageId", snapshot.RulePackageId);
      SetString(entity, schema, "RulePackageVersion", snapshot.RulePackageVersion);
      SetString(entity, schema, "CanonicalJson", snapshot.CanonicalJson);
      SetString(entity, schema, "PayloadSha256", snapshot.PayloadSha256);
      SetString(entity, schema, "UpdatedUtc", snapshot.UpdatedUtc);
      storage.SetEntity(entity);
    }

    private static DataStorage FindStorage(Document document)
    {
      DataStorage[] matches = new FilteredElementCollector(document)
        .OfClass(typeof(DataStorage))
        .Cast<DataStorage>()
        .Where(storage => string.Equals(
          storage.Name,
          StorageName,
          StringComparison.Ordinal))
        .ToArray();
      if (matches.Length > 1)
      {
        throw new InvalidDataException(
          "RVT 中存在多个 HBR Stage02 Assignment DataStorage，拒绝猜测使用哪一个。" );
      }
      return matches.SingleOrDefault();
    }

    private static Schema GetOrCreateSchema()
    {
      Schema existing = Schema.Lookup(SchemaGuid);
      if (existing != null)
      {
        EnsureSchemaFields(existing);
        return existing;
      }

      var builder = new SchemaBuilder(SchemaGuid);
      builder.SetSchemaName("HBR_BIMBaoGui_Stage02_Assignments");
      builder.SetDocumentation("湖北省 BIM 报规 Stage02 构件语义角色记录");
      builder.SetReadAccessLevel(AccessLevel.Public);
      builder.SetWriteAccessLevel(AccessLevel.Public);
      builder.AddSimpleField("SchemaVersion", typeof(string));
      builder.AddSimpleField("RulePackageId", typeof(string));
      builder.AddSimpleField("RulePackageVersion", typeof(string));
      builder.AddSimpleField("CanonicalJson", typeof(string));
      builder.AddSimpleField("PayloadSha256", typeof(string));
      builder.AddSimpleField("UpdatedUtc", typeof(string));
      return builder.Finish();
    }

    private static void EnsureSchemaFields(Schema schema)
    {
      string[] expected =
      {
        "SchemaVersion",
        "RulePackageId",
        "RulePackageVersion",
        "CanonicalJson",
        "PayloadSha256",
        "UpdatedUtc"
      };
      foreach (string fieldName in expected)
      {
        Field field = schema.GetField(fieldName);
        if (field == null || field.ValueType != typeof(string))
        {
          throw new InvalidDataException(
            "现有 Stage02 Assignment Schema 与字段合同冲突："
            + fieldName);
        }
      }
    }

    private static string GetString(
      Entity entity,
      Schema schema,
      string fieldName)
    {
      Field field = schema.GetField(fieldName);
      return field == null
        ? string.Empty
        : entity.Get<string>(field) ?? string.Empty;
    }

    private static void SetString(
      Entity entity,
      Schema schema,
      string fieldName,
      string value)
    {
      Field field = schema.GetField(fieldName);
      if (field == null)
      {
        throw new InvalidDataException(
          "Stage02 Assignment Extensible Storage 缺少字段：" + fieldName);
      }
      entity.Set<string>(field, value ?? string.Empty);
    }
  }
}
