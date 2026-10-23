using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace Hifc.Revit
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet set)
        {
            try { Management.ManagerController.Open(data.Application); return Result.Succeeded; }
            catch (Exception ex) { return CommandScope.Failure(ex, ref message); }
        }
    }
    internal static class CommandScope
    {
        internal static Element[] Selection(UIApplication ui)
        {
            var result = ui.ActiveUIDocument.Selection.GetElementIds().Select(id => ui.ActiveUIDocument.Document.GetElement(id)).Where(e => e != null).ToArray();
            if (result.Length == 0) throw new InvalidOperationException("请先在 Revit 中选择要验证的模型构件。");
            if (result.Any(e => e is ElementType || e.Category == null)) throw new InvalidOperationException("请选择模型实例。");
            if (result.Length > 5000) throw new InvalidOperationException("当前验证版每次最多处理 5000 个选择构件，请分批验证。");
            return result;
        }
        internal static Result Failure(Exception ex, ref string message)
        { message = ex.Message; TaskDialog.Show("H-IFC V1 · 未完成", ex.Message); return Result.Failed; }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class PrepareCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet set)
        {
            try
            {
                Management.SelectionController.Open(data.Application);
                return Result.Succeeded;
            }
            catch (Exception ex) { return CommandScope.Failure(ex, ref message); }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public sealed class CaptureCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet set)
        {
            try
            {
                var elements = CommandScope.Selection(data.Application);
                var dialog = new SaveFileDialog { Title = "保存参考映射取数证据", Filter = "JSON (*.json)|*.json", InitialDirectory = RuntimePaths.OutputDirectory(), FileName = "model-observations-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" };
                if (dialog.ShowDialog() != true) return Result.Cancelled;
                Evidence.Write(dialog.FileName, RevitMappingService.Capture(data.Application.ActiveUIDocument.Document, elements));
                TaskDialog.Show("H-IFC V1", "取数证据已保存：" + dialog.FileName);
                return Result.Succeeded;
            }
            catch (Exception ex) { return CommandScope.Failure(ex, ref message); }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class ExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet set)
        {
            try
            {
                var document = data.Application.ActiveUIDocument?.Document ?? throw new InvalidOperationException("请先打开需要导出的项目。");
                var path=ExportDestinationPicker.Pick(data.Application);
                if(path==null)return Result.Cancelled;
                if (!Management.ExportReviewController.Confirm(data.Application,path)) return Result.Cancelled;
                var scope = NativeExportService.ExportProject(document, path);
                TaskDialog.Show("H-IFC V1", scope.Summary + "\nIFC：" + path + "\n回执：" + Path.ChangeExtension(path, ".receipt.json"));
                return Result.Succeeded;
            }
            catch (Exception ex) { return CommandScope.Failure(ex, ref message); }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public sealed class AboutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet set)
        {
            TaskDialog.Show("H-IFC V1 · " + typeof(App).Assembly.GetName().Version.ToString(3), "Revit 2020 全面检查与属性管理\n1 阶段 → 2 模型类型 → 3 全面检查 → 4 写入 Revit。\n选中构件赋属性：Revit 选择 → 人工分类 → 自动填值及直接编辑 → 写入所选并全面复检。\n模型可读取/计算的值自动填写；红色待写入，橙色已手工写入。\n阶段、类型、检查和修改按模型暂存，重开后全面复检。\n报规依据：成功案例 01；施工图规则待建立，链接源文档单列未覆盖。\n检查报告不等于原软件同模型 A/B 或平台验收。");
            return Result.Succeeded;
        }
    }
}
