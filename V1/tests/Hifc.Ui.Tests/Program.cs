using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hifc.Reference;
using Hifc.Revit.Management;

internal sealed class FakeSession : IManagerSession
{
    internal ManagerWindow Window;
    internal ModelSnapshot Snapshot;
    internal int Scans;
    internal int[] Located;
    internal EditRequest LastPreview;
    internal EditRequest[] Written;
    public void Scan() { Scans++; Snapshot.Stage = Window.SelectedStage; Snapshot.RequestedKind = Window.RequestedKind; Window.Accept(Snapshot); }
    public void PrepareAll() { }
    public void Cancel() { Window.MarkStale("扫描已取消"); }
    public void Locate(int[] ids) { Located = ids; }
    public void Preview(EditRequest request) { LastPreview = request; }
    public void Apply(EditPreview preview) { }
    public void Write(EditRequest[] requests) { Written = requests; }
    public void Export(int[] ids, string path) { }
    public void Close() { }
}
internal static class Program
{
    private static int tests, failures;
    private static void Check(string name, Action action)
    { tests++; try { action(); Console.WriteLine("PASS " + name); } catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); } }
    private static void Assert(bool value) { if (!value) throw new Exception("assertion failed"); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
    private static ModelElementCheck Element(ReferenceCatalog catalog, int id, string slot)
    {
        return new ModelElementCheck { Id = id, UniqueId = "ui-fixture-" + id, Name = id == 1001 ? "净用地范围" : "阳台楼板", Category = "楼板", Entity = "IfcSlab", Slot = slot, Classified = true,
            Building = "示例楼栋", Level = "首层", Fields = catalog.ForSlot(slot).Where(p => p.Entity.Split(',').Contains("IfcSlab")).Select(p => ModelChecks.Evaluate(p,
                new[] { new ParameterRead { Name = p.SourceParameter, Origin = "instance", StorageType = "String", HasValue = true,
                    Value = p.RawType == "Boolean" ? "0" : p.RawType == "Real" ? "26.25" : "示例值" } })).ToArray() };
    }
    internal static void Screenshot(ManagerWindow window, string path)
    {
        window.UpdateLayout(); Pump();
        var root = (FrameworkElement)window.Content;
        Assert(root.ActualWidth > 900 && root.ActualHeight > 500);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
    }
    [STAThread]
    public static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "ui-validation"); Directory.CreateDirectory(output);
        var catalog = ReferenceCatalog.FromEmbeddedBaseline();
        var land = Element(catalog, 1001, "规划净用地"); var balcony = Element(catalog, 1002, "绿地");
        land.Fields = land.Fields.Take(2).ToArray(); balcony.Fields = balcony.Fields.Take(1).ToArray(); balcony.Fields[0].State = "未填写";
        var unknown = new ModelElementCheck { Id = 1003, UniqueId = "ui-fixture-1003", Name = "尚未分类绿地楼板", Category = "楼板", Entity = "IfcSlab", Building = "示例楼栋", Level = "二层", SuggestedSlots = new[] { "绿地" } };
        var fake = new FakeSession { Snapshot = new ModelSnapshot { DocumentKey = "standalone-ui-fixture", Title = "界面验收样本（模拟数据）", Stage = "报规", ModelKind = "总图", Complete = true, ScanId = "ui-scan-1", Elements = new[] { land, balcony, unknown } } };
        var window = new ManagerWindow(fake, Path.Combine(output, "profiles")); fake.Window = window;
        window.Show(); Pump();
        Check("window_requires_explicit_stage_before_scanning", () => Assert(fake.Scans == 0));
        Check("scan_without_stage_is_rejected", () => { bool rejected = false; try { window.StartFullCheck(); } catch (InvalidOperationException) { rejected = true; } Assert(rejected && fake.Scans == 0); });
        window.StageSelector.SelectedItem = "报规"; window.StartFullCheck();
        Check("confirmed_phase_starts_scan_without_preselection", () => Assert(fake.Scans == 1 && window.ElementGrid.SelectedItems.Count == 0 && window.VisibleCount == 3));
        Check("overview_separates_parameter_presence_and_values", () => Assert(window.Summary.ElementCount == 3 && window.Summary.ClassifiedCount == 2 && window.Summary.PresentFields == 3 && window.Summary.ValidFields == 2));
        Check("case_missing_reference_cannot_show_ready", () => Assert(!window.Summary.Ready && window.Summary.MissingRequired > 0 && window.Summary.PendingRequirements == 0));
        Check("unclassified_filter_works", () => { window.SetFilter("未分类"); Assert(window.VisibleCount == 1); });
        Check("missing_field_filter_works", () => { window.SetFilter("缺字段"); Assert(window.VisibleCount == 1); });
        Check("text_search_works", () => { window.SetFilter("全部"); window.Search.Text = "1001"; Assert(window.VisibleCount == 1); window.Search.Clear(); });
        Check("classification_preview_uses_panel_targets", () => { window.SetFilter("未分类"); window.ElementGrid.SelectedItem = unknown; window.SlotSelector.SelectedItem = "绿地"; window.PreviewClassification(); Assert(fake.LastPreview.Kind == "分类" && fake.LastPreview.Ids.Single() == 1003); });
        Check("attribute_preview_uses_property_set_and_source", () => { window.SetFilter("全部"); window.ElementGrid.SelectedItem = land; window.SelectProperty(land.Fields[0]); window.NewValue.Text = "40.5"; window.PreviewValue(); Assert(fake.LastPreview.PropertySet == "Pset_规划净用地信息属性集" && fake.LastPreview.Source == land.Fields[0].Source && fake.LastPreview.Value == "40.5"); });
        Check("locate_uses_panel_selection", () => { window.LocateSelected(); Assert(fake.Located.Single() == 1001); });
        Check("stale_scan_blocks_new_edit", () => { window.MarkStale("模型已变化"); bool blocked = false; try { window.PreviewValue(); } catch (InvalidOperationException) { blocked = true; } Assert(blocked); window.Accept(fake.Snapshot); });
        Check("display_filter_never_changes_full_summary", () => { window.Search.Text = "1001"; Assert(window.VisibleCount == 1 && window.Summary.ElementCount == 3); window.Search.Clear(); });
        Check("stage_switch_invalidates_existing_preview", () => { window.ShowPreview(new EditPreview { ScanId = fake.Snapshot.ScanId, Changes = new[] { new EditChange { Id = 1001, Source = "test" } }, Request = new EditRequest { Ids = new[] {1001} } }); Assert(window.CanApply); window.StageSelector.SelectedItem = "施工图"; Assert(!window.CanApply && !window.Current); });
        Check("construction_shows_no_planning_pass", () => { window.StartFullCheck(); Assert(!window.Summary.Ready && window.Summary.Roles.Length == 0); });
        window.StageSelector.SelectedItem = "报规"; window.StartFullCheck();
        Check("partial_snapshot_cannot_enable_writes", () => { fake.Snapshot.Complete = false; window.Accept(fake.Snapshot); bool rejected = false; try { window.ShowPreview(new EditPreview { ScanId = fake.Snapshot.ScanId, Changes = new EditChange[0] }); } catch (InvalidOperationException) { rejected = true; } Assert(rejected && !window.CanApply); fake.Snapshot.Complete = true; window.Accept(fake.Snapshot); });
        Check("type_complete_candidate_stays_out_of_completed_filter", () => {
            unknown.Classified = true; unknown.Slots = new[] { "总平构件" }; unknown.Fields = new[] { new FieldCheck { Property = "构件类型", Binding = "类型", State = "有效" } };
            unknown.Recognition = "成功案例 / Revit Tag 1003；UniqueId 对应；名称别名：绿化 → 绿地";
            window.Accept(fake.Snapshot); window.SetFilter("已完成"); Assert(!window.ElementGrid.Items.Cast<ModelElementCheck>().Contains(unknown));
        });
        Check("selecting_candidate_shows_evidence_and_proposed_class", () => {
            window.SetFilter("未分类"); window.ElementGrid.SelectedItem = unknown;
            Assert(window.DiagnosticText.Contains("UniqueId") && (string)window.SlotSelector.SelectedItem == "绿地");
        });
        Check("role_list_displays_candidate_count", () => {
            var list = (System.Windows.Controls.ListBox)typeof(ManagerWindow).GetField("roleList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(window);
            Assert(list.Items.Cast<object>().Any(item => ((string)item.GetType().GetProperty("Text").GetValue(item)).Contains("候选 1")));
        });
        window.SetFilter("全部");
        Check("standard_window_layout_renders", () => Screenshot(window, Path.Combine(output, "manager-1320.png")));
        Check("minimum_window_layout_renders", () => { window.Width = 1080; window.Height = 650; Pump(); Screenshot(window, Path.Combine(output, "manager-1080.png")); });
        window.Close();
        SimplifiedWorkflowTests.Run(Check, output);
        Console.WriteLine("RESULT failures=" + failures + "; tests=" + tests);
        File.WriteAllText(Path.Combine(output, "ui-tests.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { passed = tests - failures, tests, failures, scope = "Standalone actual WPF window with fake Revit adapter; not live-model API verification" }));
        return failures == 0 ? 0 : 1;
    }
}
