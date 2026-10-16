using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Hifc.Revit.Management;

internal static class MouseProbe
{
    internal static int Run(string output)
    {
        var app = new Application(); var fake = new FakeSession { Snapshot = ManagerImprovementsTests.Sample() };
        var window = new ManagerWindow(fake, Path.Combine(output, "probe-profile")); fake.Window = window;
        window.Show(); window.StageSelector.SelectedItem = "报规"; window.KindSelector.SelectedItem = "总图"; window.StartFullCheck();
        window.ElementGrid.SelectedItem = fake.Snapshot.Elements[0];
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (s,e) => {
            var cell = window.FieldGrid.CurrentCell;
            var item = cell.Item; var content = item == null ? null : window.FieldGrid.Columns[1].GetCellContent(item);
            var input = content == null ? null : ManagerImprovementsTests.Visual<TextBox>(content);
            File.WriteAllText(Path.Combine(output, "mouse-probe.json"), new JavaScriptSerializer().Serialize(new {
                current = window.Current, status = window.StatusText, editing = input != null, focus = input?.IsKeyboardFocusWithin,
                text = input?.Text, drafts = window.DraftRequests, original = fake.Snapshot.Elements[0].Fields[0].Value, written = fake.Written }));
        };
        timer.Start(); app.Run(window); timer.Stop(); return 0;
    }
}
