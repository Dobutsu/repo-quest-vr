# R.E.P.O. VR on Quest 3, standalone

Play [R.E.P.O.](https://store.steampowered.com/app/3241660/REPO/) in VR on a Meta Quest 3 without a PC, and join
your friends' games on PC. Their game stays vanilla, with no mods.

The game is built **from your own copy of R.E.P.O.** on Steam. This repository and its releases contain no game files.

Support me if you want: [ko-fi.com/dobutsu](https://ko-fi.com/dobutsu)

## Built on RepoXR by DaXcess

This port is built on **[RepoXR](https://github.com/DaXcess/RepoXR)** by **[DaXcess](https://github.com/DaXcess)**,
who also made [LCVR](https://github.com/DaXcess/LCVR), the VR mod for Lethal Company. RepoXR provides all of the VR:

- head and hand tracking and motion controls;
- VR menus, HUD and keyboard;
- the physical grabbing that R.E.P.O. is built around;
- support for VR players and flatscreen players in the same lobby.

This project does not add VR. It takes RepoXR as it is and makes it run on the headset itself:

- bakes it into the game;
- builds the game for Android;
- adds what a PC normally provides, a Steam client and voice chat.

If you enjoy R.E.P.O. in VR, star [RepoXR](https://github.com/DaXcess/RepoXR), get it on
[Thunderstore](https://thunderstore.io/c/repo/p/DaXcess/RepoXR) for PC VR, and say thanks on the
[RepoXR Discord](https://discord.gg/2DxNgpPZUF).

**Please do not report problems with this port to RepoXR or DaXcess.** Open an issue here instead. This is an
unofficial project that DaXcess does not maintain or endorse. The port's own changes to RepoXR, such as the head bob
slider and lighting and performance options, live in [`patches/repoxr`](patches/repoxr). They are free for RepoXR to take
if any of them are useful.

## What works

- **VR on the headset.** RepoXR's full feature set runs at the Quest 3's 72 Hz, using single-pass (multiview)
  rendering and lighting tuned for the mobile GPU. Most levels hold 55–72 FPS. Shops and crowded lobbies dip lower.
- **Singleplayer**, including the tutorial and saves.
- **Joining friends on PC.** You can join from the friends list or accept an invite inside the headset. Voice chat works
  both ways. Tested with up to 7 players on vanilla PC copies of the game.

Hosting a game from the headset has not been tested yet. The port is made and tested on a Quest 3. The Quest 3S
should behave the same. Quest 2 and Quest Pro are untested.

## How to get it

Download **RepoQuestBuilder** for Windows from [Releases](../../releases/latest) and follow its `README.txt`. In short:

1. You need Windows 10/11 (64-bit), 16 GB RAM, about 60 GB of free disk space, R.E.P.O. on Steam, a Quest 3 in
   developer mode with a USB cable, and a free Unity account.
2. The builder finds your game and downloads everything else itself: Unity 2022.3, AssetRipper and Python packages,
   each from its original source with a pinned SHA-256. It then builds the APK on your PC.
3. The first build takes 1–1.5 hours. Later updates take about 40 minutes.
4. It installs the APK on the headset. The game appears in the Library under *Unknown Sources*.

**Do not share the APK.** It contains the game's files. Everyone builds their own from their own copy.

### Steam in the headset: read this

To play online, you log in to Steam inside the game, from the **Steam** button in the main menu. The headset then
talks to Steam directly through [SteamKit2](https://github.com/SteamRE/SteamKit), an unofficial Steam client. Steam's
Subscriber Agreement does not allow third-party clients. The risk to your account is small, but it is real, so decide
for yourself. The login goes from the headset straight to Steam, and the builder never sees your password.
Singleplayer works without logging in.

## How it works

```
your R.E.P.O. (Steam)
  └─ AssetRipper ──────────► Unity project (the game's scenes, prefabs, scripts as DLLs)
  └─ HarmonyBaker ─────────► RepoXR's Harmony patches baked into the game's assemblies (no runtime patching on IL2CPP)
  └─ scripts/ (Python) ────► fixes for the export: shaders rewritten for mobile and multiview, Addressables, halos
  └─ Unity 2022.3 (IL2CPP, OpenXR, Vulkan) ─► Android APK ─► Quest
```

- `builder/` is the Windows builder (Avalonia app plus a CLI) and runs the whole pipeline. See `docs/builder.md`.
- `tools/RepoXR.Standalone` starts RepoXR without BepInEx and adds the in-headset Steam login and invites.
- `tools/SteamShim` replaces Facepunch.Steamworks with a SteamKit2 backend: lobbies, auth tickets for Photon, friends.
- `tools/HarmonyBaker`, `tools/AssemblyRenamer` and `tools/Il2CppFixes` make the Mono mod work under IL2CPP.
- `patches/repoxr` holds the changes to RepoXR. `unity-tools/` holds the Unity editor and runtime helpers.
- `docs/` contains the design notes: `feasibility.md` (how the port came together), `builder.md` (the builder and
  releases) and `public-release.md` (the legal side).

Development happens on a Mac with the same pipeline (`scripts/build.sh`). The Windows builder is the supported way for
players.

## Legal

This is an unofficial fan project. It is not affiliated with or endorsed by semiwork (R.E.P.O.), DaXcess (RepoXR),
Valve or Meta. R.E.P.O. and its assets belong to semiwork. The repository contains only the port's own code. Every
player builds the game from a copy they own.

Licensed under the **GNU GPL v3.0** (see [LICENSE](LICENSE)), the same license as RepoXR, which the port includes and
modifies. Third-party components and their licenses are listed in [`builder/SOURCES.md`](builder/SOURCES.md) and
[`builder/release/THIRD-PARTY-NOTICES.txt`](builder/release/THIRD-PARTY-NOTICES.txt).

## Credits

- **[DaXcess](https://github.com/DaXcess)**: [RepoXR](https://github.com/DaXcess/RepoXR) and
  [RepoXR-Unity](https://github.com/DaXcess/RepoXR-Unity), the VR mod this port is built on.
- **semiwork**: [R.E.P.O.](https://store.steampowered.com/app/3241660/REPO/)
- [SteamKit2](https://github.com/SteamRE/SteamKit), [AssetRipper](https://github.com/AssetRipper/AssetRipper),
  [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX),
  [Avalonia](https://github.com/AvaloniaUI/Avalonia) and [Opus](https://opus-codec.org/).
- Port by **[Dobutsu](https://github.com/Dobutsu)**.
