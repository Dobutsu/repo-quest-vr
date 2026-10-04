using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using RepoQuest.Builder.App.Localization;

namespace RepoQuest.Builder.App.Views;

/// <summary>
/// A modal dialog built in code: a title, content (paragraphs, lists, links, checkboxes) and buttons that close it with
/// a result. Every dialog of the builder is one of these; they are short-lived and mostly text.
/// </summary>
public sealed class Dialog<TResult> : Window
{
    private readonly StackPanel body = new() { Spacing = 10 };
    private readonly StackPanel buttons = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        HorizontalAlignment = HorizontalAlignment.Right,
    };
    private readonly TResult cancelResult;

    public Dialog(string title, TResult cancelResult)
    {
        this.cancelResult = cancelResult;
        Title = title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                Docked(new Border { Child = buttons, Margin = new Thickness(0, 16, 0, 0) }, Dock.Bottom),
                Docked(new Border { Child = heading, Margin = new Thickness(0, 0, 0, 12) }, Dock.Top),
                new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
            },
        };
    }

    public Dialog<TResult> Text(string text, double opacity = 1, bool bold = false)
    {
        body.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Opacity = opacity,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        });
        return this;
    }

    /// <summary>Numbered steps</summary>
    public Dialog<TResult> Steps(params string[] steps)
    {
        var list = new StackPanel { Spacing = 6, Margin = new Thickness(4, 0, 0, 0) };
        for (var i = 0; i < steps.Length; i++)
        {
            list.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("24,*"),
                Children =
                {
                    new TextBlock { Text = $"{i + 1}.", Opacity = 0.7 },
                    Column(new TextBlock { Text = steps[i], TextWrapping = TextWrapping.Wrap }, 1),
                },
            });
        }
        body.Children.Add(list);
        return this;
    }

    /// <summary>A clickable address, opened in the browser</summary>
    public Dialog<TResult> Link(string text, string url)
    {
        var link = new HyperlinkButton { Content = text, Padding = new Thickness(0) };
        ToolTip.SetTip(link, url);
        link.Click += (_, _) => OpenUrl(url);
        body.Children.Add(link);
        return this;
    }

    public Dialog<TResult> Add(Control control)
    {
        body.Children.Add(control);
        return this;
    }

    /// <summary>A button that closes the dialog with <paramref name="result"/>; the accent one is the default action</summary>
    public Dialog<TResult> Button(string text, TResult result, bool accent = false, Func<bool>? enabled = null)
    {
        var button = new Button { Content = text, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (accent)
            button.Classes.Add("accent");
        button.Click += (_, _) => Close(result);
        if (enabled is not null)
        {
            button.IsEnabled = enabled();
            enabledChecks.Add(() => button.IsEnabled = enabled());
        }
        buttons.Children.Add(button);
        return this;
    }

    /// <summary>A button that does something without closing (open Unity Hub, create a report)</summary>
    public Dialog<TResult> Action(string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => action();
        buttons.Children.Insert(0, button);
        return this;
    }

    private readonly List<Action> enabledChecks = [];

    /// <summary>Buttons whose state depends on content (a checkbox) look again</summary>
    public void Revalidate() => enabledChecks.ForEach(check => check());

    public async Task<TResult> ShowAsync(Window owner)
    {
        var result = await ShowDialog<TResult?>(owner);
        return result is null ? cancelResult : result;
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Warn($"could not open {url}: {e.Message}");
        }
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    private static Control Column(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}

/// <summary>Small pieces several dialogs use</summary>
public static class DialogParts
{
    /// <summary>Text with a details expander (tool output, log excerpts) that can be selected and copied</summary>
    public static Control Details(string details) => new Expander
    {
        Header = Loc.T("common.details"),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Content = new ScrollViewer
        {
            MaxHeight = 220,
            Content = new SelectableTextBlock
            {
                Text = details,
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            },
        },
    };

    /// <summary>A line with a bold name and a dimmed explanation</summary>
    public static Control Item(string name, string about, string? extra)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        text.Inlines!.Add(new Run(name) { FontWeight = FontWeight.SemiBold });
        text.Inlines.Add(new LineBreak());
        text.Inlines.Add(new Run(about) { Foreground = Brushes.Gray });
        if (extra is not null)
        {
            text.Inlines.Add(new LineBreak());
            text.Inlines.Add(new Run(extra) { Foreground = Brushes.Gray, FontSize = 12 });
        }
        return text;
    }
}
