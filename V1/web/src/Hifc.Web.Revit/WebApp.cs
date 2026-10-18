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
            return Result.Succeeded;
        }
        public Result OnShutdown(UIControlledApplication application) { WebCommand.CloseWindow(); return Result.Succeeded; }
    }
    public sealed class WebProjectAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication application, CategorySet categories) => application.ActiveUIDocument != null && !application.ActiveUIDocument.Document.IsFamilyDocument;
    }
}
