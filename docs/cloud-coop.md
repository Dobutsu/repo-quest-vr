# Network play: Cloud and Steam

See [Windows packaging](windows-cloud-builder.md) for the builder and preflight commands.
See [Quest screens](quest-ui.md) for the interface and acceptance checks.

## Select a mode

Open **Network play / Игра по сети** in the headset's main menu.
The window shows the active mode and the actions **Connect**, **Create game**, and **Change**.

Cloud is the default on new installs and requires your Photon Realtime and Voice App IDs before connecting.
The migration removes previously bundled App IDs and preserves explicitly configured IDs, nickname, website and
Steam session files. Very old settings without a saved mode are migrated to Cloud once.
After migration, the game preserves your manual mode selection across launches.

To change the mode, select **Change**, then **Cloud** or **Steam**.
Steam selection does not request Cloud fields.
Cloud selection opens four steps: Realtime App ID, Voice App ID, nickname and an optional room-code website.
Review the values, then select **Save**. Cancel leaves the previous settings intact.
Back preserves the draft and returns to the previous step.

Restart the game after a settings change. Until restart, the window shows both the active and selected modes.
Connect and Create game remain unavailable until the saved settings take effect.
Reverting the selection to the active settings removes the restart requirement.

## Connect through Cloud

All participants need internet access, matching game/PUN versions and matching Realtime and Voice App IDs.
Create Photon Realtime and Voice applications and use their App IDs on each device.

1. Select **Network play > Connect** while the active mode is Cloud.
2. Enter the full `RQ1.` room code from the host.
3. Alternatively, select **Open website**, copy a code, then return to the game and select **Paste**.
4. Select **Connect**. Pasting alone does not submit the code.

Open website appears when a room-code website address is configured in Cloud settings.
The game does not upload codes or add them to the website URL.

Invalid codes remain editable. Different App IDs open the Cloud settings path.
Connection attempts expire after 45 seconds. Full and closed rooms use the corresponding Photon error codes.
An expired invitation cannot create a replacement room.

## Change your headset Cloud ID

Open **Network play > Cloud settings** and continue to the nickname step.
The page shows your Cloud ID. Select **Generate new ID** to choose a replacement.
You can generate another ID or go back between steps without saving.
Cancel discards the draft.

Review the selected ID and select **Save**. If you changed the ID, confirm **Save new ID**.
With a new ID, you can appear as a different player and lose access to progress linked to your old ID.
The game keeps your saves. It does not move progress between IDs.
Restart the game to apply the change. Until restart, you cannot connect or create a room.

If a room reports that your Cloud ID is already in use, check for another connection from your own player first.
If two different players share an ID, generate a new ID for one player.
Each headset creates its own ID on its first Cloud launch. Do not copy identity files between players.

## Create a Cloud game on a headset

1. Select **Network play > Create game** while the active mode is Cloud.
2. Read the host information, then select **Continue**.
3. Choose the region and save through the game's normal screens.
4. Wait for the room. Cloud creation uses an empty password without requesting one.

The **Game created** window appears after the Photon room and lobby metadata agree.
It shows the full RQ1 code and these actions:

- **Copy code** copies the invitation and shows **Code copied**.
- **Open website** appears if a website is configured. Post the code there so other players can connect.
- **Close** returns to the game.

Only the host can share a code while waiting in the pre-game lobby. The lobby's Invite action reopens this window.
Starting the game closes the invitation and removes the headset's local `quest-room.txt` file.
There is no room-code action in the in-game pause menu; players must connect before the host starts.
The code is also written to `quest-room.txt` in the headset's application data.
The host UI/file clears when the room ends. The next launch also removes stale local invitation files.

The host headset runs gameplay authority and saves session progress.
Guests suppress MetaManager saves until disconnect teardown returns to the main menu.
When the original host leaves, guests return to the menu. Automatic host migration is not supported.

## PC participants

Keep the working LocalMultiplayer installation. Build the additional plugin with:

```powershell
.\scripts\build-cloud-host.ps1 -GameRoot 'D:\SteamLibrary\steamapps\common\REPO'
```

Copy `RepoQuest.CloudHost.dll` into that profile's `BepInEx/plugins` folder beside LocalMultiplayer.
The Windows distribution includes this plugin. It does not require RepoXR for flatscreen play.

