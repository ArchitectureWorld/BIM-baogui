using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.DB.IFC;
using Hifc.Reference;
using Revit.IFC.Common.Enums;
using Revit.IFC.Export.Exporter;
using Revit.IFC.Export.Exporter.PropertySet;
using Revit.IFC.Export.Utility;

namespace Hifc.Revit
{
    // Revit 2020 matches user Psets to both members of an instance/type pair,
    // even when the TSV declares T. Use the native exporter's schedule-owner
    // filter to allow only the exact Revit types. The scoped platform's window
    // Pset also checks exact instance owners in a calculator: the native type
    // property writer bypasses the schedule filter. No IFC is rewritten.
    internal sealed class NativeTypePropertyScope : IDisposable
    {
        readonly SingleServerService service;
        readonly Guid previous;
        readonly ScopedExporter exporter;
        bool registered;
        readonly List<PropertySetDescription> descriptions = new List<PropertySetDescription>();
        readonly Dictionary<ElementId, HashSet<ElementId>> owners = new Dictionary<ElementId, HashSet<ElementId>>();
        internal bool Applied { get; private set; }
        internal bool Restored { get; private set; }
        internal ElementId[] ScheduleIds => owners.Keys.ToArray();
        internal object Evidence { get; }
        internal Guid? ExportServerId => exporter?.GetServerId();
        internal object FloorGeometryEvidence => exporter?.FloorGeometry.Evidence;
        internal bool FloorGeometryPassed => exporter == null || exporter.FloorGeometry.Passed;
        internal int[] FloorGeometryIds => exporter?.FloorGeometry.WrittenIds ?? new int[0];

        internal static bool FiltersInstanceProperty(ReferenceProperty property, string profileId)
            => PlatformRules.IsActive(profileId) && property.Binding == "实例" && property.PropertySet == "Pset_窗信息属性集";

