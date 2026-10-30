using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Cotabby.App.Localization;

/// <summary>A display-only template for finite enum choices. The ComboBox keeps its original
/// typed item/selection identity; only an explicitly supplied authored label enters a catalog.</summary>
internal static class InterfaceEnumLabels
{
    internal static DataTemplate Create<T>(Func<T, string> source, string? table = null) where T : struct, Enum
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding { Converter = new Label<T>(source, table) });
        return new DataTemplate { VisualTree = label };
    }
    private sealed class Label<T>(Func<T, string> source, string? table) : IValueConverter where T : struct, Enum
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture) =>
            value is T choice ? InterfaceText.Get(source(choice), table) : "";
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
