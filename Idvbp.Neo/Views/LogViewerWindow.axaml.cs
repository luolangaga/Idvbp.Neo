using System.Linq;
using Avalonia.Controls;
using Idvbp.Neo.ViewModels.Pages;

namespace Idvbp.Neo.Views;

public partial class LogViewerWindow : Window
{
    public LogViewerWindow()
    {
        InitializeComponent();
        LogGrid.SelectionChanged += (_, _) => SyncSelectedLogs();
        DataContextChanged += (_, _) => SyncSelectedLogs();
    }

    public LogViewerWindow(LogViewerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        LogGrid.SelectionChanged += (_, _) => SyncSelectedLogs();
    }

    private void SyncSelectedLogs()
    {
        if (DataContext is LogViewerViewModel viewModel)
        {
            viewModel.SetSelectedLogs(LogGrid.SelectedItems.OfType<LogViewerLogItem>().Select(x => x.Entry));
        }
    }
}
