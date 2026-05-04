using System.Windows;
using VerbaCore.Models;

namespace VerbaCore.Services;

public sealed class LocalizationService
{
    private ResourceDictionary? _currentDictionary;
    private UiLanguage _currentLanguage;

    public event Action? LanguageChanged;

    public UiLanguage CurrentLanguage => _currentLanguage;

    public void Apply(UiLanguage language)
    {
        if (_currentDictionary is not null && _currentLanguage == language)
            return;

        var uri = language switch
        {
            UiLanguage.English => new Uri("Resources/Strings.en.xaml", UriKind.Relative),
            _ => new Uri("Resources/Strings.ko.xaml", UriKind.Relative)
        };

        var newDict = new ResourceDictionary { Source = uri };
        var merged = Application.Current.Resources.MergedDictionaries;

        if (_currentDictionary is not null)
            merged.Remove(_currentDictionary);

        merged.Add(newDict);
        _currentDictionary = newDict;
        _currentLanguage = language;

        LanguageChanged?.Invoke();
    }

    public string Get(string key)
    {
        return Application.Current.TryFindResource(key) as string ?? key;
    }
}
