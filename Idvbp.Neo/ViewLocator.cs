using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Idvbp.Neo.ViewModels;

namespace Idvbp.Neo;

/// <summary>
/// 视图定位器，根据视图模型类型查找并创建对应的视图控件。
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    /// <summary>
    /// 根据视图模型构建对应的视图控件。
    /// </summary>
    /// <param name="param">视图模型实例。</param>
    /// <returns>对应的视图控件，若未找到则返回提示文本。</returns>
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var fullName = param.GetType().FullName!;
        // 将 ViewModels 命名空间替换为 Views 命名空间
        fullName = fullName.Replace(".ViewModels.", ".Views.");
        // 移除类型名称末尾的 "ViewModel" 后缀
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

    /// <summary>
    /// 判断数据是否为视图模型基类实例。
    /// </summary>
    /// <param name="data">待判断的数据对象。</param>
    /// <returns>是否为视图模型基类实例。</returns>
    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}