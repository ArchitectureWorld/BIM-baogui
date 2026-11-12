using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hifc.Reference;
using Hifc.Web.Revit;

internal static class StatisticsWindowCases
{
    internal static void Run(Action<string, Action> test)
    {
        test("Web statistics windows bind the shared report DTOs and preserve the Local layout", () => {
            Exception failure = null;
            var thread = new Thread(() => {
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => {
                try
                {
                    var folder = Path.GetFullPath(Path.Combine("V1", "web", ".local", "ui-0432")); Directory.CreateDirectory(folder);
                    var assembly = typeof(WebModelSession).Assembly;
                    var buildingType = assembly.GetType("Hifc.Revit.Management.BuildingAreaStatisticsWindow", true);
                    var building = (Window)Activator.CreateInstance(buildingType, true);
                    buildingType.GetMethod("SetDocuments").Invoke(building, new object[] { new[] { new BuildingAreaDocumentOption { Key = "doc", Name = "1#综合楼", Path = "D:/model.rvt" } }, "doc" });
                    var floor = new BuildingAreaFloor { Key = "floor", Name = "1F", DeclaredProjectionArea = 20.36m, GeometricArea = 20m,
                        CalculatedArea = 10.18m, FloorRatioArea = 10.18m, NonFloorRatioArea = 0, DifferenceArea = .36m, DifferencePercent = 1.8m, Complete = true };
                    buildingType.GetMethod("ShowReport").Invoke(building, new object[] { new BuildingAreaStatisticsReport { DocumentKey = "doc", DocumentTitle = "1#综合楼",
                        BuildingName = "综合楼", BuildingNumber = "1#", CapturedUtc = DateTime.UtcNow.ToString("O"), Floors = new[] { floor },
                        Total = new BuildingAreaFloor { Name = "合计", IsTotal = true, DeclaredProjectionArea = 20.36m, GeometricArea = 20m, CalculatedArea = 10.18m, FloorRatioArea = 10.18m, Complete = true }, Complete = true } });
                    Render(building, Path.Combine(folder, "building.png"));
                    var siteType = assembly.GetType("Hifc.Revit.Management.SiteStatisticsWindow", true);
                    var site = (Window)Activator.CreateInstance(siteType, true);
                    var sources = new[] { new SiteStatisticsSource { Key = "site", Name = "总图", IsHost = true, Selected = true } };
                    siteType.GetMethod("SetSources").Invoke(site, new object[] { sources });
                    siteType.GetMethod("ShowReport").Invoke(site, new object[] { new SiteStatisticsReport { DocumentKey = "site", DocumentTitle = "总图", Sources = sources,
                        Rows = new[] { new SiteStatisticsRow { Key = "land", Name = "规划净用地", Kind = "规划净用地", DeclaredArea = 1000, GeometricArea = 1000, Complete = true } },
                        Metrics = new[] { new SiteStatisticsMetric { Name = "绿地率", DeclaredValue = 25, GeometricValue = 20, Unit = "%", Complete = true } }, Complete = true } });
                    Render(site, Path.Combine(folder, "site.png"));
                }
                catch (Exception ex) { failure = ex; }
                finally { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                }));
                Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) throw failure;
        });
    }
    private static void Render(Window window, string path)
    {
        window.ShowActivated = false; window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000; window.Top = -10000;
        try
        {
            window.Show(); window.UpdateLayout();
            if (window.ActualWidth < 1000 || window.ActualHeight < 500) throw new Exception("Statistics layout was not measured.");
            var texts = Text(window).ToArray();
            if (texts.Any(t => t.StartsWith("Hifc.Reference", StringComparison.Ordinal))) throw new Exception("A statistics DTO type name leaked into the UI.");
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(path)) encoder.Save(output);
        }
        finally { window.Close(); }
    }
    private static System.Collections.Generic.IEnumerable<string> Text(DependencyObject node)
    {
        if (node is TextBlock text) yield return text.Text ?? "";
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Text(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
}
