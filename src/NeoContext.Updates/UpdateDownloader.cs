using System.Security.Cryptography;
using NeoContext.Core.Results;

namespace NeoContext.Updates;

/// <summary>Whether a downloaded file must carry a valid Authenticode signature.</summary>
public enum SignaturePolicy
{
    /// <summary>Reject files without a valid signature (installers and packages).</summary>
    Required,

    /// <summary>Only the SHA-256 digest is checked (archives such as .zip).</summary>
    NotRequired,
}

/// <summary>
/// Downloads a release asset and only keeps it when it is verified.
/// <list type="number">
/// <item>The asset must have a SHA-256 digest from GitHub; without one it is refused.</item>
/// <item>The file is streamed to <c>.partial</c> while hashing, then the hash is compared.</item>
/// <item>When required, the Authenticode signature is checked.</item>
/// <item>Only then is the file renamed to its final name. Any failure deletes the partial file.</item>
/// </list>
/// The original app downloaded an .exe and ran it with none of these checks.
/// </summary>
public sealed class UpdateDownloader(HttpClient http)
{
    public async Task<Result<string>> DownloadAsync(ReleaseAsset asset, string directory, SignaturePolicy signaturePolicy, CancellationToken cancellationToken = default)
    {
        if (asset.Sha256 is null)
        {
            return Result<string>.Failure(ErrorKind.Integrity, $"{asset.Name} has no published SHA-256 digest, so it cannot be verified.");
        }

        if (asset.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Result<string>.Failure(ErrorKind.Integrity, $"Refusing unsafe file name '{asset.Name}'.");
        }

        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, asset.Name);
        var partialPath = finalPath + ".partial";

        try
        {
            string actualHash;
            using (var response = await http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = File.Create(partialPath);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                actualHash = Convert.ToHexStringLower(sha.GetHashAndReset());
            }

            if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partialPath);
                return Result<string>.Failure(ErrorKind.Integrity, $"{asset.Name} does not match its published SHA-256 digest.");
            }

            if (signaturePolicy == SignaturePolicy.Required)
            {
                var signature = AuthenticodeVerifier.Verify(partialPath);
                if (!signature.IsSuccess)
                {
                    File.Delete(partialPath);
                    return Result<string>.Failure(signature.Error!);
                }
            }

            File.Move(partialPath, finalPath, overwrite: true);
            return finalPath;
        }
        catch (HttpRequestException ex)
        {
            File.Delete(partialPath);
            return Result<string>.Failure(ErrorKind.Network, ex.Message);
        }
        catch (IOException ex)
        {
            File.Delete(partialPath);
            return Result<string>.Failure(ErrorKind.Unexpected, ex.Message);
        }
        catch (OperationCanceledException)
        {
            File.Delete(partialPath);
            throw;
        }
    }
}
