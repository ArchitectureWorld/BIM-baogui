using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class SiteStatisticsWindowTests
{
    static void Assert(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    static CheckBox Source(SiteStatisticsWindow window, string key) => window.SourcesPanel.Children.OfType<CheckBox>().Single(b => ((SiteStatisticsSource)b.Tag).Key == key);
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T item) yield return item; foreach (var value in Descendants<T>(child)) yield return value; }
    }
    static string Cell(DataGrid grid, string header, object item)
    {
        var text = new TextBlock { DataContext = item };
        BindingOperations.SetBinding(text, TextBlock.TextProperty, ((DataGridTextColumn)grid.Columns.Single(c => c.Header.ToString() == header)).Binding);
        return text.Text;
    }
    static SiteStatisticsSource[] Sources(bool link = false) => new[] {
        new SiteStatisticsSource { Key = "host-key", Name = "当前总图", IsHost = true, Path = @"D:\项目\总图.rvt" },
        new SiteStatisticsSource { Key = "link-key", Name = "1#楼", Selected = link, Path = @"D:\项目\1号楼.rvt" },
        new SiteStatisticsSource { Key = "unloaded", Name = "2#楼", Available = false, Notes = "链接未加载" }
    };
    static SiteStatisticsWindow Window(bool link = false) { var window = new SiteStatisticsWindow(); window.SetSources(Sources(link)); return window; }
    static SiteStatisticsReport Report(bool link = false, bool complete = true)
    {
        var selection = new[] { new SiteStatisticsSelection { SourceKey = "host-key", ElementIds = new[] { 11, 12, 11 } }, new SiteStatisticsSelection { SourceKey = "link-key", ElementIds = new[] { 21, 22 } } };
        var green = new SiteStatisticsComponent { Key = "g1", Name = "地面绿地", Kind = "绿地", Slot = "绿地", SourceKey = "host-key", SourceNames = "当前总图", ElementId = 11,
            DeclaredArea = 300m, GeometricArea = 280m, DeclaredConvertedArea = 300m, GeometricConvertedArea = 280m, Factor = 1m, ConvertedAreaApplicable = true,
            Difference = 20m, DifferencePercent = 20m / 280m * 100m, ExceedsTolerance = true, Complete = true, Included = true, Status = "已计入", Notes = "填报与几何差异超出本项目5%提示阈值。" };
        var linked = new SiteStatisticsComponent { Key = "g2", Name = "链接屋顶绿化", Kind = "绿地", Slot = "绿地", SourceKey = "link-key", SourceNames = "1#楼", ElementId = 21,
            DeclaredArea = 100m, GeometricArea = 100m, DeclaredConvertedArea = 50m, GeometricConvertedArea = 50m, Factor = 0.5m, ConvertedAreaApplicable = true, Complete = true, Included = true, Status = "已计入" };
        var parking = new SiteStatisticsComponent { Key = "p1", Name = "地面停车", Kind = "停车", Subtype = "机动车", SourceKey = "link-key", SourceNames = "1#楼", ElementId = 22,
            DeclaredAreaApplicable = false, CountApplicable = true, MotorCount = 0m, NonMotorCount = 12m, GeometricArea = 80m, Included = true, Complete = true, Status = "已计入" };
        var pending = new SiteStatisticsComponent { Key = "missing", Name = "绿地待补项", Kind = "绿地", SourceKey = "host-key", SourceNames = "当前总图", ElementId = 99,
            ConvertedAreaApplicable = true, Included = false, Complete = false, Status = "折算系数待补", Notes = "缺少折算系数，仅显示已知小计。" };
        return new SiteStatisticsReport { DocumentKey = "host-key", DocumentTitle = "供养中心总图", SourcePath = @"D:\项目\供养中心\总图.rvt", CapturedUtc = "2026-11-12T05:00:00Z", Sources = Sources(link), Complete = complete,
            Metrics = new[] {
                new SiteStatisticsMetric { Name = "规划总用地", DeclaredValue = 2000m, GeometricValue = 1995m, Unit = "m²", Formula = "总用地单独列示，不与净用地相加。", Complete = true },
                new SiteStatisticsMetric { Name = "规划净用地", DeclaredValue = 1000m, GeometricValue = 980m, Unit = "m²", Formula = "净用地作为绿地率分母。", Complete = true },
                new SiteStatisticsMetric { Name = "绿地率", DeclaredValue = link ? 35m : 30m, GeometricValue = link ? 330m / 980m * 100m : 280m / 980m * 100m, Unit = "%", Formula = "各口径折算绿地 ÷ 对应口径净用地 × 100%。", Complete = complete }
            },
            Rows = new[] {
                new SiteStatisticsRow { Name = "规划总用地", Kind = "规划总用地", SourceNames = "当前总图", DeclaredArea = 2000m, GeometricArea = 1995m, Difference = 5m, DifferencePercent = 5m / 1995m * 100m, Complete = true, IsSubtotal = true },
                new SiteStatisticsRow { Name = "规划净用地", Kind = "规划净用地", SourceNames = "当前总图", DeclaredArea = 1000m, GeometricArea = 980m, Difference = 20m, DifferencePercent = 20m / 980m * 100m, Complete = true, IsSubtotal = true },
                new SiteStatisticsRow { Name = "绿地已知小计", Kind = "绿地", SourceNames = link ? "当前总图、1#楼" : "当前总图", DeclaredArea = link ? 400m : 300m, GeometricArea = link ? 380m : 280m,
                    DeclaredConvertedArea = link ? 350m : 300m, GeometricConvertedArea = link ? 330m : 280m, ConvertedAreaApplicable = true, Difference = 20m, DifferencePercent = 20m / (link ? 380m : 280m) * 100m, ExceedsTolerance = true, Complete = complete, IsSubtotal = true, Selection = selection,
                    Notes = "绿地按来源检查后统计；此处为已知小计，详细说明与原始来源应始终保留在提示和明细中，以便核查并集、重叠及缺失属性。" },
                new SiteStatisticsRow { Name = "道路", Kind = "道路", SourceNames = "当前总图", DeclaredAreaApplicable = false, GeometricArea = 180m, IsSubtotal = true, Complete = true },
                new SiteStatisticsRow { Name = "停车", Kind = "停车", SourceNames = "1#楼", DeclaredAreaApplicable = false, GeometricArea = 80m, CountApplicable = true, MotorCount = 0m, NonMotorCount = 12m, IsSubtotal = true, Complete = true,
                    Selection = new[] { new SiteStatisticsSelection { SourceKey = "link-key", ElementIds = new[] { 22, 21 } }, new SiteStatisticsSelection { SourceKey = "foreign", ElementIds = new[] { 900 } } } }
            },
            Components = link ? new[] { green, linked, parking } : new[] { green }, Pending = complete ? new SiteStatisticsComponent[0] : new[] { pending }, Notes = new[] { "总用地与净用地不相加。", "所选范围以来源复选框为准。" }
        };
    }
    public static void Run(Action<string, Action> check, string output)
    {
        check("site_statistics_sources_require_host_and_explicit_loaded_link_selection", () => {
            var window = Window(); try {
                int requests = 0; string[] keys = null; window.RefreshRequested += value => { requests++; keys = value; };
                Assert(Source(window, "host-key").IsChecked == true && !Source(window, "host-key").IsEnabled, "Current host is optional");
                Source(window, "host-key").IsChecked = false; Assert(Source(window, "host-key").IsChecked == true, "Programmatic host uncheck was accepted");
                Assert(Source(window, "unloaded").IsChecked == false && !Source(window, "unloaded").IsEnabled, "Unloaded link is selectable");
                Source(window, "unloaded").IsChecked = true; Assert(Source(window, "unloaded").IsChecked == false, "Unloaded link was included");
                window.SetSources(Sources()); Assert(requests == 0, "Programmatic sources triggered refresh");
                window.ShowReport(Report()); Source(window, "link-key").IsChecked = true;
                Assert(window.CurrentReport == null && window.RowGrid.Items.Count == 0 && !window.ExportButton.IsEnabled && requests == 0, "Changed scope kept old data or started a premature scan");
                Click(window.RefreshButton); Click(window.RefreshButton);
                Assert(requests == 1 && keys.SequenceEqual(new[] { "host-key", "link-key" }) && !Source(window, "link-key").IsEnabled, "Selected source identities or busy guard were lost");
                window.ShowReport(Report()); Assert(window.CurrentReport == null && !window.RefreshButton.IsEnabled, "Old scope callback replaced selected sources or cleared busy");
                window.ShowReport(Report(true)); Assert(window.CurrentReport != null && window.ExportButton.IsEnabled, "Matching scope result was rejected");
            } finally { window.Close(); }
        });
        check("site_statistics_compact_overview_shows_both_metric_bases_and_independent_land_rows", () => {
            var window = Window(true); try {
                var report = Report(true); window.ShowReport(report); window.Show(); window.UpdateLayout();
                Assert(window.Width == 1200 && window.RowGrid.Columns.Sum(c => c.Width.Value) <= 1120, "Main table no longer fits approximately 1200 pixels");
                Assert(window.MetricGrid.Items.Count == 3 && window.RowGrid.Items.Count == 5 && !report.Rows.Any(r => r.Name == "面积合计"), "Gross and net land were merged or metric bases missing");
                Assert(Cell(window.MetricGrid, "填报口径", report.Metrics[2]) == "35.00" && Cell(window.MetricGrid, "单位", report.Metrics[2]) == "%", "Green rate is not displayed as percentage");
                Assert(Cell(window.MetricGrid, "几何口径", report.Metrics[2]) == (330m / 980m * 100m).ToString("0.00", CultureInfo.InvariantCulture), "Geometric green-rate basis lost");
                Assert(window.RowGrid.ActualHeight > 150 && window.MetricGrid.ActualHeight >= 110, "Metrics or category rows collapsed");
                Assert(((Grid)window.Content).Background != null, "Capture has transparent background");
                Program.Screenshot(window, Path.Combine(output, "site-statistics-overview.png"));
                window.Width = 1000; window.Height = 700; window.UpdateLayout(); Program.Screenshot(window, Path.Combine(output, "site-statistics-narrow.png"));
            } finally { window.Close(); }
        });
        check("site_statistics_missing_zero_and_inapplicable_fields_are_distinct", () => {
            var window = Window(true); try {
                var report = Report(true, false); window.ShowReport(report);
                var green = report.Rows[2]; var road = report.Rows[3]; var parking = report.Rows[4];
                Assert(Cell(window.RowGrid, "机动车\n辆", parking) == "0.00" && Cell(window.RowGrid, "非机动车\n辆", parking) == "12.00", "Real zero parking count was hidden");
                Assert(Cell(window.RowGrid, "机动车\n辆", green) == "—" && Cell(window.RowGrid, "填报折算\nm²", road) == "—" && Cell(window.RowGrid, "填报投影\nm²", road) == "—" && Cell(window.RowGrid, "差异\n%", road) == "—", "Inapplicable fields look missing");
                parking.MotorCount = null; Assert(Cell(window.RowGrid, "机动车\n辆", parking) == "待补", "Missing applicable parking count became zero");
                green.DeclaredConvertedArea = null; Assert(Cell(window.RowGrid, "填报折算\nm²", green) == "待补", "Missing green conversion is hidden");
                var countMetric = new SiteStatisticsMetric { Name = "机动车停车数量", DeclaredValue = 0m, Unit = "辆", GeometricApplicable = false };
                Assert(Cell(window.MetricGrid, "填报口径", countMetric) == "0.00" && Cell(window.MetricGrid, "几何口径", countMetric) == "—", "Parking metric invents a geometric count or hides zero");
                countMetric.GeometricApplicable = true; Assert(Cell(window.MetricGrid, "几何口径", countMetric) == "待补", "Applicable missing metric geometry was hidden");
                Assert(window.PendingGrid.Items.Count == 1 && window.ExportButton.IsEnabled && window.StatusText.Text.Contains("已知小计"), "Incomplete report is hidden or cannot export its warnings");
                Assert(window.RowGrid.RowStyle.Triggers.OfType<DataTrigger>().Any(t => ((Binding)t.Binding).Path.Path == "ExceedsTolerance"), "Tolerance warning style missing");
            } finally { window.Close(); }
        });
        check("site_statistics_busy_stale_and_changes_during_refresh_block_outdated_export", () => {
            var window = Window(); try {
                int exports = 0, refreshes = 0; window.ExportRequested += () => exports++; window.RefreshRequested += keys => refreshes++;
                window.ShowReport(Report()); window.MarkStale("模型变化"); Click(window.ExportButton); Assert(exports == 0 && !window.ExportButton.IsEnabled, "Stale report exported");
                Click(window.RefreshButton); Click(window.RefreshButton); window.MarkStale("读取期间模型变化"); window.ShowReport(Report());
                Assert(refreshes == 1 && !window.ExportButton.IsEnabled, "Refresh lost a model change or ran twice");
                Click(window.RefreshButton); window.ShowReport(Report()); Assert(window.ExportButton.IsEnabled, "Fresh capture remained stale");
                Click(window.ExportButton); Click(window.ExportButton); Assert(exports == 1 && !window.RefreshButton.IsEnabled, "Duplicate export was queued");
                window.SetBusy(false, "导出完成"); Assert(window.RefreshButton.IsEnabled && window.ExportButton.IsEnabled, "Completion failed to unlock controls");
            } finally { window.Close(); }
        });
        check("site_statistics_locate_groups_ids_by_selected_source_and_supports_pending_rows", () => {
            var window = Window(true); try {
                int requests = 0; SiteStatisticsSelection[] selected = null; window.LocateRequested += values => { selected = values; requests++; };
                var report = Report(true, false); window.ShowReport(report); window.RowGrid.SelectedItems.Add(report.Rows[2]); window.RowGrid.SelectedItems.Add(report.Rows[4]);
                Click(window.LocateButton); Click(window.LocateButton);
                Assert(requests == 1 && selected.Length == 2 && selected[0].SourceKey == "host-key" && selected[0].ElementIds.SequenceEqual(new[] { 11, 12 }) && selected[1].SourceKey == "link-key" && selected[1].ElementIds.SequenceEqual(new[] { 21, 22 }), "Category locate lost source identity, duplicates or filtering");
                window.SetBusy(false, "定位完成"); window.DetailTabs.SelectedIndex = 1; window.ComponentGrid.SelectedItems.Add(report.Components[0]); window.ComponentGrid.SelectedItems.Add(report.Components[1]); Click(window.LocateButton);
                Assert(selected.Length == 2 && selected[0].ElementIds.SequenceEqual(new[] { 11 }) && selected[1].ElementIds.SequenceEqual(new[] { 21 }), "Component locate crossed source identities");
                window.SetBusy(false, "定位完成"); window.DetailTabs.SelectedIndex = 2; window.PendingGrid.SelectedItem = report.Pending[0]; Click(window.LocateButton);
                Assert(selected.Length == 1 && selected[0].SourceKey == "host-key" && selected[0].ElementIds.SequenceEqual(new[] { 99 }), "Pending item locate failed");
                window.SetBusy(false, "定位完成"); Source(window, "link-key").IsChecked = false; Click(window.LocateButton); Assert(requests == 3, "Old source selection still locates after scope change");
            } finally { window.Close(); }
        });
        check("site_statistics_full_notes_remain_discoverable_without_widening_primary_table", () => {
            var window = Window(true); try {
                var report = Report(true, false); window.ShowReport(report); window.RowGrid.SelectedItem = report.Rows[2];
                Assert(window.SelectionDetails.Text.Contains(report.Rows[2].Notes) && window.SelectionDetails.Text.Contains(report.Rows[2].SourceNames), "Long notes or sources missing from details");
                var notes = (DataGridTextColumn)window.RowGrid.Columns.Last(); Assert(notes.Width.Value <= 130 && notes.ElementStyle.Setters.OfType<Setter>().Any(s => s.Property == ToolTipService.ToolTipProperty), "Compact notes lack a tooltip");
                window.Show(); window.DetailTabs.SelectedIndex = 2; window.UpdateLayout(); Program.Screenshot(window, Path.Combine(output, "site-statistics-pending.png"));
            } finally { window.Close(); }
        });
        check("site_statistics_host_switch_and_invalid_host_inventory_clear_old_report", () => {
            var window = Window(); try {
                window.ShowReport(Report()); var next = Sources(); next[0].Key = "another-host"; window.SetSources(next); window.ShowReport(Report());
                Assert(window.CurrentReport == null && !window.ExportButton.IsEnabled, "Late previous host report was accepted");
                window.SetSources(new[] { new SiteStatisticsSource { Key = "a", IsHost = true }, new SiteStatisticsSource { Key = "b", IsHost = true } });
                Assert(!window.RefreshButton.IsEnabled && window.StatusText.Text.Contains("唯一"), "Ambiguous host can refresh");
                window.SetSources(new SiteStatisticsSource[0]); Assert(!window.RefreshButton.IsEnabled && !window.ExportButton.IsEnabled, "Empty source inventory retains actions");
            } finally { window.Close(); }
        });
    }
}
