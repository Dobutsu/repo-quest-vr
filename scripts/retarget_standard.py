"""Point the game's materials that use Unity's built-in Standard shaders at the lamp copies (make_lamp_shaders.py).

The built-in shaders are referenced as fileID 46 (Standard) and 47 (Standard (Specular setup)) in the built-in extra
resources; the copies keep the same properties and keywords, so the materials work unchanged. Idempotent.
Usage: python scripts/retarget_standard.py <unity project>
"""

import sys
from pathlib import Path

BUILTIN = "guid: 0000000000000000f000000000000000, type: 0}"
TARGETS = {
    "46": "7c1d4e2f9a8b4c3d9e0f1a2b3c4d5e6f",  # RepoQuestStandard
    "47": "8d2e5f3a0b9c4d4e8f1a2b3c4d5e6f70",  # RepoQuestStandardSpecular
}


def main() -> int:
    counts = {k: 0 for k in TARGETS}
    for path in (Path(sys.argv[1]) / "Assets").rglob("*.mat"):
        text = path.read_text(errors="surrogateescape")
        for file_id, guid in TARGETS.items():
            old = f"m_Shader: {{fileID: {file_id}, {BUILTIN}"
            if old in text:
                text = text.replace(old, f"m_Shader: {{fileID: 4800000, guid: {guid}, type: 3}}")
                path.write_text(text, errors="surrogateescape", newline="\n")
                counts[file_id] += 1
    print(f"Standard materials retargeted to the lamp shaders: {counts['46']} Standard, {counts['47']} Specular")
    return 0


if __name__ == "__main__":
    sys.exit(main())
