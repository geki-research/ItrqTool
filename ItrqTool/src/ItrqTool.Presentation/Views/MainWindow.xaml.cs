using System.Windows;
using ItrqTool.Presentation.ViewModels;

namespace ItrqTool.Presentation.Views;

public partial class MainWindow : Window
{
    public MainWindow(WorkflowListViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
