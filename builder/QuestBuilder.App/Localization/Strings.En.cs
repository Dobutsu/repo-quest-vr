namespace RepoQuest.Builder.App.Localization;

/// <summary>The window's texts by key: English here, Russian in Strings.Ru.cs (both must have the same keys)</summary>
internal static partial class Strings
{
    public static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        ["app.subtitle"] = "Builds R.E.P.O. for Meta Quest 3 from your own copy of the game",
        ["app.version"] = "version {0}",
        ["lang.tooltip"] = "Interface language",
        ["unit.gb"] = "{0} GB",
        ["unit.mb"] = "{0} MB",

        ["common.cancel"] = "Cancel",
        ["common.close"] = "Close",
        ["common.ok"] = "OK",
        ["common.tryAgain"] = "Try again",
        ["common.license"] = "License",
        ["common.details"] = "Details",
        ["common.unknown"] = "unknown",
        ["common.checking"] = "Checking…",

        ["startup.failed"] = "The builder could not start: {0}",

        ["banner.update.title"] = "Version {0} is available",
        ["banner.update.button"] = "Update now",
        ["banner.update.downloading"] = "Downloading the update… {0}",
        ["banner.gameChanged"] = "The game was updated. Rebuild the APK to keep playing with friends on PC.",
        ["banner.builderChanged"] = "This is a new version of the builder. Rebuild the APK to get its fixes.",
        ["banner.untested"] =
            "This game version ({0}) has not been tested with this builder. Check for a builder update before building.",

        ["game.title"] = "Game folder",
        ["game.notFound"] = "Not found. Choose the folder with REPO.exe.",
        ["game.version"] = "Version: {0}",
        ["game.tested"] = "tested with this builder",
        ["game.untested"] = "not tested with this builder",
        ["game.choose"] = "Choose folder…",
        ["game.pickerTitle"] = "Choose the R.E.P.O. folder (the one with REPO.exe)",

        ["ws.title"] = "Working folder",
        ["ws.about"] = "Downloads, the Unity project and the APK are kept here.",
        ["ws.space"] = "Free: {0} · needed: about {1}",
        ["ws.spaceUnknown"] = "Free space: unknown",
        ["ws.spaceLow"] = "Not enough free space for the build.",
        ["ws.change"] = "Change…",
        ["ws.pickerTitle"] = "Choose the working folder",

        ["problem.folder-missing"] = "This folder does not exist.",
        ["problem.folder-not-game"] =
            "This is not the game's folder. Choose the folder with REPO.exe (in Steam: right-click R.E.P.O. → Manage → " +
            "Browse local files).",
        ["problem.workspace-invalid"] = "This folder cannot be used.",
        ["problem.workspace-not-ascii"] =
            "Use a path with Latin letters only: Unity breaks on other characters (for example, a Cyrillic folder name). " +
            "Something like D:\\RepoQuest works.",
        ["problem.workspace-too-long"] =
            "This path is too long: Windows build tools break on long paths. Keep it short, like D:\\RepoQuest.",
        ["problem.workspace-in-game"] =
            "The working folder cannot be inside the game's folder. Pick another place, like D:\\RepoQuest.",
        ["problem.workspace-program-files"] = "Windows protects Program Files. Pick another place, like D:\\RepoQuest.",

        ["build.title"] = "Build",
        ["build.about"] =
            "The first build takes about 1–1.5 hours plus downloads; after updates, about 40 minutes. You can use the " +
            "computer meanwhile.",
        ["build.lastApk"] = "Last APK: {0}",
        ["build.running"] = "Building… {0}",
        ["build.stopping"] = "Stopping…",

        ["step.unity-setup.title"] = "Unity",
        ["step.unity-setup.about"] = "The engine that builds the Quest version. Installed once, about 15 GB.",
        ["step.setup.title"] = "Build tools",
        ["step.setup.about"] = "Python and helper scripts, kept inside the working folder.",
        ["step.export.title"] = "Game files",
        ["step.export.about"] =
            "Copies the game's assets from your copy into a Unity project. Runs again only after game updates.",
        ["step.bake.title"] = "VR mod",
        ["step.bake.about"] = "Builds the RepoXR VR mod into the game's code.",
        ["step.project.title"] = "Quest project",
        ["step.project.about"] = "Adapts the project for the headset: graphics, controls, VR.",
        ["step.unity.title"] = "APK",
        ["step.unity.about"] = "Unity imports everything and builds the game for Quest. The longest step.",

