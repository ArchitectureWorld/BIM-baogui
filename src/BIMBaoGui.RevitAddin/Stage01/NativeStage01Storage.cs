using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal static class NativeStage01Storage
  {
    internal static readonly Guid SchemaGuid = new Guid(
      "d17f35b6-f42a-4d8f-9592-c7639b8bd320");
    internal const string StorageName = "HBR_BIMBAOGUI_STAGE01";

    internal static NativeStoredInitialization Read(Document document)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      DataStorage storage = FindStorage(document);
      if (storage == null)
        return new NativeStoredInitialization { HasRecord = false };

      Schema schema = Schema.Lookup(SchemaGuid);
      if (schema == null)
        return new NativeStoredInitialization { HasRecord = true };
      Entity entity = storage.GetEntity(schema);
      if (!entity.IsValid())
        return new NativeStoredInitialization { HasRecord = true };

      return new NativeStoredInitialization
      {
        HasRecord = true,
        PayloadJson = GetString(entity, schema, "PayloadJson"),
        PayloadHash = GetString(entity, schema, "PayloadHash"),
        FileGuid = GetString(entity, schema, "FileGuid"),
        WorkflowVersion = GetString(entity, schema, "WorkflowVersion"),
        InitializedUtc = GetString(entity, schema, "InitializedUtc")
      };
    }

    internal static void Write(
      Document document,
      NativeStoredInitialization record)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (record == null) throw new ArgumentNullException(nameof(record));
      if (string.IsNullOrWhiteSpace(record.PayloadJson)
        || string.IsNullOrWhiteSpace(record.PayloadHash)
        || string.IsNullOrWhiteSpace(record.FileGuid)
        || string.IsNullOrWhiteSpace(record.WorkflowVersion)
        || string.IsNullOrWhiteSpace(record.InitializedUtc))
      {
        throw new InvalidDataException(
          "不能写入不完整的 Stage01 初始化存储记录。");
      }

      Schema schema = GetOrCreateSchema();
      DataStorage storage = FindStorage(document) ?? DataStorage.Create(document);
      storage.Name = StorageName;
      var entity = new Entity(schema);
      SetString(entity, schema, "PayloadJson", record.PayloadJson);
      SetString(entity, schema, "PayloadHash", record.PayloadHash);
      SetString(entity, schema, "FileGuid", record.FileGuid);
      SetString(entity, schema, "WorkflowVersion", record.WorkflowVersion);
      SetString(entity, schema, "InitializedUtc", record.InitializedUtc);
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
          "RVT 中存在多个 HBR Stage01 DataStorage，拒绝猜测使用哪一个。" );
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
      builder.SetSchemaName("HBR_BIMBaoGui_Stage01");
      builder.SetDocumentation("湖北省 BIM 报规单文件初始化记录");
      builder.SetReadAccessLevel(AccessLevel.Public);
      builder.SetWriteAccessLevel(AccessLevel.Public);
      builder.AddSimpleField("PayloadJson", typeof(string));
      builder.AddSimpleField("PayloadHash", typeof(string));
      builder.AddSimpleField("FileGuid", typeof(string));
      builder.AddSimpleField("WorkflowVersion", typeof(string));
      builder.AddSimpleField("InitializedUtc", typeof(string));
      return builder.Finish();
    }

    private static void EnsureSchemaFields(Schema schema)
    {
      string[] expected =
      {
        "PayloadJson",
        "PayloadHash",
        "FileGuid",
        "WorkflowVersion",
        "InitializedUtc"
      };
      foreach (string fieldName in expected)
      {
        Field field = schema.GetField(fieldName);
        if (field == null || field.ValueType != typeof(string))
        {
          throw new InvalidDataException(
            "现有 Stage01 Extensible Storage Schema 与字段合同冲突："
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
        throw new InvalidDataException(
          "Stage01 Extensible Storage 缺少字段：" + fieldName);
      entity.Set<string>(field, value ?? string.Empty);
    }
  }
}
