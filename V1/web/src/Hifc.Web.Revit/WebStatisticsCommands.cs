using System;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Hifc.Web.Revit
{
    internal static class WebStatisticsCommands
    {
        internal static Result Open(ExternalCommandData data, string kind, ref string message)
        {
            try { NativeStatisticsController.Open(data.Application, WebCommand.NewSession(data.Application, true), kind); return Result.Succeeded; }
            catch (Exception ex) { message = ex.Message; TaskDialog.Show("HIFC Web 统计", message); return Result.Cancelled; }
        }
        internal static Result Workbook(ExternalCommandData data, bool export, ref string message)
        {
            try
            {
                var window = WebCommand.OpenCurrent(data.Application);
                window.Dispatcher.BeginInvoke(new Action(async () => {
                    try { if (export) await window.ExportWorkbookCommandAsync(); else await window.ImportWorkbookAsync(); }
                    catch (Exception ex) { TaskDialog.Show("HIFC Web 报规表", ex.Message); }
                }));
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; TaskDialog.Show("HIFC Web 报规表", message); return Result.Cancelled; }
        }
    }
    [Transaction(TransactionMode.Manual)]
    public sealed class WebBuildingStatisticsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => WebStatisticsCommands.Open(data, "building", ref message);
    }
    [Transaction(TransactionMode.Manual)]
    public sealed class WebSiteStatisticsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => WebStatisticsCommands.Open(data, "site", ref message);
    }
    [Transaction(TransactionMode.Manual)]
    public sealed class WebWorkbookExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => WebStatisticsCommands.Workbook(data, true, ref message);
    }
    [Transaction(TransactionMode.Manual)]
    public sealed class WebWorkbookImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => WebStatisticsCommands.Workbook(data, false, ref message);
    }
}
