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

internal static class BuildingAreaStatisticsWindowTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    static string Cell(DataGrid grid, string header, object item)
    {
        var text = new TextBlock { DataContext = item };
        BindingOperations.SetBinding(text, TextBlock.TextProperty, ((DataGridTextColumn)grid.Columns.Single(c => c.Header.ToString() == header)).Binding);
        return text.Text;
    }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    static BuildingAreaStatisticsWindow Window()
    {
        var window = new BuildingAreaStatisticsWindow();
        window.SetDocuments(new[] {
            new BuildingAreaDocumentOption { Key = "opaque-a", Name = "1# 供养楼", Path = @"D:\项目\1号.rvt" },
            new BuildingAreaDocumentOption { Key = "opaque-b", Name = "2# 服务楼", Path = @"D:\项目\2号.rvt" }
        }, "opaque-a");
        return window;
    }
    static BuildingAreaStatisticsReport Report(string key = "opaque-a", bool complete = true)
    {
        var one = new BuildingAreaComponent { ElementId = 11, Name = "一层面积块", LevelName = "1F", FloorKey = "one", Function = "居住", Included = true,
            CalculationFactor = 1m, FloorRatioFactor = 1m, DeclaredProjectionArea = 100m, GeometricArea = 111.125m, CalculatedArea = 100m, FloorRatioArea = 100m, NonFloorRatioArea = 0m, DifferenceArea = -11.125m, DifferencePercent = -11.125m / 111.125m * 100m, ExceedsTolerance = true, Complete = true };
        var corridor = new BuildingAreaComponent { ElementId = 12, Name = "一层公共走道", LevelName = "1F", FloorKey = "one", Function = "公共走道", Included = true,
            CalculationFactor = 1m, FloorRatioFactor = 0m, DeclaredProjectionArea = 20m, GeometricArea = 20m, CalculatedArea = 20m, FloorRatioArea = 0m, NonFloorRatioArea = 20m, DifferenceArea = 0m, DifferencePercent = 0m, Complete = true };
        var two = new BuildingAreaComponent { ElementId = 22, Name = "二层面积块", LevelName = "2F", FloorKey = "two", Function = "办公", Included = true,
            CalculationFactor = 1m, FloorRatioFactor = 1m, DeclaredProjectionArea = 80m, GeometricArea = 80m, CalculatedArea = 80m, FloorRatioArea = 80m, NonFloorRatioArea = 0m, DifferenceArea = 0m, DifferencePercent = 0m, Complete = true };
        var excluded = new BuildingAreaComponent { ElementId = 99, Name = "待识别面积块", Included = false, Complete = false, Status = "待确认楼层", Notes = "楼层映射缺失，未纳入合计。" };
        return new BuildingAreaStatisticsReport {
            DocumentKey = key, DocumentTitle = "1# 供养楼", BuildingName = "供养楼", BuildingNumber = "1#", SourcePath = @"D:\项目\供养中心\1号供养楼.rvt", CapturedUtc = "2026-11-12T02:30:00Z", Complete = complete,
            ScopeDescription = "当前单体面积载体；不含其他模型。", FormulaDescription = "计算面积 = 填报投影 × 计算系数；计容面积 = 填报投影 × 计容系数；差额 = 填报投影 − 实际几何；差异率 = 差额 / 实际几何。", Notes = new[] { "人工填写面积保留。", "缺项保留待核对状态。" },
            Floors = new[] {
                new BuildingAreaFloor { Key = "one", Name = "1F", HeightM = 3.6m, FunctionSummary = "居住：填报 100.00 m²；计算 100.00 m²；计容 100.00 m²\n公共走道：填报 20.00 m²；计算 20.00 m²；不计容 20.00 m²", ElementIds = new[] { 11, 12, 11 }, DeclaredProjectionArea = 120m, GeometricArea = 131.125m, CalculatedArea = 120m, FloorRatioArea = 100m, NonFloorRatioArea = 20m, DifferenceArea = -11.125m, DifferencePercent = -11.125m / 131.125m * 100m, ExceedsTolerance = true, Notes = "几何差异超出容差", Complete = true },
                new BuildingAreaFloor { Key = "two", Name = "2F", HeightM = complete ? 3.6m : (decimal?)null, FunctionSummary = "办公：填报 80.00 m²；计算 80.00 m²；计容 80.00 m²", ElementIds = new[] { 22 }, DeclaredProjectionArea = 80m, GeometricArea = 80m, CalculatedArea = 80m, FloorRatioArea = 80m, NonFloorRatioArea = 0m, DifferenceArea = 0m, DifferencePercent = 0m, Complete = complete, Notes = complete ? "" : "层高待核对" }
            },
            Total = new BuildingAreaFloor { Name = "合计", IsTotal = true, FunctionSummary = "本楼栋纳入统计的全部功能", DeclaredProjectionArea = 200m, GeometricArea = 211.125m, CalculatedArea = 200m, FloorRatioArea = 180m, NonFloorRatioArea = 20m, DifferenceArea = -11.125m, DifferencePercent = -11.125m / 211.125m * 100m, ExceedsTolerance = true, Complete = complete },
            Components = new[] { one, corridor, two, excluded }, Pending = complete ? new BuildingAreaComponent[0] : new[] { excluded },
            Comparisons = new[] { new BuildingAreaComparison { Name = "总建筑面积", AttributeValue = 190m, CalculatedValue = 200m, Difference = -10m, DifferencePercent = -10m / 200m * 100m, ExceedsTolerance = true, Notes = "项目属性与统计值不一致" } }
        };
    }

    public static void Run(Action<string, Action> check, string output)
    {
        check("building_area_window_has_floor_amounts_pinned_total_and_secondary_detail_tabs", () => {
            var window = Window(); try {
                var report = Report(); window.ShowReport(report);
                var headers = window.FloorGrid.Columns.Select(c => c.Header.ToString()).ToArray();
                foreach (var title in new[] { "楼层", "层高 m", "使用性质及计算面积", "申报投影 m²", "实际几何 m²", "计算面积 m²", "计容面积 m²", "不计容 m²", "几何差值 m²", "差异 %", "备注" })
                    Assert(headers.Contains(title), "Missing area column: " + title);
                Assert(window.TotalGrid.Items.Count == 1 && ReferenceEquals(window.TotalGrid.Items[0], report.Total), "Pinned total is absent or replaced by recomputed values");
                Assert(window.TotalGrid.Columns.Count == window.FloorGrid.Columns.Count && window.TotalGrid.FontWeight == FontWeights.SemiBold, "Total does not retain the floor columns and visual emphasis");
                Assert(window.DetailTabs.Items.Count == 4 && window.ComponentGrid.Items.Count == 4 && window.ComparisonGrid.Items.Count == 1, "Component details or project comparisons missing");
                window.Show(); window.UpdateLayout();
                Assert(Descendants<TextBlock>(window.DocumentSelector).Any(t => t.Text == "1# 供养楼"), "Document selector shows a CLR type instead of the model name");
                Assert(((Grid)window.Content).Background != null, "Standalone capture has no readable surface");
                Assert(window.TotalGrid.ActualHeight >= 38 && window.FloorGrid.ActualHeight > 100, "Floor content or pinned total collapsed");
                Assert(window.Width == 1200 && window.FloorGrid.Columns.Sum(c => c.Width.Value) <= 1120, "Primary area table no longer fits the compact window");
                Program.Screenshot(window, Path.Combine(output, "building-area-statistics.png"));
                window.Width = 1000; window.Height = 650; window.UpdateLayout();
                Program.Screenshot(window, Path.Combine(output, "building-area-statistics-narrow.png"));
            } finally { window.Close(); }
        });
        check("building_area_floor_function_breakdown_wraps_and_grows_without_hiding_items", () => {
            var window = Window(); try {
                var report = Report(); window.ShowReport(report); window.Show(); window.UpdateLayout();
                Program.Screenshot(window, Path.Combine(output, "building-area-statistics-multifunction.png"));
                var text = Descendants<TextBlock>(window.FloorGrid).Single(t => t.Text == report.Floors[0].FunctionSummary);
                Assert(text.TextWrapping == TextWrapping.Wrap && text.TextTrimming == TextTrimming.None, "Primary function breakdown is truncated");
                Assert(text.ActualHeight > 38 && text.ActualHeight >= text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom - 0.5, "Function breakdown is clipped by fixed row height");
                var row = (DataGridRow)window.FloorGrid.ItemContainerGenerator.ContainerFromIndex(0);
                Assert(double.IsNaN(window.FloorGrid.RowHeight) && row.ActualHeight >= text.ActualHeight, "Floor row did not grow with multiple function lines");
                foreach (var value in report.Components.Where(c => c.Included).Cast<BuildingAreaAmounts>().Concat(report.Floors).Concat(new[] { report.Total })) {
                    Assert(value.DifferenceArea == value.DeclaredProjectionArea - value.GeometricArea, "Fixture uses the wrong difference direction");
                    Assert(value.DifferencePercent == value.DifferenceArea / value.GeometricArea * 100m, "Fixture uses the wrong percentage denominator");
                }
                Assert(report.Floors.Sum(f => f.DeclaredProjectionArea.Value) == report.Total.DeclaredProjectionArea && report.Total.NonFloorRatioArea == 20m,
                    "Floor subtotals and building total disagree");
            } finally { window.Close(); }
        });
        check("building_area_document_switch_clears_old_data_requests_once_and_ignores_late_report", () => {
            var window = Window(); try {
                int requests = 0; string requested = null;
                window.RefreshRequested += key => { requests++; requested = key; };
                window.SetDocuments(new[] { new BuildingAreaDocumentOption { Key = "opaque-a", Name = "1#" }, new BuildingAreaDocumentOption { Key = "opaque-b", Name = "2#" } }, "opaque-a");
                Assert(requests == 0, "Programmatic SetDocuments triggered refresh");
                window.ShowReport(Report()); window.DocumentSelector.SelectedValue = "opaque-b";
                Assert(requests == 1 && requested == "opaque-b" && window.CurrentReport == null && window.FloorGrid.Items.Count == 0 && window.TotalGrid.Items.Count == 0,
                    "Switch left old content or misrouted refresh: requests=" + requests + ", requested=" + requested + ", selected=" + window.SelectedDocumentKey + ", floors=" + window.FloorGrid.Items.Count + ", total=" + window.TotalGrid.Items.Count);
                window.ShowReport(Report("opaque-a"));
                Assert(window.CurrentReport == null && !window.ExportButton.IsEnabled && !window.RefreshButton.IsEnabled, "Late report from old document replaced current document or cleared busy");
                window.ShowReport(Report("opaque-b"));
                Assert(window.CurrentReport.DocumentKey == "opaque-b" && window.ExportButton.IsEnabled, "Selected document report not accepted");
                window.SetDocuments(new BuildingAreaDocumentOption[0], null);
                Assert(window.CurrentReport == null && !window.ExportButton.IsEnabled && !window.RefreshButton.IsEnabled, "Closed document left exportable results");
            } finally { window.Close(); }
        });
        check("building_area_busy_and_stale_reports_block_duplicate_or_outdated_export", () => {
            var window = Window(); try {
                int refresh = 0, exports = 0; string exportKey = null;
                window.RefreshRequested += key => refresh++; window.ExportRequested += key => { exports++; exportKey = key; };
                window.ShowReport(Report()); window.MarkStale("模型面积块已变化，请刷新。");
                Click(window.ExportButton); Assert(exports == 0 && !window.ExportButton.IsEnabled, "Stale report exported");
                Click(window.RefreshButton); Click(window.RefreshButton);
                Assert(refresh == 1 && !window.DocumentSelector.IsEnabled && !window.ExportButton.IsEnabled, "Busy refresh allowed duplicate or document mutation");
                window.ShowReport(Report()); Assert(window.ExportButton.IsEnabled, "Fresh report did not restore export");
                Click(window.ExportButton); Click(window.ExportButton);
                Assert(exports == 1 && exportKey == "opaque-a" && !window.RefreshButton.IsEnabled, "Duplicate export or wrong document key");
                window.SetBusy(false, "已导出"); Assert(window.ExportButton.IsEnabled, "Completed export did not unlock UI");
                Click(window.RefreshButton); window.MarkStale("读取期间模型变化"); window.ShowReport(Report());
                Assert(!window.ExportButton.IsEnabled && window.StatusText.Text.Contains("刷新"), "Model change during refresh was lost");
                Click(window.RefreshButton); window.ShowReport(Report()); Assert(window.ExportButton.IsEnabled, "A subsequent fresh capture remained stale");
            } finally { window.Close(); }
        });
        check("building_area_locate_uses_only_current_document_and_selected_floor_component_or_pending_ids", () => {
            var window = Window(); try {
                int[] ids = null; string locatedKey = null; int requests = 0;
                window.LocateRequested += (key, selected) => { locatedKey = key; ids = selected; requests++; };
                window.ShowReport(Report(complete: false));
                window.FloorGrid.SelectedItems.Add(window.FloorGrid.Items[0]); window.FloorGrid.SelectedItems.Add(window.FloorGrid.Items[1]);
                Click(window.LocateButton); Click(window.LocateButton);
                Assert(requests == 1 && locatedKey == "opaque-a" && ids.SequenceEqual(new[] { 11, 12, 22 }), "Floor locate lost identity, duplicate filtering or multiple selection");
                window.SetBusy(false, "定位完成"); window.TotalGrid.SelectedItem = window.TotalGrid.Items[0]; Click(window.LocateButton);
                Assert(ids.SequenceEqual(new[] { 11, 12, 22 }), "Total locate included excluded or unrelated components");
                window.SetBusy(false, "定位完成"); window.DetailTabs.SelectedIndex = 1;
                window.ComponentGrid.SelectedItems.Add(window.ComponentGrid.Items[0]); window.ComponentGrid.SelectedItems.Add(window.ComponentGrid.Items[2]); Click(window.LocateButton);
                Assert(ids.SequenceEqual(new[] { 11, 22 }), "Multiple component locate failed");
                window.SetBusy(false, "定位完成"); window.DetailTabs.SelectedIndex = 2; window.PendingGrid.SelectedItem = window.PendingGrid.Items[0]; Click(window.LocateButton);
                Assert(ids.SequenceEqual(new[] { 99 }) && locatedKey == "opaque-a", "Pending item locate failed");
                window.SetBusy(false, "定位完成"); window.RefreshRequested += key => { }; window.DocumentSelector.SelectedValue = "opaque-b"; Click(window.LocateButton);
                Assert(requests == 4, "Old selection could locate in a switched document");
            } finally { window.Close(); }
        });
        check("building_area_incomplete_data_keeps_pending_values_warnings_and_export_available", () => {
            var window = Window(); try {
                window.ShowReport(Report(complete: false));
                Assert(window.PendingGrid.Items.Count == 1 && window.StatusText.Text.Contains("缺项") && window.ExportButton.IsEnabled, "Incomplete report hidden or export blocked");
                var height = (Binding)((DataGridTextColumn)window.FloorGrid.Columns[1]).Binding;
                Assert((string)height.Converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture) == "待补", "Missing amount displayed as zero or empty");
                Assert((string)height.Converter.Convert(3.6m, typeof(string), null, CultureInfo.InvariantCulture) == "3.60", "Decimal display does not have two places");
                var diff = (Binding)((DataGridTextColumn)window.FloorGrid.Columns.Single(c => c.Header.ToString() == "几何差值 m²")).Binding;
                Assert((string)diff.Converter.Convert(-11.125m, typeof(string), null, CultureInfo.InvariantCulture) == "-11.13", "Declared minus measured difference is not signed and rounded");
                Assert((string)diff.Converter.Convert(11.125m, typeof(string), null, CultureInfo.InvariantCulture) == "+11.13", "Positive difference sign is missing");
                Assert(window.FloorGrid.RowStyle.Triggers.OfType<DataTrigger>().Any(t => ((Binding)t.Binding).Path.Path == "ExceedsTolerance"), "Tolerance exceedances lack warning style");
                Assert(window.ComparisonGrid.RowStyle.Triggers.Count > 0, "Project comparisons lack warning style");
                window.Show(); window.DetailTabs.SelectedIndex = 2; window.UpdateLayout();
                Program.Screenshot(window, Path.Combine(output, "building-area-statistics-pending.png"));
                window.DetailTabs.SelectedIndex = 3; window.UpdateLayout();
                Program.Screenshot(window, Path.Combine(output, "building-area-statistics-comparisons.png"));
            } finally { window.Close(); }
        });
        check("building_area_empty_notes_and_inapplicable_total_fields_are_not_false_pending_items", () => {
            var window = Window(); try {
                var report = Report(); report.Floors[0].Notes = ""; report.Floors[1].Notes = null;
                report.Total.HeightM = 9m; report.Total.FunctionSummary = "不应作为合计使用性质显示"; report.Total.Notes = " ";
                window.ShowReport(report);
                Assert(Cell(window.FloorGrid, "备注", report.Floors[0]) == "—" && Cell(window.FloorGrid, "备注", report.Floors[1]) == "—", "Empty optional notes are displayed as missing data");
                Assert(Cell(window.TotalGrid, "层高 m", report.Total) == "—" && Cell(window.TotalGrid, "使用性质及计算面积", report.Total) == "—" && Cell(window.TotalGrid, "备注", report.Total) == "—", "Inapplicable total fields are displayed as pending");
                Assert(Cell(window.TotalGrid, "计算面积 m²", report.Total) == "200.00", "Total numeric values were hidden with inapplicable fields");
                report.Floors[0].GeometricArea = null;
                Assert(Cell(window.FloorGrid, "实际几何 m²", report.Floors[0]) == "待补", "Genuinely missing geometry is hidden as inapplicable");
                window.Show(); window.UpdateLayout();
                Program.Screenshot(window, Path.Combine(output, "building-area-statistics-empty-notes.png"));
            } finally { window.Close(); }
        });
        check("building_area_attribute_only_comparison_has_no_false_calculation_or_difference_pending", () => {
            var window = Window(); try {
                var footprint = new BuildingAreaComparison { Name = "建筑基底面积", AttributeValue = 2951.65m, AttributeOnly = true, Notes = "独立保留申报属性" };
                Assert(Cell(window.ComparisonGrid, "属性填写值 m²", footprint) == "2951.65", "Attribute-only comparison lost the actual declared attribute");
                foreach (var header in new[] { "统计值 m²", "差值 m²", "差异 %" })
                    Assert(Cell(window.ComparisonGrid, header, footprint) == "—", "Attribute-only comparison shows false missing calculation: " + header);
                footprint.AttributeValue = null;
                Assert(Cell(window.ComparisonGrid, "属性填写值 m²", footprint) == "待补", "Missing declared footprint was hidden");
                var incomplete = new BuildingAreaComparison { Name = "项目建筑面积", AttributeValue = 200m };
                Assert(Cell(window.ComparisonGrid, "统计值 m²", incomplete) == "待补", "Normal missing comparison calculation was hidden");
                var comparison = Report().Comparisons[0];
                Assert(Cell(window.ComparisonGrid, "统计值 m²", comparison) == "200.00" && Cell(window.ComparisonGrid, "差值 m²", comparison) == "-10.00", "Normal comparison numbers were changed by the attribute-only display rule");
            } finally { window.Close(); }
        });
        check("building_area_compact_notes_keep_complete_text_in_tooltip_and_selected_details", () => {
            var window = Window(); try {
                var report = Report(); report.Floors[0].Notes = "这是完整的长备注，包含来源说明、几何并集口径和仍需人工确认的事项，不应迫使主表变宽或将楼层行撑成很高的段落。";
                window.ShowReport(report); window.FloorGrid.SelectedItem = report.Floors[0];
                Assert(window.SelectionDetails.Text.Contains(report.Floors[0].Notes), "Compact notes lost their full text in details");
                var noteColumn = (DataGridTextColumn)window.FloorGrid.Columns.Single(c => c.Header.ToString() == "备注");
                Assert(noteColumn.Width.Value <= 140 && noteColumn.ElementStyle.Setters.OfType<Setter>().Any(s => s.Property == ToolTipService.ToolTipProperty && s.Value is Binding), "Compact notes lost the tooltip");
                Assert(noteColumn.ElementStyle.Setters.OfType<Setter>().Any(s => s.Property == TextBlock.TextWrappingProperty && (TextWrapping)s.Value == TextWrapping.NoWrap), "Long notes dominate the compact table");
            } finally { window.Close(); }
        });
    }
}
