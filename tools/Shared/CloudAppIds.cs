using System;

namespace RepoQuest.Cloud
{
    public static class CloudAppIds
    {
        public static bool IsValid(string value) => Guid.TryParse(value, out var id) && id != Guid.Empty;
    }
}
