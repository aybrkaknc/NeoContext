namespace NeoContext.Core.Rules;

/// <summary>A named set of common extensions offered as one button on the "File types" page.</summary>
public sealed record FileTypeGroup(string Name, IReadOnlyList<string> Extensions);

public static class FileTypeGroups
{
    public static IReadOnlyList<FileTypeGroup> All { get; } =
    [
        new("Exe", [".exe"]),
        new("Video", [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm"]),
        new("Audio", [".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a"]),
        new("Archive", [".zip", ".rar", ".7z", ".tar", ".gz"]),
        new("Document", [".doc", ".docx", ".pdf", ".txt", ".xls", ".xlsx", ".ppt", ".pptx"]),
        new("Image", [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"]),
    ];

    /// <summary>
    /// Turns what the user typed (<c>mp4</c>, <c>.MP4</c>, <c>*.mp4</c>) into <c>.mp4</c>.
    /// Returns null when nothing usable is left.
    /// </summary>
    public static string? NormalizeExtension(string? text)
    {
        var name = text?.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(name) || !name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '+' or '#'))
        {
            return null;
        }

        return "." + name;
    }
}
