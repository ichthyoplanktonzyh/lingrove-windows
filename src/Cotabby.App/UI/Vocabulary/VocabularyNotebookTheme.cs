using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Markup;
using Cotabby.App.UI.Controls;
using Microsoft.Win32;
using Cotabby.App.Localization;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Window-scoped adaptation of the shared WPF palette. Existing sidebar callers retain
/// their defaults; canonical windows opt in. High contrast uses Windows colors and visible borders.</summary>
internal static class VocabularyNotebookTheme
{
    private static readonly Style SidebarSelection = CreateSelectionStyle();
    private static readonly Style FlatIcon = CreateFlatIconStyle();
    private static readonly Style TreeRow = CreateTreeRowStyle();
    private static readonly Style Scrollbar = CreateScrollbarStyle();
    internal static void Follow(Window window)
    {
        InterfaceText.ApplyDirection(window);
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        // Scoped implicit style also reaches scrollbars created inside native ScrollViewer templates.
        window.Resources[typeof(ScrollBar)] = Scrollbar;
        WindowTheme.Follow(window);
        void Apply() => ApplyPalette(window, SystemTheme.IsDark, SystemParameters.HighContrast);
        void Changed(object sender, UserPreferenceChangedEventArgs args) => window.Dispatcher.BeginInvoke(Apply);
        Apply(); SystemEvents.UserPreferenceChanged += Changed;
        window.Closed += (_, _) => SystemEvents.UserPreferenceChanged -= Changed;
        window.SetResourceReference(Window.BackgroundProperty, "WindowBg"); window.SetResourceReference(Window.ForegroundProperty, "TextPrimary");
        window.FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); window.FontSize = 13;
    }
    // Testable palette transition; production Follow applies the same mappings on system changes.
    internal static void ApplyPalette(Window window, bool dark, bool highContrast)
    {
        WindowTheme.ApplyPalette(window.Resources, dark);
        foreach (string key in new[] { "AccentBrush", "AccentTint", "OrangeBrush", "Btn", "Input", "Combo" }) window.Resources.Remove(key);
                window.Resources["AccentText"] = highContrast ? SystemColors.HighlightTextBrush : Brushes.White;
        var error = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#FFA057" : "#A83C00")); error.Freeze(); window.Resources["OrangeBrush"] = error;
        window.Resources["NotebookScrollTrack"] = highContrast ? SystemColors.WindowBrush : BrushColor(dark ? "#292929" : "#F3F3F5");
        window.Resources["NotebookScrollThumb"] = highContrast ? SystemColors.WindowTextBrush : BrushColor(dark ? "#858585" : "#A3A3AA");
        window.Resources["NotebookScrollThumbHot"] = highContrast ? SystemColors.HighlightBrush : BrushColor(dark ? "#B0B0B0" : "#72727A");
        ConfigureAccent(window);
        if (!highContrast) return;
        foreach (var (key, brush) in new (string, Brush)[] {
            ("WindowBg", SystemColors.WindowBrush), ("SidebarBg", SystemColors.WindowBrush), ("CardBg", SystemColors.WindowBrush),
            ("CardBorder", SystemColors.WindowTextBrush), ("TextPrimary", SystemColors.WindowTextBrush), ("TextSecondary", SystemColors.WindowTextBrush),
            ("InputBg", SystemColors.WindowBrush), ("KeycapBg", SystemColors.WindowBrush), ("HoverBg", SystemColors.WindowBrush),
            ("AccentBrush", SystemColors.HighlightBrush), ("AccentTint", SystemColors.WindowBrush), ("OrangeBrush", SystemColors.WindowTextBrush) }) window.Resources[key] = brush;
    }
    private static Brush BrushColor(string value) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush; }
    private static Style CreateScrollbarStyle() => (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ScrollBar}">
          <Setter Property="Width" Value="14" />
          <Setter Property="Focusable" Value="False" />
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ScrollBar}">
            <Border Background="{DynamicResource NotebookScrollTrack}"><DockPanel>
              <RepeatButton x:Name="lineBack" DockPanel.Dock="Top" Width="14" Height="14" Content="▴" Command="ScrollBar.LineUpCommand" Focusable="False" Foreground="{DynamicResource NotebookScrollThumb}"><RepeatButton.Template><ControlTemplate TargetType="{x:Type RepeatButton}"><Border Background="Transparent"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" /></Border></ControlTemplate></RepeatButton.Template></RepeatButton>
              <RepeatButton x:Name="lineForward" DockPanel.Dock="Bottom" Width="14" Height="14" Content="▾" Command="ScrollBar.LineDownCommand" Focusable="False" Foreground="{DynamicResource NotebookScrollThumb}"><RepeatButton.Template><ControlTemplate TargetType="{x:Type RepeatButton}"><Border Background="Transparent"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" /></Border></ControlTemplate></RepeatButton.Template></RepeatButton>
              <Track x:Name="PART_Track" Orientation="{TemplateBinding Orientation}" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" ViewportSize="{TemplateBinding ViewportSize}" IsDirectionReversed="True">
                <Track.DecreaseRepeatButton><RepeatButton x:Name="decrease" Command="ScrollBar.PageUpCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="{x:Type RepeatButton}"><Border Background="Transparent" /></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton><RepeatButton x:Name="increase" Command="ScrollBar.PageDownCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="{x:Type RepeatButton}"><Border Background="Transparent" /></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
                <Track.Thumb><Thumb x:Name="thumb" MinHeight="24" Margin="3,2" Focusable="False"><Thumb.Template><ControlTemplate TargetType="{x:Type Thumb}"><Border x:Name="handle" CornerRadius="4" Background="{DynamicResource NotebookScrollThumb}" /><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="handle" Property="Background" Value="{DynamicResource NotebookScrollThumbHot}" /></Trigger><Trigger Property="IsDragging" Value="True"><Setter TargetName="handle" Property="Background" Value="{DynamicResource NotebookScrollThumbHot}" /></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              </Track>
            </DockPanel></Border>
            <ControlTemplate.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False" /><Setter TargetName="decrease" Property="Command" Value="ScrollBar.PageLeftCommand" /><Setter TargetName="increase" Property="Command" Value="ScrollBar.PageRightCommand" /><Setter TargetName="lineBack" Property="DockPanel.Dock" Value="Left" /><Setter TargetName="lineForward" Property="DockPanel.Dock" Value="Right" /><Setter TargetName="lineBack" Property="Content" Value="◂" /><Setter TargetName="lineForward" Property="Content" Value="▸" /><Setter TargetName="lineBack" Property="Command" Value="ScrollBar.LineLeftCommand" /><Setter TargetName="lineForward" Property="Command" Value="ScrollBar.LineRightCommand" /><Setter TargetName="thumb" Property="MinHeight" Value="0" /><Setter TargetName="thumb" Property="MinWidth" Value="24" /><Setter TargetName="thumb" Property="Margin" Value="2,3" /></Trigger></ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
          <Style.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter Property="Width" Value="Auto" /><Setter Property="Height" Value="14" /></Trigger></Style.Triggers>
        </Style>
        """);
    private static void ConfigureAccent(Window window)
    {
        var style = new Style(typeof(Button), (Style)window.FindResource("Btn"));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("AccentBrush")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("AccentBrush")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("AccentText")));
                style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); window.Resources["NotebookAccentBtn"] = style;
        var input = new Style(typeof(TextBox), (Style)window.FindResource("Input"));
        var inputTemplate = new ControlTemplate(typeof(TextBox)); var inputBorder = new FrameworkElementFactory(typeof(Border)); inputBorder.Name = "input";
        inputBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); inputBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1)); inputBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); inputBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        var inputGrid = new FrameworkElementFactory(typeof(Grid)); var hint = new FrameworkElementFactory(typeof(TextBlock)); hint.Name = "hint"; hint.SetValue(TextBlock.TextProperty, new TemplateBindingExtension(FrameworkElement.TagProperty)); hint.SetValue(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextSecondary")); hint.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0)); hint.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); hint.SetValue(UIElement.IsHitTestVisibleProperty, false); hint.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); inputGrid.AppendChild(hint);
        var host = new FrameworkElementFactory(typeof(ScrollViewer)); host.Name = "PART_ContentHost"; host.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty)); inputGrid.AppendChild(host); inputBorder.AppendChild(inputGrid); inputTemplate.VisualTree = inputBorder;
        var empty = new Trigger { Property = TextBox.TextProperty, Value = "" }; empty.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "hint")); inputTemplate.Triggers.Add(empty);
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focused.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"), "input")); inputTemplate.Triggers.Add(focused);
        input.Setters.Add(new Setter(Control.TemplateProperty, inputTemplate)); window.Resources["NotebookInput"] = input;
        var card = new Style(typeof(Button), (Style)window.FindResource("Btn"));
        card.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("CardBg"))); card.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("CardBorder")));
        var template = new ControlTemplate(typeof(Button)); var border = new FrameworkElementFactory(typeof(Border)); border.Name = "card";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty)); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty)); border.AppendChild(content); template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBg"), "card")); template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"), "card")); template.Triggers.Add(focus);
        card.Setters.Add(new Setter(Control.TemplateProperty, template)); window.Resources["NotebookCardBtn"] = card;
    }
    internal static T Brush<T>(T element, DependencyProperty property, string key) where T : FrameworkElement { element.SetResourceReference(property, key); return element; }
    internal static T Style<T>(T element, string key) where T : FrameworkElement => element.Styled(key);
    internal static void Button(Button value, bool accent = false) => Style(value, accent ? "NotebookAccentBtn" : "Btn");
    internal static void CardButton(Button value) => Style(value, "NotebookCardBtn");
    internal static void Input(TextBox value) => Style(value, "NotebookInput");
    internal static void Combo(ComboBox value) => Style(value, "Combo");
    internal static void Sidebar(FrameworkElement root)
    {
        // Sidebars create detached rows on refresh. Apply dynamic references to their known native
        // system brushes after construction, without changing any other caller's presentation.
        foreach (var node in Walk(root))
        {
            if (node is TextBlock text)
            {
                if (DependencyPropertyHelper.GetValueSource(text, TextBlock.ForegroundProperty).IsExpression) continue;
                string key = ReferenceEquals(text.Foreground, VocabularySidebarVisuals.Secondary) ? "TextSecondary"
                    : ReferenceEquals(text.Foreground, VocabularySidebarVisuals.Accent) ? "AccentBrush" : "TextPrimary";
                text.SetResourceReference(TextBlock.ForegroundProperty, key);
            }
            else if (node is Border border && border.Background is SolidColorBrush background && background.Color.A is > 0 and < 255)
                border.SetResourceReference(Border.BackgroundProperty, "AccentTint");
            else if (node is Shape shape)
            {
                if (shape.Fill is not null && shape.Fill != Brushes.Transparent) shape.SetResourceReference(Shape.FillProperty, "AccentBrush");
                if (shape.Stroke is not null) shape.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            }
            if (node is ListBoxItem item) item.Style = SidebarSelection;
            if (node is Button button) button.Style = FlatIcon;
        }
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root)) if (child is DependencyObject value)
            foreach (var descendant in Walk(value)) yield return descendant;
    }
    private static Style CreateFlatIconStyle()
    {
        var style = new Style(typeof(Button)); var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border)); border.Name = "icon"; border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); border.SetValue(Border.BackgroundProperty, Brushes.Transparent); border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.BorderBrushProperty, Brushes.Transparent); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content); template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBg"), "icon")); template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"), "icon")); template.Triggers.Add(focus);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
    private static Style CreateSelectionStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var template = new ControlTemplate(typeof(ListBoxItem));
        var border = new FrameworkElementFactory(typeof(Border)); border.Name = "row";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); border.SetValue(Border.BorderThicknessProperty, new Thickness(2, 0, 0, 0));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent); border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty)); border.AppendChild(content); template.VisualTree = border;
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("AccentTint"), "row"));
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"), "row")); template.Triggers.Add(selected);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBg"), "row")); template.Triggers.Add(hover);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
    internal static GridSplitter Splitter()
    {
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, BorderThickness = new Thickness(0) };
        Brush(splitter, Control.BackgroundProperty, "CardBorder");
        var template = new ControlTemplate(typeof(GridSplitter)); var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(FrameworkElement.WidthProperty, 1d); line.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        line.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); template.VisualTree = line; splitter.Template = template; return splitter;
    }
    internal static Style TreeRowStyle() => TreeRow;
    private static Style CreateTreeRowStyle()
    {
        var style = new Style(typeof(TreeViewItem));
        var template = new ControlTemplate(typeof(TreeViewItem));
        var column = new FrameworkElementFactory(typeof(StackPanel));
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var toggle = new FrameworkElementFactory(typeof(ToggleButton)); toggle.Name = "toggle"; toggle.SetValue(FrameworkElement.WidthProperty, 20d); toggle.SetValue(FrameworkElement.HeightProperty, 22d); toggle.SetValue(ContentControl.ContentProperty, "▸"); toggle.SetValue(UIElement.VisibilityProperty, Visibility.Hidden); toggle.SetValue(UIElement.FocusableProperty, false); toggle.SetValue(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimary"));         toggle.SetValue(Control.BackgroundProperty, new DynamicResourceExtension("SidebarBg"));
        var toggleTemplate = new ControlTemplate(typeof(ToggleButton)); var toggleContent = new FrameworkElementFactory(typeof(ContentPresenter)); toggleContent.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); toggleContent.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); toggleTemplate.VisualTree = toggleContent; toggle.SetValue(Control.TemplateProperty, toggleTemplate);
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsExpanded") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent), Mode = System.Windows.Data.BindingMode.TwoWay }); row.AppendChild(toggle);
        var border = new FrameworkElementFactory(typeof(Border)); border.Name = "row"; border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); border.SetValue(Border.BorderThicknessProperty, new Thickness(2, 0, 0, 0)); border.SetValue(Border.BorderBrushProperty, Brushes.Transparent); border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var header = new FrameworkElementFactory(typeof(ContentPresenter)); header.SetValue(ContentPresenter.ContentSourceProperty, "Header"); header.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty)); border.AppendChild(header); row.AppendChild(border); column.AppendChild(row);
        var items = new FrameworkElementFactory(typeof(ItemsPresenter)); items.Name = "items"; items.SetValue(FrameworkElement.MarginProperty, new Thickness(18, 0, 0, 0)); items.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); column.AppendChild(items); template.VisualTree = column;
        var hasItems = new Trigger { Property = ItemsControl.HasItemsProperty, Value = true }; hasItems.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "toggle")); template.Triggers.Add(hasItems);
        var expanded = new Trigger { Property = TreeViewItem.IsExpandedProperty, Value = true }; expanded.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "items")); expanded.Setters.Add(new Setter(ContentControl.ContentProperty, "▾", "toggle")); template.Triggers.Add(expanded);
        var selected = new Trigger { Property = TreeViewItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("AccentTint"), "row")); selected.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"), "row")); template.Triggers.Add(selected);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
    internal static void Grid(DataGrid grid)
    {
        Brush(grid, Control.BackgroundProperty, "CardBg"); Brush(grid, Control.ForegroundProperty, "TextPrimary"); Brush(grid, Control.BorderBrushProperty, "CardBorder");
        grid.HorizontalGridLinesBrush = Brushes.Transparent; grid.VerticalGridLinesBrush = Brushes.Transparent;
        var header = new Style(typeof(DataGridColumnHeader)); header.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("KeycapBg"))); header.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimary"))); header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5))); grid.ColumnHeaderStyle = header;
        var row = new Style(typeof(DataGridRow)); row.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("CardBg"))); row.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("AccentTint"))); selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimary"))); row.Triggers.Add(selected); grid.RowStyle = row;
    }
}
