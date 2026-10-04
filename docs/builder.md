# REPO Quest Builder: the players' builder

_Started 2026-10-03._ A Windows program that builds the Quest version of R.E.P.O. from the player's own copy of the
game. The player runs `RepoQuestBuilder.exe`; the program finds the game, downloads what it needs, builds the APK and
installs it on the headset. New versions are installed through it as well. The players' instructions are in
`builder/release/README.txt` (shipped in the zip).

The window is in English by default; the EN/RU switch in its header turns on Russian, and the choice is remembered.

There is no Mac release for now (the author's decision of 2026-10-03). The Mac stays the development machine: our
builds run the same pipeline from the terminal (`scripts/build.sh`), and the window is run from source for testing.

## Design

```
builder/
  QuestBuilder.Core/   all the logic: pipeline, downloads, Unity, Python, updates, installing on the Quest
  QuestBuilder.App/    the window (Avalonia), the players' exe: RepoQuestBuilder.exe
  QuestBuilder.Cli/    the same pipeline from the terminal (our builds, tests, release tools)
  QuestBuilder.Tests/  unit tests (dotnet test builder/QuestBuilder.Tests)
  kit.json             versions and SHA-256 of everything downloaded; the list of tested game versions
  python/              requirements.in + requirements.lock (hashes for Windows and macOS)
  release/             README.txt and THIRD-PARTY-NOTICES.txt for the players' zip
  licenses/, SOURCES.md  license texts and where the kit's contents come from
```

**The pipeline** (`QuestBuilder.Core/Pipeline`) is a C# port of the bash scripts `make-quest-project.sh`,
`make-quest-vr.sh`, `apply-quest-runtime.sh` and `build-quest.sh` (deleted; they remain in git history):

| Step | What it does | Rebuilt when these change |
|---|---|---|
| export | AssetRipper 2.0.0 without a window (`--headless`, local HTTP API: `/Settings/Update`, `/LoadFolder`, `/Export/UnityProject`) | the game's files (sizes and dates), AssetRipper's version, settings |
| bake | HarmonyBaker: RepoXR's patches are baked into the game's assemblies | the game's DLLs, RepoXR.dll and RepoXR.Standalone.dll from the kit, dependencies, the baker itself |
| project | a copy of the export + Python scripts + plugins, `link.xml`, RepoXR's assets; Library is kept | the export, the bake output (DLL contents), the kit's contents |
| unity | two editor runs: `QuestBuild.Prepare`, `QuestBuild.BuildApk` (`-versionName/-versionCode`) | the project, the version, build flags |

Each step writes a "stamp" of its inputs to the workspace's `state.json`. A step with an unchanged stamp is skipped,
and an interrupted build resumes from the step that failed. So a builder update rebuilds from bake onward (the export
stays), and a game update rebuilds from the export.

**AssetRipper gives assets random GUIDs on every export** (checked 2026-10-03: two exports in a row differ in 23,000
files, mostly .meta). References are consistent within one export, so the result is unaffected. But after a new export,
Unity reimports everything (~12 minutes on an M-series Mac). Builder updates do not touch the export. If reimports after
game updates become a problem, a script could replace the random GUIDs with ones derived from the asset path.

**Python scripts** stay as leaf steps. They take their paths from `scripts/lib/paths.py`: the variables
`REPOQUEST_WORK`, `REPOQUEST_GAME`, `REPOQUEST_UNITY_EDITOR`, `REPOQUEST_TOOL`; without them they use the old Mac paths.
Text is written with `newline="\n"`, and Python runs in UTF-8 mode (`PYTHONUTF8=1`), so Windows produces the same bytes
as the Mac. `make-kit.sh` checks that no text write forgot `newline=`. `stereo_halos.py` derives component IDs from a
hash (they used to be random, and Unity reimported 136 prefabs on every build).

**Checking the port (2026-10-03):** the old bash pipeline and the new one, on the same export, produced identical
hashes for all 32,502 project files (excluding Library). The baked DLLs, `addressables-entries.json` and
`shader-states.json` matched too, and the dependencies downloaded from their publishers' sites matched what was in
`work/` byte for byte.

**The kit** (`scripts/make-kit.sh` → `work/kit`): `scripts/`, `unity-tools/` and what we build from source: RepoXR.dll
with patches, RepoXR.Standalone.dll, SteamShim and SteamKit2, libopus, RepoXR's Android assets. Plus kit.json, the
Python lock file and licenses. It contains no game files: `make-kit.sh` checks that. In a release, the kit is embedded
in the exe as the `kit.zip` resource and unpacked to `<workspace>\kit\<version>`.

**What the player downloads** (SHA-256 pinned in `kit.json`): AssetRipper (GitHub), uv (GitHub) → a managed Python 3.13
and packages from `requirements.lock` with hashes, all inside the workspace. Also the RepoXR 1.2.3 release from
Thunderstore (RuntimeDeps), BepInEx 5.4.23.5 (GitHub), the NuGet packages BepInEx.BaseLib (nuget.bepinex.dev) and
CustomDiscoverStateLib (nuget.daxcess.io), the source of Unity's built-in shaders, and Unity packages from
packages.unity.com. The player does not need .NET. HarmonyBaker, AssemblyRenamer and Il2CppFixes are called as
`<exe> --tool <name> ...`. The window is a single-file exe, and inside it MonoMod (HarmonyX's detours, which
HarmonyBaker relies on) cannot find the JIT: in a single-file app, `clrjit` is embedded in the exe itself (checked
under CrossOver on 2026-10-03: "Could not locate clrjit library"). So the tools travel inside the exe separately:
`tools.zip` is QuestBuilder.Cli published as an ordinary self-contained folder. On the first build the program unpacks
it to `<workspace>\tools\builder-tools-<version>` (`ToolLauncher.For`). Our Mac builds run the tools straight from the
Cli.

**Unity** is installed through the Hub CLI (`Setup/UnitySetup.cs`):
1. The Hub installer comes from `latest.yml` on Unity's CDN and is checked against its sha512 and the "Unity
   Technologies" Authenticode signature.
2. Hub is installed silently (`/S`); Windows asks for UAC.
3. The editor is installed with
   `Unity Hub -- --headless install --version 2022.3.62f3 --changeset 96770f904ca7 --module android android-sdk-ndk-tools android-open-jdk`.

A Personal license cannot be obtained from the command line: the player signs in to Unity Hub once. The program checks
the `UnityEntitlementLicense.xml` file, and if Unity refuses to work anyway, it recognizes that in the log and shows the
instructions again. While Unity downloads, the export and the bake run in parallel.

**Windows specifics.**
- The default workspace is `<drive with the most free space>:\RepoQuest`. The path must be ASCII and at most 40
  characters: Unity, Gradle and the NDK break on Cyrillic in the user name.
- Paths in Library reach ~240 characters from the project root. So the program offers to turn on `LongPathsEnabled`
  (a system setting, through UAC).
- The program keeps the computer awake during a build.
- The builder is not signed with a paid certificate, so SmartScreen shows a warning (explained in the README).

**Updates** (`Updates/`):
- `latest.json` holds the version, RU/EN notes, and the zip's name and SHA-256 for each platform. `latest.json.sig` is
  an ECDSA P-256 signature. The public key is `builder/release-public-key.txt`, compiled into the exe. The private key
  lives in `~/.config/repoquest/release-signing-key.pem`, outside the repository. **Keep a backup: without the key, no
  update can be released.**
- The update source is compiled in at release time (`--update-source`). An `update-source.txt` file next to the exe or
  the `--update-source` option (a URL or a folder) overrides it. Public releases use
  `https://github.com/Dobutsu/repo-quest-vr/releases/latest/download`.
- Self-update: the running exe is renamed to `.old`, the new one takes its place and starts. The program then sees that
  the APK was built with the previous kit and offers to rebuild.

**Installing on the Quest** goes through adb from Unity's Android SDK. If the headset has a build with a different
signature (Android debug keys differ on every computer), the program saves `/sdcard/Android/data/io.repoxr.quest/files`
(saves, RepoXR settings, the Steam login, cosmetics), reinstalls the game and restores the data. versionCode =
major·10000 + minor·100 + patch; our dev builds use 1.

**In the headset:**
- the Steam login page warns about the risk to the account (an unofficial client);
- above the Steam button in the main menu, the port's version and author are shown: `REPO Quest <version> by Dobutsu`
  (`Application.version`: the builder's version or `dev-<commit>`).

**Measurements (M-series Mac, 2026-10-03).** The player's flow with an empty workspace (`player-build`, release 1.0.0):
export 50 s, bake 4 s, project about a minute, Unity 50 minutes (import 8.5 minutes, APK build ~41 minutes). 52 minutes
in total. Self-update 1.0.0 → 1.0.1 (`update --then-build`): signature checked, exe replaced, export reused, rebuild
38 minutes. A Windows PC is likely slower. **What to speed up:** on every build Unity recompiles ~75 shaders (mostly our
generated ones: `Semiwork/*`, `Hurtable/*`, `W/*`) even though Library is kept. The project is recreated from the export
every time, and the shader cache is not reused. That is 15–20 minutes of every rebuild.

## Our build on the Mac

```sh
scripts/build.sh [<apk name>] [-developmentBuild] [-profiling] [--force] [--only export,bake,project,unity]
```

The script refreshes the kit from the current code (`make-kit.sh`), builds the Cli and runs the pipeline with `work/` as
the workspace. Unchanged steps are skipped. Output: `work/build/<name>.apk` (default `repo-quest-vr`), logs in
`work/logs/` (`builder.log`, `unity-prepare.log`, `unity-build.log`, `assetripper.log`). Python is the same one the
player gets (`work/tools/venv` through uv), not `work/.venv`. `bake.sh` and `make-crossover-test.sh` remain for the
flatscreen test in CrossOver.

## Releasing a version

```sh
scripts/release.sh 1.0.1 --notes-ru notes-ru.txt --notes-en notes-en.txt [--update-source <url>] [--mac-test]
```

The output goes to `work/release/1.0.1/`: `RepoQuestBuilder-1.0.1-win-x64.zip` (the exe with the kit + README),
`latest.json`, `latest.json.sig`, and with `--mac-test` also a build for this Mac. Nothing is published by the script.
Public releases are uploaded to GitHub Releases (`gh release create`) with those three files.

To test an update without publishing:
1. Put the old exe in a folder with an `update-source.txt` next to it holding the path to
   `work/release/<new version>`, or serve that folder with `python3 -m http.server`.
2. Run the old exe: it should offer the update, replace itself and rebuild the APK.

## Testing

- `dotnet test builder/QuestBuilder.Tests`: VDF and finding the game (including in CrossOver bottles), plugin GUIDs
  matching the bash pipeline, manifest signing, versions, adb, kit.json, the workspace, state.json, a folder as the
  update source.
- `scripts/crossover-windows-check.sh`: the Windows build of the builder's tools under CrossOver (Windows versions of
  uv, Python and .NET) runs bake and project from the same export and kit as the Mac, and the projects are compared by
  hash. On 2026-10-03 this found a path bug in `restore_resource_names.py`; after the fix the results matched: 32,502
  files.
- Comparing against a reference when the pipeline changes: a snapshot of project file hashes (excluding Library)
  before and after.
- The player's flow without the window: `scripts/release.sh X.Y.Z --mac-test` produces a single-file
  `QuestBuilder.Cli` for the Mac with the kit and tools inside. Then `QuestBuilder.Cli player-build --workspace <folder>
  --yes`, and for self-update `QuestBuilder.Cli update --update-source work/release/<new> --workspace <folder>
  --then-build --yes`.
- The window without a display: `RepoQuestBuilder --kit work/kit --screenshot <folder>` renders the window, the build
  state and every dialog in both languages to PNG.
- A friend tests on Windows. If something fails, they click "Create report" and send the zip: only logs and a summary,
  no game files.