        ["stage.export.download"] = "Downloading AssetRipper",
        ["stage.export.load"] = "Reading the game's files",
        ["stage.export.export"] = "Exporting assets",
        ["stage.bake.download"] = "Downloading RepoXR and libraries",
        ["stage.bake.bake"] = "Building the mod into the game",
        ["stage.project.download"] = "Downloading files",
        ["stage.project.project"] = "Preparing",
        ["stage.project.done"] = "Finishing",
        ["stage.unity.import"] = "Importing assets",
        ["stage.unity.scripts"] = "Compiling scripts",
        ["stage.unity.scenes"] = "Building levels",
        ["stage.unity.shaders"] = "Compiling shaders",
        ["stage.unity.il2cpp"] = "Compiling the game's code",
        ["stage.unity.gradle"] = "Packing the APK",
        ["stage.unity-setup.hub-download"] = "Downloading Unity Hub",
        ["stage.unity-setup.hub-install"] = "Installing Unity Hub",
        ["stage.unity-setup.editor"] = "Downloading Unity",
        ["stage.unity-setup.editor-install"] = "Installing Unity",

        ["status.Pending"] = "Waiting",
        ["status.Running"] = "Running…",
        ["status.Done"] = "Done",
        ["status.UpToDate"] = "Up to date",
        ["status.Failed"] = "Failed",
        ["status.Cancelled"] = "Cancelled",

        ["button.build"] = "Build APK",
        ["button.cancelBuild"] = "Cancel",
        ["button.install"] = "Install on Quest",
        ["button.openApkFolder"] = "Open APK folder",
        ["button.report"] = "Create report",
        ["button.checkUpdates"] = "Check for updates",

        ["success.title"] = "The APK is ready!",
        ["success.noShare"] = "Do not share this APK: it contains the game's files. Every player builds their own.",

        ["log.title"] = "Log",
        ["log.debug"] = "Show everything (for troubleshooting)",
        ["log.copy"] = "Copy all",

        ["update.title"] = "Updates",
        ["update.latest"] = "You have the latest version.",
        ["update.notConfigured"] = "This copy of the builder does not check for updates.",

        ["consent.title"] = "Before the first build",
        ["consent.intro"] =
            "To build the game, the builder downloads and installs these programs. All of them are free; each comes " +
            "with its own license.",
        ["consent.download"] = "Download: about {0}",
        ["consent.unity-hub.title"] = "Unity Hub",
        ["consent.unity-hub.about"] = "Unity's launcher and installer.",
        ["consent.unity-editor.title"] = "Unity {0} with Android Build Support (SDK, NDK, OpenJDK)",
        ["consent.unity-editor.about"] = "The engine that builds the game for Quest. About 15 GB on disk.",
        ["consent.android.title"] = "Android SDK and NDK",
        ["consent.android.about"] = "Google's tools for making Android apps. Unity Hub installs them.",
        ["consent.assetripper.title"] = "AssetRipper {0}",
        ["consent.assetripper.about"] = "Turns the game's files into a Unity project. GPL license.",
        ["consent.python.title"] = "Python {0} with packages",
        ["consent.python.about"] = "Runs the build scripts. Installed only inside the working folder.",
        ["consent.long-paths.title"] = "Long file paths in Windows",
        ["consent.long-paths.about"] =
            "A system setting the build needs. Windows will ask for administrator rights.",
        ["consent.signIn"] =
            "One step is up to you: signing in to Unity Hub with a free Unity account. The builder shows you how when " +
            "it gets there.",
        ["consent.agree"] = "I accept these licenses",
        ["consent.accept"] = "Accept",

        ["notice.title"] = "Playing with friends",
        ["notice.offline"] =
            "The Quest version works offline right away. To play with friends on PC, log in to Steam inside the " +
            "headset: main menu → Steam button.",
        ["notice.risk"] =
            "The headset logs in through an unofficial Steam client (SteamKit2). Steam's rules formally forbid " +
            "third-party clients: the risk to your account is small, but it is real.",
        ["notice.privacy"] = "Your password goes only to Steam. The builder sends nothing anywhere else.",
        ["notice.ok"] = "Understood",

