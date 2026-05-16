using System.Collections.Specialized;
using System.Windows.Controls;

namespace ItrqTool.Presentation.Views;

public partial class WorkflowRunView : UserControl
{
    public WorkflowRunView()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)LogList.Items).CollectionChanged += OnLogItemsChanged;
    }

    private void OnLogItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
    }
}
