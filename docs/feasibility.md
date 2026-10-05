# REPO + RepoXR on a Meta Quest 3 without a PC: how realistic is it

_As of 2026-09-25. Game: R.E.P.O. v0.4.4.3 (Unity 2022.3.67f2). Mod: RepoXR v1.2.3._

## In short

The port is realistic, and the mod does not need to be rewritten. RepoXR can be taken whole: its Harmony patches are
"baked" into the game's files ahead of time. This already works: 74 of 75 patch classes apply (the 75th fixes HarmonyX
itself and is not needed in the newer version), as do all 7 network RPC prefixes. 221 methods are baked into
`Assembly-CSharp`, and the baked game starts without BepInEx.

Playing with friends on the regular PC version is possible too. REPO's Photon server does not let anonymous players
in, but it accepts a Steam ticket, which the headset gets by itself, logging in to the player's account with a QR code
and no Steam client. This is already tested: the probe found a friend's lobby and joined their room with a vanilla host.

The main thing not yet tested is how the game will look and run on Quest 3 hardware (shaders, performance). That is
test 3, and it needs disk space and Unity.

## Why the existing APK (R.E.P.O.E.V.R) does not fit

- It is closed source. It is a rebuilt copy of the game, not RepoXR, and they share no code.
- Its VR is phone-style, like Google Cardboard, without Quest controllers.
- It is stuck on an old game version. To play with it, friends have to roll REPO back with
  `download_depot 3241660 3241661 4154963422191387469` and install the EVRMod mod. That does not work for playing
  with vanilla friends.

## What stops RepoXR from running on a Quest, and the way around it

| # | Obstacle | Solution | Status |
|---|---|---|---|
| 1 | The Quest is ARM64 Android. Unity's Mono does not work on ARM64 Android, so IL2CPP is the only option. And IL2CPP has no Harmony: code cannot be changed while the game runs | RepoXR's patches are "baked" into the game's DLLs ahead of time by `tools/HarmonyBaker`. HarmonyX itself finds and applies the patches, as in the game, and we take the resulting IL and write it to the files | ✅ works (test 2) |
| 2 | The game's shaders and textures are built for Windows/DirectX 11 | Export with AssetRipper to a Unity 2022.3.67f2 project with the original DLLs, and rebuild for Android/Vulkan | ⏳ test 3 |
| 3 | The Quest has no Steam, and without a Steam ticket Photon does not let you into the game | Direct Steam login through SteamKit2 (QR code from the Steam mobile app): friends' lobbies, an auth ticket | ✅ joining a vanilla friend's room works (test 1) |
| 4 | Native libraries exist for Windows only (`steam_api64`, `discord_partner_sdk`, `opus_egpv`, `webrtc-audio`, `AudioIn`) | On Android: voice through Photon Voice (arm64 builds exist); Steam and Discord replaced or stubbed | 📋 after the tests |
| 5 | RepoXR starts OpenXR "the PC way" and calls the Win32 API | The APK includes Unity OpenXR with Meta Quest support; Win32 calls are disabled | 📋 after the tests |

## Test 1: Steam and Photon without a Steam client

How REPO connects to a game (from `SteamManager` and `NetworkConnect`):
- the host creates a Steam lobby and puts `RoomName`, `Region`, `BuildName` and `HasPassword` in it;
- the client connects to Photon with `AuthType = Steam` and a `SteamUser.GetAuthSessionTicket()` ticket;
- the Photon app version is `BuildName + "_" + PUN version`, currently `v0.4.4.3_2.52`.

What was tested (`tools/SteamPhotonProbe`, which runs on the Mac with the game's own Photon DLLs):

| Step | Result |
|---|---|
| The game's Photon DLLs work outside Unity (through the `tools/UnityEngineStub` stub) | ✅ they reach `ns.photonengine.io:27000` |
| Anonymous login to REPO's Photon app | ❌ `Authentication type 'None' not supported`: a Steam ticket is required |
| Steam login by QR through SteamKit2 | ✅ `logon result: OK` |
| A ticket for app 3241660 and Photon login with `AuthType = Steam` | ✅ a 234-byte ticket, `OnConnectedToMaster` in region `eu`: Photon accepts a ticket obtained without a Steam client |
| Finding a friend's lobby through the Steam friends list (`game_lobby_id` in the friend's status) | ✅ lobby found; `BuildName=v0.4.4.3`, `Region=eu`, `RoomName` read |
| Joining the Photon room of a vanilla host on PC, and leaving | ✅ `OnJoinedRoom`, `players=2/6`: `#1 <friend> (host)`, `#2 Dobutsu` |

