namespace RepoQuest.Builder;

/// <summary>
/// A failure the player can act on. <see cref="Message"/> is shown in the window as is (it names what went wrong and,
/// where possible, what to do); <see cref="Details"/> (tool output, log excerpts) goes to the log.
/// </summary>
public sealed class BuildException : Exception
{
    public BuildException(string message, string? details = null, Exception? inner = null) : base(message, inner)
    {
        Details = details;
    }

    public string? Details { get; }

    /// <summary>A stable code for the window to pick a localized explanation (null: show the message)</summary>
    public string? Code { get; init; }
}
