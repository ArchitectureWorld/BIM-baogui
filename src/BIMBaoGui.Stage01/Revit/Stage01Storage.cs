using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class Stage01Storage
  {
    private static readonly Guid SchemaGuid = new Guid("d17f35b6-f42a-4d8f-9592-c7639b8bd320");
    private const string StorageName = "HBR_BIMBAOGUI_STAGE01";

    public static StoredInitialization Read(Document document)
    {
      if (document == null) return null;
      Schema schema = Schema.Lookup(SchemaGuid);
      if (schema == null) return null;
      DataStorage storage = FindStorage(document);
      if (storage == null) return null;
      Entity entity = storage.GetEntity(schema);
      if (!entity.IsValid()) return null;
      return new StoredInitialization
      {
        PayloadJson = GetString(entity, schema, "PayloadJson"),
        PayloadHash = GetString(entity, schema, "PayloadHash"),
        FileGuid = GetString(entity, schema, "FileGuid"),
        WorkflowVersion = GetString(entity, schema, "WorkflowVersion"),
        InitializedUtc = GetString(entity, schema, "InitializedUtc")
      };
    }

    public static void Write(Document document, StoredInitialization value)
    {
      Schema schema = GetOrCreateSchema();
      DataStorage storage = FindStorage(document) ?? DataStorage.Create(document);
      storage.Name = StorageName;
      var entity = new Entity(schema);
      SetString(entity, schema, "PayloadJson", value.PayloadJson);
      SetString(entity, schema, "PayloadHash", value.PayloadHash);
      SetString(entity, schema, "FileGuid", value.FileGuid);
      SetString(entity, schema, "WorkflowVersion", value.WorkflowVersion);
      SetString(entity, schema, "InitializedUtc", value.InitializedUtc);
      storage.SetEntity(entity);

      Stage01OfficialHifcProjectionService.WriteAndVerify(document, value.PayloadJson);
    }

    private static DataStorage FindStorage(Document document)
    {
      return new FilteredElementCollector(document)
        .OfClass(typeof(DataStorage))
        .Cast<DataStorage>()
        .FirstOrDefault(storage => string.Equals(storage.Name, StorageName, StringComparison.Ordinal));
    }

    private static Schema GetOrCreateSchema()
    {
      Schema existing = Schema.Lookup(SchemaGuid);
      if (existing != null) return existing;
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

    private static string GetString(Entity entity, Schema schema, string name)
    {
      Field field = schema.GetField(name);
      return field == null ? string.Empty : entity.Get<string>(field) ?? string.Empty;
    }

    private static void SetString(Entity entity, Schema schema, string name, string value)
    {
      Field field = schema.GetField(name);
      if (field == null) throw new InvalidOperationException("Extensible Storage 字段不存在：" + name);
      entity.Set<string>(field, value ?? string.Empty);
    }
  }
}