**Result of test 1:** a device with no Steam client and no game installed finds a friend's lobby, passes Steam
authentication in Photon and gets into their room. Vanilla friends do not need to install anything.

## Test 2: baking RepoXR's patches

`scripts/bake.sh all` (for the Quest: universal patches + VR) and `scripts/bake.sh universal` (for the flatscreen
test):

| | universal | all |
|---|---|---|
| Patch classes applied | 12 of 12 | 74 of 74* |
| Network RPC prefixes | 7 | 7 |
| Methods baked into `Assembly-CSharp` | 27 | 221 |
| Other modified assemblies | RepoXR (7) | RepoXR (7), Unity.InputSystem (2), UnityEngine.UI (1) |
| Native engine method (`Camera.set_targetTexture`) | none | wrapper + 13 redirected calls |
| Types outside netstandard 2.1 | 0 | 0 |

\* Only `LeaveMyLeaveAlonePatch` is skipped: it patches HarmonyX itself, and HarmonyX 2.16 already has that fix.

Problems solved along the way:
- **Transpilers read engine state.** 6 transpilers look at `Debug.isDebugBuild` to choose instruction indices. There
  is no engine at bake time, so the baker loads a copy of `UnityEngine.CoreModule` with that value filled in ahead of
  time. It comes from how `Assembly-CSharp` was built (release).
- **A patch on a native method.** `Camera.set_targetTexture` is implemented in the engine's C++ and has no IL. The
  baker generates a wrapper (prefix → original → postfix) and redirects every call from the game's C# assemblies to
  it. On PC, Harmony intercepts exactly those calls.
- **A bug in MonoMod.** Fault blocks in iterators are closed with `leave` instead of `endfinally`. Mono forgives this;
  IL2CPP may not. The baker fixes it.
- **Checks that only make sense under BepInEx.** RepoXR requires the FixPluginTypesSerialization mod and turns itself
  off without it. But baked patches always work, so in the standalone build that check is replaced with `true`, and
  the mod's assemblies are registered with the player directly.

IL verification (`scripts/verify-bake.sh`, ILVerify: original vs baked): **no new errors**. Two errors come from
RepoXR's own transpilers, which have fallen behind the game: in `ValuableArcticSnowBike.UpdateSteering`,
`PlayerLocalCamera.GetOverrideTransform()` is called on a `Transform`; in `CosmeticTokenUIElement.OnDestroy`, the
non-generic `Object.Instantiate` returns `Object` instead of `GameObject`. Mono does not check types, so on PC this
happens to work, but the C++ from IL2CPP does not compile with it. The baker fixes both with a common pass
(`TypeRepairs`): an extra call on an object of another type that returns the same type is removed, and a `castclass`
is inserted for a downcast. Every fix is written to the bake report.

**Running in CrossOver** (`scripts/make-crossover-test.sh universal`: an APFS clone of the game, the Steam
installation untouched, saves compared against a backup): the game reaches the main menu without BepInEx, RepoXR
starts through `tools/RepoXR.Standalone` (a chainloader replacement), the mod's assets and Addressables load, and the
log has no exceptions.

Remaining: get the `all` set through IL2CPP compilation (test 3), and play a singleplayer round in flatscreen mode to
check the grabber and inventory by hand.

## Test 3: assets for Android

Inventory (`scripts/inventory.py`): 117 shaders, 32 of them the game's own (`Semiwork/*`, `Hurtable/*`, `W/*`, Shader
Graph and so on). 824 MB of textures in DXT1/DXT5/BC7, 5,126 light sources, 741 cameras. Addressables are used only for
localization.

The AssetRipper 2.0.0 export (`ScriptExportMode = DllExportWithoutRenaming`) takes 45 seconds and 2.3 GB: 14,726
assets, 3 scenes, the original DLLs in `Assets/Plugins`, Unity 2022.3.67f2. Scripts are not decompiled; objects
reference the original assemblies, so the baked DLLs drop into their place unchanged.

**Shaders.** Shader decompilation in AssetRipper 2.0 is a paid (Premium) feature. The free version exports stubs
(`//DummyShaderTextExporter`) that keep the name and all the properties. How to replace them:
- built-in ones (`Standard`, PostProcessing v2 — 24 of them, TextMeshPro — 5) come from the source of Unity and its
  packages of the same version;
