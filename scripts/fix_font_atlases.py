"""Keep the atlas textures of TextMeshPro's dynamic fonts uncompressed on Android.

R.E.P.O.'s TMP fonts are dynamic: glyphs that are not in the shipped atlas yet (a Steam name, the news text, rare
letters) are rendered into the atlas texture at runtime. AssetRipper exports the atlases as PNG textures with the
default import settings, and on Android Unity then compresses them (ASTC). Glyphs written into a compressed atlas come
out as stripes and boxes; the text that was already in the atlas still looks right. TMP creates its atlases as Alpha8
(color fonts as RGBA32): every atlas of a font asset with a population mode gets an Android override with that format,
uncompressed, and the default platform stops compressing.
Usage: python scripts/fix_font_atlases.py <unity project>
"""

import re
import sys
from pathlib import Path

ALPHA8 = 1  # TextureImporterFormat
RGBA32 = 4
COLOR_RENDER_MODES = 0x10000  # GlyphRenderMode.COLOR (69632) and up: color glyphs (emoji)

ANDROID_BLOCK = """  - serializedVersion: 4
    buildTarget: Android
    maxTextureSize: {size}
    resizeAlgorithm: 0
    textureFormat: {format}
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 1
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
"""


def font_atlases(project: Path) -> dict[str, int]:
    """Atlas texture GUID -> TextureImporterFormat, for every font asset with an atlas population mode"""
    atlases: dict[str, int] = {}
    for path in (project / "Assets").rglob("*.asset"):
        try:
            text = path.read_text(errors="replace")
        except OSError:
            continue
        if "m_AtlasPopulationMode:" not in text:
            continue
        mode = re.search(r"m_AtlasRenderMode: (\d+)", text)
        fmt = RGBA32 if mode and int(mode.group(1)) >= COLOR_RENDER_MODES else ALPHA8
        block = re.search(r"m_AtlasTextures:\n((?:  - \{[^\n]*\}\n)*)", text)
        if not block:
            continue
        for guid in re.findall(r"guid: ([0-9a-f]{32})", block.group(1)):
            atlases[guid] = fmt
    return atlases


def patch_meta(meta: Path, fmt: int) -> bool:
    text = meta.read_text()
    original = text
    # The default platform: uncompressed
    text = re.sub(r"(buildTarget: DefaultTexturePlatform\n(?:    [^\n]*\n)*?    textureCompression: )\d+",
                  r"\g<1>0", text)
    size = re.search(r"buildTarget: DefaultTexturePlatform\n    maxTextureSize: (\d+)", text)
    android = ANDROID_BLOCK.format(size=size.group(1) if size else 4096, format=fmt)
    # Replace an existing Android entry, or add one after the platform list's last entry
    existing = re.search(r"  - serializedVersion: \d+\n    buildTarget: Android\n(?:    [^\n]*\n)*", text)
    if existing:
        text = text[:existing.start()] + android + text[existing.end():]
    else:
        text = re.sub(r"(  platformSettings:\n(?:  - [^\n]*\n(?:    [^\n]*\n)*)+)", lambda m: m.group(1) + android, text, count=1)
    if text != original:
        meta.write_text(text, newline="\n")
        return True
    return False


def main() -> int:
    project = Path(sys.argv[1])
    atlases = font_atlases(project)
    changed = []
    for meta in (project / "Assets").rglob("*.meta"):
        try:
            head = meta.read_text(errors="replace")[:200]
        except OSError:
            continue
        guid = re.search(r"guid: ([0-9a-f]{32})", head)
        if guid and guid.group(1) in atlases and patch_meta(meta, atlases[guid.group(1)]):
            changed.append(meta.name.removesuffix(".meta"))
    print(f"font atlases kept uncompressed on Android ({len(atlases)} atlases): {', '.join(sorted(changed)) or 'none changed'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
