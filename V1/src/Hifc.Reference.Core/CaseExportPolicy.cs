using System;
using System.Linq;

namespace Hifc.Reference
{
    // Runtime adaptations leave the frozen vendor configuration as evidence.
    public static class CaseExportPolicy
    {
        static readonly ReferenceCatalog Catalog = ReferenceCatalog.FromEmbeddedBaseline();
        const string GeneralSite = "Pset_总平构件信息属性集";
        const string IndoorCarPark = "Pset_室内停车场信息属性集";
        public static ReferenceProperty WithBinding(ReferenceProperty p, string binding, string entity)
            => new ReferenceProperty { PropertySet=p.PropertySet, Property=p.Property, Binding=binding,
                Entity=entity, RawType=p.RawType, SourceParameter=p.SourceParameter, NativeGroup=p.NativeGroup };

        public static ReferenceProperty[] Properties(string entity, string slot, bool hasTypeEvidence)
            => Properties(entity, slot, hasTypeEvidence, null);

        public static ReferenceProperty[] Properties(string entity, string slot, bool hasTypeEvidence, string profileId)
        {
            // This legacy argument describes a source observation, not a business
            // classification. A type value must never add a second business Pset.
            return PropertiesForSlot(slot, profileId).Where(p => p.Entity.Split(',').Contains(entity)).ToArray();
        }

        public static ReferenceProperty[] PropertiesForSlot(string slot, string profileId)
        {
            if (string.IsNullOrWhiteSpace(slot)) return new ReferenceProperty[0];
            slot = ReferenceCatalog.LegacySlot(slot.Trim());
            var active = PlatformRules.IsActive(profileId);
            if (active && slot == "室内停车场") return IndoorCarParkProperties();
            // Keep legacy source names (and therefore source GUIDs) while routing
            // their values to the platform's explicitly documented Pset spelling.
            if (active && slot == "雨篷") slot = "雨蓬";
            var properties = Catalog.ForSlot(slot);
            if (slot == "窗")
            {
                if (!active)
                    return properties.Where(p => p.Property == "高度" || p.Property == "宽度")
                        .Select(p => WithBinding(p, "类型", p.Entity)).ToArray();
                var windowFields = new[] { "标高", "底高度", "高度", "宽度", "户型显示名" };
                properties = properties.Where(p => windowFields.Contains(p.Property))
                    .Select(p => WithBinding(p, "实例", p.Entity)).ToArray();
            }
            return active ? properties.Select(AdaptPlatformProperty).ToArray() : properties;
        }

        public static ReferenceProperty[] SourceProperties(string profileId)
        {
            if (!PlatformRules.IsActive(profileId)) return Catalog.Properties.ToArray();
            return Catalog.Properties.Where(p => p.NativeGroup)
                .Concat(Catalog.Slots().SelectMany(slot => PropertiesForSlot(slot, profileId)))
                .Concat(IndoorCarParkProperties()).ToArray();
        }

        public static bool HasGeneralSiteTypeEvidence(string entity, string slot, string value, string profileId)
        {
            if (entity != "IfcSlab" || ReferenceCatalog.LegacySlot((slot ?? "").Trim()) != "总平构件"
                || string.IsNullOrWhiteSpace(value)) return false;
            return !PlatformRules.IsActive(profileId)
                || PlatformRules.Choices(GeneralSite, "构件类型").Contains(value.Trim(), StringComparer.Ordinal);
        }

        public static string ValueForExport(ReferenceProperty property, string value, string profileId, string parentFunction = null)
        {
            if (!PlatformRules.IsActive(profileId)) return value;
            // Missing model input stays missing. Rule defaults are never model facts.
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (property.ExportType == "Boolean")
            {
                var text = value.Trim();
                if (text == "是" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) value = "1";
                else if (text == "否" || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) value = "0";
                else value = text;
            }
            if (!ModelChecks.ValueIsValid(property.ExportType, value))
                throw new InvalidOperationException("值“" + value + "”不符合导出类型 " + property.ExportType);
            var error = PlatformRules.ValueError(property.PropertySet, property.Property, value, parentFunction);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            return PlatformRules.Choices(property.PropertySet, property.Property, parentFunction).Length > 0 ? value.Trim() : value;
        }

        static ReferenceProperty AdaptPlatformProperty(ReferenceProperty property)
        {
            var result = WithBinding(property, property.Binding, property.Entity);
            result.PropertySet = PlatformRules.CanonicalPropertySet(property.PropertySet);
            if (result.PropertySet == GeneralSite && result.Property == "建筑密度") result.RawType = "Boolean";
            return result;
        }

        static ReferenceProperty[] IndoorCarParkProperties()
        {
            return new[] {
                IndoorProperty("投影面积", "Real"), IndoorProperty("公建大类", "Label"),
                IndoorProperty("公建小类", "Label"), IndoorProperty("停车类型", "Label"), IndoorProperty("层数", "Integer")
            };
        }
        static ReferenceProperty IndoorProperty(string property, string type)
            => new ReferenceProperty { PropertySet=IndoorCarPark, Property=property, Binding="实例", Entity="IfcSpace",
                RawType=type, SourceParameter="室内停车场" + property, NativeGroup=false };
    }
}
