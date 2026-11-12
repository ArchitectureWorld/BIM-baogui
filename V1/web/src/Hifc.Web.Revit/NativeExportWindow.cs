using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Hifc.Revit.Management;
using Hifc.Reference;
using Microsoft.Win32;

namespace Hifc.Web.Revit
{
    internal sealed class NativeExportWindow : NativeDialogWindow
    {
        private readonly TextBox project = new TextBox(), building = new TextBox(), folder = new TextBox(), version = new TextBox { Text = "1", IsReadOnly = true };
        private readonly DatePicker date = new DatePicker { SelectedDate = DateTime.Today };
        private readonly TextBlock preview = new TextBlock { TextWrapping = TextWrapping.Wrap }, error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly WebModelSession session;
        private readonly Button confirm = new Button { Content = "核对并导出 IFC" };
        private bool exporting;
        internal NativeExportWindow(WebModelSession session)
        {
            this.session = session; Title = "导出 IFC · 名称、日期和版本"; Width = 790; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            LiquidGlassTheme.Apply(this); var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
            panel.Children.Add(LiquidGlassTheme.Title("确认名称和保存位置"));
            panel.Children.Add(new TextBlock { Text = "阶段：报规    模型类型：" + session.Result.ModelKind + "\n导出前核对已写入属性及资料，临时导出处理完成后回滚并核验模型。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 14) });
            var names = ExportFileNaming.SuggestNames(session.ModelPath, session.Title, session.Result.ModelKind);
            project.Text = names.ProjectName;
            building.Text = names.BuildingName;
            date.IsEnabled = false;
            building.IsReadOnly = session.Result.ModelKind == "总图";
            folder.Text = session.Workspace.LastExport == null ? ExportFileNaming.InitialDirectory(session.ModelPath, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)) : Path.GetDirectoryName(session.Workspace.LastExport.IfcPath);
            Row(panel, "项目名称", project); Row(panel, "单体 / 总图", building); Row(panel, "导出日期", date); Row(panel, "版本 V", version);
            var directory = new DockPanel(); var browse = new Button { Content = "浏览…", Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(browse, Dock.Right); directory.Children.Add(browse); directory.Children.Add(folder); Row(panel, "保存文件夹", directory);
            panel.Children.Add(preview); panel.Children.Add(new TextBlock { Text = "最终目录仅发布一个 IFC；已有同名文件保留，映射与回执保存在 Web 内部目录。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) });
            panel.Children.Add(error); var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            actions.Children.Add(new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) }); LiquidGlassTheme.Primary(confirm); actions.Children.Add(confirm); panel.Children.Add(actions);
            foreach (var text in new[] { project, building, folder }) { LiquidGlassTheme.SearchBox(text); text.TextChanged += (s, e) => Refresh(); }
            date.SelectedDateChanged += (s, e) => Refresh();
            browse.Click += (s, e) => { var dialog = new SaveFileDialog { Filter = "IFC (*.ifc)|*.ifc", FileName = NameForExport(), InitialDirectory = folder.Text, OverwritePrompt = true }; if (dialog.ShowDialog(this) == true) folder.Text = Path.GetDirectoryName(dialog.FileName); };
            confirm.Click += async (s, e) => {
                if (exporting) return;
                try {
                    date.SelectedDate = DateTime.Today; Refresh();
                    var path = NativeExportExecutor.OutputPath(folder.Text, NameForExport()); confirm.IsEnabled = false; exporting = true;
                    await session.PreviewExportAsync(Path.GetFileName(path), project.Text.Trim(), building.Text.Trim());
                    var plan = session.ExportPlan;
                    if (MessageBox.Show(this, "导出核对已完成。\n" + string.Join("\n", plan.Notices.Select(n => n.Detail).Distinct()) + "\n\n" + path + "\n\n确认执行原生 IFC 导出？", "导出前核对", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
                    await session.ExportAsync(Path.GetDirectoryName(path)); exporting = false; Complete();
                } catch (Exception ex) { error.Text = ex.Message; } finally { exporting = false; confirm.IsEnabled = true; }
            };
            Closing += (s, e) => { if (exporting) e.Cancel = true; };
            Refresh();
        }
        private string NameForExport()
        {
            if (string.IsNullOrWhiteSpace(project.Text) || string.IsNullOrWhiteSpace(building.Text) || date.SelectedDate == null || !int.TryParse(version.Text, out var number) || number < 1)
                throw new InvalidOperationException("请补全项目、单体、日期及正整数版本。");
            return ExportFileNaming.StemForNames(project.Text.Trim(), building.Text.Trim(), "报规", session.Result.ModelKind, date.SelectedDate.Value) + "_V" + number + ".ifc";
        }
        private void Refresh()
        {
            if (exporting) return;
            try {
                if (Directory.Exists(folder.Text)) {
                    var stem = ExportFileNaming.StemForNames(project.Text.Trim(), building.Text.Trim(), "报规", session.Result.ModelKind, date.SelectedDate.Value);
                    var next = ExportFileNaming.NextPath(folder.Text, stem);
                    version.Text = Regex.Match(Path.GetFileName(next), @"_V([0-9]+)\.ifc$").Groups[1].Value;
                }
                preview.Text = Path.GetFileName(NativeExportExecutor.OutputPath(folder.Text, NameForExport())); error.Text = ""; confirm.IsEnabled = true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException || ex is IOException) { error.Text = ex.Message; confirm.IsEnabled = false; }
        }
        private static void Row(Panel parent, string title, UIElement value)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(value, 1); grid.Children.Add(value); parent.Children.Add(grid);
        }
    }
}
