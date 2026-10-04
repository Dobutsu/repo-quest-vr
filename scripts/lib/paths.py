"""Where the pipeline's inputs and outputs live.

The defaults fit this checkout on the Mac (work/ next to scripts/, Unity under /Applications/Unity/Hub). The Quest
builder (builder/) runs the same scripts from its kit and points them elsewhere with environment variables:
  REPOQUEST_WORK          working folder (default <repo>/work)
  REPOQUEST_GAME          the R.E.P.O. install, the folder with REPO_Data (default <work>/game)
  REPOQUEST_UNITY_EDITOR  the Unity 2022.3.62f3 install: the folder with Unity.app (macOS) or Editor\\ (Windows)
  REPOQUEST_TOOL          JSON list, the command that runs one of the builder's .NET tools: [<exe>, "--tool"]

Scripts write text with "\\n" line endings on every OS (newline="\\n"; Windows would write "\\r\\n"), and the builder
runs Python in UTF-8 mode, so a Windows build produces the same project as the Mac.
"""

import json
import os
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
WORK = Path(os.environ.get("REPOQUEST_WORK") or ROOT / "work")
GAME = Path(os.environ.get("REPOQUEST_GAME") or WORK / "game")
UNITY_EDITOR = Path(os.environ.get("REPOQUEST_UNITY_EDITOR") or "/Applications/Unity/Hub/Editor/2022.3.62f3")

TOOL_PROJECTS = {"assembly-renamer": "AssemblyRenamer"}


def unity_resources(editor: Path = UNITY_EDITOR) -> Path:
    """The editor's Resources folder: Unity.app/Contents/Resources (macOS) or Editor/Data/Resources (Windows).
    `editor` may also be the Unity.app bundle itself."""
    if editor.suffix == ".app":
        return editor / "Contents" / "Resources"
    mac = editor / "Unity.app" / "Contents" / "Resources"
    return mac if mac.is_dir() else editor / "Editor" / "Data" / "Resources"


def builtin_packages(editor: Path = UNITY_EDITOR) -> Path:
    """Packages that ship inside the editor (com.unity.ugui, ...)"""
    return unity_resources(editor) / "PackageManager" / "BuiltInPackages"


def run_tool(name: str, args: list[str]) -> None:
    """Run one of the .NET tools: through the builder when it set REPOQUEST_TOOL, otherwise built from tools/<Name>
    with the dotnet SDK of this checkout"""
    command = os.environ.get("REPOQUEST_TOOL")
    if command:
        subprocess.run([*json.loads(command), name, *args], check=True)
        return
    project = TOOL_PROJECTS[name]
    dotnet = Path.home() / ".dotnet" / "dotnet"
    tool = ROOT / "tools" / project
    subprocess.run([dotnet, "build", "-c", "Release", "-v", "quiet", "-nologo", tool], check=True, capture_output=True)
    subprocess.run([dotnet, tool / "bin" / "Release" / "net8.0" / f"{project}.dll", *args], check=True)