        ["license.title"] = "Unity needs a free license",
        ["license.failed"] = "Unity refused to run without a license.",
        ["license.intro"] =
            "Unity is free, but it runs only after you sign in with a Unity account. It takes a couple of minutes:",
        ["license.step1"] = "Open Unity Hub (the button below).",
        ["license.step2"] = "Sign in, or create a free Unity account.",
        ["license.step3"] =
            "If Hub asks for a license: Preferences → Licenses (Manage licenses) → Add → \"Get a free personal license\".",
        ["license.step4"] = "Do not install the editor Hub suggests (Unity 6): you don't need it.",
        ["license.step5"] = "Come back here and press \"Check again\".",
        ["license.openHub"] = "Open Unity Hub",
        ["license.checkAgain"] = "Check again",

        ["untested.title"] = "Untested game version",
        ["untested.text"] =
            "Your game version ({0}) has not been tested with this builder. The build may fail, or the game may " +
            "misbehave in the headset. A builder update usually adds support soon: check for updates first.",
        ["untested.build"] = "Build anyway",

        ["cancel.title"] = "Stop the build?",
        ["cancel.text"] = "Finished steps are kept: the next build continues from where this one stopped.",
        ["cancel.stop"] = "Stop",
        ["cancel.continue"] = "Keep building",

        ["close.title"] = "Close the builder?",
        ["close.text"] =
            "The builder is still working. Closing the window stops it; finished build steps are kept.",
        ["close.stop"] = "Stop and close",
        ["close.keep"] = "Don't close",

        ["error.title"] = "Something went wrong",
        ["error.reportHint"] = "If you ask for help, attach a report: it holds only logs, no game files.",
        ["error.report"] = "Create report",
        ["error.installInHub"] = "Install in Unity Hub",
        ["error.installInHubHint"] =
            "Unity Hub opens its install window for the right Unity version. Tick \"Android Build Support\" together " +
            "with \"OpenJDK\" and \"Android SDK & NDK Tools\", install, then press \"Build APK\" again.",
        ["error.unexpected"] = "An unexpected error: {0}",

