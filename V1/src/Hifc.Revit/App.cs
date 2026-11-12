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
            Add(panel, "HifcV1Prepare", "选中构件\n赋属性", typeof(PrepareCommand), "读取 Revit 当前选择，人工指定业务类目，自动填值并直接编辑；仅写入所选实例，写入后全项目检查。");
            Add(panel, "HifcV1Capture", "导出取数\nJSON", typeof(CaptureCommand), "保存所选构件的实例/类型参数、值、类型及来源证据。");
            Add(panel, "HifcV1AreaStatistics", "单体面积\n统计", typeof(BuildingAreaStatisticsCommand), "按楼层和使用性质统计填报投影、实际几何、计算及计容面积，支持打开的单体切换、构件定位和 Excel 导出。");
            Add(panel, "HifcV1SiteStatistics", "总图\n统计", typeof(SiteStatisticsCommand), "汇总用地、绿地、道路和停车，比较填报与实际几何，展示两种绿地率口径，可选择纳入已加载单体链接。");
            Add(panel, "HifcV1Export", "导出项目\nIFC", typeof(ExportCommand), "默认导出当前项目全部可导出模型，包含面积（Area），无需预选；已分类载体输出对应 HIFC 属性，临时导出改动整体回滚，附导出回执。");
            Add(panel, "HifcV1About", "版本与\n验证范围", typeof(AboutCommand), "Revit 2020 验证版。原软件真实模型 A/B 尚待验证。");
            var workbookPanel = application.CreateRibbonPanel(tab, "报规表");
            Add(workbookPanel, "HifcV1WorkbookExport", "报规表\n导出", typeof(ReportingWorkbookExportCommand), "按当前模型的楼栋和实际楼层生成空白报规表，所有业务填报值留空，供设计师填写。");
            Add(workbookPanel, "HifcV1WorkbookImport", "报规表\n导入", typeof(ReportingWorkbookImportCommand), "读取设计师填写的报规表，通过现有预览、冲突选择和模型属性写入流程导入。");
            var syncPanel = application.CreateRibbonPanel(tab, "平台同步");
            Add(syncPanel, "HifcV1PlatformSyncPreview", "平台同步\n综合报告", typeof(PlatformSync.PlatformSyncCommand), "设置平台、确认对应模型、预览并同步本次成功导出的 IFC；计算核验完成后自动下载综合报告及对应模型说明，支持独立重试。");
            Smoke.Register(application);
            application.ControlledApplication.DocumentSavedAs+=Management.ManagerController.SavedAs;
            return Result.Succeeded;
        }
        private static void Add(RibbonPanel panel, string id, string caption, Type type, string tooltip)
        {
            var data = new PushButtonData(id, caption, typeof(App).Assembly.Location, type.FullName) { ToolTip = tooltip };
            if (type != typeof(AboutCommand)) data.AvailabilityClassName = typeof(ProjectAvailable).FullName;
            panel.AddItem(data);
        }
        public Result OnShutdown(UIControlledApplication application) { application.ControlledApplication.DocumentSavedAs-=Management.ManagerController.SavedAs; Management.SiteStatisticsController.Shutdown(); Management.BuildingAreaStatisticsController.Shutdown(); Management.SelectionController.Shutdown(); Management.ManagerController.Shutdown(); PlatformSync.PlatformSyncController.Shutdown(); return Result.Succeeded; }
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
