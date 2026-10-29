using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Hifc.Revit.Management
{
    internal static class ExportReviewController
    {
        // Both interactive export entry points pass through this guard and review.
        // The service remains UI-free for native acceptance and controlled batch runs.
        internal static bool Confirm(UIApplication ui,string outputPath=null)
        {
            var doc=ui.ActiveUIDocument.Document;
            ManagerController.EnsureExportReady(doc);
            var review=NativeExportService.ReviewProject(doc);review.OutputPath=outputPath;
            var dialog=new ExportReviewWindow(review);
            new System.Windows.Interop.WindowInteropHelper(dialog).Owner=ui.MainWindowHandle;
            dialog.LocateRequested+=ids=>{
                var targets=ids.Select(id=>doc.GetElement(new ElementId(id))).Where(e=>e!=null).ToArray();
                ui.ActiveUIDocument.Selection.SetElementIds(targets.Select(e=>e.Id).ToArray());
                var geometry=targets.Where(e=>!(e is ProjectInfo)&&!(e is Level)).Select(e=>e.Id).ToArray();
                if(geometry.Length>0)ui.ActiveUIDocument.ShowElements(geometry);
            };
            return dialog.ShowDialog()==true;
        }
    }
}
