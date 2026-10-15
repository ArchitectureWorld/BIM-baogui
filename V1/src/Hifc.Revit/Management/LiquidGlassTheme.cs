using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Hifc.Revit.Management
{
    // Revit 2020 / .NET Framework 4.8: dependency-free, native WPF glass-inspired styling.
    // The opaque data cells intentionally remain readable; no WebView or native-window blur hooks.
    internal static class LiquidGlassTheme
    {
        // Revit 2020 WPF uses device-independent pixels (DIP), scaled by Windows DPI.
        internal const double ControlHeight = 36;
        internal const double ControlRadius = 10;
        internal const double ControlGap = 8;
        internal const double ControlFontSize = 12;
        internal static SolidColorBrush Color(string hex) =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        internal static Brush Gradient(string first, string last)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(first), 0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(last), 1));
            brush.Freeze();
            return brush;
        }

        internal static void Apply(Window window)
        {
            window.Background = Gradient("#F9FCFF", "#E8F1FA");
            window.Foreground = Color("#19324F");
            window.FontFamily = new FontFamily("Microsoft YaHei UI");
            window.FontSize = ControlFontSize;
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Gradient("#FAFFFFFF", "#E6EDF6FF")));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Color("#28476A")));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Color("#C7D8EA")));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(15, 0, 15, 0)));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, ControlHeight));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, ControlHeight));
            style.Setters.Add(new Setter(Control.FontSizeProperty, ControlFontSize));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Medium));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate()));
            window.Resources[typeof(Button)] = style;
        }

        private static ControlTemplate ButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "surface";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(ControlRadius));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            content.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            content.SetBinding(ContentPresenter.VerticalAlignmentProperty, new Binding("VerticalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Color("#81ACDD"), "surface"));
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.OpacityProperty, 0.83, "surface"));
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.46));
            template.Triggers.Add(hover); template.Triggers.Add(pressed); template.Triggers.Add(disabled);
            return template;
        }

        internal static void Primary(Button button)
        {
            button.Background = Gradient("#438CF0", "#176BD8");
            button.Foreground = Color("#FFFFFF");
            button.BorderBrush = Color("#73A8EA");
            button.FontWeight = FontWeights.SemiBold;
            button.Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#5A85C4"), BlurRadius = 12, ShadowDepth = 3, Opacity = 0.22 };
        }

        internal static Border Card(UIElement child, Thickness margin)
        {
            return new Border
            {
                Child = child, Margin = margin, Padding = new Thickness(13),
                Background = Gradient("#F5FFFFFF", "#DEFFFFFF"),
                BorderBrush = Color("#F7FFFFFF"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(17),
                Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#6889AC"), BlurRadius = 20, ShadowDepth = 5, Opacity = 0.15 }
            };
        }

        internal static TextBlock Title(string value) => new TextBlock
        {
            Text = value, FontWeight = FontWeights.SemiBold, FontSize = 14,
            Foreground = Color("#173A65"), VerticalAlignment = VerticalAlignment.Center
        };

        internal static Border Pill(string value) => new Border
        {
            Background = Gradient("#F2F7FC", "#EAF1F8"),
            BorderBrush = Color("#E1EBF5"), BorderThickness = new Thickness(1),
            Height = ControlHeight, CornerRadius = new CornerRadius(ControlRadius),
            Padding = new Thickness(12, 0, 12, 0),
            Child = new TextBlock { Text = value, Foreground = Color("#617B98"), FontSize = ControlFontSize, VerticalAlignment = VerticalAlignment.Center }
        };


        private static ControlTemplate ComboTemplate()
        {
            // Native ComboBox template with a full-height hit target and a real Popup.
            // WPF still owns keyboard navigation, item selection, and IsDropDownOpen.
            var root = new FrameworkElementFactory(typeof(Grid));
            var surface = new FrameworkElementFactory(typeof(Border));
            surface.Name = "comboSurface";
            surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(ControlRadius));
            surface.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            surface.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            surface.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            root.AppendChild(surface);

            var selected = new FrameworkElementFactory(typeof(ContentPresenter));
            selected.Name = "SelectionContent";
            selected.SetBinding(ContentPresenter.ContentProperty, new Binding("SelectionBoxItem") { RelativeSource = RelativeSource.TemplatedParent });
            selected.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("SelectionBoxItemTemplate") { RelativeSource = RelativeSource.TemplatedParent });
            selected.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 0, 28, 0));
            selected.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            selected.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            selected.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(selected);

            var arrow = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            arrow.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 1,1 L 5,5 L 9,1"));
            arrow.SetValue(System.Windows.Shapes.Path.StrokeProperty, Color("#6483A4"));
            arrow.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.7);
            arrow.SetValue(FrameworkElement.WidthProperty, 10.0);
            arrow.SetValue(FrameworkElement.HeightProperty, 6.0);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 13, 0));
            arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(arrow);

            var toggleVisual = new FrameworkElementFactory(typeof(Border));
            toggleVisual.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var toggleTemplate = new ControlTemplate(typeof(ToggleButton)) { VisualTree = toggleVisual };
            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.Name = "DropDownToggle";
            toggle.SetValue(Control.TemplateProperty, toggleTemplate);
            toggle.SetValue(Control.FocusableProperty, false);
            toggle.SetBinding(ToggleButton.IsCheckedProperty,
                new Binding("IsDropDownOpen") { RelativeSource = RelativeSource.TemplatedParent, Mode = BindingMode.TwoWay });
            root.AppendChild(toggle);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.StaysOpenProperty, false);
            popup.SetBinding(Popup.IsOpenProperty,
                new Binding("IsDropDownOpen") { RelativeSource = RelativeSource.TemplatedParent, Mode = BindingMode.TwoWay });
            var dropSurface = new FrameworkElementFactory(typeof(Border));
            dropSurface.SetValue(Border.BackgroundProperty, Color("#FAFFFFFF"));
            dropSurface.SetValue(Border.BorderBrushProperty, Color("#CFDFEE"));
            dropSurface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            dropSurface.SetValue(Border.CornerRadiusProperty, new CornerRadius(ControlRadius));
            dropSurface.SetValue(Border.PaddingProperty, new Thickness(4));
            dropSurface.SetValue(UIElement.EffectProperty, new DropShadowEffect
            {
                Color = (Color)ColorConverter.ConvertFromString("#6D8BAB"),
                BlurRadius = 13, ShadowDepth = 4, Opacity = 0.21
            });
            dropSurface.SetBinding(FrameworkElement.MinWidthProperty,
                new Binding("ActualWidth") { RelativeSource = RelativeSource.TemplatedParent });
            var listScroll = new FrameworkElementFactory(typeof(ScrollViewer));
            listScroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            listScroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            listScroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
            listScroll.SetValue(FrameworkElement.MaxHeightProperty, 280.0);
            var items = new FrameworkElementFactory(typeof(ItemsPresenter));
            listScroll.AppendChild(items);
            dropSurface.AppendChild(listScroll);
            popup.AppendChild(dropSurface);
            root.AppendChild(popup);

            var template = new ControlTemplate(typeof(ComboBox)) { VisualTree = root };
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.46));
            template.Triggers.Add(disabled);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Color("#80AEDC"), "comboSurface"));
            template.Triggers.Add(hover);
            return template;
        }

        internal static void Combo(ComboBox combo)
        {
            combo.Height = ControlHeight;
            combo.MinHeight = ControlHeight;
            combo.Background = Gradient("#FCFFFFFF", "#EDF5FD");
            combo.Foreground = Color("#213F62");
            combo.BorderBrush = Color("#C6D7E9");
            combo.BorderThickness = new Thickness(1);
            combo.Padding = new Thickness(14, 0, 30, 0);
            combo.FontSize = ControlFontSize;
            combo.FontWeight = FontWeights.Medium;
            combo.VerticalContentAlignment = VerticalAlignment.Center;
            combo.Margin = new Thickness(0, 0, ControlGap, 0);
            combo.Template = ComboTemplate();
            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 12, 7)));
            itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, ControlFontSize));
            combo.ItemContainerStyle = itemStyle;
        }

        internal static void SearchBox(TextBox box)
        {
            box.Height = ControlHeight;
            box.MinHeight = ControlHeight;
            box.BorderThickness = new Thickness(0);
            box.Background = Brushes.Transparent;
            box.Foreground = Color("#193A5F");
            box.FontSize = ControlFontSize;
            box.Padding = new Thickness(5, 0, 5, 0);
            box.VerticalContentAlignment = VerticalAlignment.Center;
        }

        internal static void Table(DataGrid grid)
        {
            grid.Background = Color("#FBFFFFFF");
            grid.BorderThickness = new Thickness(0);
            grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
            grid.RowBackground = Color("#FFFFFFFF");
            grid.AlternatingRowBackground = Color("#F3F8FD");
            grid.AlternationCount = 2;
            grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            grid.ColumnHeaderHeight = 36;
            grid.RowHeight = 32;
            grid.RowHeaderWidth = 0;
            grid.HorizontalGridLinesBrush = Color("#ECF1F7");
            grid.BorderBrush = Color("#DEE8F3");
            grid.FontSize = 12;
            var header = new Style(typeof(DataGridColumnHeader));
            header.Setters.Add(new Setter(Control.BackgroundProperty, Gradient("#F4F9FF", "#EAF2FB")));
            header.Setters.Add(new Setter(Control.ForegroundProperty, Color("#36516F")));
            header.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 5, 6, 5)));
            header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            header.Setters.Add(new Setter(Control.BorderBrushProperty, Color("#E6EDF5")));
            grid.ColumnHeaderStyle = header;
            var row = new Style(typeof(DataGridRow));
            var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Color("#DCEBFF")));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Color("#153E73")));
            row.Triggers.Add(selected);
            grid.RowStyle = row;
        }

        internal static void Categories(ListBox list)
        {
            // Use an explicit item template. A custom ListBoxItem ControlTemplate
            // does not automatically honor DisplayMemberPath on all WPF hosts.
            var layout = new FrameworkElementFactory(typeof(StackPanel));
            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new Binding("Role.Slot"));
            name.SetValue(TextBlock.ForegroundProperty, Color("#234C76"));
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            var subtitle = new FrameworkElementFactory(typeof(TextBlock));
            subtitle.SetBinding(TextBlock.TextProperty, new Binding("Subtitle"));
            subtitle.SetValue(TextBlock.ForegroundProperty, Color("#7088A4"));
            subtitle.SetValue(TextBlock.FontSizeProperty, 11.0);
            subtitle.SetValue(TextBlock.MarginProperty, new Thickness(0, 5, 0, 0));
            subtitle.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            layout.AppendChild(name); layout.AppendChild(subtitle);
            var itemTemplate = new DataTemplate { VisualTree = layout };
            list.ItemTemplate = itemTemplate;
            list.Background = Brushes.Transparent;
            list.BorderThickness = new Thickness(0);
            list.Padding = new Thickness(0);
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Color("#28496D")));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 10, 8, 10)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "itemSurface";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetValue(ContentPresenter.ContentTemplateProperty, itemTemplate);
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };
            var chosen = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            chosen.Setters.Add(new Setter(Control.BackgroundProperty, Color("#DCEBFF")));
            chosen.Setters.Add(new Setter(Control.ForegroundProperty, Color("#1768C9")));
            var hovered = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hovered.Setters.Add(new Setter(Control.BackgroundProperty, Color("#EDF5FF")));
            template.Triggers.Add(hovered); template.Triggers.Add(chosen);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            list.ItemContainerStyle = style;
        }
    }
}
