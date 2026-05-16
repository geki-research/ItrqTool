namespace ItrqTool.Presentation.Logging;

using System.Collections.ObjectModel;
using ItrqTool.Presentation.UIModels;

public interface IUiLogSink
{
    ObservableCollection<LogEntry> Entries { get; }
    void Add(LogEntry entry);
    void Clear();
}
