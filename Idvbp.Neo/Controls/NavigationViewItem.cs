using System;

namespace Idvbp.Neo.Controls;

/// <summary>
/// Navigation item that carries the target page type directly instead of using a string tag.
/// </summary>
public class NavigationViewItem : FluentAvalonia.UI.Controls.NavigationViewItem
{
    // Preserve FluentAvalonia's control theme for the derived type.
    protected override Type StyleKeyOverride => typeof(FluentAvalonia.UI.Controls.NavigationViewItem);

    /// <summary>
    /// Gets the page type that should be displayed when this item is invoked.
    /// </summary>
    public Type? TargetPageType { get; init; }
}
