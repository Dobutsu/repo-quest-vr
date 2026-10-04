using RepoQuest.Builder.App.Localization;
using RepoQuest.Builder.Quest;

namespace RepoQuest.Builder.App.Views;

/// <summary>Every dialog of the builder, built and not yet shown (the window shows them; --screenshot renders them)</summary>
public static class Dialogs
{
    private static readonly string[] HubInstallCodes = ["unity-install-failed", "hub-install", "hub-uac", "hub-feed", "hub-signature"];

    public static Dialog<bool> Error(Exception error, Action? createReport, Action? installInHub)
    {
        var build = error as BuildException;
        var explained = build?.Code is { } code ? Loc.TryT($"err.{code}") : null;
        var dialog = new Dialog<bool>(Loc.T("error.title"), false)
            .Text(explained ?? (build is not null ? build.Message : Loc.F("error.unexpected", error.Message)));
        if (explained is not null)
            dialog.Text(build!.Message, opacity: 0.7);
        if (build?.Code is { } hubCode && HubInstallCodes.Contains(hubCode) && installInHub is not null)
        {
            dialog.Text(Loc.T("error.installInHubHint"));
            dialog.Action(Loc.T("error.installInHub"), installInHub);
        }
        var details = build is not null ? build.Details : error.ToString();
        if (!string.IsNullOrWhiteSpace(details))
            dialog.Add(DialogParts.Details(details));
        if (createReport is not null)
        {
            dialog.Text(Loc.T("error.reportHint"), opacity: 0.7);
            dialog.Action(Loc.T("error.report"), createReport);
        }
        return dialog.Button(Loc.T("common.close"), true, accent: true);
    }

    public static Dialog<bool> Message(string title, params string[] paragraphs)
    {
        var dialog = new Dialog<bool>(title, false);
        foreach (var paragraph in paragraphs)
            dialog.Text(paragraph);
        return dialog.Button(Loc.T("common.ok"), true, accent: true);
    }

    public static Dialog<bool> Confirm(string title, string text, string yes, string no) =>
        new Dialog<bool>(title, false).Text(text).Button(no, false).Button(yes, true, accent: true);

    public static Dialog<int> PickDevice(IReadOnlyList<AdbDevice> devices)
    {
        var dialog = new Dialog<int>(Loc.T("install.pick.title"), -1).Text(Loc.T("install.pick.text"));
        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            dialog.Button($"{device.Model ?? Loc.T("install.deviceUnknown")} ({device.Serial})", i, accent: device.IsQuest);
        }
        return dialog.Button(Loc.T("common.cancel"), -1);
    }

    /// <summary>No headset, or one that has not allowed USB debugging; true = try again</summary>
    public static Dialog<bool> InstallHelp(bool unauthorized)
    {
        if (unauthorized)
        {
            return new Dialog<bool>(Loc.T("install.unauthorized.title"), false)
                .Text(Loc.T("install.unauthorized.text"))
                .Button(Loc.T("common.cancel"), false)
                .Button(Loc.T("common.tryAgain"), true, accent: true);
        }
        var dialog = new Dialog<bool>(Loc.T("install.none.title"), false)
            .Text(Loc.T("install.none.intro"))
            .Steps(Loc.T("install.none.step1"), Loc.T("install.none.step2"), Loc.T("install.none.step3"));
        if (OperatingSystem.IsWindows())
        {
            dialog.Text(Loc.T("install.none.drivers"))
                .Link("Oculus ADB Drivers", "https://developers.meta.com/horizon/downloads/package/oculus-adb-drivers/");
        }
        return dialog.Text(Loc.T("install.none.alternative"), opacity: 0.7)
            .Button(Loc.T("common.cancel"), false)
            .Button(Loc.T("common.tryAgain"), true, accent: true);
    }

    public static Dialog<bool> Consent(IReadOnlyList<ConsentItem> items)
    {
        var dialog = new Dialog<bool>(Loc.T("consent.title"), false).Text(Loc.T("consent.intro"));
        foreach (var item in items)
        {
            var title = Loc.F($"consent.{item.Id}.title", item.Version);
            var size = item.DownloadBytes > 0 ? Loc.F("consent.download", Loc.Size(item.DownloadBytes)) : null;
            dialog.Add(DialogParts.Item(title, Loc.T($"consent.{item.Id}.about"), size));
            if (item.LicenseUrl is { } url)
                dialog.Link(Loc.T("common.license"), url);
        }
        dialog.Text(Loc.T("consent.signIn"), opacity: 0.85);
        var agree = new Avalonia.Controls.CheckBox { Content = Loc.T("consent.agree") };
        agree.IsCheckedChanged += (_, _) => dialog.Revalidate();
        return dialog.Add(agree)
            .Button(Loc.T("common.cancel"), false)
            .Button(Loc.T("consent.accept"), true, accent: true, enabled: () => agree.IsChecked == true);
    }

    public static Dialog<bool> OnlineNotice() =>
        new Dialog<bool>(Loc.T("notice.title"), true)
            .Text(Loc.T("notice.offline"))
            .Text(Loc.T("notice.risk"))
            .Text(Loc.T("notice.privacy"), opacity: 0.8)
            .Button(Loc.T("notice.ok"), true, accent: true);

    public static Dialog<LicenseChoice> License(bool afterFailure, Action openHub)
    {
        var dialog = new Dialog<LicenseChoice>(Loc.T("license.title"), LicenseChoice.Cancel);
        if (afterFailure)
            dialog.Text(Loc.T("license.failed"), bold: true);
        return dialog.Text(Loc.T("license.intro"))
            .Steps(Loc.T("license.step1"), Loc.T("license.step2"), Loc.T("license.step3"), Loc.T("license.step4"),
                Loc.T("license.step5"))
            .Action(Loc.T("license.openHub"), openHub)
            .Button(Loc.T("common.cancel"), LicenseChoice.Cancel)
            .Button(Loc.T("license.checkAgain"), LicenseChoice.CheckAgain, accent: true);
    }

    public static Dialog<bool> Untested(string? gameVersion) =>
        new Dialog<bool>(Loc.T("untested.title"), false)
            .Text(Loc.F("untested.text", gameVersion ?? "?"))
            .Button(Loc.T("common.cancel"), false)
            .Button(Loc.T("untested.build"), true, accent: true);
}
