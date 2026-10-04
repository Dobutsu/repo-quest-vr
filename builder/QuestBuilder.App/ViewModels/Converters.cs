using Avalonia.Data.Converters;
using Avalonia.Media;
using RepoQuest.Builder.Pipeline;

namespace RepoQuest.Builder.App.ViewModels;

/// <summary>Small value converters the main window's templates use</summary>
public static class Converters
{
    public static readonly IValueConverter IsPending =
        new FuncValueConverter<StepStatus, bool>(status => status == StepStatus.Pending);

    public static readonly IValueConverter DebugOpacity =
        new FuncValueConverter<bool, double>(debug => debug ? 0.6 : 1.0);

    private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#C77C00"));
    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#D13438"));

    /// <summary>Warnings and errors stand out; other lines keep the theme's text color (unset, not null)</summary>
    public static readonly IValueConverter LevelBrush = new LevelBrushConverter();

    private sealed class LevelBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value switch
            {
                LogLevel.Warning => Warning,
                LogLevel.Error => Error,
                _ => Avalonia.AvaloniaProperty.UnsetValue,
            };

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
