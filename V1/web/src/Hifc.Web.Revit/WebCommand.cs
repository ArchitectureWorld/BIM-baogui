using System;
using System.IO;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    [Transaction(TransactionMode.Manual)]
    public sealed class WebCommand : IExternalCommand
    {
        private static WebModelWindow window;
        internal static void CloseWindow() { window?.Close(); window = null; }
        internal static WebModelSession NewSession(UIApplication application, bool statisticsOnly = false)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HIFC", "0.5.0-Web");
            return new WebModelSession(new NativeModelAccess(application, statisticsOnly), new Uri("https://192.168.6.45:8443/"), root);
        }
        internal static WebModelWindow OpenCurrent(UIApplication application)
        {
            var document = application.ActiveUIDocument?.Document ?? throw new InvalidOperationException("请打开项目模型。");
            var key = Canonical.Hash((document.PathName ?? "").ToLowerInvariant() + "|" + document.ProjectInformation.UniqueId);
            if (window?.IsVisible == true && window.Session.DocumentKey != key)
            {
                if (window.Session.Busy) throw new InvalidOperationException("上一模型正在处理，请完成后打开当前模型窗口。");
                window.Close();
            }
            if (window?.IsVisible == true) { window.Activate(); return window; }
            window = new WebModelWindow(NewSession(application));
            new System.Windows.Interop.WindowInteropHelper(window).Owner = application.MainWindowHandle;
            window.Show(); return window;
        }
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try {
                UpdateCoordinator.Start();
                OpenCurrent(data.Application); return Result.Succeeded;
            } catch (Exception ex) { message = ex.Message; TaskDialog.Show("HIFC Web", ex.Message); return Result.Cancelled; }
        }
    }
}
