using NeoContext.Core.Results;

namespace NeoContext.Updates;

/// <summary>The answer to "is there a newer version?".</summary>
/// <param name="Latest">The newest release, or null when the app is up to date.</param>
public sealed record UpdateCheckResult(ReleaseInfo? Latest)
{
    public bool IsUpdateAvailable => Latest is not null;
}

/// <summary>
/// Compares the running version with the latest release. It never shows UI and never downloads
/// anything; the caller decides what to do with the result.
/// </summary>
public sealed class UpdateChecker(GitHubReleaseClient client, SemVersion currentVersion)
{
    public SemVersion CurrentVersion { get; } = currentVersion;

    public async Task<Result<UpdateCheckResult>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var latest = await client.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        if (!latest.IsSuccess)
        {
            // "No releases yet" is not an error for the user: they simply have the newest build.
            return latest.Error!.Kind == ErrorKind.NotFound
                ? new UpdateCheckResult(null)
                : Result<UpdateCheckResult>.Failure(latest.Error);
        }

        return new UpdateCheckResult(latest.Value.Version > CurrentVersion ? latest.Value : null);
    }
}

/// <summary>When the next automatic check is due.</summary>
public static class UpdateSchedule
{
    /// <summary>
    /// Due when there was never a successful check or the interval has passed. Callers store the
    /// time only after a successful check, so one network failure does not block checks for days
    /// (the original stored it before checking).
    /// </summary>
    public static bool IsDue(DateTimeOffset? lastSuccessfulCheck, int intervalDays, DateTimeOffset now) =>
        lastSuccessfulCheck is null || now - lastSuccessfulCheck.Value >= TimeSpan.FromDays(intervalDays);
}
