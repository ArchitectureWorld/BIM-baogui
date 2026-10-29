using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Common.Enums;
using Revit.IFC.Common.Utility;
using Revit.IFC.Export.Exporter;
using Revit.IFC.Export.Utility;

namespace Hifc.Revit
{
    // Revit 2020's specialized floor fallback splits disconnected profiles into
    // several IfcSlabs and repeats the full-element Pset on every part. Invoke
    // Autodesk's shipped generic slab writer instead, within Document.Export.
    // It emits one product containing all solids; no output file is rewritten.
    internal sealed class NativeFloorGeometry
    {
        readonly MethodInfo writeSlab = typeof(Exporter).Assembly
            .GetType("Revit.IFC.Export.Exporter.FloorExporter", true)
            .GetMethod("ExportGenericSlab", BindingFlags.Public | BindingFlags.Static);
        readonly List<int> written = new List<int>();
        readonly List<string> errors = new List<string>();
        internal int[] WrittenIds => written.ToArray();
        internal object Evidence => new { strategy="NATIVE_GENERIC_SLAB_SINGLE_CARRIER", element_ids=written.ToArray(), errors=errors.ToArray() };
        internal bool Passed => errors.Count == 0;

        internal bool TryExport(ExporterIFC exporter, Element element, ProductWrapper wrapper)
        {
            if (!(element is Floor)) return false;
            string predefined;
            var info = ExporterUtil.GetExportType(exporter, element, out predefined);
            if (info.ExportInstance != IFCEntityType.IfcSlab ||
                ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(info.ExportInstance)) return false;
            try
            {
                if (writeSlab == null) throw new InvalidOperationException("当前原生 IFC 导出器没有单载体楼板入口。");
                using (var options = GeometryUtil.GetIFCExportGeometryOptions())
                using (var geometry = element.get_Geometry(options))
                {
                    if (geometry == null) throw new InvalidOperationException("楼板几何不可读取。");
                    exporter.PushExportState(element, geometry);
                    try
                    {
                        using (var sub = new SubTransaction(element.Document))
                        {
                            sub.Start();
                            writeSlab.Invoke(null, new object[] { exporter, element, geometry, predefined, wrapper });
                            var slabs = wrapper.GetAllObjects().Count(h => IFCAnyHandleUtil.IsSubTypeOf(h, IFCEntityType.IfcSlab));
                            if (slabs != 1) throw new InvalidOperationException("原生楼板未生成唯一业务载体：" + slabs);
                            if (element.AssemblyInstanceId != ElementId.InvalidElementId)
                                ExporterCacheManager.AssemblyInstanceCache.RegisterElements(element.AssemblyInstanceId, wrapper);
                            if (element.GroupId != ElementId.InvalidElementId)
                                ExporterCacheManager.GroupCache.RegisterElements(element.GroupId, wrapper);
                            sub.RollBack();
                        }
                    }
                    finally
                    {
                        exporter.PopExportState();
                        ExporterStateManager.PreserveElementParameterCache(element, false);
                    }
                }
                written.Add(element.Id.IntegerValue);
                return true;
            }
            catch (Exception error)
            {
                var actual = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                errors.Add(element.Id.IntegerValue + "：" + actual.Message);
                throw new InvalidOperationException("楼板原生导出失败：" + element.Id.IntegerValue, actual);
            }
        }
    }
}
