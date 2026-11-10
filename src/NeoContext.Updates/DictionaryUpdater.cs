using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using NeoContext.Core.Dictionaries;
using NeoContext.Core.Results;

namespace NeoContext.Updates;

/// <summary>
/// Downloads newer dictionary files. A <c>manifest.json</c> lists each file with its SHA-256; a file is kept only
/// when its hash matches, its name is one of the known dictionaries and (for XML) it parses. Files go to
/// <c>web\</c> through a temporary name, so a failed download never damages the working copy.
/// The manifest is not signed: the hash guards against corruption, not against a compromised repository.
/// </summary>
public sealed class DictionaryUpdater(HttpClient http, string baseUrl, DictionaryStore store)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const int MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>Returns the names of the files that were replaced (empty when everything is current).</summary>
    public async Task<Result<IReadOnlyList<string>>> UpdateAsync(CancellationToken cancellationToken = default)
    {
        if (store.WebDirectory is not { } webDirectory)
        {
            return Result<IReadOnlyList<string>>.Failure(ErrorKind.NotSupported, "No dictionary folder.");
        }

        var updated = new List<string>();
        try
        {
            var manifestJson = await http.GetStringAsync(new Uri(new Uri(baseUrl), "manifest.json"), cancellationToken).ConfigureAwait(false);
            var files = JsonSerializer.Deserialize<Manifest>(manifestJson, JsonOptions)?.Files ?? [];
            Directory.CreateDirectory(webDirectory);

            foreach (var file in files.Where(f => DictionaryStore.FileNames.Contains(f.Name) && f.Sha256?.Length == 64))
            {
                var target = Path.Combine(webDirectory, file.Name);
                if (File.Exists(target) && string.Equals(Hash(await File.ReadAllBytesAsync(target, cancellationToken).ConfigureAwait(false)), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bytes = await http.GetByteArrayAsync(new Uri(new Uri(baseUrl), file.Name), cancellationToken).ConfigureAwait(false);
                if (bytes.Length > MaxFileBytes || !string.Equals(Hash(bytes), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<IReadOnlyList<string>>.Failure(ErrorKind.Integrity, $"{file.Name} does not match its published SHA-256 digest.");
                }

                if (file.Name.EndsWith(".xml", StringComparison.Ordinal))
                {
                    using var stream = new MemoryStream(bytes);
                    _ = XDocument.Load(stream);
                }

                var temp = target + ".partial";
                await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temp, target, overwrite: true);
                updated.Add(file.Name);
            }

            return updated;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or System.Xml.XmlException or UriFormatException)
        {
            return Result<IReadOnlyList<string>>.Failure(ex is HttpRequestException ? ErrorKind.Network : ErrorKind.Unexpected, ex.Message);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed record Manifest(List<ManifestFile>? Files);

    private sealed record ManifestFile(string Name, string? Sha256);
}