        internal NativeTypePropertyScope(Document doc, Element[] elements, SingleServerService nativeService, Element[] geometryElements = null)
        {
            var profileId = Management.PlatformContext.ProfileId(doc);
            var plan = elements.SelectMany(e => RevitMappingService.Properties(doc,e).Where(p => p.Binding=="类型" || FiltersInstanceProperty(p, profileId))
                .Select(p => new {Element=e,Property=p})).ToArray();
            if(plan.Length==0 && !(geometryElements ?? elements).OfType<Floor>().Any()) { Applied=true;Restored=true;Evidence=new object[0];return; }
            service=nativeService??throw new InvalidOperationException("原生 IFC 导出服务不可用。");
            previous=service.GetActiveServerId();
            var evidence=new List<object>();
            foreach(var group in plan.GroupBy(x=>new{x.Property.PropertySet,x.Property.Entity,x.Property.Binding}))
            {
                var representative=group.First().Element;
                var schedule=ViewSchedule.CreateSchedule(doc,representative.Category.Id);
                schedule.Name="HIFC 临时类型路由 "+Guid.NewGuid().ToString("N");
                var isType = group.Key.Binding == "类型";
                var ownerIds=new HashSet<ElementId>(group.Select(x=>isType ? x.Element.GetTypeId() : x.Element.Id));
                if(ownerIds.Contains(ElementId.InvalidElementId))throw new InvalidOperationException("属性缺少有效实例或类型载体。");
                owners.Add(schedule.Id,ownerIds);
                var description=new PropertySetDescription {Name=group.Key.PropertySet,ViewScheduleId=schedule.Id};
                foreach(var entity in group.Key.Entity.Split(','))
                    description.EntityTypes.Add((IFCEntityType)Enum.Parse(typeof(IFCEntityType),entity+(isType ? "Type" : "")));
                var properties=group.Select(x=>x.Property).GroupBy(p=>p.Property).Select(g=>g.First()).ToArray();
                foreach(var property in properties)
                {
                    var propertyType=(PropertyType)Enum.Parse(typeof(PropertyType),property.ExportType);
                    var entry=isType
                        ? new PropertySetEntry(propertyType,ReferencePolicy.BridgeName(property))
                        : new PropertySetEntry(propertyType,property.Property,new PropertySetEntryMap(
                            new InstanceBridgeCalculator(ownerIds,property,propertyType)) {UseCalculatorOnly=true});
                    entry.PropertyName=property.Property;
                    description.AddEntry(entry);
                }
                descriptions.Add(description);
                evidence.Add(new {property_set=group.Key.PropertySet,entity=group.Key.Entity+(isType ? "Type" : ""),binding=group.Key.Binding,
                    allowed_type_ids=isType ? ownerIds.Select(id=>id.IntegerValue).OrderBy(id=>id).ToArray() : new int[0],
                    allowed_instance_ids=isType ? new int[0] : ownerIds.Select(id=>id.IntegerValue).OrderBy(id=>id).ToArray(),
                    properties=properties.Select(p=>new{property=p.Property,source=ReferencePolicy.BridgeName(p),export_type=p.ExportType}).ToArray()});
            }
            Evidence=evidence.ToArray();
            // Revit's built-in server is a native facade, not the managed Exporter
            // instance. Use Autodesk's shipped exporter through its supported
            // subclass extension, scoped to this single Document.Export call.
            exporter=new ScopedExporter(Initialize);
            try
            {
                service.AddServer(exporter);registered=true;
                service.SetActiveServer(exporter.GetServerId());
            }
            catch { Dispose();throw; }
        }
        void Initialize(IList<IList<PropertySetDescription>> propertySets)
        {
            foreach(var entry in owners)ExporterCacheManager.ViewScheduleElementCache.Add(entry.Key,entry.Value);
            propertySets.Add(descriptions);
            Applied=true;
        }
        public void Dispose()
        {
            if(!registered)return;
            service.SetActiveServer(previous);
            service.RemoveServer(exporter.GetServerId());
            registered=false;
            Restored=service.GetActiveServerId()==previous&&!service.GetRegisteredServerIds().Contains(exporter.GetServerId());
        }
        sealed class InstanceBridgeCalculator : PropertyCalculator
        {
            readonly HashSet<ElementId> allowedOwners;
            readonly Guid bridgeGuid;
            readonly PropertyType propertyType;
            string text;
            double number;
            internal InstanceBridgeCalculator(HashSet<ElementId> allowedOwners, ReferenceProperty property, PropertyType propertyType)
            {
                if(propertyType!=PropertyType.Real && propertyType!=PropertyType.Text)
                    throw new InvalidOperationException("实例窗属性不支持当前导出类型："+property.ExportType);
                this.allowedOwners=allowedOwners;
                bridgeGuid=ReferencePolicy.BridgeGuid(property);
                this.propertyType=propertyType;
            }
            public override bool Calculate(ExporterIFC exporterIFC, IFCExtrusionCreationData data, Element element, ElementType elementType)
            {
                text=null;number=0;
                // CreateElementTypeProperties skips ViewScheduleId filtering and
                // passes an ElementType as both arguments. Reject it before any
                // parameter lookup; never fall back to a type's native dimensions.
                if(element==null || element is ElementType || !allowedOwners.Contains(element.Id))return false;
                var parameter=element.get_Parameter(bridgeGuid);
                if(parameter==null || parameter.StorageType!=StorageType.String || !parameter.HasValue)return false;
                text=parameter.AsString();
                if(string.IsNullOrEmpty(text))return false;
                // Bridges already contain the validated export value in metres.
                return propertyType==PropertyType.Text || (double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out number)
                    && !double.IsNaN(number) && !double.IsInfinity(number));
            }
            public override string GetStringValue()=>text;
            public override double GetDoubleValue()=>number;
        }
        sealed class ScopedExporter : Exporter
        {
            internal readonly NativeFloorGeometry FloorGeometry = new NativeFloorGeometry();
            readonly Guid id=Guid.NewGuid();
            readonly PropertySetsToExport initialize;
            internal ScopedExporter(PropertySetsToExport initialize) {this.initialize=initialize;}
            public override Guid GetServerId()=>id;
            public override void ExportElementImpl(ExporterIFC exporterIFC, Element element, ProductWrapper productWrapper)
            {
                if (!FloorGeometry.TryExport(exporterIFC, element, productWrapper))
                    base.ExportElementImpl(exporterIFC, element, productWrapper);
            }
            protected override void InitializePropertySets()
            {
                m_PropertySetsToExport+=initialize;
                base.InitializePropertySets();
            }
        }
    }
}
