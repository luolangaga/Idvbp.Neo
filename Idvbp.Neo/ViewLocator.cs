using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Idvbp.Neo.ViewModels;

namespace Idvbp.Neo;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var fullName = param.GetType().FullName!;
        // Replace ViewModels namespace with Views namespace
        fullName = fullName.Replace(".ViewModels.", ".Views.");
        // Strip "ViewModel" suffix from the type name
        var lastDot = fullName.LastIndexOf('.');
        var typeName = fullName[(lastDot + 1)..];
        if (typeName.EndsWith("ViewModel"))
            fullName = fullName[..(lastDot + 1)] + typeName[..^"ViewModel".Length];
        var type = Type.GetType(fullName);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + fullName };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}