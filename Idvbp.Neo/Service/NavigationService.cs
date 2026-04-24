using System;
using Idvbp.Neo.Core.Abstractions.Controls;
using Idvbp.Neo.Core.Abstractions.Services;

namespace Idvbp.Neo.Service;

/// <summary>
/// Default navigation service for the main window.
/// </summary>
public class NavigationService : INavigationService
{
    /// <inheritdoc />
    public INavigationView? NavigationControl { get; set; }

    /// <inheritdoc />
    public IFrame? FrameControl { get; set; }

    /// <inheritdoc />
    public void SetNavigationControl(INavigationView navigationControl)
    {
        NavigationControl = navigationControl;
    }

    /// <inheritdoc />
    public void SetFrameControl(IFrame frameControl)
    {
        FrameControl = frameControl;
    }

    /// <inheritdoc />
    public bool Navigate(Type? pageType)
    {
        if (FrameControl == null) throw new NullReferenceException("Frame not set");

        // Null page types represent non-navigable items and should be ignored quietly.
        return pageType is not null && FrameControl.Navigate(pageType);
    }

    /// <inheritdoc />
    public bool GoBack()
    {
        if (FrameControl == null) throw new NullReferenceException("Frame not set");
        if (!FrameControl.CanGoBack) return false;

        FrameControl.GoBack();
        return true;
    }
}
