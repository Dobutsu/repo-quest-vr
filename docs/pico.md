# Pico 4

Since builder 1.5.0 there is one APK for Quest and Pico: the Quest build, converted by ovrport. Nothing in the Unity
project changes for Pico; the differences are one pipeline step after Unity and a few runtime helpers that act on Pico
only.

## The universal step (builder)

`builder/QuestBuilder.Core/Pipeline/UniversalStep.cs` runs for every build (before 1.5.0 it was the "pico" step, only
when the player picked Pico 4; `QuestBuilder.Cli build --quest-only` still leaves it out for quick Quest tests). It runs
[ovrport](https://github.com/ovrport/app) 1.2.3's command-line jar on Unity's APK, with Eclipse Temurin JRE 17 (ovrport
needs Java 17; Unity brings 11). Both are pinned by SHA-256 in `builder/kit.json`. ovrport:

- swaps `libopenxr_loader.so` for its own (instance 3.3.0, the newest not marked experimental), which loads the headset
  maker's loader by `ro.product.brand` (`libopenxr_loader_meta.so` on a Quest, `_pico` on a Pico, where it also maps the
  Touch controller bindings to `bytedance/pico4(s)_controller` and tells the game it runs on a Quest 2);
- adds the manifest entries Pico needs (`pvr.app.type=vr`, the Khronos `IMMERSIVE_HMD` category) next to the Quest's,
  which stay (`com.oculus.intent.category.VR`, `com.oculus.vr.focusaware`; `com.oculus.supportedDevices` becomes
  `all`), and leaves the game's own libraries alone;
- signs with a key it makes once in `<workspace>/pico/ovrport-ws/signatures`, so a later APK installs over the earlier
  one. The first universal APK on a headset that has a Quest-only build (signed with Unity's debug key) cannot replace
  it: the installer copies the game's data off, reinstalls and puts the data back (`QuestInstaller`). Losing that
  folder means the same once more.

Left out of ovrport's recommended patches: `patch_replace_icon_label` (it looks the package up in Meta's store, where
this one is not) and, since 1.5.0, `patch_mark_as_debuggable` (Android runs debuggable apps slower, and the same APK
now runs on the Quest). The step takes a few minutes; its log is `<workspace>/logs/ovrport.log`.

Windows checked 2026-11-26: the win-x64 CLI under CrossOver ran the step on the 0.5.4 APK in about 2 minutes, and the
result matched the Mac's entry for entry (outside META-INF) except for the case below.

**Mac: names that differ only in case.** Unity's APK has `res/Dm.xml` + `res/dM.xml` and `res/EE.xml` + `res/ee.xml`
(the same adaptive-icon XML, short names from resource shrinking). ovrport on macOS (case-insensitive disk) keeps one of
each pair, so the Mac's universal APK lacks `res/dM.xml` and `res/ee.xml` although resources.arsc still names them; the
Windows run keeps all four. The game starts and plays on a Quest 3 without them (only icon resources are involved), but
fix it before a Mac release (e.g. ovrport's workspace on a case-sensitive volume, or put the missing entries back and
re-sign).

## The GPU hang (unity-tools/Runtime/ShaderWarmup.cs)

On a Pico 4 (Adreno 650, Vulkan driver 0682.0) most launches hung the GPU in the main menu: the kernel logged
`kgsl-3d0: GPU hang detected`, the OpenXR runtime failed `xrAcquireSwapchainImage`, Unity shut XR down. It happened
with a warm Vulkan pipeline cache too, so it was not shader compilation as such. Letting renderers in a few at a time
stopped it: from the first frame, every renderer whose material variant (renderer kind, shader, keywords) has not been
drawn yet is held back, and the variants come in one every half second, in the real cameras. A variant that ran ten
seconds without XR being lost is listed in `files/shader-warmup/warm.txt` and drawn right away next time, so only the
first start and new places are slow. `files/shader-warmup/log.txt` on the headset tells what came in when; if XR is
lost anyway, it lists what came in during the last ten seconds, and the game quits with a message to start it again.

It is on for the Pico 4 only (`android.os.Build.BRAND` is Pico and the GPU an Adreno 650): the Pico 4 Ultra's Adreno 740
did not hang, and the per-frame scan cost it frames. A file `files/shader-warmup-mode` containing `on` or `off` overrides
that.
UI, post-processing and deferred lighting are not held.

What exactly hangs the Adreno 650 is not known. `unity-tools/Runtime/GpuHangProbe.cs` (idle unless asked for) was the
diagnostic that brought features and shaders back one by one; its findings were noisy, and slow reveal alone fixed it.