- the game's own (32: `Semiwork/*`, `Hurtable/*`, `W/*`, `Fresnel*`, `Lava`, `Water` and others) are mostly simple
  Amplify Shader Editor shaders (textures, normals, emission, fresnel). Options: rewrite them as surface shaders with
  the same properties, decompile the DXBC with open tools (extracting it through UnityPy), or buy AssetRipper Premium.

**Unity version.** 2022.3.67f2, which the game is built with, is an Extended LTS build: it is only available with a
Unity Enterprise subscription. So the latest public 2022.3.62f3 is used. The project format is the same, and
compatibility with friends depends on the game's code, not on the engine's patch version.

**The Quest project** (`scripts/make-quest-project.sh`):
- The first import of the export for Android takes 11.5 minutes (14,771 files) and finishes without errors.
- `scripts/package_swap.py` replaces the package DLLs from the export with the real packages of the same versions
  (InputSystem 1.14.2, uGUI 1.0.0, TMP 3.0.7, Localization 1.5.9, Addressables 1.27.0, PostProcessing 3.5.0 and
  others). This is needed for the XR packages and for building Addressables for Android. References are rebound with
  Unity's formula for scripts from DLLs (MD4 of "namespace + class"): 606 classes, 5,678 references in 308 files, none
  unresolved. 29 stub shaders (PostProcessing, TMP) are replaced with the real ones.
- Auto-referencing is turned off for the game's DLLs: `Assembly-CSharp` has a global `Version` class, and it shadowed
  `System.Version` in InputSystem's code.
- The Addressables settings do not ship with the game; they are editor data. They are restored from its
  `catalog.json` (`scripts/addressables_catalog.py` + `AddressablesRestore.cs`): 28 localization entries with the same
  addresses, labels and bundle layout.
- AssetRipper exports PostProcessing's compute shaders as serialized `.asset` files with DX11 bytecode; they are
  rebound to the `.compute` files from the package (the reference changes `type: 2 → 3`).
- The build takes two editor runs (`scripts/build-quest.sh`): after the player settings change, Unity requires a
  domain reload and refuses to build before it.
- The target is Android API 34: that is the Quest 3's system (Horizon OS on Android 14), and Unity's SDK has that
  platform (API 32 would need installing with a license acceptance).

**The first APK** (vanilla game, no VR): 388–424 MB, with about 2.3 GB of data inside. A full build takes about 20
minutes, a repeat build about 15. It installs on a Quest 3 in 15 seconds and runs on the Quest as a flat window. The
road to a working game:

| Symptom on the Quest | Cause | Solution |
|---|---|---|
| `sharedassets0.assets is corrupted! [Position out of bounds!]` | The names `Assembly-CSharp(-firstpass)` are reserved by Unity: during the player build, all the game's scripts counted as "missing" (≈106,000 warnings), and the data went out without layout | The DLLs are renamed to `REPO.Game(.FirstPass)` (`scripts/rename_game_assemblies.py`, `tools/AssemblyRenamer`); references in scenes go by GUID, so they do not break; `UnityEvent` (2,559 places) and the Easy Save 3 list are rewritten |
| Closes right after the menu: `Steam not initialized. Quitting game.` | Android has no `steam_api64` | `tools/SteamShim`: our own `Facepunch.Steamworks.Win64` with the same API (12 types, ~50 members; ILVerify clean); an offline backend for now, SteamKit2 next |
| An exception every frame from `discord_partner_sdk`, `user32` | Windows-only native libraries | Android replacements for `DiscordManager.Start/UpdateDiscordRichPresence/RefreshDiscordRichPresence` and `WindowManager.Awake` (the bodies are written into the game at bake time) |
| Localization tables do not load (`No Location found for Key=d022236e…`) | Localization looks tables up by the asset GUID from the original project, and AssetRipper handed out new ones | The original GUIDs are taken from the Addressables catalog keys and given back to the assets (`scripts/restore_guids.py`, 28 assets) |
| No 3D graphics, only the UI is visible | AssetRipper's shader stubs declare the matrices as ordinary globals; under Vulkan they are zero, and no geometry is drawn (562 materials: walls, floors, characters, valuables) | `scripts/generate_shaders.py`: 35 stubs → surface shaders for lit ones and vert/frag for UI and particles, properties and GUIDs kept |
| The menu works but is invisible | The UI is drawn into a `Render Texture Overlay` in RGBA32F, which Adreno cannot blend into | The format is changed to RGBA16F (`scripts/fix_render_textures.py`); still being checked on the headset |

