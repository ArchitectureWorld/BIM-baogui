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
                var elements = CommandScope.Selection(data.Application);
                var doc = data.Application.ActiveUIDocument.Document;
                var entities = elements.Select(e => RevitMappingService.Entity(doc, e)).Distinct().ToArray();
                var slots = RevitMappingService.Catalog.Slots().Where(slot => entities.All(entity =>
                    RevitMappingService.Catalog.ForSlot(slot).Any(p => p.Entity.Split(',').Contains(entity)))).ToArray();
                if (slots.Length == 0) throw new InvalidOperationException("所选构件没有共同的参考工规类目。本版支持楼板、墙、屋顶、门窗、房间、常规模型及楼梯段；请按类目分批选择。");
                var window = new RoleWindow(slots, elements.Length);
                new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                if (window.ShowDialog() != true) return Result.Cancelled;
                var directory = RuntimePaths.NewRunDirectory();
                Evidence.Write(Path.Combine(directory, "before.json"), RevitMappingService.Capture(doc, elements));
                using (var tx = new Transaction(doc, "H-IFC V1 准备参考映射参数"))
                {
                    tx.Start();
                    RevitMappingService.Prepare(doc, elements, window.SelectedSlot, directory);
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("参数准备事务未提交。");
                }
                Evidence.Write(Path.Combine(directory, "after.json"), RevitMappingService.Capture(doc, elements));
                TaskDialog.Show("H-IFC V1", "已为 " + elements.Length + " 个选择构件设置类目：" + window.SelectedSlot + "。\n原映射参数已补充；业务数值请填写后执行导出验证。\n记录：" + directory);
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
                var elements = CommandScope.Selection(data.Application);
                var dialog = new SaveFileDialog { Title = "工规参考映射导出验证", Filter = "IFC (*.ifc)|*.ifc", InitialDirectory = RuntimePaths.OutputDirectory(), FileName = "reference-reproduction-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".ifc" };
                if (dialog.ShowDialog() != true) return Result.Cancelled;
                NativeExportService.Export(data.Application.ActiveUIDocument.Document, elements, dialog.FileName);
                TaskDialog.Show("H-IFC V1", "Revit 已导出 IFC，临时模型改动已回滚。\n请用原软件同模型导出文件执行 A/B 对照。\nIFC：" + dialog.FileName + "\n回执：" + Path.ChangeExtension(dialog.FileName, ".receipt.json"));
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
            TaskDialog.Show("H-IFC V1 · 0.4.0", "Revit 2020 全面检查与属性管理\n1 阶段 → 2 模型类型 → 3 全面检查 → 4 写入 Revit。\n模型可读取/计算的值自动填写；在右侧直接修改，红色待写入，橙色已手工写入。\n阶段、类型、检查和修改按模型暂存，重开后全面复检。\n报规依据：成功案例 01；施工图规则待建立，链接源文档单列未覆盖。\n检查报告不等于原软件同模型 A/B 或平台验收。");
            return Result.Succeeded;
        }
    }
}
