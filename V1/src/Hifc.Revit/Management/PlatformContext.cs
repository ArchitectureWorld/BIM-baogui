using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    // Persisted settings use the existing management key. Live settings follow the
    // native document identity, including unsaved projects and Save As operations.
    internal static class PlatformContext
    {
        private sealed class Entry
        {
            internal Document Document;
            internal PlanningSettings Settings;
            internal string Stage;
        }
        private static readonly List<Entry> Current = new List<Entry>();
        private static Entry Find(Document doc)
        {
            Current.RemoveAll(e => !e.Document.IsValidObject);
            // Revit can create different managed wrappers for the same native
            // document; do not use wrapper hashes as live configuration identity.
            return Current.FirstOrDefault(e => e.Document.Equals(doc));
        }
        private static Entry Get(Document doc)
        {
            var entry = Find(doc);
            if (entry != null) return entry;
            var key = ModelReader.Key(doc);
            var store = new ManagementStateStore(Path.Combine(RuntimePaths.OutputDirectory(), "management-profiles"), key);
            var state = store.Load();
            entry = new Entry { Document=doc, Settings=state.Planning ?? new PlanningSettings(), Stage=state.Stage };
            Current.Add(entry);
            return entry;
        }
        internal static PlanningSettings Settings(Document doc) => Get(doc).Settings;
        internal static string Stage(Document doc) => Get(doc).Stage;
        internal static void Set(Document doc, PlanningSettings settings)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
            var value = serializer.Deserialize<PlanningSettings>(serializer.Serialize(settings ?? new PlanningSettings()));
            Get(doc).Settings = value;
        }
        internal static void SetStage(Document doc, string stage) => Get(doc).Stage = stage;
        internal static string ProfileForStage(string stage, string configuredProfileId)
            => stage == "报规" ? configuredProfileId ?? "" : "";
        internal static string ProfileId(Document doc) => ProfileForStage(Stage(doc), Settings(doc).RuleProfileId);
        internal static string CanonicalSlot(string slot, string profileId)
            => ReferenceCatalog.LegacySlot(PlatformRules.IsActive(profileId)
                ? PlatformRules.CanonicalPropertySet(slot ?? "") : (slot ?? "").Trim());
        internal static bool IsBuildingLevel(Document doc, Level level)
        {
            var settings = Settings(doc);
            if (!PlatformRules.IsActive(ProfileId(doc))) return true;
            if (settings.LevelsConfirmed) return (settings.BuildingLevelUniqueIds ?? new string[0]).Contains(level.UniqueId);
            return new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .Where(e => e.LevelId == level.Id && (e is Floor || e is Area))
                .Any(e => HasConfirmedBuildingRole(doc, e, settings));
        }
        private static bool HasConfirmedBuildingRole(Document doc, Element element, PlanningSettings settings)
        {
            var slot = CanonicalSlot(RevitMappingService.Slot(doc, element), settings.RuleProfileId);
            if (slot == "建筑区域") return element is Floor && RevitMappingService.SourceEntity(doc, element) == "IfcSlab";
            if (slot == "室内停车场") return element is Area;
            if (!string.IsNullOrEmpty(slot)) return false;
            var roles = (settings.Roles ?? new PlanningRoleChoice[0]).Where(r => r.UniqueId == element.UniqueId).ToArray();
            if (roles.Length != 1) return false;
            var role = roles[0];
            slot = CanonicalSlot(role.Slot, settings.RuleProfileId);
            if (!(slot == "建筑区域" && element is Floor || slot == "室内停车场" && element is Area)) return false;
            var row = CaseModelReader.Inventory(doc, element);
            return string.IsNullOrEmpty(row.CoverageNote) && string.IsNullOrEmpty(row.Issue)
                && (slot != "建筑区域" || row.SourceEntity == "IfcSlab") && role.Identity == PlanningInputs.Identity(row);
        }
    }
}
