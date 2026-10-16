# What the kit carries and where its source is

The kit is everything the REPO Quest Builder adds to the player's own copy of R.E.P.O. to make the Quest build. It
contains no part of the game.

| Part | What it is | License | Source |
|---|---|---|---|
| scripts/, unity-tools/ | The port's pipeline scripts and Unity tooling | GPL-3.0 | this repository |
| prebuilt/repoxr/RepoXR.dll | RepoXR 1.2.3 with the port's patches (patches/repoxr) | GPL-3.0 (licenses/GPL-3.0.txt) | https://github.com/DaXcess/RepoXR + patches/repoxr in this repository |
| prebuilt/standalone/RepoXR.Standalone.dll | Starts RepoXR without BepInEx; Android overrides; Steam login UI | GPL-3.0 | tools/RepoXR.Standalone in this repository |
| prebuilt/steam/Facepunch.Steamworks.Win64.dll | SteamShim: the Steam API the game uses, backed by SteamKit2 | GPL-3.0 | tools/SteamShim in this repository |
| prebuilt/steam/SteamKit2.dll | SteamKit2 2.5.0 | LGPL-2.1 (licenses/LGPL-2.1.txt) | https://github.com/SteamRE/SteamKit |
| prebuilt/steam/protobuf-net*.dll | protobuf-net | Apache-2.0 (licenses/Apache-2.0.txt) | https://github.com/protobuf-net/protobuf-net |
| prebuilt/steam/System.Collections.Immutable.dll | .NET library | MIT (licenses/MIT-dotnet.txt) | https://github.com/dotnet/runtime |
| prebuilt/android/libopus_egpv.so | libopus 1.5.2 for Android arm64 | BSD-3-Clause (licenses/opus-BSD-3-Clause.txt) | https://opus-codec.org (scripts/build-opus-android.sh) |
| prebuilt/repoxr-android/ | RepoXR's asset bundle and Addressables built for Android | GPL-3.0 | https://github.com/DaXcess/RepoXR-Unity + unity-tools/RepoXRAssets, scripts/build-repoxr-assets.sh |

Downloaded by the builder at build time from their publishers (pinned by SHA-256 in kit.json): AssetRipper, uv and
Python with the packages in python/requirements.lock, the RepoXR 1.2.3 release (its RuntimeDeps), BepInEx 5, the NuGet
packages BepInEx.BaseLib and CustomDiscoverStateLib, Unity's built-in shader source; for a Pico build, Eclipse
Temurin JRE 17 and ovrport's command-line jar (https://github.com/ovrport/app, GPL-3.0), which fetches its own OpenXR
loader libraries. Unity itself comes through Unity
Hub under Unity's terms.