- To join a headset or PC host, return to the main menu, press **F8**, paste the RQ1 code, then select **Join room**.
- To host on PC, create a lobby without a password. F8 shows its code after the room is ready.
- The PC host also writes its code to `BepInEx/config/quest-room.txt`.

On PC, set `Photon / AppIdRealtime` and `Photon / AppIdVoice` in
`BepInEx/config/io.repoquest.cloudhost.cfg`, then restart. They must match the headset settings.
The plugin bypasses native Steam lobby calls only during a code-based guest session.
Photon owns the authoritative room membership; the local lobby adapter supplies compatibility metadata.

## Connect or create through Steam

Select **Network play > Change > Steam**, confirm, then restart.
The main window shows the Steam account or a sign-in message.
Connect opens friends with available games. Create game uses the game's normal hosting path.
If you are not signed in, the selected action first opens Steam login and resumes after successful login.
Cancelling login returns to the menu. Steam Guard retains its approval and code-entry branches.

Switching modes does not erase the saved Steam session or Cloud fields.
The headset uses the existing unofficial SteamKit client for Steam mode; the login screen retains its account-risk notice.
Steam hosting on the headset still needs device verification. The implemented code-based host path is Cloud.

## Configuration and ADB

Files live under `Application.persistentDataPath`, normally `/sdcard/Android/data/io.repoxr.quest/files`:

| File or field | Purpose |
|---|---|
| `cloud-coop.json` | Saved mode, Photon IDs, nickname, selected Cloud ID and website |
| `cloudPlayerId` | Optional selected identity in `cloud-coop.json`; after restart, overrides both profile IDs |
| `modeSchemaVersion: 2` | Current Cloud configuration schema |
| `enabled: true/false` | Cloud / Steam selection for the next launch |
| `cloud-profile.json` | Persistent headset identity (`userId` and `gameId`); do not copy it between headsets |
| `cloud-join` | One-shot RQ1 invitation consumed from the main menu in active Cloud mode |
| `quest-room.txt` | Current headset-host invitation |
| `*.bak` | Previous configuration/profile backup where applicable |

Old settings without `cloudPlayerId` retain the existing profile identity. Saving a new ID updates settings first.
On the next Cloud launch, the game applies that selection to both profile IDs before it enables networking.
A failed profile write blocks Cloud; the next launch retries the saved selection. Previous files have `.bak` backups.
To restore an older identity, restore matching settings and profile backups while the game is stopped.

When editing JSON manually, stop the game first and include `"modeSchemaVersion": 2` to preserve the configured mode and IDs.
An old file without that marker migrates to Cloud on the next launch.

```powershell
adb -s HOST_SERIAL pull /sdcard/Android/data/io.repoxr.quest/files/quest-room.txt .\room.txt
adb -s GUEST_SERIAL push .\room.txt /sdcard/Android/data/io.repoxr.quest/files/cloud-join
adb -s GUEST_SERIAL logcat -s Unity
```

Use the actual package name if it differs. Keep original APKs and save backups before changing signing keys.

## Build and validation

Rebuild SteamShim and the patched RepoXR, then RepoXR.Standalone, the kit and the Windows builder.
RepoXR needs .NET SDK 10 for its C# 14 sources. The builder projects target .NET 8.
Packaging bakes standalone patches with RepoXR patches in one pass and preserves reflection targets for IL2CPP.
Missing patch targets fail the bake. Keep `bake-report.json` when adapting to another game build.

```powershell
dotnet test tools/CloudMultiplayer.Tests -c Release
dotnet test builder/QuestBuilder.Tests -c Release
```

If using the local .NET 10 installation without .NET 8, set `DOTNET_ROLL_FORWARD=Major` for these test runs.
Automated checks cover settings migration, draft isolation, validation, localization, modal geometry and RQ1 routing.
Packaging/preflight does not compile a new APK or prove headset rendering, clipboard access or gameplay compatibility.

Device acceptance requires a headset host, headset guest and PC completing lobby, level, shop, next level and save/reload.
Check voice in both directions, objects, VR poses, controller navigation, scrolling, cancellation and repeat joins.
Also repeat PC hosting, Steam login and mode changes. Measure frame rate on the target host headset.
Existing unmatched VR transpiler searches remain a device-verification item in the bake report.
