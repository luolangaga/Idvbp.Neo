using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Idvbp.Neo.Core.Abstractions.Services;

namespace Idvbp.Neo.Services;

public class SystemService : ISystemService
{
    public void OpenPath(string pathOrUrl)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = pathOrUrl,
            UseShellExecute = true
        });
    }

    public void OpenUrl(string url)
    {
        OpenPath(url);
    }

    public Task<string?> PickFileAsync(string title, string[] patterns)
    {
        throw new NotSupportedException("桌面文件选择器需要通过 ISystemService 的 Avalonia 实现使用。");
    }

    public string GetCurrentDirectory() => Environment.CurrentDirectory;

    public string? GetAppBaseDirectory() => AppContext.BaseDirectory;
}
