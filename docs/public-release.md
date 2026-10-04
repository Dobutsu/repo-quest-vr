# A public project: everyone builds the Quest version from their own copy of the game

Notes from 2026-09-26. The question: can the project go on GitHub so that other players build the Quest version from
the R.E.P.O. files they bought on Steam and play, without infringing on the developers' rights? And can that be made
really simple (put the game files in a folder, install the APK on the headset)?

The short answer: the "everyone builds from their own copy" model is feasible. "Put the files in a folder and install
the APK" is not possible. "Without infringing" cannot be guaranteed without the developers' permission. This is not
legal advice, only an understanding of the situation as of that date.

## Technical side: how simple it can get

**Fully simple, with no build step, is not possible.** The PC game's files do not work on the Quest as they are:
- **Shaders** are compiled for DirectX 11, which the Quest does not understand. They have to be recreated.
- **Textures** are compressed as BC/DXT, which the Quest's GPU does not support. They have to be recompressed as ASTC.
- **The game's code** runs on Mono on PC. The Quest needs IL2CPP, that is, compilation to native ARM64 code.

Only the Unity Editor can do all of this. So every player's computer has to run a real build.

**What is realistically achievable** is a one-click builder:
1. The program installs what is needed: Unity 2022.3 with the Android module through Unity Hub, .NET, Python,
   AssetRipper. The player accepts the Unity license themselves.
2. The player points it at the REPO folder from Steam and clicks "Build".
3. They connect the Quest in developer mode and click "Install".

Requirements: Mac or Windows, ~50 GB of disk, about an hour for the first build. Rebuild after every REPO update: the
version has to match friends on PC. Our scripts in `scripts/` are already almost such a pipeline. What is missing is a
wrapper and a test on a clean machine. Developer mode on the Quest is free, and the APK can also be installed with
SideQuest.

## Legal side

**What is good about the "own files" model:**
- The repository holds only our code, scripts and patches. We do not distribute game files, APKs or the export
  (everything in `work/`). Many fan ports work this way: the engine is built from the player's original game data.
- Only the game's owner can play online: they log in to their own Steam account, and Photon accepts a genuine Steam
  ticket. Nothing is cracked, and we do not bypass the ownership check.
- The licenses of our code allow publishing. RepoXR is GPL-3.0, so our port is GPL as well, with open source code.
  SteamKit2 is LGPL, Opus is BSD, HarmonyX is MIT.

**What stays risky:**
1. **The game's license agreement (EULA)** may forbid reverse engineering, modification and derivative works. The
   developers tolerate REPO mods (RepoXR, Thunderstore), but tolerating mods is not the same as allowing a port to
   another platform.
2. **Photon servers** are paid for by the developer, per concurrent player. Quest players are legitimate owners, but
   they connect to those servers with an unofficial client.
3. **Steam's rules:** SteamKit2 is a third-party client. It is widely used, but formally Valve's rules do not approve of
   third-party software, and the risk to the account is not zero.
4. **Jurisdiction:** whether reverse engineering for interoperability is allowed depends heavily on the country.

## The agreement, examined (2026-09-29)

**R.E.P.O. has no EULA of its own.** The Steam store has no link to a game agreement (the address
`store.steampowered.com/eula/3241660_eula_0` is empty), the store data has no legal notes, and the game files have
nothing either. Only the Steam Subscriber Agreement (SSA) applies; the version of 2026-09-10 was examined. Every buyer
accepts it, and semiwork appears in it as Valve's licensor.

**What semiwork has said publicly** (none of it is written permission):
- April 2025, a Q&A on YouTube: "We encourage mods", along with a request to mod authors not to inflate server traffic;
- v0.4.0 (May 2026): a [MODDED] marker appears in the main menu when mods are installed, so mods are treated as normal;
- March 2026: console versions are "planned" and will be taken on closer to 1.0. An unofficial port to another
  platform touches those plans more than an ordinary mod does.

**SSA clauses that concern the port:**

