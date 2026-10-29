using System;
using System.IO;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;

internal static class NativeSettingsCases
{
    internal static void Run(Action<string, Action> test)
    {
        test("new Web workspace restores RVT settings without touching Local preferences", () => {
            var settings = new ModelSettings { RuleProfileId = "model-profile", LevelsConfirmed = true, BuildingLevelUniqueIds = new[] { "ground" }, TopBoundaryLevelUniqueId = "roof", Underground = new UndergroundSelection { Confirmed = true, GroundElevationM = 0, BaseLevelUniqueId = "ground", LevelUniqueIds = new[] { "b1" } } };
            settings.CommonValues["建筑区域|系数"] = "0"; var record = Record(settings); var workspace = new SavedWorkspace();
            NativeSettingsState.Observe(workspace, record);
            Require(NativeSettingsState.Same(workspace.Settings, settings) && !workspace.SettingsDirty && !workspace.SettingsConflict && workspace.LastModelSettingsHash == record.Hash, "Fresh state did not restore the model configuration.");
        });
        test("settings draft survives local restart and requires a choice when the model settings changed", () => {
            var original = Record(new ModelSettings { RuleProfileId = "original" }); var changed = Record(new ModelSettings { RuleProfileId = "another-editor" });
            var key = new string('c', 64); var workspace = new SavedWorkspace { DocumentKey = key }; NativeSettingsState.Observe(workspace, original);
            var draft = new ModelSettings { RuleProfileId = "local-draft" }; draft.CommonValues["test|zero"] = "0"; draft.CommonValues["test|blank"] = ""; NativeSettingsState.Edit(workspace, draft);
            var store = new WorkspaceStore(Path.Combine(Environment.CurrentDirectory, ".local", "settings-state-tests", Guid.NewGuid().ToString("N"))); store.Save(workspace); workspace = store.Load(key);
            NativeSettingsState.Observe(workspace, changed);
            Require(workspace.SettingsDirty && workspace.SettingsConflict && workspace.Settings.RuleProfileId == "local-draft" && workspace.LastModelSettingsHash == original.Hash, "Model refresh overwrote an uncommitted local draft.");
            NativeSettingsState.Resolve(workspace, changed, true);
            Require(workspace.SettingsDirty && !workspace.SettingsConflict && workspace.Settings.RuleProfileId == "local-draft" && workspace.LastModelSettingsHash == changed.Hash, "Keeping the local draft did not adopt the current model precondition.");
            NativeSettingsState.Resolve(workspace, changed, false);
            Require(!workspace.SettingsDirty && workspace.Settings.RuleProfileId == "another-editor", "Adopting model settings did not replace the local draft.");
        });
        test("corrupt RVT settings and old local-only workspaces never erase draft settings", () => {
            var workspace = new SavedWorkspace { Settings = new ModelSettings { RuleProfileId = "legacy-local" }, LastCheck = new CheckResult() };
            NativeSettingsState.Observe(workspace, NativeSettingsState.Record(new ProjectDataRecord[0]));
            Require(workspace.SettingsDirty && workspace.Settings.RuleProfileId == "legacy-local", "Legacy local settings were treated as disposable defaults.");
            var bad = Record(new ModelSettings()); bad.Json = "{broken"; bad.Hash = Canonical.Hash(bad.Json); var failed = false;
            try { NativeSettingsState.Observe(workspace, bad); } catch (InvalidOperationException) { failed = true; }
            Require(failed && workspace.SettingsDirty && workspace.Settings.RuleProfileId == "legacy-local", "Corrupt model settings overwrote local state.");
        });
    }
    private static ProjectDataRecord Record(ModelSettings settings) { var json = JsonWire.Serialize(settings); return new ProjectDataRecord { SchemaId = ModelStorageSchemas.WebSettingsId, SchemaName = ModelStorageSchemas.WebSettingsName, Json = json, Hash = Canonical.Hash(json) }; }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
