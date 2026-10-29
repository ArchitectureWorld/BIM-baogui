using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.Windows.Threading;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;

internal static class NativeWindowSmokeCases
{
    internal static void Run(Action<string, Action> test)
    {
        test("calculation underground and materials windows render LiquidGlass labels and usable columns without a Revit host", () => {
            Exception failure = null;
            var worker = new Thread(() => {
                try {
                    var root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "V1", "web", ".local", "native-window-smoke")); Directory.CreateDirectory(root);
                    var result = new CheckResult { ModelKind = "单体建筑", RuleProfiles = new[] { new RuleProfileInfo { Id = "test-profile", Title = "平台规则方案（离线界面夹具）", Scope = "用于构造界面，不代表服务器业务验收。" } },
                        CommonFields = new[] { new FieldResult { Key = "建筑区域|是否计容", Slot = "建筑区域", Property = "是否计容", RawType = "Boolean", InputKind = "boolean", Editable = true },
                            new FieldResult { Key = "建筑技术|建筑用途", Slot = "建筑技术", Property = "建筑用途", RawType = "Text", InputKind = "editable-choice", Options = new[] { "住宅", "公共建筑" }, Editable = true } },
                        CalculationIssues = new[] { "夹具：人工范围尚待确认。" }, Elements = new[] {
                            new ElementResult { Element = new ElementRecord { Id = 101, UniqueId = "floor", Name = "地上建筑区域", Level = "首层", Entity = "IfcSlab", ProjectedAreaM2 = 1250 }, CandidateSlots = new[] { "建筑区域" }, CandidateEvidence = new[] { "水平楼板投影" }, Identity = "identity" },
                            new ElementResult { Element = new ElementRecord { Id = 102, UniqueId = "b1", Name = "地下一层", Entity = "IfcBuildingStorey", LevelElevationM = -4.2 } },
                            new ElementResult { Element = new ElementRecord { Id = 103, UniqueId = "l1", Name = "首层", Entity = "IfcBuildingStorey", LevelElevationM = 0 } },
                            new ElementResult { Element = new ElementRecord { Id = 104, UniqueId = "roof", Name = "屋面上边界", Entity = "IfcBuildingStorey", LevelElevationM = 6 } }
                        } };
                    var settings = new ModelSettings { RuleProfileId = "test-profile", LevelsConfirmed = true, BuildingLevelUniqueIds = new[] { "l1" }, TopBoundaryLevelUniqueId = "roof",
                        Underground = new UndergroundSelection { Confirmed = true, GroundElevationM = 0, LevelUniqueIds = new[] { "b1" }, BaseLevelUniqueId = "l1" } };
                    var assembly = typeof(ProfileStore).Assembly;
                    var planning = (Window)Activator.CreateInstance(assembly.GetType("Hifc.Web.Revit.NativeSettingsWindow", true), BindingFlags.Instance | BindingFlags.NonPublic, null,
                        new object[] { result, settings, new Func<int[], Task>(_ => Task.CompletedTask) }, null);
                    try {
                        OpenOffscreen(planning);
                        if (!FindAll<TextBlock>(planning).Any(t => t.Text == result.RuleProfiles[0].Title)) throw new Exception("Rule profile displays a CLR type name instead of its label.");
                        Snapshot(planning, Path.Combine(root, "calculation-range.png"));
                        var tabs = Find<TabControl>(planning); tabs.SelectedIndex = 1; planning.UpdateLayout(); Snapshot(planning, Path.Combine(root, "common-values.png"));
                        if (FindAll<DataGrid>(planning).Single().Columns.Last().ActualWidth < 300) throw new Exception("Common value editor column collapsed.");
                        if (FindAll<DataGrid>(planning).Single().Columns.Take(2).Any(c => c.ActualWidth < 150)) throw new Exception("Common field labels collapsed after switching tabs.");
                        tabs.SelectedIndex = 2; planning.UpdateLayout(); Snapshot(planning, Path.Combine(root, "building-levels.png"));
                    } finally { planning.Close(); }
                    var underground = (Window)Activator.CreateInstance(assembly.GetType("Hifc.Web.Revit.NativeUndergroundWindow", true), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { result, settings.Underground }, null);
                    try { OpenOffscreen(underground); Snapshot(underground, Path.Combine(root, "underground.png")); } finally { underground.Close(); }
                    var materialType = assembly.GetType("Hifc.Web.Revit.NativeMaterialWindow", true);
                    var materialSession = new MaterialSession(result);
                    var material = (Window)Activator.CreateInstance(materialType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { materialSession, "workbook" }, null);
                    try {
                        OpenOffscreen(material);
                        materialType.GetField("path", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(material, "界面夹具.xlsx");
                        materialType.GetField("content", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(material, "AA==");
                        ((Task)materialType.GetMethod("Preview", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(material, null)).GetAwaiter().GetResult();
                        material.UpdateLayout(); Snapshot(material, Path.Combine(root, "workbook-conflicts.png"));
                        Find<TabControl>(material).SelectedIndex = 1; material.UpdateLayout(); Snapshot(material, Path.Combine(root, "workbook-references.png"));
                    } finally { material.Close(); }
                    result.ProjectData = new[] { new ProjectDataRecord { SchemaId = "b238a635-91f4-46cc-ac56-f41b7adac784", SchemaName = "HifcManualRedlineV1",
                        Json = "{\"Unit\":1,\"Frame\":2,\"Axes\":1,\"Accepted\":true,\"CoordinateSystemName\":\"人工图纸坐标系（界面夹具）\",\"Points\":[{\"Sequence\":1,\"X\":100,\"Y\":200,\"SourceCellX\":\"B4\",\"SourceCellY\":\"C4\"},{\"Sequence\":2,\"X\":120,\"Y\":200,\"SourceCellX\":\"B5\",\"SourceCellY\":\"C5\"}]}" } };
                    var redline = (Window)Activator.CreateInstance(materialType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { materialSession, "redline" }, null);
                    try { OpenOffscreen(redline); Find<TabControl>(redline).SelectedIndex = 2; redline.UpdateLayout(); Snapshot(redline, Path.Combine(root, "redline-coordinates.png")); }
                    finally { redline.Close(); }
                    if (Directory.GetFiles(root, "*.png").Any(p => new FileInfo(p).Length < 1024)) throw new Exception("A window rendering was empty.");
                } catch (Exception ex) { failure = ex; }
            });
            worker.SetApartmentState(ApartmentState.STA); worker.IsBackground = true; worker.Start();
            if (!worker.Join(30000)) throw new Exception("Window construction did not finish in 30 seconds.");
            if (failure != null) throw new Exception("WPF construction failed: " + failure.Message, failure);
        });
    }
    private static void OpenOffscreen(Window window) { window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000; window.ShowActivated = false; window.ShowInTaskbar = false; window.Show(); window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); }
    private static T Find<T>(DependencyObject parent) where T : DependencyObject { if (parent is T found) return found; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var item = Find<T>(VisualTreeHelper.GetChild(parent, i)); if (item != null) return item; } return null; }
    private static IEnumerable<T> FindAll<T>(DependencyObject parent) where T : DependencyObject { if (parent is T found) yield return found; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var item in FindAll<T>(VisualTreeHelper.GetChild(parent, i))) yield return item; }
    private static void Snapshot(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(path)) encoder.Save(output);
    }
    private sealed class MaterialSession : INativeMaterialSession
    {
        public CheckResult Result { get; }
        public MaterialSession(CheckResult result) { Result = result; }
        public Task AcceptMaterialAsync(MaterialPreview preview, MaterialDecision decision, string sourcePath) => Task.CompletedTask;
        public Task<MaterialPreview> PreviewMaterialAsync(MaterialPreviewRequest request) => Task.FromResult(new MaterialPreview { Id = "layout-fixture", Kind = "workbook", FileName = request.FileName,
            Tables = new[] { new MaterialTableInfo { Id = "table", Title = "建筑面积报规表（界面夹具）" } }, Sha256 = new string('a', 64), Issues = new[] { "此数据仅验证界面布局，不代表业务核对通过。" },
            Changes = new[] { new MaterialChange { Key = "area", ElementName = "综合楼", Property = "建筑面积", CurrentValue = "1250", Value = "1280", SourceLocation = "Sheet1!E12", IsConflict = true, CanSelect = true, Selected = false, Issue = "与模型已填值不同；请核对来源后选择。" } },
            References = new[] { new MaterialReference { Field = "计容建筑面积", Value = "1280", Unit = "m²", Status = "参考资料", SourceLocation = "Sheet1!E12" } } });
    }
}
