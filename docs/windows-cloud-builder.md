# Windows Cloud co-op builder

## Windows package

The headset-host package is `dist/cloud-coop-1.4.0/RepoQuestBuilder.exe`. It embeds both the modified kit and a separate
self-contained tool host, so double-clicking it does not require `--kit`, a .NET installation or the source checkout.
Keep `builder-startup.json` beside it to use the package's default workspace.
Remembered valid game/workspace/language choices take priority over these defaults.

## Run the APK build yourself

1. Run `RepoQuestBuilder.exe` from that folder.
2. Check the game folder and Meta Quest selection, then click **Build APK**.
3. Accept the required downloads/installations. The builder installs Unity **6000.3.17f1** and Android SDK/NDK/JDK.
4. When prompted, sign in to Unity Hub and activate a suitable Unity license, then continue in the builder.
5. The APK is written under `C:/RepoQuest/build/` by default. Keep the working folder for incremental builds.

The long APK build is not part of packaging or preflight. Neither AssetRipper export nor Unity is started by the
checks below. Installing a new APK may require backing up saves if its Android signing key differs from the old APK.

For any PC participant, copy the included `RepoQuest.CloudHost.dll` to the BepInEx/plugins folder of the working
LocalMultiplayer profile. On Quest, open **Network play**. Cloud is the default; older Steam settings
migrate to Cloud once. Connect accepts the host's RQ1 code. Create game hosts on the headset without a password.
The created-room window offers Copy code and Close. If a website is configured, Open website lets you post the code manually.
Invite reopens this window in the waiting lobby. All players must connect before the host starts the game.
PC guests use F8 > Paste code > Join room while the lobby is waiting.
Change selects Cloud or Steam. Cloud settings request Realtime ID, Voice ID, nickname and an optional website before saving.
Mode/settings changes require restart; until then the window shows the active and selected modes.
Set your Photon Realtime and Voice App IDs in Cloud settings and in the PC plugin's config before connecting.

## Repackage after changing standalone code

Use a **Windows builder with matching Unity and RepoXR versions** as the resource source, and a .NET SDK 10 compiler. The script uses
`work/dotnet/dotnet.exe` when available, or `dotnet` from PATH (override with `-DotNet`).

```powershell
.\scripts\package-windows.ps1 `
  -BaseBuilder 'C:\Downloads\RepoQuestBuilder-1.4.0-win-x64\RepoQuestBuilder.exe' `
  -GameRoot 'C:\Games\REPO' `
  -Version 1.4.0
```

The source EXE is read as a .NET bundle and its embedded kit is extracted **without executing the source builder**.
Unity version, changeset and RepoXR version must match this checkout. Use a game version recognized by both kits. The source kit provides the
already-built Android RepoXR AssetBundle/Addressables and arm64 Opus library, avoiding the Mac-only asset/Opus scripts.

Rebuild patched RepoXR first, including the current shared QuestStrings catalog. The script defaults to
`work/repoxr-src/bin/Release/netstandard2.1/RepoXR.dll`; override it with `-RepoXRDll <path>`.
Preflight rejects older catalogs without the Network play strings or kits missing modal/translation patches.
The script recompiles SteamShim, RepoXR.Standalone and the PC
plugin from this checkout, overlays current Python/Unity tooling, includes SHA-256-verified dependency archives, and
publishes new Windows tools and the single-file GUI.
Changes to RepoXR assets or Unity versions require rebuilding the corresponding Android assets instead of reusing
the old kit.

Packaging ends by running the **actual output EXE** with `--check --bake --prepare-tools`. It verifies:

- the embedded kit, native arm64 Opus, UnityFS assets and localization catalog/bundles;
- the expected Cloud adapter and backend types in the compiled assemblies;
- the game's tested fingerprint and matching builder/kit versions;
- exact EXE and PC plugin product versions matching the supplied release version;
- managed Python/packages in the workspace and the separately unpacked self-contained tool host;
- a complete Harmony bake via that packaged tool host, without exporting game assets or running Unity.

`preflight.json` in the output folder is regenerated on each packaging run. `readyToStart: true` means the builder is
ready to start its workflow; it does not mean Unity is installed/licensed or that an APK has already been produced.
Unity readiness and license status appear separately, with instructions for the user's first build.

## Repeat checks without an APK build

```powershell
.\dist\cloud-coop-1.4.0\RepoQuestBuilder.exe --check --bake --prepare-tools --report C:\RepoQuest\preflight.json
```

For automated callers, wait for the process exit code: `0` indicates successful preflight. Detailed tool output goes
to `C:/RepoQuest/logs/preflight.log`. The check does not accept installation dialogs, change Defender exclusions,
activate Unity licenses or invoke the long export/Unity build steps.

The kit and dependency caches contain port/mod resources and public third-party components, not game assemblies.
Game-derived baked DLLs remain in the player's workspace. The original installed game is not modified.
