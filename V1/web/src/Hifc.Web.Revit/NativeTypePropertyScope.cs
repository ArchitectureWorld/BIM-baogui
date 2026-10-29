using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.DB.IFC;
using Hifc.Web.Contracts;
using Revit.IFC.Common.Enums;
using Revit.IFC.Export.Exporter;
using Revit.IFC.Export.Exporter.PropertySet;
using Revit.IFC.Export.Utility;

namespace Hifc.Web.Revit
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

        internal NativeTypePropertyScope(Document doc, NativeExportRoute[] routes, SingleServerService nativeService, bool hasFloors)
        {
            if(routes.Length==0 && !hasFloors) { Applied=true;Restored=true;Evidence=new object[0];return; }
            service=nativeService??throw new InvalidOperationException("原生 IFC 导出服务不可用。");
            previous=service.GetActiveServerId();
            var evidence=new List<object>();
            foreach(var route in routes)
            {
                var isType = route.Origin == "type";
                if(!isType && route.Origin != "instance")throw new InvalidOperationException("导出属性路由来源无效。");
                var routeOwners=route.OwnerUniqueIds.Select(id=>doc.GetElement(id)??throw new InvalidOperationException("导出载体已删除。")).ToArray();
                if(routeOwners.Length==0 || routeOwners.Any(e=>(e is ElementType)!=isType || e.Category==null))throw new InvalidOperationException("导出载体身份无效。");
                var schedule=ViewSchedule.CreateSchedule(doc,routeOwners[0].Category.Id);
                schedule.Name="HIFC Web 临时属性路由 "+Guid.NewGuid().ToString("N");
                var ownerIds=new HashSet<ElementId>(routeOwners.Select(e=>e.Id));
                owners.Add(schedule.Id,ownerIds);
                var description=new PropertySetDescription {Name=route.PropertySet,ViewScheduleId=schedule.Id};
                foreach(var entity in route.Entity.Split(','))
                    description.EntityTypes.Add((IFCEntityType)Enum.Parse(typeof(IFCEntityType),entity+(isType && !entity.EndsWith("Type",StringComparison.Ordinal) ? "Type" : "")));
                foreach(var property in route.Properties)
                {
                    var propertyType=(PropertyType)Enum.Parse(typeof(PropertyType),property.ExportType);
                    var entry=isType
                        ? new PropertySetEntry(propertyType,property.BridgeName)
                        : new PropertySetEntry(propertyType,property.Property,new PropertySetEntryMap(
                            new InstanceBridgeCalculator(ownerIds,property,propertyType)) {UseCalculatorOnly=true});
                    entry.PropertyName=property.Property;
                    description.AddEntry(entry);
                }
                descriptions.Add(description);evidence.Add(route);
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
            bool boolean;
            int integer;
            internal InstanceBridgeCalculator(HashSet<ElementId> allowedOwners, NativeExportProperty property, PropertyType propertyType)
            {
                if(propertyType!=PropertyType.Real && propertyType!=PropertyType.Text && propertyType!=PropertyType.Boolean && propertyType!=PropertyType.Integer)
                    throw new InvalidOperationException("实例窗属性不支持当前导出类型："+property.ExportType);
                this.allowedOwners=allowedOwners;
                bridgeGuid=new Guid(property.BridgeGuid);
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
                if(propertyType==PropertyType.Boolean) { if(text=="1"){boolean=true;return true;} if(text=="0"){boolean=false;return true;} return bool.TryParse(text,out boolean); }
                if(propertyType==PropertyType.Integer)return int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out integer);
                // Bridges already contain the validated export value in metres.
                return propertyType==PropertyType.Text || (double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out number)
                    && !double.IsNaN(number) && !double.IsInfinity(number));
            }
            public override string GetStringValue()=>text;
            public override double GetDoubleValue()=>number;
            public override bool GetBooleanValue()=>boolean;
            public override int GetIntValue()=>integer;
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
