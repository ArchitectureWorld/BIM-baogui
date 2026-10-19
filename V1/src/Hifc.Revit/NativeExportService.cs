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
        internal static object Export(Document doc, Element[] elements, string ifcPath)
        {
            if (elements.Length == 0) throw new InvalidOperationException("请先选择要验证导出的模型构件。");
            if (doc.IsFamilyDocument) throw new InvalidOperationException("当前版本需要项目文档。");
            if (elements.Any(e => e is ElementType || e.Category == null)) throw new InvalidOperationException("请选择模型实例。");
            var selectedProperties=elements.SelectMany(e=>RevitMappingService.Properties(doc,e)).ToArray();
            var path = Path.GetFullPath(ifcPath);
            if (File.Exists(path)) throw new InvalidOperationException("目标 IFC 已存在，请选择新的文件名。");
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            var configPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + ".psets.tsv");
            if (File.Exists(configPath)) throw new InvalidOperationException("导出配置目标已存在。");
            File.WriteAllText(configPath, RevitMappingService.Catalog.BuildSelectedExportConfiguration(selectedProperties), new UTF8Encoding(false));
            var before = Evidence.Hash(Evidence.Json(RevitMappingService.Capture(doc, elements)));
            // IfcSpace has no Tag attribute. Keep Revit's native export identity so
            // the read-only IFC audit can resolve an Area without guessing by name.
            var spatialIdentities = elements.Where(e=>e is Area || e is ProjectInfo || e is Level).Select(area => new
            {
                element_id = area.Id.IntegerValue, unique_id = area.UniqueId, ifc_entity = RevitMappingService.Entity(doc,area),
                export_id = ExportUtils.GetExportId(doc, area.Id).ToString("N"),
                ifc_guid_override = area.get_Parameter(BuiltInParameter.IFC_GUID)?.AsString()
                ,native_global_id=NativeSpatialGuid(doc,area)
            }).ToArray();
            var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.IFCExporterService) as SingleServerService;
            var server = service?.GetActiveServerId();
            object materialization = null;
            string[] suppressedFallbackBindings=null;
            bool exported = false;
            TransactionStatus rollback;
            using (var transaction = new Transaction(doc, "H-IFC V1 临时物化与导出（回滚）"))
            {
                if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("导出事务未启动。");
                try
                {
                    RevitMappingService.EnsureBridges(doc, elements, directory);
                    materialization = RevitMappingService.Materialize(doc, elements);
                    suppressedFallbackBindings=SuppressNumericFallback(doc,elements,selectedProperties);
                    var viewType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                    var view = View3D.CreateIsometric(doc, viewType.Id);
                    var visible=elements.Where(e=>!(e is SpatialElement)&&!(e is ProjectInfo)&&!(e is Level)).Select(e=>e.Id).ToArray();
                    if(visible.Length>0){view.IsolateElementsTemporary(visible);view.ConvertTemporaryHideIsolateToPermanent();}
                    doc.Regenerate();
                    using (var options = new IFCExportOptions { FileVersion = IFCVersion.IFC4RV, FilterViewId = view.Id })
                    {
                        // Area is not visible in 3D. A view-only collector silently
                        // omits it; the explicit list also prevents exporting other Areas.
                        options.AddOption("ElementsForExport", string.Join(";", elements.Select(e => e.Id.IntegerValue)
                            .Distinct().OrderBy(id => id).Select(id => id.ToString(CultureInfo.InvariantCulture))));
                        options.AddOption("ExportRoomsInView", "true");
                        options.AddOption("ExportUserDefinedPsets", "true");
                        options.AddOption("ExportUserDefinedPsetsFileName", configPath);
                        // Internal Revit Psets would expose temporary bridge names in the IFC.
                        options.AddOption("ExportInternalRevitPropertySets", "false");
                        options.AddOption("ExportIFCCommonPropertySets", "true");
                        options.AddOption("ExportBaseQuantities", "true");
                        options.AddOption("SitePlacement", "2");
                        exported = doc.Export(directory, Path.GetFileNameWithoutExtension(path), options);
                    }
                }
                finally { rollback = transaction.GetStatus() == TransactionStatus.Started ? transaction.RollBack() : transaction.GetStatus(); }
            }
            var after = Evidence.Hash(Evidence.Json(RevitMappingService.Capture(doc, elements)));
            if (rollback != TransactionStatus.RolledBack || before != after)
                throw new InvalidOperationException("导出后模型回滚或取值一致性核验失败，不能标为成功。");
            if (service?.GetActiveServerId() != server) throw new InvalidOperationException("导出服务身份发生变化。");
            if (!exported || !File.Exists(path) || new FileInfo(path).Length == 0)
                throw new InvalidOperationException("Revit 未产生有效 IFC 文件。");
            var receipt = new
            {
                schema_version = "hifc.native-export-receipt/1.2", ifc_path = path, ifc_sha256 = Evidence.FileHash(path),
                configuration_path = configPath, configuration_sha256 = Evidence.FileHash(configPath),
                doc_export_calls = 1, ifc_postprocessed = false, source_snapshot_before = before, source_snapshot_after = after,
                model_transaction = rollback.ToString(), exporter_server_id = server?.ToString(), selected_elements = elements.Length,
                spatial_export_identities = spatialIdentities,
                suppressed_fallback_bindings=suppressedFallbackBindings,
                materialization, reference_software_ab_verified = false
            };
            Evidence.Write(Path.ChangeExtension(path, ".receipt.json"), receipt);
            return receipt;
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
            var numeric=properties.Where(p=>p.ExportType=="Real"||p.ExportType=="Boolean").ToArray();
            var names=new HashSet<string>(numeric.Select(p=>p.Property),StringComparer.Ordinal);
            var definitions=new List<Definition>();var iterator=doc.ParameterBindings.ForwardIterator();
            iterator.Reset();while(iterator.MoveNext())if(names.Contains(iterator.Key.Name))definitions.Add(iterator.Key);
            var removed=definitions.Select(d=>d.Name).Distinct().ToArray();
            foreach(var definition in definitions)if(!doc.ParameterBindings.Remove(definition))throw new InvalidOperationException("无法临时隔离原生导出器的同名回退来源："+definition.Name);
            doc.Regenerate();
            foreach(var element in elements)
                foreach(var property in numeric.Where(p=>p.Entity.Split(',').Contains(RevitMappingService.Entity(doc,element))))
                {
                    var bridge=element.get_Parameter(ReferencePolicy.BridgeGuid(property));
                    if(bridge?.HasValue==true && ModelChecks.ValueIsValid(property.ExportType,bridge.AsString()))continue;
                    var type=doc.GetElement(element.GetTypeId());
                    var owners=type==null?new[]{element}:new[]{element,type};
                    if(owners.Any(o=>o.GetParameters(property.Property).Any(p=>p.HasValue && (p.StorageType!=StorageType.String || !string.IsNullOrWhiteSpace(p.AsString())))))
                        throw new InvalidOperationException("原生导出器存在无法安全隔离的族或内置同名来源："+element.Id.IntegerValue+" / "+property.Property+"。未输出可能串类目的 IFC。");
                }
            return removed;
        }
    }
}
