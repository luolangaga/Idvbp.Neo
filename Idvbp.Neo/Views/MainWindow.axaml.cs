using Avalonia.Controls;
using Avalonia.Interactivity;
using Idvbp.Neo.ViewModels;

namespace Idvbp.Neo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MenuListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vmClicked && sender is ListBox listBoxItem)
        {
            vmClicked.NavigateCommand.Execute(listBoxItem.SelectedItem as MainWindowViewModel.NavigationItem);
        }
    }

    private void FooterListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            MenuListBox_SelectionChanged(sender, e);
        }
    }
}
