using System.Windows;
using System.Windows.Controls;

namespace Hifc.Revit
{
    internal sealed class RoleWindow : Window
    {
        private readonly ComboBox selector;
        internal string SelectedSlot => selector.SelectedItem as string;
        internal RoleWindow(string[] slots, int count)
        {
            Title = "H-IFC V1 · 准备原映射参数";
            Width = 460; Height = 255; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(new TextBlock { Text = "选择 " + count + " 个构件的参考软件类目。", Margin = new Thickness(0, 0, 0, 14) });
            selector = new ComboBox { ItemsSource = slots, SelectedIndex = 0, IsEditable = false, MinHeight = 30 };
            panel.Children.Add(selector);
            panel.Children.Add(new TextBlock { Text = "保留原参数名及已有值。补充缺失参数并设置类目；\n业务属性值需要填写后再导出。", Margin = new Thickness(0, 14, 0, 14), TextWrapping = TextWrapping.Wrap });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", Width = 80, Height = 30, IsCancel = true };
            var confirm = new Button { Content = "准备参数", Width = 100, Height = 30, Margin = new Thickness(10, 0, 0, 0), IsDefault = true };
            confirm.Click += (sender, args) => { DialogResult = true; };
            buttons.Children.Add(cancel); buttons.Children.Add(confirm); panel.Children.Add(buttons); Content = panel;
        }
    }
}
