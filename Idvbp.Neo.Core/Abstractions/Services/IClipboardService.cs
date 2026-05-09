using System.Threading.Tasks;

namespace Idvbp.Neo.Core.Abstractions.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);

    Task<string?> GetTextAsync();
}