After that, the menu, tutorial, sound, physics and localization work on the Quest. REPO has no gamepad support even on
PC (only aim axes), so the flatscreen test needs a keyboard and mouse. In VR, RepoXR provides the controls.

Known small issues: for 6 dynamic TMP fonts (Noto Sans JP/KR/TC/Thai/Arabic, LiberationSans Fallback), AssetRipper did
not export the empty atlases. They need to be recreated, or those languages' characters will be missing.

**The VR build** (`scripts/bake.sh all android`, `scripts/make-quest-vr.sh`, `scripts/build-quest.sh repo-quest-vr`):
the same project plus XR Management 4.5.0, OpenXR 1.14.3 (Meta Quest, Touch, Touch Plus) and XR Interaction Toolkit
2.6.4, the baked game with the full patch set (221 methods, 0 errors), RepoXR with its dependencies as Android plugins,
and the mod's assets built for Android (`scripts/build-repoxr-assets.sh`) inside the APK. InputSystem and uGUI are
built from source in the project, so RepoXR's three patches on their code (`XRSupport.Initialize`,
`InputSettings.backgroundBehavior`, `Button.OnPointerClick`) are applied by editing the source of the embedded packages
(`scripts/patch_packages.py`). Skipping `XRSupport.Initialize` is safe: OpenXR 1.14 registers its own `PoseControl`
and controller layouts.

What came up in the VR build:
- `typeof(X)` in attribute arguments is stored as a string with the assembly name (`"GameDirector, Assembly-CSharp"`),
  so after the rename to `REPO.Game` the IL2CPP linker failed on RepoXR's `[HarmonyPatch(typeof(...))]` (222 such
  places). `tools/AssemblyRenamer` now parses attribute blobs and rewrites those too.
- XR Management 4.5 does not create `XRManagerSettings` along with the general settings; `QuestXRSetup` creates them
  explicitly.

**The first VR run on the headset** (build of 2026-09-25): the OpenXR session comes up, and RepoXR's menu and the
graphics are visible in the headset. What got in the way and how it was fixed:

| Symptom | Cause | Solution |
|---|---|---|
| Only the Quest loading screen, the menu sound plays | The IL2CPP linker stripped `RepoXR.Standalone`: nothing references the loader, it starts through `RuntimeInitializeOnLoadMethod` | `Assets/RepoQuest/link.xml` (the loader, RepoXR, BepInEx, XR assemblies) |
| The picture is "stuck" to the head | `Could not create a device for 'OpenXR Head Tracking'`: the port of RepoXR's patch disabled `XRSupport.Initialize` entirely, while on PC it skips only the second call, so the `XRHMD`/`XRController` layouts were never registered | The first call runs, repeated ones are skipped (`patch_packages.py`) |
| Controllers do not work | In OpenXR 1.14.3, the profiles' `FinishSetup` expects a `StickControl`, but without the `USE_STICK_CONTROL_THUMBSTICKS` symbol the stick is declared as a `Vector2Control`; RepoXR's bindings also use `primary2DAxis/up` | The symbol is set in `QuestXRSetup` |
| RepoXR's menu buttons and pages show "missing script" | RepoXR's bundle references the game's scripts in `Assembly-CSharp`, but in the APK the game is `REPO.Game` | The game stubs in RepoXR's asset project are built into a `REPO.Game` asmdef, and `UnityEvent` calls are retargeted (`build-repoxr-assets.sh`) |
| `TypeInitializationException` in `HarmonyLib.AccessTools` | The static constructor builds a fast invoker through `DynamicMethodDefinition` (Emit), which IL2CPP cannot do; RepoXR's settings page and its RPC registry break | An Android replacement of `MethodInvoker.GetHandler` with `MethodBase.Invoke` in BepInEx's 0Harmony (the baker's `--rewrite`) |

**Result (2026-09-25): REPO with RepoXR runs standalone on a Quest 3.** Head and controller tracking, RepoXR's menu
rays and buttons work, and the tutorial can be played through. Open questions: a `NullReferenceException` in
`Map.AddValuable` when valuables are discovered (the valuable does not appear on the map; the cause is not visible
statically), performance (below), multiplayer (SteamShim is still offline) and voice chat (no Android native libraries
for Photon Voice).

Performance in the tutorial (VR, multi-pass, 100% resolution): 43 FPS on average, 21 at the minimum, against a target
of 72; 18.6 ms of GPU per frame. In the main menu (VR, multi-pass, 100% resolution): 62–65 of 72 FPS, the GPU 97% busy
at CPU4/GPU4 levels. Next, without rebuilding: `SinglePassRendering` (multiview) and `CameraResolution` in RepoXR's
config, and the game's own graphics settings. Foveated rendering on the built-in pipeline through OpenXR needs the Meta
Core XR SDK and does not work with the PPv2 post-processing that REPO uses.

**Changes to RepoXR.** The `external/RepoXR` submodule stays a clean v1.2.3; our changes live in
`patches/repoxr/*.patch`. `scripts/prepare-repoxr.sh` applies them to a copy in `work/repoxr-src` and builds it, and
that RepoXR.dll is what goes into the bake. The first patch is **Head Bob**: in VR the camera hangs under the game's
`CameraBob` object (`… → Bob → Noise → … → Tilt → Camera Main`), and the walking sway (up-down, yaw and roll) moves the
whole view. RepoXR damped it to 25% with a hardcoded constant; now it is a 0–100% slider in RepoXR Settings → Gameplay
(25% by default, as before). Only the camera offset is scaled: the footsteps play in the same `CameraBob.Update`.
Without a rebuild, the game's Gameplay → Camera Animation = 0 setting does the same (and also turns off landing and
tilts). Tested on the headset: at 0%, the motion sickness from walking is gone, and footsteps are still heard.

