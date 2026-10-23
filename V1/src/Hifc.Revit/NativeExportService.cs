using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.DB.IFC;
using Hifc.Reference;

namespace Hifc.Revit
{
    internal static class NativeExportService
    {
        internal static Management.ExportReviewData ReviewProject(Document doc)
        {
            var scope=ProjectExportScope.Read(doc);
            var profile=Management.PlatformContext.ProfileId(doc);
            var inputs=scope.InputReview.Concat(scope.Issues.Where(i=>!scope.InputReview.Any(r=>r.ElementId==i.element_id&&r.PropertySet==i.property_set&&r.Property==i.property))
                .Select(i=>new InputReviewItem{ElementId=i.element_id,Name=i.name,PropertySet=i.property_set,Property=i.property??"映射 / 几何",Code=i.code,Detail=i.detail})).ToList();
            var blockers=new List<string>();
            try { Management.CaseEdits.ValidateNetLand(doc); }
            catch(InvalidOperationException error) {
                blockers.Add(error.Message);
                inputs.Add(new InputReviewItem{ElementId=doc.ProjectInformation.Id.IntegerValue,Name="项目范围",Property="净用地范围",Code="EXPORT_SCOPE_REVIEW",Detail=error.Message});
            }
            FloorAreaPlan plan=null;string areaPlanError=null;
            try { plan=Management.FloorAreaSynchronization.ForExport(doc,scope.MappedElements); }
            catch(InvalidOperationException error) { areaPlanError=error.Message;blockers.Add(error.Message); }
            var areas=new List<Management.ExportAreaReviewItem>();
            foreach(var element in scope.MappedElements)
                foreach(var property in RevitMappingService.Properties(doc,element).Where(p=>
                    p.Property=="投影面积"||p.Property=="楼层面积"||p.Property=="建筑面积"))
                {
                    var item=new Management.ExportAreaReviewItem{ElementId=element.Id.IntegerValue,Name=element.Name,PropertySet=property.PropertySet,Property=property.Property};
                    try {
                        var value=RevitMappingService.ResolvedExportValue(doc,element,property,plan,out var read,out var allocation);
                        item.SourceValue=RevitMappingService.ExportValue(doc,element,property,read,profile)??"未填写";
                        item.ExportValue=value??"不输出";
                        item.Reason=allocation?.Reason??"采用当前已写入模型的参数；不以几何复算覆盖人工值";
                        if(areaPlanError!=null&&(property.PropertySet==FloorAreaPolicy.RegionSet||property.PropertySet==FloorAreaPolicy.FloorSet))
                        {
                            item.ExportValue="无法确定";item.Reason=areaPlanError;
                            inputs.Add(new InputReviewItem{ElementId=item.ElementId,Name=item.Name,PropertySet=property.PropertySet,Property=property.Property,Code="FLOOR_AREA_REVIEW",Detail=areaPlanError});
                        }
                    }catch(InvalidOperationException error) {
                        blockers.Add(error.Message);item.SourceValue="需核对来源";item.ExportValue="无法确定";item.Reason=error.Message;
                        if(!inputs.Any(i=>i.ElementId==item.ElementId&&i.PropertySet==property.PropertySet&&i.Property==property.Property))
                            inputs.Add(new InputReviewItem{ElementId=item.ElementId,Name=item.Name,PropertySet=property.PropertySet,Property=property.Property,Code="EXPORT_AREA_REVIEW",Detail=error.Message});
                    }
                    areas.Add(item);
                }
            if(areaPlanError!=null&&!inputs.Any(i=>i.Code=="FLOOR_AREA_REVIEW"))
                inputs.Add(new InputReviewItem{ElementId=doc.ProjectInformation.Id.IntegerValue,Name="楼层面积同步",Property="区域范围",Code="FLOOR_AREA_REVIEW",Detail=areaPlanError});
            return new Management.ExportReviewData{Title=doc.Title,ProfileId=profile,Inputs=inputs.ToArray(),Areas=areas.ToArray(),BlockingReasons=blockers.Distinct().ToArray()};
        }
        internal static ProjectExportScope ExportProject(Document doc, string ifcPath)
        {
            PlatformSync.PlatformSyncRegistry.BeginExport(doc);
            Management.CaseEdits.ValidateNetLand(doc);
            var scope = ProjectExportScope.Read(doc);
            ExportCore(doc, scope.Elements, scope.MappedElements, ifcPath, scope);
            return scope;
        }

