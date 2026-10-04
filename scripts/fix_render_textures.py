"""Switch render textures to formats Quest's GPU can alpha-blend into.

R.E.P.O. renders its HUD/menus (Camera Overlay) into "Render Texture Overlay", an RGBA32 float target. Desktop GPUs
blend into 32-bit float targets; Adreno does not, so every alpha-blended UI element vanishes. RGBA16 float blends
everywhere and is plenty for UI.
Usage: python scripts/fix_render_textures.py <unity project>
"""

import re
import sys
from pathlib import Path

# GraphicsFormat values: 49-52 = R32..R32G32B32A32_SFloat, 48 = R16G16B16A16_SFloat
FLOAT32_FORMATS = {"49", "50", "51", "52"}
FLOAT16_RGBA = "48"


def main() -> int:
    project = Path(sys.argv[1])
    changed = []
    for path in (project / "Assets").rglob("*.renderTexture"):
        text = path.read_text()
        updated = re.sub(r"(m_ColorFormat: )(\d+)", lambda m: m.group(1) + (FLOAT16_RGBA if m.group(2) in FLOAT32_FORMATS else m.group(2)), text)
        if updated != text:
            path.write_text(updated, newline="\n")
            changed.append(path.name)
    print(f"render textures switched to RGBA16F: {', '.join(changed) or 'none'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
