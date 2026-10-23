using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace IdleGuard.Services;

#if WINDOWS
[WinRT.GeneratedBindableCustomProperty]
#endif
public sealed partial class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Current { get; } = new();
    private readonly Dictionary<string, Dictionary<string, string>> _languages = new();
    public string Language { get; private set; } = "zh-CN";
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language);
    public string this[string key] => _languages[Language].GetValueOrDefault(key) ?? key;

    private LocalizationService()
    {
        foreach (var language in new[] { "en-US", "zh-CN" })
        {
            using var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream($"IdleGuard.Strings.{language}.json")
                ?? throw new InvalidOperationException($"Missing language resource: {language}");
            _languages[language] = JsonSerializer.Deserialize(stream, AppJsonContext.Default.LanguageDictionary)!;
        }
    }

    public void SetLanguage(string language)
    {
        if (!_languages.ContainsKey(language) || Language == language) return;
        Language = language;
        PropertyChanged?.Invoke(this, new("Item[]"));
        PropertyChanged?.Invoke(this, new(nameof(Language)));
    }

    public string Format(string key, params object[] args) => string.Format(Culture, this[key], args);
    public event PropertyChangedEventHandler? PropertyChanged;
}
