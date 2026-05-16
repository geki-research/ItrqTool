namespace ItrqTool.Presentation.Logging;

using Microsoft.Extensions.Logging;

public sealed class UiLogSinkProvider : ILoggerProvider
{
    private readonly IUiLogSink _sink;

    public UiLogSinkProvider(IUiLogSink sink)
    {
        _sink = sink;
    }

    public ILogger CreateLogger(string categoryName)
        => new UiLogSinkLogger(_sink, categoryName);

    public void Dispose() { }
}
