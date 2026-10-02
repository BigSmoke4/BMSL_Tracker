using System.Security.Claims;
using System.Text;

namespace BMSL_Tracker.Services;

/// <summary>
/// Derives stable, unique usernames from external (Google) identities.
/// Kept free of ASP.NET Identity dependencies so the naming policy is unit-testable.
/// </summary>
public static class ExternalUserNames
{
    /// <summary>Custom claim carrying the human-friendly name (e.g. "Jane Doe") from the provider.</summary>
    public const string DisplayNameClaimType = "urn:bmsl:display-name";

    private const int BaseNameMaxLength = 32;
    private const string FallbackPrefix = "user";

    public static string? GetEmail(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.Email);

    public static string? GetDisplayName(ClaimsPrincipal principal)
    {
        var name = principal.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        var given = principal.FindFirstValue(ClaimTypes.GivenName);
        var family = principal.FindFirstValue(ClaimTypes.Surname);
        var composed = string.Join(' ', new[] { given, family }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return string.IsNullOrWhiteSpace(composed) ? null : composed.Trim();
    }

    /// <summary>
    /// Builds the preferred base username: e-mail local part (drop +tag), otherwise the display name.
    /// Output contains only [a-z0-9._-] and is lower-case.
    /// </summary>
    public static string BuildBaseUserName(string? email, string? displayName)
    {
        var raw = FirstUsable(
            Sanitize(StripPlusTag(GetEmailLocalPart(email))),
            Sanitize(displayName?.Replace(' ', '.')),
            FallbackPrefix);

        if (raw.Length == 0)
        {
            raw = FallbackPrefix;
        }

        return raw.Length > BaseNameMaxLength ? raw[..BaseNameMaxLength].TrimEnd('.', '_', '-') : raw;
    }

    /// <summary>
    /// Returns <paramref name="baseName"/> or <c>baseName&lt;n&gt;</c> (n = 1..999, then random)
    /// for the first candidate that <paramref name="isTaken"/> reports as available.
    /// </summary>
    public static async Task<string> ReserveUniqueUserNameAsync(
        string baseName, Func<string, Task<bool>> isTaken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentNullException.ThrowIfNull(isTaken);

        if (!await isTaken(baseName))
        {
            return baseName;
        }

        for (var i = 1; i <= 999; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = $"{baseName}{i}";
            if (!await isTaken(candidate))
            {
                return candidate;
            }
        }

        for (var attempt = 0; attempt < 64; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = $"{baseName}{Random.Shared.Next(100_000, 1_000_000)}";
            if (!await isTaken(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to reserve a unique user name for the external login.");
    }

    private static string FirstUsable(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return FallbackPrefix;
    }

    private static string? GetEmailLocalPart(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }

    private static string StripPlusTag(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var plus = value.IndexOf('+');
        return plus > 0 ? value[..plus] : value;
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            // ASCII alphanumerics only — must stay inside Identity's AllowedUserNameCharacters.
            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if ((ch is '.' or '_' or '-') && builder.Length > 0 && builder[^1] != ch)
            {
                builder.Append(ch);
            }
            // everything else (spaces, diacritics, emojis, '@', '+') is dropped
        }

        return builder.ToString();
    }
}
