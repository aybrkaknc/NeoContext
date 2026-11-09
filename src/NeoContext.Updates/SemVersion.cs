using System.Diagnostics.CodeAnalysis;

namespace NeoContext.Updates;

/// <summary>
/// A tolerant semantic version. Accepts a leading "v", two or three numeric parts, a pre-release
/// suffix and build metadata (<c>v1.2</c>, <c>1.2.3-beta.2</c>, <c>1.2.3+abc</c>). The original
/// app used System.Version, which throws on any of those.
/// </summary>
public sealed record SemVersion(int Major, int Minor, int Patch, string PreRelease = "") : IComparable<SemVersion>
{
    public bool IsPreRelease => PreRelease.Length > 0;

    public static SemVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"'{text}' is not a version.");

    public static bool TryParse(string? text, [NotNullWhen(true)] out SemVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim().TrimStart('v', 'V');
        var plus = value.IndexOf('+');
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var preRelease = "";
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = value[(dash + 1)..];
            value = value[..dash];
        }

        var parts = value.Split('.');
        if (parts.Length is < 1 or > 4 || parts.Any(p => !int.TryParse(p, out var n) || n < 0))
        {
            return false;
        }

        var numbers = parts.Select(int.Parse).Concat([0, 0]).ToArray();
        version = new SemVersion(numbers[0], numbers[1], numbers[2], preRelease);
        return true;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
        {
            return core;
        }

        // A release sorts after any of its pre-releases: 1.0.0-beta < 1.0.0.
        if (IsPreRelease != other.IsPreRelease)
        {
            return IsPreRelease ? -1 : 1;
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>Dot-separated identifiers; numeric ones compare as numbers, so beta.10 &gt; beta.2.</summary>
    private static int ComparePreRelease(string a, string b)
    {
        var left = a.Split('.');
        var right = b.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var leftIsNumber = int.TryParse(left[i], out var l);
            var rightIsNumber = int.TryParse(right[i], out var r);
            var result = (leftIsNumber, rightIsNumber) switch
            {
                (true, true) => l.CompareTo(r),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left[i], right[i]),
            };

            if (result != 0)
            {
                return result;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;

    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;

    public static bool operator >=(SemVersion a, SemVersion b) => a.CompareTo(b) >= 0;

    public static bool operator <=(SemVersion a, SemVersion b) => a.CompareTo(b) <= 0;

    public override string ToString() => IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";
}
