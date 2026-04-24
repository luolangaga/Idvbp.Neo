using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

public partial class FrontManagePageViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<object> _externalFrontendWindows = new();
}
