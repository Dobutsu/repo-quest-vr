using System;

namespace RepoQuest.Cloud
{
    internal static class CloudInput
    {
        internal static string Clipboard(string text, int limit)
        {
            var value = (text ?? "").Trim();
            if (value.Length == 0) throw new FormatException("Clipboard is empty.");
            if (value.Length > limit) throw new FormatException("Clipboard text is too long.");
            if (value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new FormatException("Copy a single complete code or App ID.");
            return value;
        }
    }
}
