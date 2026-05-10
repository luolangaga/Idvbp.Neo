using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Idvbp.Neo.Views.Pages;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.Pages.HomePageViewModel vm)
        {
            await vm.LoadAllRoomsCommand.ExecuteAsync(null);
        }
    }

    private async void SwitchRoom_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string roomId } && DataContext is ViewModels.Pages.HomePageViewModel vm)
        {
            await vm.SwitchToRoomAsync(roomId);
        }
    }

    private async void DeleteRoom_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string roomId } && DataContext is ViewModels.Pages.HomePageViewModel vm)
        {
            await vm.DeleteRoomAsync(roomId);
        }
    }
}
