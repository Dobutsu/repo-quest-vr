"""Add the XR packages to the Quest project's manifest, at the versions RepoXR's asset project (RepoXR-Unity) is built with.
Usage: python scripts/xr_packages.py <unity project>
"""

import json
import sys
from pathlib import Path

PACKAGES = {
    "com.unity.xr.management": "4.5.0",
    "com.unity.xr.openxr": "1.14.3",
    "com.unity.xr.interaction.toolkit": "2.6.4",
}


def main() -> int:
    path = Path(sys.argv[1]) / "Packages" / "manifest.json"
    manifest = json.loads(path.read_text())
    manifest["dependencies"].update(PACKAGES)
    path.write_text(json.dumps(manifest, indent=2) + "\n", newline="\n")
    print(f"XR packages in the manifest: {', '.join(f'{name} {version}' for name, version in PACKAGES.items())}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
