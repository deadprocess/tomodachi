using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Tomodachi.Notch;

/// <summary>Inhalt der Notch liegt zwischen den Ohren: Ohrradius → Thickness(r, 0, r, 0).</summary>
public sealed class EarMarginConverter : IValueConverter
{
    public static readonly EarMarginConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double ear ? new Thickness(ear, 0, ear, 0) : new Thickness();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
