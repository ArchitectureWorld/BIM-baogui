using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIMBaoGui.RevitAddin.Acceptance
{
  [Transaction(TransactionMode.Manual)]
  public sealed class NativeOfficialCarrierProbeCommand : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements)
    {
      try
      {
        string manifestPath = NativeOfficialCarrierProbeService.Execute(
          commandData?.Application
          ?? throw new InvalidOperationException(
            "PROBE_REVIT_APPLICATION_MISSING"));
        TaskDialog.Show(
          "验收载体探针",
          "验收副本已写入并保存。\nSeed manifest：" + manifestPath);
        return Result.Succeeded;
      }
      catch (Exception exception)
      {
        message = exception.Message;
        return Result.Failed;
      }
    }
  }
}
