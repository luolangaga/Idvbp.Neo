using System.Threading.Tasks;

namespace Idvbp.Neo.Core.Abstractions.Services;

public interface ISystemService
{
    void OpenPath(string pathOrUrl);

    void OpenUrl(string url);

    Task<string?> PickFileAsync(string title, string[] patterns);

    string GetCurrentDirectory();

    string? GetAppBaseDirectory();
}
