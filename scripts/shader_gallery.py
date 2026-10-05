"""The shader oracle: the same prefabs rendered by the PC game and by the port, compared picture by picture.

The PC game draws with the original (compiled) shaders, the port with scripts/generate_shaders.py's stand-ins. Both
render every listed prefab from Resources alone, under the same fixed light, through unity-tools/Runtime/
ShaderGallery.cs; the report ranks prefabs, and the shaders they use, by how far the port's picture is from the PC's.
  list     work/gallery/resources.txt: the prefabs in the Quest project's Resources (--only cosmetics,enemies,...)
  pc       the reference, in the CrossOver test copy C:\\REPO-mirror (BepInEx + tools/ShaderGallery.Pc). Steam must be
           logged in in the bottle first; --launch starts the game, otherwise start it yourself
  editor   the port's shaders in the Unity editor on this Mac (Metal), no APK build: work/unity/REPOQuest
  quest    the port on the headset (Vulkan): the game must be running (QuestDiagnostics, files/shader-gallery)
  compare  <a> <b>: work/gallery/report-<a>-<b>/index.html and summary.tsv
Each run writes work/gallery/<name>/ (<key>.png, <key>.txt: renderers, materials, shaders; index.tsv; done).
Usage: python3 scripts/shader_gallery.py list [--only level,valuables]
       python3 scripts/shader_gallery.py pc --launch | editor | quest
       python3 scripts/shader_gallery.py compare pc editor
Needs numpy and Pillow for compare.
"""

import argparse
import html
import os
import shutil
import statistics
import subprocess
import sys
import time
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / "lib"))
from paths import ROOT, UNITY_EDITOR, WORK  # noqa: E402

GALLERY = WORK / "gallery"
LIST = GALLERY / "resources.txt"
PROJECT = WORK / "unity" / "REPOQuest"
BOTTLE = Path.home() / "Library/Application Support/CrossOver/Bottles" / os.environ.get("BOTTLE", "Steam")
MIRROR = BOTTLE / "drive_c" / "REPO-mirror"
WINE = Path("/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine")
DOTNET = Path.home() / ".dotnet" / "dotnet"
ADB = UNITY_EDITOR / "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"
QUEST_FILES = "/sdcard/Android/data/io.repoxr.quest/files"
# Shaders that animate with _Time (or the game's own timers): differences there are expected noise
ANIMATED = ("Lava", "Water", "RotatingAlbedo", "Scroll", "Pulse")


def read_list(path: Path) -> list[str]:
    return [line.strip() for line in path.read_text().splitlines() if line.strip() and not line.startswith("#")]


def cmd_list(args):
    resources = PROJECT / "Assets" / "Resources"
    paths = sorted(p.relative_to(resources).with_suffix("").as_posix() for p in resources.rglob("*.prefab"))
    if args.only:
        prefixes = tuple(f"{folder.strip('/')}/" for folder in args.only.split(","))
        paths = [p for p in paths if p.startswith(prefixes)]
    GALLERY.mkdir(parents=True, exist_ok=True)
    LIST.write_text("\n".join(paths) + "\n", newline="\n")
    counts = defaultdict(int)
    for p in paths:
        counts[p.split("/")[0] if "/" in p else "."] += 1
    print(f"{len(paths)} prefabs -> {LIST}: " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))


def wait_for(check, what: str, timeout_min: float):
    deadline = time.time() + timeout_min * 60
    while not check():
        if time.time() > deadline:
            sys.exit(f"timed out waiting for {what}")
        time.sleep(10)


def collect(source: Path, name: str):
    dest = GALLERY / name
    if dest.exists():
        shutil.rmtree(dest)
    shutil.copytree(source, dest)
    print(f"{dest}: {(dest / 'done').read_text().strip()}")