**Stale import artifacts.** Valuables did not appear on the map, and `ImpactSmoke` failed when items hit things. A
diagnostic replacement of `Map.AddValuable` showed that the marker's `spriteRenderer` was empty; in the APK's data,
every reference of that `MapValuable` was empty, although the prefab's YAML sets them. The cause was the project's
`Library`: the first import happened while the game's assembly was still called `Assembly-CSharp.dll`, and after the
rename to `REPO.Game` the prefab files did not change, so Unity did not reimport them; the editor held `MonoBehaviour`s
with no data (after a forced reimport the references are there, per `ImportDiagnostics`). 1,346 prefabs and 1,585
`.asset` files with the game's scripts were affected. Scenes were not: they are read from the files at build time.
`build-quest.sh` now reimports everything that uses the game's scripts, once per `Library`.

**Multiplayer: Steam without a client (stage 0, 2026-09-25).** The scenario is joining a friend who hosts on PC.
SteamShim (`Facepunch.Steamworks.Win64` in the APK) got a `RepoQuest.Steam.SteamKitBackend` backend on SteamKit2 2.5
(the last version with a netstandard build that works in Unity): login with a QR login's refresh token using the
device's own `LoginID` and machine id (SteamKit 2.5 crashed on IOKit on macOS), a "playing R.E.P.O." status, joining a
friend's lobby with its data, members and events, and the friends list with their lobbies (`game_lobby_id` from
`CMsgClientPersonaState`). The auth ticket is ported from SteamKit 3.4 (`AuthTicketHandler`: a game connect token +
session header, registration through `ClientAuthList`, the ownership ticket). `tools/SteamShimProbe` drives the shim
the way the game does: login, name, ticket → Photon `OnConnectedToMaster: authenticated`, including with protobuf-net
without code generation (`AutoCompile = false`, as under IL2CPP). Left to do with a friend: joining their lobby and
room, the invite format. In the APK: SteamKit2, protobuf-net(.Core), System.Collections.Immutable as Android plugins
(System.Memory/Buffers are Unity's shims, Registry/AccessControl are AOT-profile facades), `link.xml`. The login is
copied to the headset with `scripts/quest-steam-login.sh`, and `scripts/quest-join.sh <name>` joins a friend before
the VR menu exists.

**Stage 1 (Steam on the headset, 2026-09-26): done.** The headset logs in to Steam by itself in ~2 s and registers the
auth ticket. What had to be fixed for IL2CPP (`tools/Il2CppFixes`, editing the IL of the copies in the APK):
`Process.StartTime` in the `SteamKit2.SteamClient` constructor (→ `DateTime.Now`), and
`ParameterInfo.GetRequiredCustomModifiers` in protobuf-net's tuple detection (→ no modifiers). SteamKit2's async code
without `ConfigureAwait(false)` bound itself to Unity's main thread, so the first connection is started from the
thread pool.
**Joining a friend in VR:** the game's "Join friend" main menu button opens the Steam overlay through
`SteamFriends.OpenOverlay("friends")`. That is a call into the shim, so without patching the game, on the Quest it
shows friends sitting in R.E.P.O. lobbies in the game's popup (JOIN / NEXT); JOIN does the same as "Join Game" in
Steam.

**Steam login on the headset (2026-09-30).** A "Steam: …" button to the right of Quit game in the main menu (and Join
friend, while there is no login) opens the login right in the game. The account name and password are entered on a
page built from the game's "Rename save file" page: RepoXR's VR keyboard, the password shown as asterisks. Then Steam
Guard: confirmation in the Steam app, or a code from the app or from email. `RepoQuest.Steam.PasswordLogin` talks to
Steam's authentication service with plain unified messages (`Authentication.*#1`). SteamKit 2.5's
`SteamAuthentication` does not fit: it builds calls from compiled expression trees and `MakeGenericMethod`, which is
unreliable under IL2CPP. The password goes only to Steam, encrypted with the account's RSA key; the headset keeps a
refresh token (`steam-session.json`). After login, the game switches to the account without a restart. LOG OUT revokes
the token (`Authentication.RevokeToken`), but the SteamID and name stay: upgrades in singleplayer saves are tied to
the SteamID. On the Mac, `SteamShimProbe login` / `logout` goes the same way with its own
`work/steam-session-password.json`. Tested on the Mac: a wrong password gives a clear error. Tested on the headset on
2026-10-04: log out, a wrong password, then a correct one with Steam Guard confirmation in the app, and the game
reconnected without a restart.

**Voice.** Photon Voice in the game calls only the standard libopus API from `opus_egpv` (`opus_encoder_create`,
`opus_decode`, `opus_encoder_ctl`…; on Android arm64 the variadic `*_ctl` is called correctly through P/Invoke), so
libopus (BSD) built for arm64 with Unity's NDK as `libopus_egpv.so` is enough. Still to check: which microphone the
game's `Recorder` picks (Unity Microphone works; `AndroidAudioInAEC` needs Photon's AAR).

