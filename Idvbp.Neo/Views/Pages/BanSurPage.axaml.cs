using Avalonia.Controls;
using Avalonia.Input;
using Idvbp.Neo.ViewModels.Pages;

namespace Idvbp.Neo.Views.Pages;

public partial class BanSurPage : UserControl
{
    public BanSurPage()
    {
        InitializeComponent();
    }

    private async void OnBanSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (sender is not TextBox { DataContext: BanSlotItem slot })
        {
            return;
        }

        e.Handled = true;
        await slot.ConfirmPendingSelectionAsync();
    }
}
