namespace ItrqTool.Presentation.Logging;

using System.Collections.ObjectModel;
using System.Windows.Threading;
using ItrqTool.Presentation.UIModels;

public sealed class UiLogSink : IUiLogSink
{
    private readonly Dispatcher? _dispatcher;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public UiLogSink(Dispatcher? dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Add(LogEntry entry)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
            Entries.Add(entry);
        else
            _dispatcher.BeginInvoke(() => Entries.Add(entry));
    }

    public void Clear()
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
            Entries.Clear();
        else
            _dispatcher.BeginInvoke(() => Entries.Clear());
    }
}
