namespace ItrqTool.Presentation.Converters;

using System.Globalization;
using System.Windows.Data;
using Microsoft.Extensions.Logging;

public sealed class LogLevelShortConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is LogLevel level ? level switch
        {
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            LogLevel.Debug => "DBG",
            LogLevel.Trace => "TRC",
            _ => ""
        } : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
