using System;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Idvbp.Neo.Service;

/// <summary>
/// Resolves FluentAvalonia navigation pages from the application service provider.
/// </summary>
public class NavigationPageFactory(IServiceProvider serviceProvider) : INavigationPageFactory
{
    /// <inheritdoc />
    public Control? GetPage(Type srcType)
    {
        // Prefer DI so pages receive their registered DataContext and any future constructor services.
        var page = serviceProvider.GetService(srcType)
            ?? ActivatorUtilities.CreateInstance(serviceProvider, srcType);

        return page as Control;
    }

    /// <inheritdoc />
    public Control? GetPageFromObject(object target)
    {
        return target switch
        {
            Type pageType => GetPage(pageType),
            Control control => control,
            _ => null
        };
    }
}
