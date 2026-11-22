using System;

namespace RepoQuest.Cloud
{
    internal static class CloudWebsite
    {
        internal const int MaxLength = 2048;

        internal static string Normalize(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0) return "";
            if (value.Length > MaxLength) throw new FormatException("Enter a valid HTTP or HTTPS website address.");
            foreach (var character in value)
                if (char.IsControl(character)) throw new FormatException("Enter a valid HTTP or HTTPS website address.");
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || string.IsNullOrEmpty(uri.Host))
                throw new FormatException("Enter a valid HTTP or HTTPS website address.");
            return value;
        }

        internal static string Load(string value)
        {
            try { return Normalize(value); }
            catch (FormatException) { return ""; }
        }
    }
}
