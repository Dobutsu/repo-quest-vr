namespace RepoQuest.Builder.Setup;

/// <summary>
/// Whether Unity can run: a Personal license comes from signing in to Unity Hub (it cannot be activated from the
/// command line). The license files say whether Hub has made one; the editor's own refusal (a "no license" error in
/// its log, see UnityBuildStep) is the final word.
/// </summary>
public static class UnityLicense
{
    public static IEnumerable<string> LicenseFiles()
    {
        if (Platform.IsWindows)
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Unity", "licenses", "UnityEntitlementLicense.xml");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Unity", "Unity_lic.ulf");
        }
        else if (Platform.IsMac)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, "Library", "Unity", "licenses", "UnityEntitlementLicense.xml");
            yield return "/Library/Application Support/Unity/Unity_lic.ulf";
        }
    }

    /// <summary>Hub has stored a license for this user (it may still be expired; Hub renews it while signed in)</summary>
    public static bool LooksLicensed() => LicenseFiles().Any(File.Exists);
}
