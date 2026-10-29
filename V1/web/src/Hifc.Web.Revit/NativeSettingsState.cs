using System;
using System.Linq;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public static class NativeSettingsState
    {
        public static ProjectDataRecord Record(ProjectDataRecord[] records)
        {
            var found = (records ?? new ProjectDataRecord[0]).Where(r => r.SchemaId == ModelStorageSchemas.WebSettingsId).ToArray();
            if (found.Length > 1) throw new InvalidOperationException("模型中有重复的 Web 设置记录。");
            return found.SingleOrDefault() ?? new ProjectDataRecord { SchemaId = ModelStorageSchemas.WebSettingsId, SchemaName = ModelStorageSchemas.WebSettingsName, Hash = Canonical.Hash("") };
        }
        public static ModelSettings Decode(ProjectDataRecord record)
        {
            if (record == null || record.SchemaId != ModelStorageSchemas.WebSettingsId || record.SchemaName != ModelStorageSchemas.WebSettingsName || record.Hash != Canonical.Hash(record.Json ?? ""))
                throw new InvalidOperationException("模型内 Web 设置的身份或散列无效，未覆盖本机设置。");
            if (record.Json == null) return new ModelSettings();
            ModelSettings settings;
            try { settings = JsonWire.Deserialize<ModelSettings>(record.Json); }
            catch (Newtonsoft.Json.JsonException ex) { throw new InvalidOperationException("模型内 Web 设置格式损坏，原记录与本机草稿均保留。", ex); }
            if (settings == null || settings.Roles == null || settings.Roles.Any(r => r == null) || settings.CommonValues == null || settings.BuildingLevelUniqueIds == null
                || settings.Underground == null || settings.Underground.LevelUniqueIds == null)
                throw new InvalidOperationException("模型内 Web 设置不完整，未覆盖本机设置。");
            return settings;
        }
        public static bool Same(ModelSettings left, ModelSettings right) => Canonical.ConfigurationHash(left, new ProjectDataRecord[0]) == Canonical.ConfigurationHash(right, new ProjectDataRecord[0]);
        public static void Observe(SavedWorkspace workspace, ProjectDataRecord record)
        {
            var persisted = Decode(record);
            // Workspaces from the former local-only implementation have no baseline.
            // Retain their settings as a draft instead of treating them as committed.
            if (workspace.LastModelSettingsHash == null && (workspace.LastCheck != null || !Same(workspace.Settings, new ModelSettings()))) workspace.SettingsDirty = true;
            if (Same(workspace.Settings, persisted))
            {
                workspace.Settings = persisted; workspace.SettingsDirty = false; workspace.SettingsConflict = false; workspace.LastModelSettingsHash = record.Hash; return;
            }
            if (workspace.SettingsDirty)
            {
                workspace.SettingsConflict = workspace.LastModelSettingsHash == null ? record.Json != null : workspace.LastModelSettingsHash != record.Hash;
                if (workspace.LastModelSettingsHash == null && record.Json == null) workspace.LastModelSettingsHash = record.Hash;
                return;
            }
            workspace.Settings = persisted; workspace.LastModelSettingsHash = record.Hash; workspace.SettingsConflict = false;
        }
        public static void Edit(SavedWorkspace workspace, ModelSettings settings)
        {
            workspace.Settings = JsonWire.Deserialize<ModelSettings>(JsonWire.Serialize(settings ?? new ModelSettings())); workspace.SettingsDirty = true;
        }
        public static void Resolve(SavedWorkspace workspace, ProjectDataRecord current, bool keepLocal)
        {
            var persisted = Decode(current);
            if (!keepLocal) workspace.Settings = persisted;
            workspace.LastModelSettingsHash = current.Hash; workspace.SettingsConflict = false; workspace.SettingsDirty = !Same(workspace.Settings, persisted);
        }
        public static void Committed(SavedWorkspace workspace, NativeDataWrite write)
        {
            if (write == null) return;
            var persisted = Decode(new ProjectDataRecord { SchemaId = write.SchemaId, SchemaName = write.SchemaName, Json = write.Json, Hash = write.Hash });
            if (!Same(workspace.Settings, persisted)) throw new InvalidOperationException("模型已写入，但设置与当前草稿不同，请重新检查，原草稿仍保留。");
            workspace.SettingsDirty = false; workspace.SettingsConflict = false; workspace.LastModelSettingsHash = write.Hash;
        }
    }
}
