# Shader gallery: the port's shaders against the PC game's

The port cannot use the game's shaders: AssetRipper's free export has no shader code, so `scripts/generate_shaders.py`
writes stand-ins from the properties, the parsed fixed-function state and, where someone read it, the DX11 bytecode
(`scripts/shader_disasm.py`). Until now the stand-ins were checked by eye in the headset. The gallery is a mechanical
check (an *oracle*, in universal-modder's terms): the same prefabs rendered by the PC game with the original shaders
and by the port with ours, compared picture by picture.

## How it works

`unity-tools/Runtime/ShaderGallery.cs` renders a list of prefabs from `Resources` (1,211 of them: enemies, valuables,
items, cosmetics, level modules, effects) one by one, 2 km below the scene, into PNGs:

- Nothing of a prefab runs. It is instantiated under an inactive parent, so no `Awake`, and only copies of its
  visible renderers are drawn: meshes, skinned meshes in their rest pose (bound to the inactive clone's bones) and
  sprites. Particles, lines and trails are skipped; LOD groups draw LOD 0.
- Fixed lighting: one directional light, flat ambient light, a grey reflection probe. No shadows, fog, point lights
  (on the Quest the lamps are our own shader code) or post-processing; anisotropic filtering forced on. The game's
  other lights and render settings are switched off for the render and put back.
- Objects at 512 px from the front-right; level modules at 1024 px from high above, framed by their floor area, with
  the near plane cutting off the ceiling.
- Next to each `<key>.png`, `<key>.txt` lists its renderers, materials and shaders. `index.tsv` has a status per
  prefab, and `done` records the Unity version, graphics API, tier and quality level.

The same file runs in three places:

| Where | What it shows | How |
|---|---|---|
| PC game, CrossOver test copy `C:\REPO-mirror` | the reference: original shaders, D3D11 | BepInEx plugin `tools/ShaderGallery.Pc`; waits for `shader-gallery/request.txt` in the game folder |
| Unity editor on the Mac | our shaders, Metal; no APK build, about 30 s | `unity-tools/Editor/ShaderGalleryEditor.cs` in batch mode |
| Headset | our shaders, Vulkan on Adreno | `QuestDiagnostics`: `files/shader-gallery` → `files/gallery/` |

## Running it

```sh
python3 scripts/shader_gallery.py list                  # work/gallery/resources.txt (--only enemies,valuables)
python3 scripts/shader_gallery.py pc --launch           # reference → work/gallery/pc (Steam logged in in the bottle)
python3 scripts/shader_gallery.py editor                # → work/gallery/editor
python3 scripts/shader_gallery.py quest                 # → work/gallery/quest (game running on the headset)
python3 scripts/shader_gallery.py compare pc editor     # → work/gallery/report-pc-editor/index.html, summary.tsv
```

The PC reference only changes with the game, so it is made once per game version. After changing
`generate_shaders.py`, rerun the pipeline's project step, then `editor` and `compare`. Everything lands in `work/`
because the pictures are the game's assets.

For `pc`, Steam in the bottle must be logged in first (`steam.exe -silent`, then wait for `Logged On` in
`logs/connection_log.txt`); otherwise the game quits. `-shutdown` closes Steam and the game with it. The game's
window appears for about two minutes, and the game quits by itself when the gallery is done.

## Reading the report

The score is the mean |difference| (0–255) over the pixels where either picture shows the object, after a 2×2 box
filter so that edges rasterised a pixel apart do not count. The report also gives the share of pixels off by more
than 32, the silhouette mismatch (seen in one picture only: missing geometry, broken alpha clipping) and the mean
colour shift. Shaders are ranked by the median score of the prefabs that use them.

Known noise: texture compression (BC on PC, ASTC on Android), shaders animated by time (`Lava`, `Water`), and
`Standard`, which the port replaces with `RepoQuest/Standard` (lamps) and whose BRDF Unity picks by graphics tier
(the PC uses the high tier; Android has no tier settings of its own, so the defaults apply). A score of about 5 is
the floor; above 12 is worth a look.

## First results (2026-10-05, PC vs editor)

| Shader | Median | What differs |
|---|---|---|
| `Slow Projectile` | 58 | The Void Staff's projectile is a plain white sphere: no red texture, no spikes |
| `Semiwork/Alpha Clip` | 28 | Shop modules (Bunker 2) |
| `ShinyMetal` | 23 | No metallic shine (Golden Swirl is flat yellow). **Fixed 2026-10-08**: hand-written from the bytecode (`unity-tools/ShaderOverrides/ShinyMetal.shader`); its four valuables' mean pixel difference from the PC went from 3.5–7.4 to 0.7–2.2 |
| `Custom/MagnifyingShader` | 21 | The magnifying glass's lens is opaque grey instead of see-through |
| `Water` | 19 | Partly time, partly look |
| `Ciconia Studio/…/Cutout` | 10–11 | Alpha clipping missing on some materials: a grey panel between the harp's strings |
| `Fresnel Opaque` / `Fresnel Transparent` | 6–9 | The crystal ball has no white rim; the cocktail glass is darker |

Still to do: a run on the headset (needs an APK with this `QuestDiagnostics`) to separate Metal from Vulkan, and the
fixes themselves.
