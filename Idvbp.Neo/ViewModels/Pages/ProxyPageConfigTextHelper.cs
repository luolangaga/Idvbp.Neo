using System;
using System.Text.Json;
using System.Xml.Linq;

namespace Idvbp.Neo.ViewModels.Pages;

public static class ProxyPageConfigTextHelper
{
    public static string DetectFormat(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Plain Text";
        }

        var trimmed = text.Trim();

        if (LooksLikeJson(trimmed))
        {
            return "JSON";
        }

        if (LooksLikeXml(trimmed))
        {
            return "XML";
        }

        if (LooksLikeHtml(trimmed))
        {
            return "HTML";
        }

        if (LooksLikeYaml(trimmed))
        {
            return "YAML";
        }

        if (LooksLikeToml(trimmed))
        {
            return "TOML";
        }

        if (LooksLikeIni(trimmed))
        {
            return "INI";
        }

        if (LooksLikeJavaScript(trimmed))
        {
            return "JavaScript";
        }

        if (LooksLikeCss(trimmed))
        {
            return "CSS";
        }

        return "Plain Text";
    }

    public static string BuildSummary(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "未配置";
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var firstLine = lines[0].Trim();
        var preview = firstLine.Length > 48 ? firstLine[..48] + "..." : firstLine;

        return lines.Length > 1
            ? $"{preview} ({lines.Length} 行)"
            : preview;
    }

    public static bool TryFormat(string? text, out string formatted)
    {
        formatted = text ?? string.Empty;
        var format = DetectFormat(text);

        try
        {
            switch (format)
            {
                case "JSON":
                {
                    using var document = JsonDocument.Parse(text!);
                    formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    return true;
                }
                case "XML":
                case "HTML":
                {
                    var document = XDocument.Parse(text!, LoadOptions.PreserveWhitespace);
                    formatted = document.ToString();
                    return true;
                }
                default:
                    return false;
            }
        }
        catch
        {
            formatted = text ?? string.Empty;
            return false;
        }
    }

    public static string GetSuggestedExtension(string format)
    {
        return format switch
        {
            "JSON" => ".json",
            "XML" => ".xml",
            "HTML" => ".html",
            "YAML" => ".yml",
            "TOML" => ".toml",
            "INI" => ".ini",
            "JavaScript" => ".js",
            "CSS" => ".css",
            _ => ".txt"
        };
    }

    private static bool LooksLikeJson(string text)
    {
        if (!(text.StartsWith('{') && text.EndsWith('}')) &&
            !(text.StartsWith('[') && text.EndsWith(']')))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeXml(string text)
    {
        if (!text.StartsWith('<') || !text.EndsWith('>'))
        {
            return false;
        }

        try
        {
            _ = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeHtml(string text)
    {
        return text.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<body", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<div", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<script", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeYaml(string text)
    {
        return text.Contains(": ") &&
               !text.Contains('{') &&
               !text.Contains('}') &&
               (text.Contains('\n') || text.StartsWith("---", StringComparison.Ordinal));
    }

    private static bool LooksLikeToml(string text)
    {
        return text.Contains(" = ") && text.Contains('[') && text.Contains(']');
    }

    private static bool LooksLikeIni(string text)
    {
        return text.Contains('=') && text.Contains('[') && text.Contains(']');
    }

    private static bool LooksLikeJavaScript(string text)
    {
        return text.Contains("function", StringComparison.Ordinal) ||
               text.Contains("const ", StringComparison.Ordinal) ||
               text.Contains("let ", StringComparison.Ordinal) ||
               text.Contains("=>", StringComparison.Ordinal);
    }

    private static bool LooksLikeCss(string text)
    {
        return text.Contains('{') &&
               text.Contains('}') &&
               text.Contains(':') &&
               text.Contains(';');
    }
}
