using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace BIMBaoGui.RevitAddin.Workflow
{
  internal static class NativeWorkflowResultStorage
  {
    internal static readonly Guid SchemaGuid = new Guid(
      "9f1de04a-406b-4c15-b693-1f3b7f1ea043");
    internal const string SchemaName = "HBR_NATIVE_WORKFLOW_RESULTS_V1";
    internal const string StorageName = "HBR Native Workflow Results";

    internal static NativeWorkflowResultEnvelope Read(
      Document document,
      string sourceFeature)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      string fieldName = ResolveFieldName(sourceFeature);
      DataStorage storage = FindStorage(document);
      if (storage == null) return null;
      Schema schema = Schema.Lookup(SchemaGuid);
      if (schema == null) return null;
      EnsureSchemaFields(schema);
      Entity entity = storage.GetEntity(schema);
      if (!entity.IsValid()) return null;
      string storedJson = GetString(entity, schema, fieldName);
      if (string.IsNullOrWhiteSpace(storedJson)) return null;
      Dictionary<string, object> wrapper;
      try
      {
        wrapper = new JavaScriptSerializer
        {
          MaxJsonLength = 8 * 1024 * 1024,
          RecursionLimit = 128
        }.Deserialize<Dictionary<string, object>>(storedJson);
      }
      catch (Exception exception)
      {
        throw new InvalidDataException(
          "RVT 中的 workflow result JSON 无法解析。",
          exception);
      }
      object canonicalValue;
      object hashValue;
      if (wrapper == null
        || !wrapper.TryGetValue("canonicalJson", out canonicalValue)
        || !(canonicalValue is string)
        || !wrapper.TryGetValue("resultHash", out hashValue)
        || !(hashValue is string))
      {
        throw new InvalidDataException(
          "RVT 中的 workflow result 缺少 canonicalJson/resultHash。" );
      }
      try
      {
        return NativeWorkflowResultCanonicalizer.ParseCanonical(
          (string)canonicalValue,
          (string)hashValue);
      }
      catch (Exception exception)
      {
        throw new InvalidDataException(
          "RVT 中的 workflow result 不符合公共结果合同。",
          exception);
      }
    }

    internal static void Write(
      Document document,
      NativeWorkflowResultEnvelope envelope)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (envelope == null) throw new ArgumentNullException(nameof(envelope));
      if (!document.IsModifiable)
      {
        throw new InvalidOperationException(
          "NativeWorkflowResultStorage.Write 必须在调用方已开启的 Revit transaction 中执行。" );
      }
      string fieldName = ResolveFieldName(envelope.SourceFeature);
      if (!string.Equals(
          envelope.ResultHash,
          NativeWorkflowResultCanonicalizer.ComputeResultHash(envelope),
          StringComparison.Ordinal)
        || !string.Equals(
          envelope.CanonicalJson,
          NativeWorkflowResultCanonicalizer.SerializeCanonical(envelope),
          StringComparison.Ordinal))
      {
        throw new InvalidDataException(
          "拒绝写入 canonical JSON 或 result hash 不匹配的 workflow result。" );
      }

      Schema schema = GetOrCreateSchema();
      DataStorage storage = FindStorage(document) ?? DataStorage.Create(document);
      storage.Name = StorageName;
      Entity existing = storage.GetEntity(schema);
      var entity = new Entity(schema);
      foreach (string name in FieldNames)
      {
        SetString(
          entity,
          schema,
          name,
          existing.IsValid() ? GetString(existing, schema, name) : string.Empty);
      }
      SetString(entity, schema, fieldName, SerializeEnvelope(envelope));
      storage.SetEntity(entity);
    }

    private static readonly string[] FieldNames =
    {
      "Stage01Json",
      "Stage02AJson",
      "Stage02BJson"
    };

    private static string ResolveFieldName(string sourceFeature)
    {
      switch ((sourceFeature ?? string.Empty).Trim().ToUpperInvariant())
      {
        case "STAGE01": return "Stage01Json";
        case "STAGE02A": return "Stage02AJson";
        case "STAGE02B": return "Stage02BJson";
        default:
          throw new ArgumentException(
            "Workflow source feature must be STAGE01, STAGE02A, or STAGE02B.",
            nameof(sourceFeature));
      }
    }

    private static string SerializeEnvelope(NativeWorkflowResultEnvelope envelope)
    {
      var builder = new StringBuilder();
      builder.Append("{\"canonicalJson\":\"");
      builder.Append(NativeWorkflowResultCanonicalizer.Escape(
        envelope.CanonicalJson));
      builder.Append("\",\"resultHash\":\"");
      builder.Append(NativeWorkflowResultCanonicalizer.Escape(
        envelope.ResultHash));
      builder.Append("\"}");
      return builder.ToString();
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
          "RVT 中存在多个 HBR Native Workflow Results DataStorage，拒绝猜测使用哪一个。" );
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
      builder.SetSchemaName(SchemaName);
      builder.SetDocumentation("湖北省 BIM 报规跨阶段 workflow 结果记录");
      builder.SetReadAccessLevel(AccessLevel.Public);
      builder.SetWriteAccessLevel(AccessLevel.Public);
      foreach (string fieldName in FieldNames)
        builder.AddSimpleField(fieldName, typeof(string));
      return builder.Finish();
    }

    private static void EnsureSchemaFields(Schema schema)
    {
      foreach (string fieldName in FieldNames)
      {
        Field field = schema.GetField(fieldName);
        if (field == null || field.ValueType != typeof(string))
        {
          throw new InvalidDataException(
            "现有 workflow result Schema 与字段合同冲突：" + fieldName);
        }
      }
    }

    private static string GetString(
      Entity entity,
      Schema schema,
      string fieldName)
    {
      Field field = schema.GetField(fieldName);
      return field == null ? string.Empty : entity.Get<string>(field) ?? string.Empty;
    }

    private static void SetString(
      Entity entity,
      Schema schema,
      string fieldName,
      string value)
    {
      Field field = schema.GetField(fieldName);
      if (field == null)
        throw new InvalidDataException("Workflow result Schema 缺少字段：" + fieldName);
      entity.Set<string>(field, value ?? string.Empty);
    }
  }
}
