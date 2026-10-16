# Pico 4

The Pico build is the Quest build, converted. Nothing in the Unity project changes for Pico; the differences are one
pipeline step after Unity and two runtime helpers that act on Pico.

## The pico step (builder)

`builder/QuestBuilder.Core/Pipeline/PicoStep.cs` runs only when the player picks Pico 4 in the builder (or
`QuestBuilder.Cli build --headset pico`). It runs [ovrport](https://github.com/ovrport/app) 1.2.3's command-line jar on
the Quest APK, with Eclipse Temurin JRE 17 (ovrport needs Java 17; Unity brings 11). Both are pinned by SHA-256 in
`builder/kit.json`. ovrport:

- swaps `libopenxr_loader.so` for its own (instance 3.3.0, the newest not marked experimental), which picks Pico's
  OpenXR runtime by `ro.product.brand`, maps the Touch controller bindings to `bytedance/pico4(s)_controller` and tells
  the game it runs on a Quest 2;
- adds the manifest entries Pico needs (`pvr.app.type=vr`) and leaves the game's own libraries alone;
- signs with a key it makes once in `<workspace>/pico/ovrport-ws/signatures`, so a later APK installs over the earlier
  one. Losing that folder means the next install replaces the app (the installer keeps the saves).

`patch_replace_icon_label` is left out: it looks the package up in Meta's store, where this one is not. The step takes
under a minute; switching the headset in the builder does not rebuild anything in Unity. Its log is
`<workspace>/logs/ovrport.log`.

## The GPU hang (unity-tools/Runtime/ShaderWarmup.cs)

On a Pico 4 (Adreno 650, Vulkan driver 0682.0) most launches hung the GPU in the main menu: the kernel logged
`kgsl-3d0: GPU hang detected`, the OpenXR runtime failed `xrAcquireSwapchainImage`, Unity shut XR down. It happened
with a warm Vulkan pipeline cache too, so it was not shader compilation as such. Letting renderers in a few at a time
stopped it: from the first frame, every renderer whose material variant (renderer kind, shader, keywords) has not been
drawn yet is held back, and the variants come in one every half second, in the real cameras. A variant that ran ten
seconds without XR being lost is listed in `files/shader-warmup/warm.txt` and drawn right away next time, so only the
first start and new places are slow. `files/shader-warmup/log.txt` on the headset tells what came in when; if XR is
lost anyway, it lists what came in during the last ten seconds, and the game quits with a message to start it again.

It is on for Pico only (`android.os.Build.BRAND`); a file `files/shader-warmup` containing `on` or `off` overrides that.
UI, post-processing and deferred lighting are not held.

What exactly hangs the Adreno 650 is not known. `unity-tools/Runtime/GpuHangProbe.cs` (not in the repository) was the
diagnostic that brought features and shaders back one by one; its findings were noisy, and slow reveal alone fixed it.

## The microphone prompt (unity-tools/Runtime/MicPermission.cs)

Unity asks for every dangerous permission in the manifest as the game starts; for this game that is the microphone,
for voice chat. On Pico the system's permission window takes the focus while the OpenXR session is still coming up,
and Unity's OpenXR plugin crashed (SIGSEGV in `libUnityOpenXR.so`) half a second after the game got it back: the first
start after every install died. `unity-tools/Editor/SkipPermissionsManifest.cs` sets `unityplayer.SkipPermissionsDialog`
for every build, and `MicPermission` asks once per install: on Pico in the main menu, after five seconds of steady XR;
on Quest right away, as Unity did. The voice chat looks the microphone up every frame and picks up a late grant by
itself. The prompt in the menu is new and not yet confirmed on a Pico headset.
