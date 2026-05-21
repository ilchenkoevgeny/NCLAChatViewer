namespace NclaChatViewer.Services;

public sealed class ThemeService
{
    public void ApplyTheme(string theme)
    {
        string normalizedTheme = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";

        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;

        for (int i = dictionaries.Count - 1; i >= 0; i--)
        {
            string? source = dictionaries[i].Source?.OriginalString;
            if (source is not null && source.Contains("Themes/", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Add(new System.Windows.ResourceDictionary
        {
            Source = new Uri($"Themes/{normalizedTheme}.xaml", UriKind.Relative)
        });
    }
}
