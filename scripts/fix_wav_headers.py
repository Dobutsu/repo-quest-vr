"""Write the sizes into the headers of the WAV files AssetRipper exported.

AssetRipper 2.0.0 writes every PCM AudioClip as a WAV whose RIFF size and data chunk size are 0 (the samples follow
in full). Unity 2022.3 imported those; Unity 6's FSBTool stops at "Failed decoding audio clip", and the APK gets
none of the 3,418 clips of v0.5.0. The data chunk is the last one and runs to the end of the file, so the sizes
follow from the file size. Headers that already have sizes are left alone; idempotent.
Usage: python scripts/fix_wav_headers.py <unity project>
"""

import struct
import sys
from pathlib import Path


def fix(path):
    data = bytearray(path.read_bytes())
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE" or struct.unpack_from("<I", data, 4)[0] != 0:
        return False
    offset = 12
    while offset + 8 <= len(data):
        chunk, size = data[offset:offset + 4], struct.unpack_from("<I", data, offset + 4)[0]
        if chunk == b"data":
            if size == 0:
                struct.pack_into("<I", data, offset + 4, len(data) - offset - 8)
            break
        offset += 8 + size + (size & 1)
    else:
        raise ValueError(f"{path}: no data chunk")
    struct.pack_into("<I", data, 4, len(data) - 8)
    path.write_bytes(data)
    return True


def main():
    assets = Path(sys.argv[1]) / "Assets"
    fixed = sum(fix(p) for p in assets.rglob("*.wav"))
    print(f"WAV headers: {fixed} fixed")


if __name__ == "__main__":
    main()