        // Explicit local scope is retained only for developer acceptance fixtures.
        internal static object Export(Document doc, Element[] elements, string ifcPath)
        {
            if (elements.Length == 0) throw new InvalidOperationException("请先选择要验证导出的模型构件。");
            if (doc.IsFamilyDocument) throw new InvalidOperationException("当前版本需要项目文档。");
            if (elements.Any(e => e is ElementType || e.Category == null)) throw new InvalidOperationException("请选择模型实例。");
            return ExportCore(doc, elements, elements, ifcPath, null);
        }

        private static object ExportCore(Document doc, Element[] elements, Element[] mappedElements, string ifcPath, ProjectExportScope project)
        {
            var exportId = Guid.NewGuid().ToString("D");
            // Type properties are inherited by every exported instance of that type.
            // Never silently widen a user's per-instance window classification.
            foreach (var group in mappedElements.Where(e => RevitMappingService.Entity(doc,e) == "IfcWindow"
                && ReferenceCatalog.LegacySlot(RevitMappingService.Slot(doc,e) ?? "") == "窗"
                && RevitMappingService.Properties(doc,e).Any(p => p.PropertySet == "Pset_窗信息属性集" && p.Binding == "类型"))
                .GroupBy(e => e.GetTypeId().IntegerValue))
            {
                var selectedIds = new HashSet<int>(group.Select(e => e.Id.IntegerValue));
                if (elements.Any(e => e.GetTypeId().IntegerValue == group.Key && !selectedIds.Contains(e.Id.IntegerValue)))
                    throw new InvalidOperationException("窗类型“" + doc.GetElement(new ElementId(group.Key))?.Name
                        + "”还有未确认的同类型实例。类型属性会由所有同类型窗继承；请确认整组适用窗，或先在 Revit 中复制类型后分别分类。");
            }
            var selectedProperties=mappedElements.SelectMany(e=>RevitMappingService.Properties(doc,e)).ToArray();
            var floorAreas=project==null?new FloorAreaPlan():Management.FloorAreaSynchronization.ForExport(doc,mappedElements);
            var path = Path.GetFullPath(ifcPath);
            if (File.Exists(path)) throw new InvalidOperationException("目标 IFC 已存在，请选择新的文件名。");
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            var artifacts=ExportArtifacts.Create(path,ExportArtifacts.DefaultRoot,exportId);
            var configPath=artifacts.ConfigurationPath;
            // Native 2020 ignores the T/I distinction when matching an entity
            // pair. Type Psets use an exact owner filter in NativeTypePropertyScope.
            var profileId = Management.PlatformContext.ProfileId(doc);
            File.WriteAllText(configPath, RevitMappingService.Catalog.BuildSelectedExportConfiguration(selectedProperties.Where(p=>p.Binding!="类型"
                && !NativeTypePropertyScope.FiltersInstanceProperty(p, profileId))), new UTF8Encoding(false));
            var before = SnapshotHash(doc, mappedElements);
            // IfcSpace has no Tag attribute. Keep Revit's native export identity so
            // the read-only IFC audit can resolve an Area without guessing by name.
            var spatialElements = project == null ? elements : elements.OfType<Area>().Where(ProjectExportScope.ValidArea).Cast<Element>()
                .Concat(elements.Where(e => e is ProjectInfo || e is Level)).ToArray();
            var spatialIdentities = spatialElements.Where(e=>e is Area || e is ProjectInfo || e is Level).Select(area => new
            {
                element_id = area.Id.IntegerValue, unique_id = area.UniqueId, ifc_entity = area is Area ? "IfcSpace" : RevitMappingService.Entity(doc,area),
                export_id = ExportUtils.GetExportId(doc, area.Id).ToString("N"),
                ifc_guid_override = area.get_Parameter(BuiltInParameter.IFC_GUID)?.AsString()
                ,native_global_id=NativeSpatialGuid(doc,area)
            }).ToArray();
            var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.IFCExporterService) as SingleServerService;
            var server = service?.GetActiveServerId();
            object materialization = null;
            object carrierOverrides = null;
            string[] suppressedFallbackBindings=null;
            bool exported = false;
            NativeTypePropertyScope typeScope=null;
            TransactionStatus rollback;
            using (var transaction = new Transaction(doc, "H-IFC V1 临时物化与导出（回滚）"))
            {
                if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("导出事务未启动。");
                try
                {
                    carrierOverrides = OverrideControlSlabs(doc, mappedElements, artifacts.DirectoryPath);
                    if (mappedElements.Length > 0) RevitMappingService.EnsureBridges(doc, mappedElements, artifacts.DirectoryPath);
                    materialization = RevitMappingService.MaterializeWithFloorAreas(doc, mappedElements,floorAreas);
                    suppressedFallbackBindings=SuppressNumericFallback(doc,elements,selectedProperties);
                    var viewId = ElementId.InvalidElementId;
                    if (project == null)
                    {
                        var viewType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                            .First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                        var view = View3D.CreateIsometric(doc, viewType.Id);
                        var visible=elements.Where(e=>!(e is SpatialElement)&&!(e is ProjectInfo)&&!(e is Level)).Select(e=>e.Id).ToArray();
                        if(visible.Length>0){view.IsolateElementsTemporary(visible);view.ConvertTemporaryHideIsolateToPermanent();}
                        viewId = view.Id;
                    }
                    doc.Regenerate();
                    // Revit 2020's Reference View writer flattens curve loops to
                    // unsegmented IndexedPolyCurve points. Its native DTV path retains
                    // composite analytic curves, avoiding the observed open/short edges.
                    // This is a formal Revit API property, not an AddOption key.
                    // Its default is false; a custom string with the same name is ignored.
                    using (var options = new IFCExportOptions { FileVersion = IFCVersion.IFC4DTV, FilterViewId = viewId, ExportBaseQuantities = true })
                    {
                        // Whole-document mode deliberately supplies neither a filter
                        // view nor ElementsForExport. Revit collects all spatial and
                        // non-spatial model objects, including unclassified geometry.
                        foreach (var option in NativeExportProfile.BuildOptions(configPath,
                            project == null ? elements.Select(e => e.Id.IntegerValue).ToArray() : null))
                            options.AddOption(option.Key, option.Value);
                        using(typeScope=new NativeTypePropertyScope(doc,mappedElements,service,elements))
                            exported = doc.Export(artifacts.DirectoryPath, Path.GetFileNameWithoutExtension(path), options);
                        if(!typeScope.Applied || !typeScope.Restored)throw new InvalidOperationException("原生类型属性隔离回调未执行或未恢复。");
                        if(!typeScope.FloorGeometryPassed)throw new InvalidOperationException("楼板原生导出存在失败，不能将缺少业务载体的 IFC 标为成功。");
                    }
                }
                finally { rollback = transaction.GetStatus() == TransactionStatus.Started ? transaction.RollBack() : transaction.GetStatus(); }
            }
            var after = SnapshotHash(doc, mappedElements);
            if (rollback != TransactionStatus.RolledBack || before != after)
                throw new InvalidOperationException("导出后模型回滚或取值一致性核验失败，不能标为成功。");
            if(typeScope.ScheduleIds.Any(id=>doc.GetElement(id)!=null))throw new InvalidOperationException("临时类型路由视图未回滚。");
            if (service?.GetActiveServerId() != server) throw new InvalidOperationException("导出服务身份发生变化。");
            if (!exported || !File.Exists(artifacts.StagedIfcPath) || new FileInfo(artifacts.StagedIfcPath).Length == 0)
                throw new InvalidOperationException("Revit 未产生有效 IFC 文件。");
            var ifcHash=Evidence.FileHash(artifacts.StagedIfcPath);
            var receipt = new
            {
                schema_version = "hifc.native-export-receipt/1.5", export_id = exportId, ifc_path = path, ifc_sha256 = ifcHash,
                receipt_path=artifacts.ReceiptPath, internal_artifact_directory=artifacts.DirectoryPath,
                configuration_path = configPath, configuration_sha256 = Evidence.FileHash(configPath),
                doc_export_calls = 1, ifc_postprocessed = false, source_snapshot_before = before, source_snapshot_after = after,
                ifc_version="IFC4",model_view="DesignTransferView",tessellation_level_of_detail=1.0,
                export_base_quantities=true,quantity_floor_ids=typeScope.FloorGeometryIds,
                source_snapshot_scope = "MAPPED_HIFC_CARRIERS",
                model_transaction = rollback.ToString(), exporter_server_id = server?.ToString(),
                export_scope = project == null ? "EXPLICIT_LOCAL_ACCEPTANCE" : "CURRENT_PROJECT",
                selected_elements = project == null ? (int?)elements.Length : null,
                model_records = elements.Length, mapped_elements = mappedElements.Length,
                area_objects = elements.OfType<Area>().Count(), valid_area_objects = elements.OfType<Area>().Count(ProjectExportScope.ValidArea),
                linked_source_documents_exported = false, current_view_filter_applied = project == null,
                mapping_issues = project?.Issues ?? new ProjectExportIssue[0],
                input_review = project?.InputReview ?? new InputReviewItem[0],
                platform_profile_id = Management.PlatformContext.ProfileId(doc),
                platform_rule_scope = PlatformRules.IsActive(Management.PlatformContext.ProfileId(doc)) ? PlatformRules.Title : null,
                spatial_export_identities = spatialIdentities,
                suppressed_fallback_bindings=suppressedFallbackBindings,
                carrier_overrides=carrierOverrides,
                floor_area_synchronization=floorAreas,
                native_type_routing=typeScope.Evidence,type_routing_callback_restored=typeScope.Restored,
                native_floor_geometry=typeScope.FloorGeometryEvidence,
                type_export_server_id=typeScope.ExportServerId?.ToString(),original_export_server_restored=service?.GetActiveServerId()==server,
                temporary_type_schedules_rolled_back=true,
                materialization, reference_software_ab_verified = false
            };
            Evidence.Write(artifacts.ReceiptPath, receipt);
            artifacts.Publish(ifcHash);
            // File existence alone is not success. This optional registration follows
            // all rollback, model consistency, exporter restoration and receipt checks.
            if (project != null) {
                project.ReceiptPath=artifacts.ReceiptPath;
                PlatformSync.PlatformSyncRegistry.RegisterCompleted(doc, path, artifacts.ReceiptPath, exportId);
            }
            return receipt;
        }
        private static string SnapshotHash(Document doc, Element[] elements)
        {
            // Hash one carrier at a time so a large project does not hit the JSON
            // serializer's size limit or reintroduce the old 5,000-selection cap.
            return Evidence.Hash(string.Join("\n", elements.OrderBy(e => e.UniqueId, StringComparer.Ordinal)
                .Select(e => Evidence.Hash(Evidence.Json(RevitMappingService.Capture(doc, new[] { e }))))));
        }
        private static object OverrideControlSlabs(Document doc, Element[] elements, string directory)
        {
            var records = new List<object>();
            foreach (var element in elements.Where(e => RevitMappingService.SourceEntity(doc, e) == "IfcSlab"
                && ReferenceCatalog.LegacySlot(RevitMappingService.Slot(doc, e) ?? "") == "规划控制线"))
            {
                var before = RevitMappingService.Reads(doc, element, "IfcExportAs");
                var beforeType = RevitMappingService.Reads(doc, element, "IfcExportType");
                foreach (var name in new[] { "IfcExportAs", "IfcExportType" })
                {
                    if (element.GetParameters(name).Count == 0)
                    {
                        RevitMappingService.EnsureDefinitions(doc, new[] { element }, new[] {
                            new ParameterDefinition { Name = name, Guid = ReferencePolicy.SourceGuid(name), Temporary = true }
                        }, directory);
                        doc.Regenerate();
                    }
                    var parameters = element.GetParameters(name);
                    var value = name == "IfcExportAs" ? "IfcBuildingElementProxy" : "NOTDEFINED";
                    if (parameters.Count != 1 || parameters[0].IsReadOnly || parameters[0].StorageType != StorageType.String
                        || !parameters[0].Set(value) || parameters[0].AsString() != value)
                        throw new InvalidOperationException("规划控制线楼板无法临时设置实例 IFC 导出参数：" + element.Id.IntegerValue + " / " + name);
                }
                records.Add(new { element_id = element.Id.IntegerValue, unique_id = element.UniqueId,
                    source_entity = "IfcSlab", export_entity = "IfcBuildingElementProxy", predefined_type = "NOTDEFINED",
                    original_export_as = before, original_export_type = beforeType });
            }
            doc.Regenerate();
            return records.ToArray();
        }
        private static string NativeSpatialGuid(Document doc,Element element)
        {
            if(element is ProjectInfo)
            {
                var value=ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(RevitMappingService.Reads(doc,element,"IfcBuilding GUID"),"IfcBuilding GUID"));
                return !string.IsNullOrWhiteSpace(value)&&value.Length==22?value:ExporterIFCUtils.CreateProjectLevelGUID(doc,IFCProjectLevelGUIDType.Building);
            }
            return element is Level?ExporterIFCUtils.CreateAlternateGUID(element):null;
        }
        private static string[] SuppressNumericFallback(Document doc,Element[] elements,ReferenceProperty[] properties)
        {
            // The 2020 native exporter retries Real/Boolean by IFC property name
            // when the configured bridge is unset. For example a parking-bay
            // "公建车位" can otherwise create an unrelated car-park Pset.
            // Bridges already contain every source value. Remove only project
            // bindings with these fallback names inside the export transaction;
            // rollback restores their definitions, bindings and values together.
            // Type Psets are already restricted to exact type owners by the native
            // schedule filter; unrelated instance built-ins cannot leak into them.
            var profileId = Management.PlatformContext.ProfileId(doc);
            // These window fields are exported only through an exact instance-owner
            // description. Their peer windows' built-in dimensions are safe to retain.
            var numeric=properties.Where(p=>p.Binding!="类型" && !NativeTypePropertyScope.FiltersInstanceProperty(p, profileId)
                && (p.ExportType=="Real"||p.ExportType=="Boolean")).ToArray();
            if (numeric.Length == 0) return new string[0];
            var names=new HashSet<string>(numeric.Select(p=>p.Property),StringComparer.Ordinal);
            var definitions=new List<Definition>();var iterator=doc.ParameterBindings.ForwardIterator();
            iterator.Reset();while(iterator.MoveNext())if(names.Contains(iterator.Key.Name))definitions.Add(iterator.Key);
            var removed=definitions.Select(d=>d.Name).Distinct().ToArray();
            foreach(var definition in definitions)if(!doc.ParameterBindings.Remove(definition))throw new InvalidOperationException("无法临时隔离原生导出器的同名回退来源："+definition.Name);
            doc.Regenerate();
            foreach(var element in elements)
            {
                // Ordinary unclassified geometry also participates in native Pset
                // fallback. Check the original IFC carrier without requiring a slot.
                var entity = MappingEntity(doc, element);
                foreach(var property in numeric.Where(p=>p.Entity.Split(',').Contains(entity)))
                {
                    var type=doc.GetElement(element.GetTypeId());
                    var owner = property.Binding == "类型" ? type : element;
                    var bridge=owner?.get_Parameter(ReferencePolicy.BridgeGuid(property));
                    if(bridge?.HasValue==true && ModelChecks.ValueIsValid(property.ExportType,bridge.AsString()))continue;
                    var owners=property.Binding=="类型" ? (type==null?new Element[0]:new[]{type}) : type==null?new[]{element}:new[]{element,type};
                    if(owners.Any(o=>o.GetParameters(property.Property).Any(p=>p.HasValue && (p.StorageType!=StorageType.String || !string.IsNullOrWhiteSpace(p.AsString())))))
                        throw new InvalidOperationException("原生导出器存在无法安全隔离的族或内置同名来源："+element.Id.IntegerValue+" / "+property.Property+"。未输出可能串类目的 IFC。");
                }
            }
            return removed;
        }
        private static string MappingEntity(Document doc, Element element)
        {
            try { return RevitMappingService.Entity(doc, element); }
            catch (InvalidOperationException) { return RevitMappingService.SourceEntity(doc, element); }
        }
    }
}
