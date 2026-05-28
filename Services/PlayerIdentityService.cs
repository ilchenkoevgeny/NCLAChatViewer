namespace NclaChatViewer.Services;

public static class PlayerIdentityService
{
    public static bool IsPlaceholder(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "@", StringComparison.Ordinal);
    }

    public static string ExtractDescriptor(string? player)
    {
        if (string.IsNullOrWhiteSpace(player))
        {
            return string.Empty;
        }

        int atIndex = player.IndexOf('@');
        return atIndex >= 0
            ? player[atIndex..].Trim()
            : string.Empty;
    }

    public static string NormalizeForSearch(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }
}
