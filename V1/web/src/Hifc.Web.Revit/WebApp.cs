using System;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Hifc.Web.Revit
{
    public sealed class WebApp : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tab = "HIFC-V1-Web";
            try { application.CreateRibbonTab(tab); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }
            var panel = application.CreateRibbonPanel(tab, "模型属性管理");
            var command = new PushButtonData("HifcV1WebManager", "模型检查\n属性管理", Assembly.GetExecutingAssembly().Location, typeof(WebCommand).FullName) {
                AvailabilityClassName = typeof(WebProjectAvailable).FullName,
                ToolTip = "全面检查当前报规模型，编辑属性并预览写入；写后自动回读和全面复检。",
                LongDescription = "HIFC-V1-Web 使用独立账号、服务器和草稿目录。打开项目后点击此按钮，首次登记手机号及真实姓名，后续自动连接。"
            };
            panel.AddItem(command);
            var statistics = application.CreateRibbonPanel(tab, "统计与报规表");
            Add(statistics, "HifcWebBuildingStatistics", "单体面积\n统计", typeof(WebBuildingStatisticsCommand), "分层核对已写面积、实际几何和计容面积，导出统计 Excel。");
            Add(statistics, "HifcWebSiteStatistics", "总图\n统计", typeof(WebSiteStatisticsCommand), "统计当前总图及所选链接的用地、绿地、道路和停车。");
            Add(statistics, "HifcWebWorkbookExport", "报规表\n导出", typeof(WebWorkbookExportCommand), "按当前模型楼栋和实际楼层生成业务值留空的填写表。");
            Add(statistics, "HifcWebWorkbookImport", "报规表\n导入", typeof(WebWorkbookImportCommand), "匹配报规表与模型，预览并处理已有值冲突。");
            UpdateCoordinator.Start();
            ComponentCoordinator.Start();
            return Result.Succeeded;
        }
        public Result OnShutdown(UIControlledApplication application) { ComponentCoordinator.Stop(); WebCommand.CloseWindow(); return Result.Succeeded; }
        private static void Add(RibbonPanel panel, string id, string title, Type command, string description)
        {
            panel.AddItem(new PushButtonData(id, title, Assembly.GetExecutingAssembly().Location, command.FullName) {
                AvailabilityClassName = typeof(WebProjectAvailable).FullName, ToolTip = description
            });
        }
    }
    public sealed class WebProjectAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication application, CategorySet categories) => application.ActiveUIDocument != null && !application.ActiveUIDocument.Document.IsFamilyDocument;
    }
}