def cmd_pc(args):
    if not (MIRROR / "BepInEx").is_dir():
        sys.exit(f"no BepInEx test copy at {MIRROR} (repo-truck-mirror: scripts/crossover-setup.sh)")
    project = ROOT / "tools" / "ShaderGallery.Pc"
    subprocess.run([DOTNET, "build", "-c", "Release", "-nologo", "-v", "quiet", project], check=True, capture_output=True)
    plugin = MIRROR / "BepInEx" / "plugins" / "RepoQuestShaderGallery"
    plugin.mkdir(parents=True, exist_ok=True)
    shutil.copy2(project / "bin/Release/netstandard2.1/RepoQuest.ShaderGallery.dll", plugin)

    out = MIRROR / "shader-gallery"
    out.mkdir(exist_ok=True)
    if (out / "out").exists():
        shutil.rmtree(out / "out")
    (out / "request.tmp").write_text("#quit\n" + "\n".join(read_list(args.list)) + "\n", newline="\n")
    (out / "request.tmp").rename(out / "request.txt")  # the plugin must never see half a list
    if args.launch:
        steam = subprocess.run(["pgrep", "-f", "steam.exe"], capture_output=True).returncode == 0
        if not steam:
            sys.exit("Steam is not running in the bottle: start it, wait until it is logged in, then run again")
        subprocess.Popen([WINE, "--bottle", BOTTLE.name, "--workdir", "C:\\REPO-mirror", "C:\\REPO-mirror\\REPO.exe"],
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        print("game started; it renders after ~20 s and quits when done")
    else:
        print(f"request written; start C:\\REPO-mirror\\REPO.exe in the {BOTTLE.name} bottle")
    wait_for(lambda: (out / "out" / "done").exists(), "the PC gallery", args.timeout)
    collect(out / "out", args.name or "pc")


def cmd_editor(args):
    # The pipeline copies unity-tools into the project on every build; a gallery run must not wait for one
    for src, dst in (("Runtime/ShaderGallery.cs", "Runtime"), ("Runtime/QuestDiagnostics.cs", "Runtime"),
                     ("Editor/ShaderGalleryEditor.cs", "Editor")):
        shutil.copy2(ROOT / "unity-tools" / src, PROJECT / "Assets" / "RepoQuest" / dst)
    out = GALLERY / (args.name or "editor")
    if out.exists():
        shutil.rmtree(out)
    log = WORK / "logs" / "unity-gallery.log"
    unity = UNITY_EDITOR / "Unity.app" / "Contents" / "MacOS" / "Unity"
    print(f"Unity is rendering (log: {log})")
    result = subprocess.run([unity, "-batchmode", "-projectPath", PROJECT, "-buildTarget", "Android",
                             "-executeMethod", "RepoQuest.Editor.ShaderGalleryEditor.Run",
                             "-galleryList", args.list, "-galleryOut", out, "-logFile", log])
    if result.returncode != 0 or not (out / "done").exists():
        sys.exit(f"the editor run failed (exit code {result.returncode}), see {log}")
    print(f"{out}: {(out / 'done').read_text().strip()}")


def adb(*args: str, check=True) -> subprocess.CompletedProcess:
    return subprocess.run([ADB, *args], check=check, capture_output=True, text=True)


def cmd_quest(args):
    adb("push", str(args.list), f"{QUEST_FILES}/shader-gallery.tmp")
    adb("shell", f"mv {QUEST_FILES}/shader-gallery.tmp {QUEST_FILES}/shader-gallery")
    print("request pushed; the game must be running on the headset (and the headset awake)")
    wait_for(lambda: adb("shell", f"test -f {QUEST_FILES}/gallery/done", check=False).returncode == 0,
             "the headset gallery", args.timeout)
    pulled = GALLERY / "quest-pull"
    if pulled.exists():
        shutil.rmtree(pulled)
    pulled.mkdir(parents=True)
    adb("pull", f"{QUEST_FILES}/gallery", str(pulled))
    collect(pulled / "gallery", args.name or "quest")
    shutil.rmtree(pulled)


# ---- compare ----

def read_index(run: Path) -> dict[str, dict]:
    rows = (run / "index.tsv").read_text().splitlines()
    head = rows[0].split("\t")
    return {r["key"]: r for r in (dict(zip(head, row.split("\t"))) for row in rows[1:] if row)}


def shaders_of(run: Path, key: str) -> list[str]:
    found = []
    for line in (run / f"{key}.txt").read_text().splitlines():
        for slot in line.split("\t")[2:]:
            if " | " in slot:
                found.append(slot.split(" | ", 1)[1])
    return found


def load(path: Path):
    import numpy as np
    from PIL import Image
    image = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32)
    h, w, _ = image.shape  # 2x2 box filter: edges rasterised a pixel apart do not count as differences
    return image[:h // 2 * 2, :w // 2 * 2].reshape(h // 2, 2, w // 2, 2, 3).mean(axis=(1, 3))


def background(image):
    import numpy as np
    corners = np.array([image[0, 0], image[0, -1], image[-1, 0], image[-1, -1]])
    return np.median(corners, axis=0)


def measure(a, b) -> dict:
    """Over the pixels where either picture shows the object: mean |difference| (0-255), the share of pixels off by
    more than 32, the silhouette mismatch and the mean signed colour shift of b against a"""
    import numpy as np
    mask_a = np.abs(a - background(a)).max(axis=2) > 3
    mask_b = np.abs(b - background(b)).max(axis=2) > 3
    union, both = mask_a | mask_b, mask_a & mask_b
    if not union.any():
        return {"score": 0.0, "bad": 0.0, "silhouette": 0.0, "shift": (0.0, 0.0, 0.0)}
    diff = np.abs(a - b)
    shift = (b[both] - a[both]).mean(axis=0) if both.any() else np.zeros(3)
    return {"score": float(diff[union].mean()), "bad": float((diff[union].max(axis=1) > 32).mean() * 100),
            "silhouette": float((union & ~both).sum() / union.sum() * 100), "shift": tuple(float(s) for s in shift)}


def diff_image(a, b, path: Path):
    import numpy as np
    from PIL import Image
    d = np.clip(np.abs(a - b).max(axis=2) * 4, 0, 255).astype(np.uint8)
    heat = np.stack([d, np.clip(d.astype(int) * 2 - 255, 0, 255).astype(np.uint8), np.zeros_like(d)], axis=2)
    Image.fromarray(heat).save(path)


def cmd_compare(args):
    a_dir, b_dir = GALLERY / args.a, GALLERY / args.b
    out = args.out or GALLERY / f"report-{args.a}-{args.b}"
    (out / "diff").mkdir(parents=True, exist_ok=True)
    a_index, b_index = read_index(a_dir), read_index(b_dir)
    rows, only = [], []
    for key in sorted(set(a_index) | set(b_index)):
        sa, sb = a_index.get(key, {}).get("status", "-"), b_index.get(key, {}).get("status", "-")
        path = (a_index.get(key) or b_index[key])["path"]
        if sa != "ok" or sb != "ok":
            if sa != sb:
                only.append((path, sa, sb))
            continue
        a, b = load(a_dir / f"{key}.png"), load(b_dir / f"{key}.png")
        m = measure(a, b)
        diff_image(a, b, out / "diff" / f"{key}.png")
        rows.append({"key": key, "path": path, "shaders": sorted(set(shaders_of(a_dir, key))), **m})
    rows.sort(key=lambda r: -r["score"])

    by_shader = defaultdict(list)
    for r in rows:
        for s in r["shaders"]:
            by_shader[s].append(r)
    shaders = sorted(((s, statistics.median(r["score"] for r in rs), rs) for s, rs in by_shader.items()),
                     key=lambda t: -t[1])

    with open(out / "summary.tsv", "w", newline="\n") as f:
        f.write("score\tbad%\tsilhouette%\tshift_r\tshift_g\tshift_b\tpath\tshaders\n")
        for r in rows:
            f.write(f"{r['score']:.1f}\t{r['bad']:.1f}\t{r['silhouette']:.1f}\t" + "\t".join(f"{s:+.0f}" for s in r["shift"])
                    + f"\t{r['path']}\t{'; '.join(r['shaders'])}\n")
    write_report(out, args, rows, shaders, only, a_dir, b_dir)
    print(f"{len(rows)} compared, {len(only)} on one side only -> {out / 'index.html'}")
    for s, median, rs in shaders[:args.top]:
        print(f"  {median:5.1f}  {s}  ({len(rs)} prefabs, worst {rs[0]['path']} {rs[0]['score']:.1f})")


def write_report(out: Path, args, rows, shaders, only, a_dir: Path, b_dir: Path):
    def rel(p: Path) -> str:
        return html.escape(os.path.relpath(p, out))

    def shift(s) -> str:
        return " ".join(f"{c}{v:+.0f}" for c, v in zip("RGB", s))

    e = html.escape
    parts = [f"""<title>Shader gallery: {e(args.a)} vs {e(args.b)}</title>
<style>
:root {{ --bg: #f6f6f4; --fg: #1d1d1f; --muted: #6b6b70; --line: #deded8; --bad: #b3261e; }}
@media (prefers-color-scheme: dark) {{ :root {{ --bg: #19191b; --fg: #ececee; --muted: #9a9aa2; --line: #333338; --bad: #ff8a80; }} }}
body {{ background: var(--bg); color: var(--fg); font: 14px/1.4 system-ui, sans-serif; margin: 0; padding: 16px; }}
h1 {{ font-size: 20px; margin: 0 0 4px; }} h2 {{ font-size: 16px; margin: 28px 0 8px; }}
.muted {{ color: var(--muted); }} table {{ border-collapse: collapse; }}
td, th {{ border-bottom: 1px solid var(--line); padding: 4px 8px; text-align: left; vertical-align: top; }}
td.n {{ text-align: right; font-variant-numeric: tabular-nums; }} .hot {{ color: var(--bad); font-weight: 600; }}
.wrap {{ overflow-x: auto; }} img {{ width: 192px; height: 192px; image-rendering: pixelated; display: block; }}
.pics {{ display: flex; gap: 4px; }} .pics figure {{ margin: 0; }} figcaption {{ font-size: 11px; color: var(--muted); }}
input {{ font: inherit; padding: 4px 8px; margin: 8px 0; width: min(420px, 100%); }}
</style>
<h1>Shader gallery: {e(args.a)} vs {e(args.b)}</h1>
<p class="muted">{e(args.a)}: {e((a_dir / 'done').read_text().strip())}<br>{e(args.b)}: {e((b_dir / 'done').read_text().strip())}<br>
Score: mean |difference| in 0–255 over the object's pixels (2×2 averaged). Bad: % of those pixels off by more than 32.
Silhouette: % seen in one picture only. Shift: mean colour of {e(args.b)} minus {e(args.a)}. Shaders that animate
({', '.join(ANIMATED)}) differ by time as well.</p>
<h2>Shaders, worst first (median score of the prefabs that use them)</h2><div class="wrap"><table>
<tr><th>median</th><th>prefabs</th><th>shader</th><th>worst prefabs</th></tr>"""]
    for s, median, rs in shaders:
        worst = ", ".join(f'<a href="#{e(r["key"])}">{e(r["path"])}</a> {r["score"]:.0f}' for r in rs[:4])
        parts.append(f'<tr><td class="n{" hot" if median > 12 else ""}">{median:.1f}</td><td class="n">{len(rs)}</td>'
                     f"<td>{e(s)}</td><td>{worst}</td></tr>")
    parts.append("</table></div>")
    if only:
        parts.append(f"<h2>Not rendered on both sides ({len(only)})</h2><div class='wrap'><table>"
                     f"<tr><th>prefab</th><th>{e(args.a)}</th><th>{e(args.b)}</th></tr>")
        parts += [f"<tr><td>{e(p)}</td><td>{e(sa)}</td><td>{e(sb)}</td></tr>" for p, sa, sb in only]
        parts.append("</table></div>")
    parts.append(f"""<h2>Prefabs, worst first ({len(rows)})</h2>
<input id="filter" placeholder="Filter by path or shader" oninput="filterRows(this.value)">
<div class="wrap"><table id="rows"><tr><th>score</th><th>bad</th><th>silh.</th><th>shift</th><th>prefab</th><th>pictures</th></tr>""")
    for r in rows:
        pics = "".join(f'<figure><img loading="lazy" src="{src}" alt=""><figcaption>{e(cap)}</figcaption></figure>'
                       for src, cap in ((rel(a_dir / f"{r['key']}.png"), args.a), (rel(b_dir / f"{r['key']}.png"), args.b),
                                        (rel(out / "diff" / f"{r['key']}.png"), "difference ×4")))
        parts.append(f'<tr id="{e(r["key"])}" data-text="{e((r["path"] + " " + " ".join(r["shaders"])).lower())}">'
                     f'<td class="n{" hot" if r["score"] > 12 else ""}">{r["score"]:.1f}</td><td class="n">{r["bad"]:.0f}%</td>'
                     f'<td class="n">{r["silhouette"]:.0f}%</td><td>{shift(r["shift"])}</td>'
                     f'<td>{e(r["path"])}<br><span class="muted">{"<br>".join(e(s) for s in r["shaders"])}</span></td>'
                     f'<td><div class="pics">{pics}</div></td></tr>')
    parts.append("""</table></div>
<script>
function filterRows(text) {
  text = text.toLowerCase();
  for (const row of document.querySelectorAll('#rows tr[data-text]'))
    row.hidden = text && !row.dataset.text.includes(text);
}
</script>""")
    (out / "index.html").write_text("\n".join(parts), newline="\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("list")
    p.add_argument("--only", help="comma-separated Resources folders (cosmetics, enemies, items, level, valuables...)")
    for name in ("pc", "editor", "quest"):
        p = sub.add_parser(name)
        p.add_argument("--list", type=Path, default=LIST)
        p.add_argument("--name", help=f"output folder under work/gallery (default: {name})")
        p.add_argument("--timeout", type=float, default=90, help="minutes")
        if name == "pc":
            p.add_argument("--launch", action="store_true", help="start the game (Steam must be logged in)")
    p = sub.add_parser("compare")
    p.add_argument("a")
    p.add_argument("b")
    p.add_argument("--out", type=Path)
    p.add_argument("--top", type=int, default=15, help="shaders to print")
    args = parser.parse_args()
    {"list": cmd_list, "pc": cmd_pc, "editor": cmd_editor, "quest": cmd_quest, "compare": cmd_compare}[args.command](args)


if __name__ == "__main__":
    main()
