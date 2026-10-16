# Signing the builder (SignPath Foundation)

_Planned 2026-10-10._ `RepoQuestBuilder.exe` is unsigned, so Windows SmartScreen stops players with "Windows protected
your PC" and they have to click "More info → Run anyway". SignPath Foundation signs open-source software for free with
its own certificate. Its conditions (https://signpath.org/terms) shape what comes first.

## What SignPath asks for, and where we stand

| Condition | State |
|---|---|
| OSI license for all components, no proprietary components | GPL-3.0. Unity is downloaded by the player from Unity, not shipped |
| Only binaries built from our own source, verifiably, in CI | **Not yet.** The exe embeds the kit, and the kit's DLLs compile against the game's assemblies, which cannot be on GitHub. Plan below |
| Uninstallation for anything the software installs | Done: "Uninstall…" in the window (headset, workspace, Defender exclusion; Unity and Unity Hub through Windows Settings, since their own installers put them there) |
| System changes announced with warnings | Long paths and the Defender exclusion are asked for, with administrator prompts |
| No hacking tools, no circumvention of security measures | The builder reads the player's own game files; nothing scans for or exploits vulnerabilities |
| Privacy policy if data is sent anywhere | The builder sends nothing. See the statement below |
| MFA for everyone with repository or SignPath access | The author enables it on GitHub |
| "Code signing policy" on the home page and the release pages | Text below, added once SignPath accepts the project |
| A verifiable reputation for executables | **The risk.** The repository went public on 2026-10-05 and has no stars yet. SignPath may say "come back later" |

Acceptance is at SignPath's discretion. If they decline, the alternative is a paid certificate (an individual
open-source certificate, for example from Certum). With either, SmartScreen's warning fades as downloads build
reputation; it does not disappear on day one.

## After acceptance: build the exe in GitHub Actions

The builder itself (App, Core, Cli and the tools) does not reference the game. Only the kit does: RepoXR and
RepoXR.Standalone compile against the game's assemblies, RepoXR's Android assets are built with Unity, and libopus with
the NDK. So the kit leaves the exe:

1. The kit becomes its own release file, `kit-<version>.zip`, still built on the Mac by `scripts/make-kit.sh`.
2. Its SHA-256 goes into a committed file (for example `builder/release/kit-pin.json`) and is compiled into the exe.
3. `Kit.cs` downloads the kit from the release and checks the hash, as every other download does, instead of unpacking
   an embedded resource. `kit.zip` leaves the `EmbeddedResource` items of `QuestBuilder.App` and `QuestBuilder.Cli`.
4. `.github/workflows/release.yml` (windows-latest) publishes `QuestBuilder.Cli` as `tools.zip` and
   `QuestBuilder.App` as the exe, the same commands as `scripts/release.sh`, and submits the exe to SignPath. Every
   signing request is approved by hand.
5. `scripts/release.sh` splits in two. Before CI: kit → draft release → commit the pin → tag. After CI: download the
   signed exe → zip with the README → `latest.json` with the kit entry → ECDSA signature with the local key, which never
   leaves the Mac → `gh release`.

## Code signing policy (for the README and the release pages, once accepted)

> **Code signing policy.** Free code signing provided by [SignPath.io](https://signpath.io), certificate by
> [SignPath Foundation](https://signpath.org).
>
> - Committers and reviewers: [Dobutsu](https://github.com/Dobutsu)
> - Approvers: [Dobutsu](https://github.com/Dobutsu)
>
> **Privacy.** The builder does not send any information to any networked system. It downloads Unity Hub and the Unity
> Editor through Unity's own installers, and the tools the build needs from their publishers (GitHub, the Python package
> index, NuGet feeds, Thunderstore), each pinned by SHA-256. Unity fetches its packages from Unity's registry during the
> build. The builder checks this repository's GitHub Releases for its own updates. Logging in to Steam happens later,
> in the game on the headset, directly with Steam; the builder never sees it.
