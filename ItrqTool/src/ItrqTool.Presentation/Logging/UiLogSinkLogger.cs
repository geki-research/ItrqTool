namespace ItrqTool.Presentation.Logging;

using Microsoft.Extensions.Logging;
using ItrqTool.Presentation.UIModels;

public sealed class UiLogSinkLogger : ILogger
{
    private readonly IUiLogSink _sink;
    private readonly string _category;
    private readonly string _shortCategory;
    private readonly LogLevel _minimum;

    public UiLogSinkLogger(IUiLogSink sink, string categoryName, LogLevel minimum = LogLevel.Information)
    {
        _sink = sink;
        _category = categoryName;
        _minimum = minimum;

        var dot = categoryName.LastIndexOf('.');
        _shortCategory = dot >= 0 ? categoryName[(dot + 1)..] : categoryName;
    }

    public bool IsEnabled(LogLevel logLevel)
        => logLevel >= _minimum && logLevel != LogLevel.None;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (exception is not null)
            message = $"{message}\n{exception}";

        _sink.Add(new LogEntry(
            Timestamp: DateTimeOffset.Now,
            Level: logLevel,
            Category: _category,
            ShortCategory: _shortCategory,
            Message: message));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
