using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ItrqTool.Presentation.UIModels;
using ItrqTool.Presentation.ViewModels;

namespace ItrqTool.Presentation.Views;

public partial class WorkflowListView : UserControl
{
    public WorkflowListView()
    {
        InitializeComponent();
    }

    private void WorkflowTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not WorkflowListViewModel vm) return;
        vm.SelectedWorkflow = e.NewValue as WorkflowListItem;
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (DataContext is not WorkflowListViewModel vm) return;
        if (vm.SelectedWorkflow is null) return;
        if (vm.SelectCurrentCommand.CanExecute(null))
            vm.SelectCurrentCommand.Execute(null);
    }
}
