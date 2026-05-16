namespace ItrqTool.Presentation.Converters;

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

public sealed class LogLevelColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Warning = new(Color.FromRgb(0xB5, 0x81, 0x05));
    private static readonly SolidColorBrush Error = new(Color.FromRgb(0xC5, 0x30, 0x30));
    private static readonly SolidColorBrush Default = new(Colors.Black);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is LogLevel level ? level switch
        {
            LogLevel.Warning => Warning,
            LogLevel.Error or LogLevel.Critical => Error,
            _ => Default
        } : Default;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
