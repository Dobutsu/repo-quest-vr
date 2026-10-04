using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace RepoQuest.Builder.App.Localization;

/// <summary>
/// <c>{l:Tr key}</c> in XAML: a text that follows the language switch. It binds to <see cref="Loc.Table"/> (a plain
/// property that changes on every switch) and looks the key up in it, so no indexer change notifications are involved.
/// </summary>
public sealed class TrExtension
{
    public TrExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; }

    public ReflectionBinding ProvideValue(IServiceProvider serviceProvider) => new(nameof(Loc.Table))
    {
        Source = Loc.Instance,
        Mode = BindingMode.OneWay,
        Converter = LookupConverter.Instance,
        ConverterParameter = Key,
    };

    private sealed class LookupConverter : IValueConverter
    {
        public static readonly LookupConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            parameter is string key ? Loc.T(key) : null;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