        ["err.download"] =
            "A download failed. Check your internet connection and try again: the build continues where it stopped.",
        ["err.download-hash"] =
            "A downloaded file is damaged (its checksum does not match). Try again; if it happens again, create a report.",
        ["err.assetripper-start"] =
            "AssetRipper, the program that reads the game's files, did not start. An antivirus may be blocking it: try " +
            "again, or add the working folder to the antivirus exceptions.",
        ["err.assetripper"] =
            "AssetRipper stopped before it finished reading the game's files. Try again; if it happens again, create a report.",
        ["err.export-empty"] =
            "The game's files could not be extracted. Verify the game in Steam (Properties → Installed Files → Verify " +
            "integrity of game files) and try again.",
        ["err.bake-failed"] =
            "This version of the game is not supported yet: the VR mod does not fit it. Wait for a builder update " +
            "(the builder checks for updates when it starts).",
        ["err.bake-tool"] =
            "The builder could not build the VR mod into the game on this computer — it is not your game's version. " +
            "Please create a report and send it to us.",
        ["err.unity-missing"] =
            "Unity is not installed. Install Unity 2022.3.62f3 with Android Build Support (SDK, NDK and OpenJDK) in " +
            "Unity Hub, then build again.",
        ["err.unity-license"] =
            "Unity refused to work without a license. Sign in to Unity Hub, add a free Personal license and build again.",
        ["err.disk-full"] =
            "The disk is full. Free up space (the first build needs about 40 GB) and build again: it continues where it stopped.",
        ["err.disk-space"] =
            "Not enough space in the working folder: the first build needs about 40 GB, later ones about 6 GB. Free up " +
            "space or choose a working folder on another disk.",
        ["err.disk-space-unity"] =
            "There is not enough space for Unity (about 20 GB) on the system disk or on the working folder's disk. " +
            "Free up space and try again.",
        ["err.unity-android"] =
            "Unity is missing its Android modules. In Unity Hub open Installs → Unity 2022.3.62f3 → Add modules and add " +
            "Android Build Support with OpenJDK and Android SDK & NDK Tools.",
        ["err.unity-compile"] =
            "The project's code did not compile. That is the builder's fault, not yours: create a report and send it to us.",
        ["err.unity-failed"] =
            "Unity could not build the APK. Try again; if it happens again, create a report and send it to us.",
        ["err.unity-install-failed"] =
            "Unity Hub could not install Unity by itself. Install it from Unity Hub's own window: use the button below.",
        ["err.hub-feed"] = "Could not reach Unity's download server. Check your internet connection and try again.",
        ["err.hub-hash"] = "The Unity Hub installer was damaged while downloading. Try again.",
        ["err.hub-signature"] =
            "The downloaded Unity Hub installer is not signed by Unity, so it was not run. Try again later, or install " +
            "Unity Hub from unity.com yourself.",
        ["err.hub-uac"] =
            "Installing Unity Hub needs administrator rights, and the Windows prompt was declined. Build again and " +
            "choose \"Yes\" when Windows asks.",
        ["err.hub-install"] = "Unity Hub could not be installed. Install it from unity.com, then build again.",
        ["err.quest-unauthorized"] =
            "The headset has not allowed USB debugging yet. Put it on, accept the prompt (tick \"Always allow from this " +
            "computer\") and try again.",
        ["err.quest-offline"] = "The headset is not responding. Reconnect the cable, wake the headset up and try again.",
        ["err.quest-storage"] =
            "Not enough space on the headset: the game needs about 4 GB. Delete something from the headset and try again.",
        ["err.quest-install"] =
            "Installing on the headset failed. Reconnect the headset and try again; the headset's answer is in the details.",
        ["err.quest-backup"] =
            "Your saves could not be copied from the headset, so nothing was changed on it. Reconnect the headset and " +
            "try again.",
        ["err.quest-restore"] =
            "The game is installed, but your saves could not be put back. They are safe on this computer, in the " +
            "working folder (backups); try installing again.",
        ["err.adb-missing"] =
            "The program that installs games on the headset (adb) comes with Unity, which is not installed yet. Build " +
            "the APK first, or install it with SideQuest or Meta Quest Developer Hub.",
        ["err.update-signature"] = "The update is not signed by the builder's authors, so it was not installed.",
        ["err.update-offline"] = "Could not check for updates. Check your internet connection.",
        ["err.game-missing"] = "The game was not found. Choose the game's folder (the one with REPO.exe).",

        ["install.searching"] = "Looking for the headset…",
        ["install.stop"] = "Closing the game on the headset…",
        ["install.install"] = "Installing on the headset (a minute or two)…",
        ["install.backup"] = "Copying your saves from the headset…",
        ["install.uninstall"] = "Removing the old version…",
        ["install.restore"] = "Putting your saves back…",
        ["install.done.title"] = "Installed on the headset",
        ["install.done.text"] =
            "Find the game on the headset in Library → Unknown Sources (the filter at the top right).",
        ["install.done.reinstalled"] =
            "The old copy was signed by another computer, so it could not be updated in place: the game was " +
            "reinstalled, and your saves and settings were copied back.",
        ["install.none.title"] = "Headset not found",
        ["install.none.intro"] = "Connect the headset to this computer:",
        ["install.none.step1"] =
            "Turn on Developer Mode for the headset in the Meta Horizon app on your phone (headset settings → " +
            "Developer Mode).",
        ["install.none.step2"] = "Connect it with a USB-C cable that carries data (some cables only charge).",
        ["install.none.step3"] = "Put the headset on and allow USB debugging.",
        ["install.none.drivers"] = "On Windows, also install Meta's \"Oculus ADB Drivers\":",
        ["install.none.alternative"] =
            "You can also install the APK yourself with SideQuest or Meta Quest Developer Hub.",
        ["install.unauthorized.title"] = "Allow USB debugging",
        ["install.unauthorized.text"] =
            "The headset is connected but does not trust this computer yet. Put it on and accept \"Allow USB " +
            "debugging?\" (tick \"Always allow from this computer\").",
        ["install.pick.title"] = "Choose the headset",
        ["install.pick.text"] = "Several devices are connected. Which one gets the game?",
        ["install.pick.button"] = "Install",
        ["install.deviceUnknown"] = "Android device",
    };
}
