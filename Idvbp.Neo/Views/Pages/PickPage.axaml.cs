using Avalonia.Controls;
using Avalonia.Input;
using Idvbp.Neo.ViewModels.Pages;

namespace Idvbp.Neo.Views.Pages;

public partial class PickPage : UserControl
{
    public PickPage()
    {
        InitializeComponent();
    }

    private async void OnCharacterSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (sender is not TextBox { DataContext: PickSlotItem slot })
        {
            return;
        }

        e.Handled = true;
        await slot.ConfirmPendingSelectionAsync();
    }
}
