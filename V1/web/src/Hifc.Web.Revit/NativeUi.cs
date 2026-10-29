using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hifc.Revit.Management;

namespace Hifc.Web.Revit
{
    internal static class NativeUi
    {
        internal static void NamedCombo(ComboBox combo, string path)
        {
            LiquidGlassTheme.Combo(combo);
            // LiquidGlass's custom selection presenter does not resolve a named
            // DisplayMemberPath on every WPF host. Supply the same explicit template
            // for the selected item and popup rows.
            combo.DisplayMemberPath = "";
            var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding(path));
            combo.ItemTemplate = new DataTemplate { VisualTree = text };
        }
        internal static void Tabs(TabControl tabs)
        {
            var style = new Style(typeof(TabItem)); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
            tabs.Resources[typeof(TabItem)] = style;
        }
    }
}
