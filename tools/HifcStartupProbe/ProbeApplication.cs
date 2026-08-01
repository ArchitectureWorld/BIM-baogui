using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.UI;

namespace BIMBaoGui.Diagnostics
{
  public sealed class HifcStartupProbeApplication : IExternalApplication
  {
    private const string VendorAssembly =
      @"C:\Program Files\HIFCTool\REVIT2020\net48\Hust.XAR.Shell.dll";

    public Result OnStartup(UIControlledApplication application)
    {
      try
      {
        WriteLog(InvokeVendorStartup(application));
      }
      catch
      {
        // A diagnostic helper must never become a second Revit startup blocker.
      }
      return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
      return Result.Succeeded;
    }

    internal static string InvokeVendorStartup(UIControlledApplication application)
    {
      if (application == null)
        throw new ArgumentNullException(nameof(application));
      if (VendorRibbonIsPresent(application))
        return "Vendor ribbon is already present; skipped duplicate OnStartup invocation.";
      if (!File.Exists(VendorAssembly))
        return "Vendor assembly is missing: " + VendorAssembly;

      try
      {
        Assembly assembly = Assembly.LoadFrom(VendorAssembly);
        Type type = assembly.GetType("Hust.XAR.Shell.App", true);
        object instance = Activator.CreateInstance(type);
        MethodInfo method = type.GetMethod(
          "OnStartup",
          BindingFlags.Public | BindingFlags.Instance,
          null,
          new[] { typeof(UIControlledApplication) },
          null);
        if (method == null)
          return "Vendor OnStartup(UIControlledApplication) was not found.";

        object result = method.Invoke(instance, new object[] { application });
        return "Vendor OnStartup result: "
          + (result == null ? "<null>" : result.ToString());
      }
      catch (Exception exception)
      {
        return FormatExceptionChain(exception);
      }
    }

    private static bool VendorRibbonIsPresent(UIControlledApplication application)
    {
      try
      {
        string[] panels = application
          .GetRibbonPanels("BIM辅助设计工具")
          .Select(panel => panel.Name ?? string.Empty)
          .ToArray();
        return panels.Contains("数据构建", StringComparer.Ordinal)
          && panels.Contains("数据导出", StringComparer.Ordinal);
      }
      catch
      {
        return false;
      }
    }

    internal static string FormatExceptionChain(Exception exception)
    {
      var builder = new StringBuilder(4096);
      int depth = 0;
      for (Exception current = exception;
        current != null;
        current = current.InnerException)
      {
        builder.Append("Exception[")
          .Append(depth.ToString(CultureInfo.InvariantCulture))
          .Append("]: ")
          .AppendLine(current.GetType().FullName);
        builder.Append("Message: ").AppendLine(current.Message ?? string.Empty);
        builder.AppendLine(current.StackTrace ?? "<no stack>");
        depth++;
      }
      return builder.ToString();
    }

    private static void WriteLog(string message)
    {
      string directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BIMBaoGui",
        "Diagnostics");
      Directory.CreateDirectory(directory);
      string path = Path.Combine(
        directory,
        "HifcStartupProbe-"
          + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
          + ".log");
      File.WriteAllText(
        path,
        "CapturedUtc: "
          + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
          + Environment.NewLine
          + "VendorAssembly: "
          + VendorAssembly
          + Environment.NewLine
          + message,
        new UTF8Encoding(false));
    }
  }
}
