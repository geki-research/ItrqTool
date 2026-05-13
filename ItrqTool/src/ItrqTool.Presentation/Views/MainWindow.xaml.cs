using System.Windows;
using ItrqTool.Presentation.ViewModels;

namespace ItrqTool.Presentation.Views;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
