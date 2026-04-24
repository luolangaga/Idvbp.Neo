using System;
using Idvbp.Neo.Core.Abstractions.Controls;

namespace Idvbp.Neo.Controls;

/// <summary>
/// Application navigation view wrapper used by the navigation service.
/// </summary>
public class NavigationView : FluentAvalonia.UI.Controls.NavigationView, INavigationView
{
    // Preserve FluentAvalonia's control theme for the derived type.
    protected override Type StyleKeyOverride => typeof(FluentAvalonia.UI.Controls.NavigationView);
}
