using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

/// <summary>
/// 半场得分模型，包含求生者与监管者得分。
/// </summary>
public partial class RoundHalfScore : ObservableObject
{
    [ObservableProperty]
    private int _survivorScore;

    [ObservableProperty]
    private int _hunterScore;
}