## Pico 4 Ultra: the focus sphere (patches/repoxr/0008)

On a Pico 4 Ultra (Adreno 740, Vulkan driver 512.772) the levels ran at 13-15 FPS while the menu held 90. The render
thread spent most of each frame in the kernel: the driver allocates and frees GPU memory for every `vkCmdCopyImage`, and
the frame had one, of the whole eye texture. It came from RepoXR's focus sphere (the VR stand-in for the ceiling eye's
forced look), whose shader has a `GrabPass` and whose renderer was on all the time, even fully faded out. Quest's driver
does that copy in about 0.02 ms, the Ultra's in about 55 ms. Patch 0008 turns the renderer on only while the sphere
shows: 75-84 FPS on the Ultra. `unity-tools/Native/RepoQuestVk.cpp` (Vulkan copy counters for PerfLog, idle in normal
builds) is what found it.

## The microphone prompt (unity-tools/Runtime/MicPermission.cs)

Unity asks for every dangerous permission in the manifest as the game starts; for this game that is the microphone,
for voice chat. On Pico the system's permission window takes the focus while the OpenXR session is still coming up,
and Unity's OpenXR plugin crashed (SIGSEGV in `libUnityOpenXR.so`) half a second after the game got it back: the first
start after every install died. `unity-tools/Editor/SkipPermissionsManifest.cs` sets `unityplayer.SkipPermissionsDialog`
for every build, and `MicPermission` asks once per install: on Pico in the main menu, after five seconds of steady XR;
on Quest right away, as Unity did. The voice chat looks the microphone up every frame and picks up a late grant by
itself. The prompt in the menu is new and not yet confirmed on a Pico headset.

## Application SpaceWarp (AppSW) on Pico 4 and Pico 4 Ultra

Tests in November 2026 on a Pico 4 (`A8110`, Pico OS `5.9.9`, Adreno 650; three test builds with a probe that wrote a
report, kept in the private test notes):

1. **The extension works.** Pico's OpenXR runtime offers Meta's `XR_FB_space_warp` and Unity's OpenXR plugin enables
   it (`active: True`); `SpaceWarpXr.SetSpaceWarp(true)` returns `XrResult 0`; the display subsystem reports
   `hasMotionVectorPass = true` with a `376x376x2` `R16G16B16A16_SFloat` target (depth `D24_UNorm_S8_UInt`); 811 frames
   of motion vectors went to the runtime without an error. `XR_FB_display_refresh_rate` switches 72/90 Hz. (The probe
   saw the runtime named "Oculus 67.522.0": ovrport's loader reports the Quest's.)

2. **The frame rate does not halve by itself.** On a Quest the runtime paces `xrWaitFrame` to two display frames
   under SpaceWarp (VrApi `VSnc=2`). Pico's does not: with SpaceWarp on, Unity went on rendering 86-90 frames a second,
   so nothing was saved. `QuestSpaceWarp.ConfigureCadence` therefore caps the frame rate on a Pico only
   (`QualitySettings.vSyncCount = 0`, `Application.targetFrameRate` = half the display rate): Pico 4 at 72 Hz -> 36,
   Pico 4 Ultra (Adreno 740 / `A9210`) at 90 Hz -> 45 (**not tested on an Ultra**). Turning SpaceWarp off puts back
   the vSync count, cap and display rate from before. On a Quest it changes nothing. With the cap the Pico 4 held 36.

3. **Default on the Pico 4.** Graphics Mode Performance (the default) has SpaceWarp on on the Pico 4
   (`patches/repoxr/0045`): one pixel light, no post-processing, 36 rendered at 72 Hz, a 27.8 ms budget per frame.
   Elsewhere Performance stays without it. Turning SpaceWarp off by hand switches the mode to Personal, as any change
   of a managed setting does, and stays off.

## One APK for Quest and Pico

Builder 1.5.0 makes only the universal APK (above). What tells the headsets apart at runtime:

- the OpenXR loader (ovrport's, by `ro.product.brand`);
- `ShaderWarmup` (Pico 4: brand Pico and Adreno 650), `MicPermission` (Pico: asks in the menu), `QuestSpaceWarp`'s
  frame rate cap (Pico), RepoXR's Graphics Mode default (`0045`, Pico 4) — each checks for itself; on a Quest all of
  them do what a Quest-only build did.

Tested: on Pico 4 and Pico 4 Ultra the ovrport APK since October 2026. On a Quest the ovrport APK is new with 1.5.0:
tested on a Quest 3 (runtime Oculus, Touch Plus controllers, 72 fps, Application SpaceWarp 36/72).
