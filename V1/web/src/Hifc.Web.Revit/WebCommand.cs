using System;
using System.IO;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Hifc.Web.Revit
{
    [Transaction(TransactionMode.Manual)]
    public sealed class WebCommand : IExternalCommand
    {
        private static WebModelWindow window;
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try {
                if (window?.IsVisible == true) { window.Activate(); return Result.Succeeded; }
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HIFC", "0.5.0-Web");
                var session = new WebModelSession(new NativeModelAccess(data.Application), new Uri("https://192.168.6.45:8443/"), root);
                window = new WebModelWindow(session); new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                window.Show(); return Result.Succeeded;
            } catch (Exception ex) { message = ex.Message; TaskDialog.Show("HIFC Web", ex.Message); return Result.Cancelled; }
        }
    }
}
