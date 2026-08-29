using System.Globalization;
using Ailo.Services;
using Ailo.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ailo.Localization;

public sealed class LocalizationService : ObservableObject
{
    private string _language = string.Empty;

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [new("zh-CN", "简体中文"), new("zh-TW", "繁體中文"), new("en-US", "English")];

    public string CurrentLanguage => _language;
    public string this[string key] => Resources.ResourceManager.GetString(key, Resources.Culture ?? CultureInfo.CurrentUICulture) ?? key;

    public void SetLanguage(string language)
    {
        var option = LanguageOptions.FirstOrDefault(item => string.Equals(item.Key, language, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            return;
        }

        _language = option.Key;
        var culture = CultureInfo.GetCultureInfo(option.Key);
        Resources.Culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        OnPropertyChanged(string.Empty);
    }
}