**Performance and lighting (2026-09-26).** The project's Android quality level is "Medium" with `pixelLightCount: 1`
(on PC the game runs at "Ultra", 12): the flashlight and the lamps shared one per-pixel slot, and a surface went dark
until the light spot reached it. RepoXR's second patch (`0002-pixel-lights-setting`) is a Rendering → Pixel Lights
slider (1–12, 3 by default), applied on the fly. Measurements in the tutorial (`VrApi FPS=`): initially 42.5 FPS at
~18 ms GPU; overclocking through Quest Games Optimizer did not help; Motion Blur/Grain off and lower shadows/lights in
the game's settings: GPU ~12 ms, 47–53 FPS (the same with Pixel Lights 3). Then the limit is the CPU (~18 ms per
frame): single pass in RepoXR gives a white left eye (the effects are not multiview), and the next step is profiling
with a development build.

**Shaders from the original.** The game's compiled shaders carry a parsed form next to the bytecode: for each pass,
Blend, ColorMask, ZWrite, ZTest, Cull and tags. `scripts/shader_states.py` extracts it (117 shaders), and
`generate_shaders.py` takes the state from there, along with whether the shader is lit, instead of guessing by name.
For example, `UI Overlay` (drawing the UI over the frame) is `Blend One OneMinusSrcAlpha`, `ColorMask RGB`, not
ordinary alpha blending. Properties that the shader reads from the materials' saved values are added to its
`Properties`.

**Formulas from the bytecode (2026-10-02).** The shaders' code itself is in the game too: DX11 bytecode for every
variant. `scripts/shader_disasm.py "<shader name>"` prints it pass by pass with material property names instead of
registers, so a replacement can be checked against the original instead of guessed. This found:
- Semiwork's own shaders (`Hurtable/*`, `Semiwork/Standard`, `Semiwork/Alpha Clip`, `RotatingAlbedoShader`) multiply
  emission by the alpha of `_EmissionColor`. The Mentalist (`Enemy Floater`) has an emission color of `(1, 1, 1, 0)`
  with no texture: on PC it has no glow, but ours glowed white all over. The game colors players while keeping the
  material's alpha (`PlayerMaterial.ColorSet`), so that alpha is also the glow strength of the colored parts;
