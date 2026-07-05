using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace WTK.MediaForge.Studio.Localization;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new(
        "WTK.MediaForge.Studio.Resources.Strings",
        typeof(LocalizationManager).Assembly);

    private CultureInfo _currentCulture = CultureInfo.GetCultureInfo("pt-BR");

    private LocalizationManager()
    {
    }

    public static LocalizationManager Instance { get; } = new();

    public CultureInfo CurrentCulture => _currentCulture;

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return Resources.GetString(key, _currentCulture)
                ?? Resources.GetString(key, CultureInfo.InvariantCulture)
                ?? key;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (Equals(_currentCulture, culture))
        {
            return;
        }

        _currentCulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
