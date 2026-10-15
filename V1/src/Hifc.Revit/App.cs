using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;

namespace Hifc.Revit
{
    public sealed class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tab = "H-IFC V1";
            try { application.CreateRibbonTab(tab); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }
            var panel = application.CreateRibbonPanel(tab, "模型属性管理");
            Add(panel, "HifcV1Manager", "模型检查\n属性管理", typeof(ManagerCommand), "人工确认阶段，识别模型类型；每次全面检查当前文档并自动生成属性补齐预览，写入后全面复检及报告缺失。");
            Add(panel, "HifcV1Prepare", "准备映射\n参数", typeof(PrepareCommand), "为选择构件设置参考类目并补充缺失的原映射参数；不自动填写业务数值。");
            Add(panel, "HifcV1Capture", "导出取数\nJSON", typeof(CaptureCommand), "保存所选构件的实例/类型参数、值、类型及来源证据。");
            Add(panel, "HifcV1Export", "工规导出\n验证", typeof(ExportCommand), "按参考映射导出所选构件。临时物化与视图在导出后回滚，附导出回执。");
            Add(panel, "HifcV1About", "版本与\n验证范围", typeof(AboutCommand), "Revit 2020 验证版。原软件真实模型 A/B 尚待验证。");
            Smoke.Register(application);
            return Result.Succeeded;
        }
        private static void Add(RibbonPanel panel, string id, string caption, Type type, string tooltip)
        {
            var data = new PushButtonData(id, caption, typeof(App).Assembly.Location, type.FullName) { ToolTip = tooltip };
            if (type != typeof(AboutCommand)) data.AvailabilityClassName = typeof(ProjectAvailable).FullName;
            panel.AddItem(data);
        }
        public Result OnShutdown(UIControlledApplication application) { Management.ManagerController.Shutdown(); return Result.Succeeded; }
    }

    public sealed class ProjectAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication application, Autodesk.Revit.DB.CategorySet categories)
        { return application.ActiveUIDocument != null && !application.ActiveUIDocument.Document.IsFamilyDocument; }
    }

    internal static class RuntimePaths
    {
        internal static string OutputDirectory()
        {
            var workspace = Environment.GetEnvironmentVariable("HIFC_V1_WORKSPACE");
            var settings = Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location), "settings.json");
            if (string.IsNullOrWhiteSpace(workspace) && File.Exists(settings))
            {
                var values = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, string>>(File.ReadAllText(settings));
                if (values != null) values.TryGetValue("workspace_root", out workspace);
            }
            var root = !string.IsNullOrWhiteSpace(workspace) && Path.IsPathRooted(workspace) && Directory.Exists(workspace)
                ? Path.Combine(workspace, "outputs") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HIFC-V1");
            Directory.CreateDirectory(root);
            return root;
        }
        internal static string NewRunDirectory()
        {
            var directory = Path.Combine(OutputDirectory(), DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
