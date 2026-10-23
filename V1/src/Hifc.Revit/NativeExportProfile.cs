using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Hifc.Revit
{
    internal static class NativeExportProfile
    {
        // A null local scope means Revit's whole-document collector, including
        // spatial objects. An explicit empty list must never expand that scope.
        internal static Dictionary<string, string> BuildOptions(string configPath, int[] localIds = null)
        {
            var options = new Dictionary<string, string> {
                ["ExportRoomsInView"] = "true",
                ["UseActiveViewGeometry"] = "false",
                ["ExportLinkedFiles"] = "false",
                ["ExportUserDefinedPsets"] = "true",
                ["ExportUserDefinedPsetsFileName"] = configPath,
                ["ExportInternalRevitPropertySets"] = "false",
                ["ExportIFCCommonPropertySets"] = "true",
                ["TessellationLevelOfDetail"] = "1",
                ["SitePlacement"] = "2"
            };
            if (localIds != null)
            {
                if (localIds.Length == 0) throw new InvalidOperationException("局部验证导出范围为空。");
                options["ElementsForExport"] = string.Join(";", localIds.Distinct().OrderBy(id => id)
                    .Select(id => id.ToString(CultureInfo.InvariantCulture)));
            }
            return options;
        }
    }
}
