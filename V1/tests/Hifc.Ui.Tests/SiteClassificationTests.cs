using System;
using System.IO;
using System.Linq;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class SiteClassificationTests
{
    static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static void Run(Action<string, Action> check, string output)
    {
        var floor = new ModelElementCheck { Id = 717724, UniqueId = "site-control-floor", Name = "用地红线", Category = "楼板", Entity = "IfcSlab" };
        var proxy = new ModelElementCheck { Id = 717725, UniqueId = "site-control-proxy", Name = "控制线实体", Category = "常规模型", Entity = "IfcBuildingElementProxy" };
        var wall = new ModelElementCheck { Id = 717726, UniqueId = "site-wall", Name = "围墙", Category = "墙", Entity = "IfcWall" };
        var snapshot = ManagerImprovementsTests.Sample(); snapshot.Elements = new[] { floor, proxy, wall };
        var fake = new FakeSession { Snapshot = snapshot };
        var window = new ManagerWindow(fake, Path.Combine(output, "site-classes-" + Guid.NewGuid().ToString("N"))); fake.Window = window;
        window.Show(); window.StageSelector.SelectedItem = "报规"; window.KindSelector.SelectedItem = "总图"; window.StartFullCheck();
        check("planning_control_is_visible_for_a_selected_floor", () => {
            window.ElementGrid.SelectedItem = floor;
            Assert(window.SlotSelector.Items.Cast<string>().Contains("规划控制线"), "Planning control disappeared for a floor");
        });
        check("floor_and_proxy_share_manual_planning_control", () => {
            window.ElementGrid.SelectedItems.Clear(); window.ElementGrid.SelectedItems.Add(floor); window.ElementGrid.SelectedItems.Add(proxy);
            Assert(window.SlotSelector.Items.Cast<string>().Contains("规划控制线"), "Mixed supported carriers lost their common control-line role");
        });
        check("planning_control_is_not_offered_for_a_wall", () => {
            window.ElementGrid.SelectedItem = wall;
            Assert(!window.SlotSelector.Items.Cast<string>().Contains("规划控制线"), "Wall was silently accepted as a control-line proxy");
        });
        check("planning_control_floor_can_change_back_to_green", () => {
            var source = typeof(ModelElementCheck).GetProperty("SourceEntity");
            Assert(source != null, "Original floor carrier is not retained");
            source.SetValue(floor, "IfcSlab"); floor.Entity = "IfcBuildingElementProxy"; floor.Slot = "规划控制线";
            window.ElementGrid.SelectedItem = null; window.ElementGrid.SelectedItem = floor;
            Assert(window.SlotSelector.Items.Cast<string>().Contains("绿地"), "Control-line conversion trapped the floor in proxy-only roles");
        });
        window.Close();
    }
}