- Hurtable's fresnel uses the mesh normal (not the normal map), with no upper clamp, and is added to the color and,
  through `_FresnelEmission`, to the emission; the damage flash (`_ColorOverlay`) covers the emission too. Two-sided
  variants have the rim on front faces only; `Hurtable Fade` has back faces with `_backfaces` transparency;
- `W/ExtrationArea` (the red frame of the extraction zone during the countdown) does not draw the faces of a
  9.6 × 15.4 × 9.4 m cube: using the camera's depth texture, it finds the scene surface behind each pixel and
  highlights its intersection with a box of half-size `_Size = 0.2` (3.8 × 3.7 m around the platform, the bottom on the
  floor), a dashed line and a fading glow outside. A depth texture on the Quest means an extra depth pass of the whole
  scene per eye, so the replacement draws the same thing on the floor with geometry (the cube's bottom face becomes a
  square at floor level). There are no lines on walls or players.

**Animations: merged constraint curves.** Clips that animate the weights of several sources of one constraint (Elsa's
head and tail follow the small or the large body, Trudge's look target) store them as attributes 15, 31… (source
index × 16 + 15). AssetRipper names them all `m_Sources.Array.data[0].weight` and writes their keys into one curve,
interleaved. In the game, the small body's weight jumped between 0 and 1 and dropped to 0 at the end of the clip, and
the large body's weight was not animated at all. When a clip froze on its last frame (Elsa being petted), the head
moved to the constraint's rest pose, under the floor. `scripts/fix_constraint_curves.py` splits the curves back apart
in the order of the clip's bindings (52 curves in 27 clips); the result was checked against the game's decoded
original clips.

**Diagnostics on the headset.** `adb screencap` on the Quest is empty, so the build includes `QuestDiagnostics`: on
`scripts/quest-diag.sh`, the game saves a screen frame, the contents of every camera's RenderTexture and a report on
cameras, canvases and visible UI elements, and the script pulls them into `work/diag/`.

**Shader gallery (2026-10-05).** The generated shaders are now checked against the PC game mechanically: the same 1,211
prefabs from `Resources` rendered under fixed lighting by the PC game (original shaders, in CrossOver) and by the port
(editor or headset), compared picture by picture. See `docs/shader-gallery.md`.

## Risks

- **Performance on the XR2 Gen 2.** REPO draws many dynamic lights with shadows and fog, and VR renders for two eyes.
  A Quest quality profile will be needed (lights, shadows, FFR, multiview), and it is not yet known whether that will
  be enough.
- **Shaders.** Some of the game's own shaders will have to be rewritten by hand if decompilation fails.
- **SteamKit2 inside IL2CPP.** It needs a netstandard 2.1 port and an AOT-compatible protobuf serializer. Logging in to
  Steam with a third-party client is not officially supported (DepotDownloader, ASF and others use it).
- **Updates.** Every REPO or RepoXR update requires a rebuild: export, bake, APK. The RepoXR-Unity project has already
  moved to Unity 6.3. If REPO moves too, the pipeline will have to be reconfigured.
- **Legal.** The finished APK contains the game's assets and must not be distributed. The build happens locally from
  the player's own copy. RepoXR is distributed under GPL-3.0.

## How to reproduce

```sh
work/.venv/bin/python scripts/inventory.py work/game --json work/inventory.json
scripts/bake.sh all && scripts/verify-bake.sh all
scripts/make-crossover-test.sh universal   # then run C:\REPO-baked\REPO.exe --disable-vr in the Steam bottle
~/.dotnet/dotnet run --project tools/SteamPhotonProbe -- login                       # QR login, once
~/.dotnet/dotnet run --project tools/SteamPhotonProbe -- photon                      # ticket → Photon master
~/.dotnet/dotnet run --project tools/SteamPhotonProbe -- photon --friend <name> --join-room
```
`work/` holds the game's files and everything derived from them, so it is in `.gitignore`.

Since 2026-10-03, the Quest project and the APK are built by `scripts/build.sh`: the same pipeline as the players'
builder (`docs/builder.md`). The scripts `make-quest-project.sh`, `make-quest-vr.sh`, `apply-quest-runtime.sh` and
`build-quest.sh` mentioned above were moved into it without changing the output, and deleted; they remain in git
history.
