using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BIMBaoGui.Stage01.Stage02;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class Stage02MetadataStorage
  {
    private static readonly Guid SchemaGuid =
      new Guid("af183ed4-1631-48c0-b7e5-bfbda5a72845");
    private const string StorageName = "HBR_BIMBAOGUI_STAGE02_AUDIT";

    internal static string ReadSavedRole(
      Document document,
      string uniqueId)
    {
      if (document == null || string.IsNullOrWhiteSpace(uniqueId))
        return string.Empty;
      Schema schema = Schema.Lookup(SchemaGuid);
      if (schema == null) return string.Empty;
      return FindAudits(document, schema, uniqueId)
        .OrderByDescending(item => item.AuditUtc, StringComparer.Ordinal)
        .Select(item => item.RoleId)
        .FirstOrDefault() ?? string.Empty;
    }

    internal static void WriteAuditOnly(
      Document document,
      Stage02Preview preview)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (preview == null) throw new ArgumentNullException(nameof(preview));
      Schema schema = GetOrCreateSchema();
      string auditUtc = DateTime.UtcNow.ToString(
        "O",
        CultureInfo.InvariantCulture);
      foreach (Stage02MatchedElement matched in preview.Elements)
      {
        DataStorage storage = FindStorage(
          document,
          schema,
          matched.Element.UniqueId) ?? DataStorage.Create(document);
        storage.Name = StorageName;
        var entity = new Entity(schema);
        Set(entity, schema, "RoleId", matched.RoleId);
        Set(entity, schema, "RulePackageId", preview.RulePackageId);
        Set(entity, schema, "RulePackageVersion", preview.RulePackageVersion);
        Set(entity, schema, "RulePackageSha256", preview.RulePackageSha256);
        Set(entity, schema, "PreviewHash", preview.PreviewHash);
        Set(entity, schema, "UniqueId", matched.Element.UniqueId);
        Set(
          entity,
          schema,
          "PropertyIds",
          string.Join("\n", matched.Operations
            .Select(operation => operation.PropertyId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)));
        Set(entity, schema, "AuditUtc", auditUtc);
        storage.SetEntity(entity);
      }
    }

    private static IEnumerable<AuditRecord> FindAudits(
      Document document,
      Schema schema,
      string uniqueId)
    {
      foreach (DataStorage storage in new FilteredElementCollector(document)
        .OfClass(typeof(DataStorage))
        .Cast<DataStorage>())
      {
        if (!string.Equals(storage.Name, StorageName, StringComparison.Ordinal))
          continue;
        Entity entity = storage.GetEntity(schema);
        if (!entity.IsValid()) continue;
        if (!string.Equals(
          Get(entity, schema, "UniqueId"),
          uniqueId,
          StringComparison.Ordinal))
          continue;
        yield return new AuditRecord
        {
          Storage = storage,
          RoleId = Get(entity, schema, "RoleId"),
          AuditUtc = Get(entity, schema, "AuditUtc")
        };
      }
    }

    private static DataStorage FindStorage(
      Document document,
      Schema schema,
      string uniqueId)
    {
      return FindAudits(document, schema, uniqueId)
        .OrderByDescending(item => item.AuditUtc, StringComparer.Ordinal)
        .Select(item => item.Storage)
        .FirstOrDefault();
    }

    private static Schema GetOrCreateSchema()
    {
      Schema existing = Schema.Lookup(SchemaGuid);
      if (existing != null) return existing;
      var builder = new SchemaBuilder(SchemaGuid);
      builder.SetSchemaName("HBR_BIMBaoGui_Stage02_Audit");
      builder.SetDocumentation(
        "湖北省 BIM 报规 Stage02 规则、载体与确认身份审计记录");
      builder.SetReadAccessLevel(AccessLevel.Public);
      builder.SetWriteAccessLevel(AccessLevel.Public);
      builder.AddSimpleField("RoleId", typeof(string));
      builder.AddSimpleField("RulePackageId", typeof(string));
      builder.AddSimpleField("RulePackageVersion", typeof(string));
      builder.AddSimpleField("RulePackageSha256", typeof(string));
      builder.AddSimpleField("PreviewHash", typeof(string));
      builder.AddSimpleField("UniqueId", typeof(string));
      builder.AddSimpleField("PropertyIds", typeof(string));
      builder.AddSimpleField("AuditUtc", typeof(string));
      return builder.Finish();
    }

    private static string Get(Entity entity, Schema schema, string name)
    {
      Field field = schema.GetField(name);
      return field == null
        ? string.Empty
        : entity.Get<string>(field) ?? string.Empty;
    }

    private static void Set(
      Entity entity,
      Schema schema,
      string name,
      string value)
    {
      Field field = schema.GetField(name);
      if (field == null)
        throw new InvalidOperationException("Stage02 审计 schema 字段缺失。");
      entity.Set(field, value ?? string.Empty);
    }

    private sealed class AuditRecord
    {
      internal DataStorage Storage { get; set; }
      internal string RoleId { get; set; }
      internal string AuditUtc { get; set; }
    }
  }
}
