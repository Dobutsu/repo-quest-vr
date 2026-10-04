using System.ComponentModel;
using System.Globalization;

namespace RepoQuest.Builder.App.Localization;

/// <summary>
/// The window's texts in Russian and English. XAML shows them with <c>{l:Tr key}</c> (bound to <see cref="Table"/>, so
/// switching the language re-renders every text at once); code asks <see cref="T"/> and <see cref="F"/>, and view
/// models listen to <see cref="LanguageChanged"/> for the texts they compose.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private Loc()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised on the UI thread after <see cref="Table"/> changed</summary>
    public event Action? LanguageChanged;

    /// <summary>"ru" or "en"</summary>
    public string Language { get; private set; } = "en";

    /// <summary>The current language's texts; a new instance on every switch (bindings follow this property)</summary>
    public IReadOnlyDictionary<string, string> Table { get; private set; } = Strings.En;

    /// <summary>Number formats of the current language (decimal comma in Russian)</summary>
    public CultureInfo Culture => Language == "ru" ? Russian : English;

    public void SetLanguage(string language)
    {
        language = language == "ru" ? "ru" : "en";
        if (language == Language)
            return;
        Language = language;
        Table = language == "ru" ? Strings.Ru : Strings.En;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Table)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke();
    }

    /// <summary>Russian on a Russian system, English everywhere else</summary>
    public static string SystemLanguage() =>
        CultureInfo.CurrentUICulture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";

    /// <summary>The text for a key; English if the current language lacks it, the key itself if both do</summary>
    public static string T(string key) => TryT(key) ?? key;

    /// <summary>The text for a key, or null when neither language has it (stage codes Core may add later)</summary>
    public static string? TryT(string key) =>
        Instance.Table.TryGetValue(key, out var text) || Strings.En.TryGetValue(key, out text) ? text : null;

    /// <summary>A text with placeholders ({0}, {1}) filled in the current language's number format</summary>
    public static string F(string key, params object?[] args) => string.Format(Instance.Culture, T(key), args);

    /// <summary>Sizes as people read them: "6 GB", "1.5 GB", "150 MB" (decimal comma and ГБ/МБ in Russian)</summary>
    public static string Size(long bytes)
    {
        const double gb = 1L << 30, mb = 1L << 20;
        if (bytes >= gb)
            return F("unit.gb", Math.Round(bytes / gb, bytes >= 10 * gb ? 0 : 1));
        return F("unit.mb", Math.Max(1, Math.Round(bytes / mb)));
    }
}
