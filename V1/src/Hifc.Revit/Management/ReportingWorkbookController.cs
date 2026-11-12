using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.UI;
using Hifc.Reference;
using Microsoft.Win32;

namespace Hifc.Revit.Management
{
    internal static class ReportingWorkbookController
    {
        internal static bool Export(UIApplication ui)
        {
            var document = ui.ActiveUIDocument?.Document ?? throw new InvalidOperationException("请先打开需要导出报规表的项目。");
            var structure = ReportingWorkbookModelReader.Read(document);
            var name = new string(document.Title.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            var picker = new SaveFileDialog {
                Title = "导出当前模型的空白报规表", Filter = "Excel 报规表 (*.xlsx)|*.xlsx", DefaultExt = ".xlsx",
                InitialDirectory = string.IsNullOrWhiteSpace(document.PathName) ? RuntimePaths.OutputDirectory() : Path.GetDirectoryName(document.PathName),
                FileName = name + "_空白报规表_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx", OverwritePrompt = true
            };
            if (picker.ShowDialog() != true) return false;
            var temporary = Path.Combine(Path.GetDirectoryName(picker.FileName), ".hifc-reporting-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                ReportingWorkbookTemplate.WriteForModel(temporary, structure.Kind, structure.BuildingName, structure.LevelNames);
                if (File.Exists(picker.FileName)) File.Replace(temporary, picker.FileName, null);
                else File.Move(temporary, picker.FileName);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            TaskDialog.Show("H-IFC V1 · 报规表已导出", "所有业务填报值已留空，供设计师填写。\n模型类型：" + structure.Kind +
                (structure.Kind == "单体建筑" ? "\n楼栋：" + structure.BuildingName + "\n来源楼层：" + structure.LevelNames.Length : "") + "\n保存位置：" + picker.FileName);
            return true;
        }
    }
}
