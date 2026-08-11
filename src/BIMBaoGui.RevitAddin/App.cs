using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.RevitAddin.McpBridge;

namespace BIMBaoGui.RevitAddin
{
  public sealed class App : IExternalApplication
  {
    private const string RibbonTabName = "湖北BIM报规";
    private const string RibbonPanelName = "报规工作台";

    public static readonly DockablePaneId WorkspacePaneId =
      new DockablePaneId(new Guid("37B3D929-E93E-4A5B-9C09-B4735FC62699"));

    public Result OnStartup(UIControlledApplication application)
    {
      if (application == null) return Result.Failed;

      try
      {
        try
        {
          application.CreateRibbonTab(RibbonTabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
          // Revit throws when another loaded version already created the tab.
        }

        RibbonPanel panel = application
          .GetRibbonPanels(RibbonTabName)
          .FirstOrDefault(item => string.Equals(
            item.Name,
            RibbonPanelName,
            StringComparison.Ordinal))
          ?? application.CreateRibbonPanel(RibbonTabName, RibbonPanelName);

        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        var buttonData = new PushButtonData(
          "BIMBaoGui.OpenWorkspace",
          "打开报规\n工作台",
          assemblyPath,
          typeof(ShowWorkspaceCommand).FullName)
        {
          ToolTip = "打开独立的 Revit 2020 BIM 报规工作台。"
        };
        panel.AddItem(buttonData);

        application.RegisterDockablePane(
          WorkspacePaneId,
          "湖北BIM报规",
          WorkspaceDockablePaneProvider.Instance);

        try
        {
          RevitExternalEventDispatcher.EnsureInitialized();
          McpBridgeHost.Start();
        }
        catch (Exception exception)
        {
          // MCP is a sidecar. Its failure must not disable the manual workspace.
          McpBridgeHost.RecordStartupFailure(exception);
        }
        return Result.Succeeded;
      }
      catch
      {
        return Result.Failed;
      }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
      McpBridgeHost.Stop();
      RevitExternalEventDispatcher.Dispose();
      return Result.Succeeded;
    }
  }

  [Transaction(TransactionMode.Manual)]
  public sealed class ShowWorkspaceCommand : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements)
    {
      try
      {
        if (commandData == null || commandData.Application == null)
          throw new InvalidOperationException("Revit UIApplication 不可用。");

        RevitExternalEventDispatcher.EnsureInitialized();
        DockablePane pane = commandData.Application.GetDockablePane(
          App.WorkspacePaneId);
        pane.Show();
        WorkspaceDockablePaneProvider.Instance.Control.RequestRefresh();
        return Result.Succeeded;
      }
      catch (Exception exception)
      {
        message = exception.Message;
        return Result.Failed;
      }
    }
  }

  internal sealed class WorkspaceDockablePaneProvider
    : IDockablePaneProvider
  {
    private static readonly Lazy<WorkspaceDockablePaneProvider> LazyInstance =
      new Lazy<WorkspaceDockablePaneProvider>(
        () => new WorkspaceDockablePaneProvider());

    private WorkspaceDockablePaneProvider()
    {
      Control = new WorkspaceControl();
    }

    internal static WorkspaceDockablePaneProvider Instance =>
      LazyInstance.Value;

    internal WorkspaceControl Control { get; }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
      if (data == null) throw new ArgumentNullException(nameof(data));
      data.FrameworkElement = Control;
      data.InitialState = new DockablePaneState
      {
        DockPosition = DockPosition.Right
      };
    }
  }
}
