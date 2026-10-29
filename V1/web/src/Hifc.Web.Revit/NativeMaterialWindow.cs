using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hifc.Revit.Management;
using Hifc.Web.Contracts;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace Hifc.Web.Revit
{
    internal sealed class NativeMaterialWindow : NativeDialogWindow
    {
        private readonly INativeMaterialSession session;
        private readonly string kind;
        private string path, content;
        private MaterialPreview preview;
        private bool busy;
        private readonly ComboBox tables = new ComboBox { DisplayMemberPath = "Title", SelectedValuePath = "Id", Width = 230 }, target = new ComboBox { DisplayMemberPath = "Element.Name", SelectedValuePath = "Element.UniqueId", Width = 230 };
        private readonly ComboBox unit = new ComboBox(), frame = new ComboBox(), axes = new ComboBox();
        private readonly CheckBox accepted = new CheckBox { Content = "确认坐标由人工填写 / 审核" }, retain = new CheckBox { Content = "同时保留资料参考字段与来源", IsChecked = true };
        private readonly TextBox coordinateSystem = new TextBox { Width = 220 };
        private readonly TextBox tolerance = new TextBox { Text = "0.01", Width = 85 }, minimumGap = new TextBox { Text = "0.001", Width = 85 };
        private readonly TextBlock source = new TextBlock { TextWrapping = TextWrapping.Wrap }, status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock redlineReport = new TextBlock { Text = "点击重新预览后，分别显示模型双载体与人工坐标核对结果。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
        private readonly DataGrid changes = NativeSettingsWindow.Table(new MaterialChange[0]), references = NativeSettingsWindow.Table(new MaterialReference[0]);
        private readonly DataGrid points = NativeSettingsWindow.Table(new PointRow[0]);
        private readonly Button accept = new Button { Content = "暂存所选资料，返回写入预览", IsEnabled = false };
        internal NativeMaterialWindow(INativeMaterialSession session, string kind)
        {
            this.session = session; this.kind = kind; Title = kind == "redline" ? "红线人工资料与口径确认" : "报规 Excel · 匹配与冲突预览";
            Width = 1120; Height = 760; MinWidth = 940; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner; LiquidGlassTheme.Apply(this);
            var root = new DockPanel { Margin = new Thickness(20) }; Content = root; var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            header.Children.Add(LiquidGlassTheme.Title(Title));
            var toolbar = new WrapPanel { Margin = new Thickness(0, 12, 0, 10) }; header.Children.Add(toolbar);
            var import = new Button { Content = "选择 Excel 文件", Margin = new Thickness(0, 0, 8, 0) }; toolbar.Children.Add(import);
            if (kind == "workbook") {
                NativeUi.NamedCombo(tables, "Title"); NativeUi.NamedCombo(target, "Element.Name"); toolbar.Children.Add(tables); toolbar.Children.Add(target);
                target.ItemsSource = session.Result.Elements.Where(e => e.Element.Entity == "IfcBuilding").ToArray(); target.SelectedIndex = 0;
            }
            var refresh = new Button { Content = "重新预览 / 核对选择", Margin = new Thickness(8, 0, 0, 0) }; toolbar.Children.Add(refresh);
            source.Margin = new Thickness(0, 0, 0, 10); header.Children.Add(source);
            if (kind == "redline") {
                var metadata = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
                Configure(unit, new[] { "单位未确认", "米（m）", "毫米（mm）" }); Configure(frame, new[] { "框架未确认", "Revit 内部坐标", "当前项目位置共享坐标" }); Configure(axes, new[] { "X/Y 未确认", "X=东 / Y=北", "X=北 / Y=东" });
                metadata.Children.Add(unit); metadata.Children.Add(frame); metadata.Children.Add(axes); metadata.Children.Add(coordinateSystem); header.Children.Add(metadata); header.Children.Add(accepted);
                var geometryOptions = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) }; geometryOptions.Children.Add(new TextBlock { Text = "水平容差 m：", VerticalAlignment = VerticalAlignment.Center }); geometryOptions.Children.Add(tolerance);
                geometryOptions.Children.Add(new TextBlock { Text = "最小实体净距 m：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) }); geometryOptions.Children.Add(minimumGap); header.Children.Add(geometryOptions);
                header.Children.Add(new TextBlock { Text = "单位、框架或轴向未确认时可仅存资料，人工坐标对比仍为待核对。模型轮廓不会生成或覆盖人工坐标。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10) });
            }
            var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); status.Margin = new Thickness(0, 10, 0, 10); footer.Children.Add(status); footer.Children.Add(retain);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; actions.Children.Add(new Button { Content = "关闭", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) }); LiquidGlassTheme.Primary(accept); actions.Children.Add(accept); footer.Children.Add(actions);
            var tabs = new TabControl { BorderThickness = new Thickness(0), Background = Background }; NativeUi.Tabs(tabs); root.Children.Add(tabs);
            var selectedColumn = NativeSettingsWindow.CheckColumn("选择", "Selected", 60);
            var enabledStyle = new Style(typeof(CheckBox)); enabledStyle.Setters.Add(new Setter(IsEnabledProperty, new Binding("CanSelect"))); selectedColumn.ElementStyle = enabledStyle; selectedColumn.EditingElementStyle = enabledStyle; changes.Columns.Add(selectedColumn);
            NativeSettingsWindow.Column(changes, "对象", "ElementName", 130); NativeSettingsWindow.Column(changes, "属性", "Property", 150); NativeSettingsWindow.Column(changes, "模型原值", "CurrentValue", 100); NativeSettingsWindow.Column(changes, "资料拟写值", "Value", 120); NativeSettingsWindow.Column(changes, "来源", "SourceLocation", 130); NativeSettingsWindow.Column(changes, "冲突 / 提示", "Issue", 320);
            tabs.Items.Add(new TabItem { Header = "字段匹配与冲突", Content = changes });
            foreach (var column in new[] { new[] { "字段", "Field" }, new[] { "数值", "Value" }, new[] { "楼层", "LevelName" }, new[] { "单位", "Unit" }, new[] { "状态", "Status" }, new[] { "来源", "SourceLocation" } }) NativeSettingsWindow.Column(references, column[0], column[1], 150);
            references.IsReadOnly = true; tabs.Items.Add(new TabItem { Header = "收集资料与来源", Content = references });
            if (kind == "redline") { NativeSettingsWindow.Column(points, "序号", "Sequence", 80); NativeSettingsWindow.Column(points, "图纸 X", "X", 210); NativeSettingsWindow.Column(points, "图纸 Y", "Y", 210); NativeSettingsWindow.Column(points, "X 来源", "SourceCellX", 150); NativeSettingsWindow.Column(points, "Y 来源", "SourceCellY", 150); tabs.Items.Add(new TabItem { Header = "人工坐标资料", Content = points }); tabs.Items.Add(new TabItem { Header = "只读核对结果", Content = new ScrollViewer { Content = redlineReport, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); }
            import.Click += async (s, e) => {
                if (busy) return; var dialog = new OpenFileDialog { Filter = "Excel 工作簿 (*.xlsx)|*.xlsx", CheckFileExists = true };
                if (dialog.ShowDialog(this) != true) return;
                try { if (new FileInfo(dialog.FileName).Length > 16 * 1024 * 1024) throw new InvalidOperationException("资料文件超过 16 MB，请使用仅含所需表格的副本。"); path = dialog.FileName; content = Convert.ToBase64String(File.ReadAllBytes(path)); preview = null; await Preview(); }
                catch (Exception ex) { status.Text = ex.Message; }
            };
            refresh.Click += async (s, e) => { if (!busy) await Preview(); };
            accept.Click += async (s, e) => {
                if (busy || preview == null) return; changes.CommitEdit(DataGridEditingUnit.Cell, true); changes.CommitEdit(DataGridEditingUnit.Row, true);
                busy = true; try {
                    await session.AcceptMaterialAsync(preview, new MaterialDecision { PreviewId = preview.Id, Accepted = true, RetainReferences = retain.IsChecked == true,
                        SelectedKeys = preview.Changes.Where(c => c.Selected && c.CanSelect).Select(c => c.Key).ToArray() }, path); busy = false; Complete();
                } catch (Exception ex) { status.Text = ex.Message; } finally { busy = false; }
            };
            foreach (var selector in new[] { tables, target, unit, frame, axes }) selector.SelectionChanged += (s, e) => { if (!busy) accept.IsEnabled = false; };
            coordinateSystem.TextChanged += (s, e) => { if (!busy) accept.IsEnabled = false; }; accepted.Checked += (s, e) => { if (!busy) accept.IsEnabled = false; }; accepted.Unchecked += (s, e) => { if (!busy) accept.IsEnabled = false; };
            tolerance.TextChanged += (s, e) => { if (!busy) accept.IsEnabled = false; }; minimumGap.TextChanged += (s, e) => { if (!busy) accept.IsEnabled = false; };
            Closing += (s, e) => { if (busy) e.Cancel = true; };
            status.Text = "选择文件后由服务器解析。空白资料不覆盖模型原值；冲突项需逐项确认。";
            if (kind == "redline") {
                var saved = session.Result.ProjectData.FirstOrDefault(d => d.SchemaId == "b238a635-91f4-46cc-ac56-f41b7adac784");
                if (!string.IsNullOrEmpty(saved?.Json)) {
                    preview = new MaterialPreview { Kind = kind, DataJson = saved.Json }; busy = true;
                    try { ShowRedlineData(saved.Json); source.Text = "当前模型已保存的人工红线资料"; status.Text = "已载入模型资料。调整口径后点击“重新预览 / 核对选择”。"; }
                    catch (Exception ex) { preview = null; status.Text = "已有资料读取失败，未覆盖原资料：" + ex.Message; }
                    finally { busy = false; }
                }
            }
        }
        private async System.Threading.Tasks.Task Preview()
        {
            if (string.IsNullOrEmpty(content) && string.IsNullOrEmpty(preview?.DataJson)) { status.Text = "请先选择资料文件。"; return; }
            busy = true; accept.IsEnabled = false;
            try {
                string data = null;
                if (kind == "redline" && !string.IsNullOrEmpty(preview?.DataJson)) {
                    var json = JObject.Parse(preview.DataJson); json["Unit"] = unit.SelectedIndex; json["Frame"] = frame.SelectedIndex; json["Axes"] = axes.SelectedIndex;
                    json["Accepted"] = accepted.IsChecked == true; json["CoordinateSystemName"] = coordinateSystem.Text; data = json.ToString(Newtonsoft.Json.Formatting.None);
                }
                var selectedTable = tables.SelectedValue as string;
                if (!double.TryParse(tolerance.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var allowed) || double.IsNaN(allowed) || double.IsInfinity(allowed) || allowed <= 0
                    || !double.TryParse(minimumGap.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var gap) || double.IsNaN(gap) || double.IsInfinity(gap) || gap < 0)
                    throw new InvalidOperationException("容差必须大于零，实体净距不得小于零，单位 m。");
                preview = await session.PreviewMaterialAsync(new MaterialPreviewRequest { Kind = kind, FileName = path == null ? "模型内红线资料" : Path.GetFileName(path), SourcePath = path, ContentBase64 = content,
                    TableId = selectedTable, TargetBuildingUniqueId = target.SelectedValue as string, DataJson = data, ToleranceM = allowed, MinimumGapM = gap });
                tables.ItemsSource = preview.Tables; tables.SelectedValue = selectedTable; if (tables.SelectedIndex < 0 && preview.Tables.Length == 1) tables.SelectedIndex = 0;
                changes.ItemsSource = preview.Changes; references.ItemsSource = preview.References;
                source.Text = (path ?? "当前模型红线资料") + "\nSHA-256 " + preview.Sha256;
                status.Text = string.Join("\n", preview.Issues) + "\n" + preview.Changes.Length + " 项匹配，" + preview.Changes.Count(c => c.IsConflict) + " 项冲突，" + preview.References.Length + " 项参考资料。";
                if (kind == "redline" && !string.IsNullOrEmpty(preview.DataJson)) {
                    ShowRedlineData(preview.DataJson);
                    redlineReport.Text = string.Join("\n\n", preview.References.Select(r => r.Field + "：" + r.Status + "\n" + r.Value)) + "\n\n" + string.Join("\n", preview.Issues);
                }
                accept.IsEnabled = !string.IsNullOrEmpty(preview.Id);
            } catch (Exception ex) { status.Text = ex.Message; }
            finally { busy = false; }
        }
        private void ShowRedlineData(string data)
        {
            var json = JObject.Parse(data); unit.SelectedIndex = json.Value<int?>("Unit") ?? 0; frame.SelectedIndex = json.Value<int?>("Frame") ?? 0; axes.SelectedIndex = json.Value<int?>("Axes") ?? 0;
            accepted.IsChecked = json.Value<bool?>("Accepted") == true; coordinateSystem.Text = json.Value<string>("CoordinateSystemName") ?? "";
            points.ItemsSource = json["Points"]?.ToObject<PointRow[]>() ?? new PointRow[0];
        }
        private static void Configure(ComboBox selector, string[] choices) { selector.ItemsSource = choices; selector.SelectedIndex = 0; selector.Width = 190; selector.Margin = new Thickness(0, 0, 8, 0); LiquidGlassTheme.Combo(selector); }
        private sealed class PointRow { public int Sequence { get; set; } public double X { get; set; } public double Y { get; set; } public string SourceCellX { get; set; } public string SourceCellY { get; set; } }
    }
}
