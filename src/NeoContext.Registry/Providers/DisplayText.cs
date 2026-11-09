using NeoContext.Core.Abstractions;

namespace NeoContext.Registry.Providers;

/// <summary>Turns raw registry strings into text a person can read.</summary>
internal static class DisplayText
{
    /// <summary>
    /// Resolves indirect strings (<c>@shell32.dll,-8496</c>) and removes keyboard accelerators
    /// (<c>&amp;Open</c> becomes <c>Open</c>). Returns null when nothing readable is left.
    /// </summary>
    public static string? Clean(string? raw, IIndirectStringResolver resolver)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.StartsWith('@') ? resolver.Resolve(raw) : raw;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // "&&" is a literal ampersand; a single "&" marks the accelerator key.
        return text.Replace("&&", "\0", StringComparison.Ordinal)
                   .Replace("&", "", StringComparison.Ordinal)
                   .Replace('\0', '&')
                   .Trim();
    }

    /// <summary>The first candidate that resolves to readable text.</summary>
    public static string? FirstReadable(IIndirectStringResolver resolver, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var text = Clean(candidate, resolver);
            if (text is not null)
            {
                return text;
            }
        }

        return null;
    }
}