| Clause | What it says | Our situation |
|---|---|---|
| 2.A | The license is for personal, non-commercial use only | Complied with |
| 2.G, para. 1 | Without **Valve's** written consent you may not copy, translate, reverse engineer, derive source code, modify, disassemble, decompile or create derivative works, "except as permitted by applicable law" | The whole pipeline falls under this: AssetRipper, decompilation, IL2CPP, replacing shaders. Formally, every mod on Thunderstore breaks it too, RepoXR included |
| 2.G(ii) | You may not emulate Valve's protocols for network features "through protocol emulation… adding components… for any purpose, including network play" | SteamShim on SteamKit2 falls under this almost word for word |
| 4.B | You may not interfere with Steam or content without Valve's permission, or control Steam processes with third-party software | SteamShim again. No cheats; the game is co-op |
| 1.C | You may not let others use your account | Everyone logs in with their own account on their own headset. The builder must never send passwords anywhere |
| 4.D | Valve may restrict or close an account without notice | The whole account with its whole library is at risk, not only R.E.P.O. |
| 10 | The law of the country of residence applies in the EU and the UK; elsewhere, the law of Washington State | — |

**Conclusions:**
1. **semiwork's permission is not enough.** Under the SSA, consent comes from Valve. As the rights holder, semiwork
   decides the repository's fate: they can file a DMCA notice with GitHub. Using SteamKit2 is a matter between each
   player and Valve, and semiwork's permission does not settle it.
2. **The risks differ by party.** The SSA does not forbid the author from publishing their own tools, so the main risk
   for the repository is a copyright claim from semiwork. Players risk their accounts: that is a contractual risk. In
   practice Valve leaves co-op mods alone, and open projects have used SteamKit2 for years (ArchiSteamFarm,
   DepotDownloader), but there are no guarantees.
3. **The "applicable law" exception most likely does not cover the port.** In the EU, a contract cannot forbid
   decompilation for interoperability with an independently created program (Directive 2009/24/EC, Articles 6 and 8).
   But the port adapts the game itself; it is not interoperability with another program. In Russia, Article 1280 of the
   Civil Code allows modifying a program "to make it work on the user's hardware", but only "unless the contract
   provides otherwise", and the SSA does.
4. **No protection is circumvented.** The game has no DRM (Steam's `drm_notice` is empty), and Steam login uses a real
   account. This sets the project apart from cracks and keeps it outside anti-circumvention law (DMCA §1201 and its
   equivalents).
5. **The repository contains no game code.** Checked on 2026-09-29: both tracked files and new untracked ones
   (ShaderOverrides, lamps) match the decompiled game only in trivial lines such as `if (SemiFunc.RunIsShop() || ...)`.

## What to do before publishing

1. **Write to the developers (semiwork)** and ask for permission. They are friendly to mods, and their "yes" removes
   the main risk for the repository, a copyright claim. It does not remove the SSA risk from SteamKit2 (see above). Ask
   specifically: may we publish a builder that uses the player's own copy, may Quest clients connect to their Photon,
   and does this get in the way of their plans for other platforms.
2. **Move the network part (SteamShim/SteamKit2) into a separate module** that the player turns on themselves, with a
   clear warning about the risk to their account. The offline build works without it. Valve's written consent is
   almost certainly out of reach, so warning players is the honest option.
3. **Get in touch with DaXcess, the author of RepoXR.** The GPL allows the port, but it is better to talk. Some of our
   patches, such as the Head Bob and Pixel Lights sliders (`patches/repoxr/`), could go into RepoXR itself.
4. **Check before publishing** that nothing from the game gets into the repository: decompiled code, the export,
   shader data, the Steam session. All of it lives in `work/` now and must be reliably excluded.

## The builder (since 2026-10-03)

A one-click builder exists for Windows: `RepoQuestBuilder.exe`. Its design and release process are described in
`docs/builder.md`. The network part stayed in, but it only turns on when the player logs in to Steam in the headset.
The login page and the builder both warn about the risk to the account. The builder sends nothing anywhere and never
handles passwords: the login happens in the headset, directly with Steam.

**Published 2026-10-05:** the public repository https://github.com/Dobutsu/repo-quest-vr (GPL-3.0, like RepoXR; its
history starts fresh, the full history is in the private repository) and the builder in GitHub Releases (from
1.0.2; since 1.0.3 its window opens in English). The builder looks for updates there too
(`releases/latest/download`). An email to semiwork went out on 2026-09-29; no reply as of 2026-10-05. DaXcess has not
been contacted yet. The README thanks DaXcess and asks people not to take the port's problems to him.

## Open questions

- Follow up with semiwork (no reply to the 2026-09-29 email), possibly through another channel.
- Get in touch with DaXcess.
