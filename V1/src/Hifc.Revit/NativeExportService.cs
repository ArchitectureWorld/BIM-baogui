using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExternalService;

namespace Hifc.Revit
{
    internal static class NativeExportService
    {
        internal static object Export(Document doc, Element[] elements, string ifcPath)
        {
            if (elements.Length == 0) throw new InvalidOperationException("请先选择要验证导出的模型构件。");
            if (doc.IsFamilyDocument) throw new InvalidOperationException("当前版本需要项目文档。");
            if (elements.Any(e => e is ElementType || e.Category == null)) throw new InvalidOperationException("请选择模型实例。");
            foreach (var element in elements) RevitMappingService.Properties(doc, element);
            var path = Path.GetFullPath(ifcPath);
            if (File.Exists(path)) throw new InvalidOperationException("目标 IFC 已存在，请选择新的文件名。");
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            var configPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + ".psets.tsv");
            if (File.Exists(configPath)) throw new InvalidOperationException("导出配置目标已存在。");
            File.WriteAllText(configPath, RevitMappingService.Catalog.BuildExportConfiguration(), new UTF8Encoding(false));
            var before = Evidence.Hash(Evidence.Json(RevitMappingService.Capture(doc, elements)));
            // IfcSpace has no Tag attribute. Keep Revit's native export identity so
            // the read-only IFC audit can resolve an Area without guessing by name.
            var spatialIdentities = elements.OfType<Area>().Select(area => new
            {
                element_id = area.Id.IntegerValue, unique_id = area.UniqueId, ifc_entity = "IfcSpace",
                export_id = ExportUtils.GetExportId(doc, area.Id).ToString("N"),
                ifc_guid_override = area.get_Parameter(BuiltInParameter.IFC_GUID)?.AsString()
            }).ToArray();
            var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.IFCExporterService) as SingleServerService;
            var server = service?.GetActiveServerId();
            object materialization = null;
            bool exported = false;
            TransactionStatus rollback;
            using (var transaction = new Transaction(doc, "H-IFC V1 临时物化与导出（回滚）"))
            {
                if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("导出事务未启动。");
                try
                {
                    RevitMappingService.EnsureBridges(doc, elements, directory);
                    materialization = RevitMappingService.Materialize(doc, elements);
                    var viewType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                    var view = View3D.CreateIsometric(doc, viewType.Id);
                    view.IsolateElementsTemporary(elements.Select(e => e.Id).ToArray());
                    view.ConvertTemporaryHideIsolateToPermanent();
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
                schema_version = "hifc.native-export-receipt/1.1", ifc_path = path, ifc_sha256 = Evidence.FileHash(path),
                configuration_path = configPath, configuration_sha256 = Evidence.FileHash(configPath),
                doc_export_calls = 1, ifc_postprocessed = false, source_snapshot_before = before, source_snapshot_after = after,
                model_transaction = rollback.ToString(), exporter_server_id = server?.ToString(), selected_elements = elements.Length,
                spatial_export_identities = spatialIdentities,
                materialization, reference_software_ab_verified = false
            };
            Evidence.Write(Path.ChangeExtension(path, ".receipt.json"), receipt);
            return receipt;
        }
    }
}
