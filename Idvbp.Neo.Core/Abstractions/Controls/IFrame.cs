using System;

namespace Idvbp.Neo.Core.Abstractions.Controls;

/// <summary>
/// 应用程序导航框架的抽象接口。
/// </summary>
public interface IFrame
{
    /// <summary>
    /// 导航到由视图类型表示的页面。
    /// </summary>
    /// <param name="pageType">要导航到的 Avalonia 控件类型。</param>
    /// <returns>导航被底层框架接受时返回 <see langword="true"/>。</returns>
    public bool Navigate(Type pageType);

    /// <summary>
    /// 获取框架的后退栈中是否存在可返回的条目。
    /// </summary>
    public bool CanGoBack { get; }

    /// <summary>
    /// 导航到框架的前一个条目。
    /// </summary>
    public void GoBack();
}
