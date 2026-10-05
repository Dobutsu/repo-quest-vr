"""Check what is about to be published: no game files, no secrets, no personal paths.

Runs on folders, zips (nested zips included) and git history, before a push to the public repository, a GitHub
release or a Thunderstore upload. The idea comes from universal-modder's `um publish check`; the rules are ours.

FAIL  a file byte-identical to a file of the game install; the game's assemblies (Assembly-CSharp, REPO.Game) or
      Unity player data (*.assets, *.resS, globalgamemanagers, level0, global-metadata.dat...); private keys, Steam
      refresh tokens (steam-session*.json, JWTs), API tokens, .env files; a real user's home folder in text
      (/Users/<name>/, C:\\Users\\<name>\\)
WARN  a home folder inside a binary (PDB paths of locally built DLLs); Unity bundles (UnityFS), which must hold only
      our or RepoXR's assets; decompiler fingerprints in code (FUN_/DAT_/sub_ names, ILSpy headers); with --history,
      commit authors that are not GitHub noreply addresses
Usage:
  python scripts/publish_check.py <folder|zip>... [--game <REPO install>] [--no-game]
  python scripts/publish_check.py --history <git repo> [--refs <ref>...]   (default: every ref)
Exit code 1 if anything FAILs. Placeholders such as /Users/<you>/ or C:\\Users\\%USERNAME%\\ pass.
"""

import argparse
import hashlib
import io
import re
import subprocess
import sys
import zipfile
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / "lib"))
from paths import GAME  # noqa: E402

MIN_GAME_FILE = 64  # smaller files match by accident (empty configs, licence stubs)
SKIP_DIRS = {".git", "__pycache__", ".venv", "node_modules"}
GAME_ASSEMBLIES = {"assembly-csharp.dll", "assembly-csharp-firstpass.dll", "repo.game.dll", "repo.game.firstpass.dll"}
PLAYER_DATA = re.compile(r"(?i)^(globalgamemanagers(\.assets)?|level\d+|sharedassets\d+\.assets|resources\.assets"
                         r"|data\.unity3d|global-metadata\.dat|gameassembly\.dll|unityplayer\.dll)$|\.(assets|ress|resource)$")
SECRET_FILES = re.compile(r"(?i)^(steam-session.*\.json|.*\.env|\.env.*|.*signing-key.*\.pem|id_(rsa|ed25519|ecdsa))$")
SECRETS = [
    ("private key", re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----")),
    ("JWT / Steam refresh token", re.compile(rb"\beyA[iI][A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}"
                                             rb"|\beyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}")),
    ("GitHub token", re.compile(rb"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{60,})")),
    ("Anthropic key", re.compile(rb"\bsk-ant-[A-Za-z0-9_-]{20,}")),
    ("OpenAI key", re.compile(rb"\bsk-(?:proj|svcacct|admin)-[A-Za-z0-9_-]{32,}")),
    ("AWS key id", re.compile(rb"\bAKIA[0-9A-Z]{16}\b")),
    ("Steam Web API key", re.compile(rb"(?i)steam[\w ]{0,20}(?:api)?[_ ]?key\W{1,4}[0-9A-F]{32}\b")),
]
DECOMPILED = [
    ("Ghidra name", re.compile(r"\b(?:FUN|DAT|LAB|PTR)_[0-9a-fA-F]{6,}\b")),
    ("IDA name", re.compile(r"\b(?:sub|loc|unk|off|dword|qword)_[0-9A-F]{5,}\b")),
    ("decompiler header", re.compile(r"(?im)^\s*(?://|#)\s*(?:Decompiled with|Decompiler:|ILSpy|dnSpy|JetBrains decompiler)"
                                     r"|^#region Assembly ")),
]
CODE = {".cs", ".py", ".sh", ".ps1", ".c", ".cpp", ".h", ".shader", ".cginc", ".hlsl", ".js", ".ts"}
# Home folders: the name group is checked against placeholders
HOMES = re.compile(r"(?:[A-Za-z]:[\\/]+Users[\\/]+|(?<![\w.])/Users/|(?<![\w.])/home/)([^\\/\s\"'`<>:*?|,;)\]]+)")
PLACEHOLDER_NAMES = {"you", "user", "username", "me", "name", "shared", "public", "default", "runner", "runneradmin",
                     "deck", "steamuser", "crossover", "x", "...", "пользователь"}


class Report:
    def __init__(self):
        self.fails: list[str] = []
        self.warns: list[str] = []

    def fail(self, where: str, why: str):
        self.fails.append(f"FAIL  {where}: {why}")

    def warn(self, where: str, why: str):
        self.warns.append(f"WARN  {where}: {why}")


def game_index(game: Path) -> dict[int, list[Path]]:
    by_size: dict[int, list[Path]] = defaultdict(list)
    for p in game.rglob("*"):
        if p.is_file():
            by_size[p.stat().st_size].append(p)
    return by_size


class GameFiles:
    """Size first, then SHA-256 of the game files of that size (hashed once)"""
    def __init__(self, game: Path | None):
        self.game = game
        self.by_size = game_index(game) if game else {}
        self.hashes: dict[Path, str] = {}

    def match(self, data: bytes) -> Path | None:
        if len(data) < MIN_GAME_FILE or len(data) not in self.by_size:
            return None
        digest = hashlib.sha256(data).hexdigest()
        for p in self.by_size[len(data)]:
            if p not in self.hashes:
                self.hashes[p] = hashlib.sha256(p.read_bytes()).hexdigest()
            if self.hashes[p] == digest:
                return p
        return None


def is_text(data: bytes) -> bool:
    return b"\0" not in data[:8192]


def home_folders(text: str) -> list[tuple[int, str]]:
    """(line, match) for each home folder that is not a placeholder"""
    found = []
    for m in HOMES.finditer(text):
        name = m.group(1)
        if name.lower() in PLACEHOLDER_NAMES or any(c in name for c in "<>$%{}*"):
            continue
        found.append((text.count("\n", 0, m.start()) + 1, m.group(0)))
    return found


def check_file(where: str, name: str, data: bytes, game: GameFiles, report: Report):
    lower = name.lower()
    if (hit := game.match(data)) is not None:
        report.fail(where, f"identical to the game's {hit.relative_to(game.game).as_posix()}")
    if lower in GAME_ASSEMBLIES:
        report.fail(where, "the game's assembly")
    elif PLAYER_DATA.search(name):
        report.fail(where, "Unity player data")
    if SECRET_FILES.match(name):
        report.fail(where, "a secrets file by its name")
    if data[:7] == b"UnityFS":
        report.warn(where, f"Unity bundle ({len(data) >> 10} KB): must hold only our or RepoXR's assets")
    for label, rx in SECRETS:
        if rx.search(data):
            report.fail(where, label)
    if is_text(data):
        text = data.decode("utf-8", errors="replace")
        for line, path in home_folders(text):
            report.fail(f"{where}:{line}", f"personal path {path!r}")
        if Path(lower).suffix in CODE:
            for label, rx in DECOMPILED:
                if found := rx.findall(text):
                    report.warn(where, f"{label} x{len(found)} (e.g. {found[0].strip()!r})")
    else:
        strings = {m.group(0).decode("ascii") for m in re.finditer(rb"[\x20-\x7e]{6,}", data)}
        strings |= {m.group(0).decode("utf-16-le") for m in re.finditer(rb"(?:[\x20-\x7e]\x00){6,}", data)}
        paths = sorted({path for s in strings for _, path in home_folders(s)})
        if paths:
            report.warn(where, f"home folder in a binary: {', '.join(paths[:3])}{' ...' if len(paths) > 3 else ''}")
    if lower.endswith(".zip"):
        check_zip(where, data, game, report)


def check_zip(where: str, data: bytes, game: GameFiles, report: Report):
    try:
        archive = zipfile.ZipFile(io.BytesIO(data))
    except zipfile.BadZipFile:
        report.warn(where, "not a readable zip")
        return
    for info in archive.infolist():
        if not info.is_dir():
            check_file(f"{where}!{info.filename}", Path(info.filename).name, archive.read(info), game, report)


def check_path(target: Path, game: GameFiles, report: Report):
    if target.is_file():
        check_file(target.name, target.name, target.read_bytes(), game, report)
        return
    for p in sorted(target.rglob("*")):
        if p.is_file() and not SKIP_DIRS.intersection(p.relative_to(target).parts):
            check_file(p.relative_to(target).as_posix(), p.name, p.read_bytes(), game, report)


def git(repo: Path, *args: str, data: bytes | None = None) -> bytes:
    return subprocess.run(["git", "-C", str(repo), *args], input=data, check=True, capture_output=True).stdout


def check_history(repo: Path, refs: list[str], game: GameFiles, report: Report):
    """Every blob reachable from the refs (each once), and every commit's author and committer"""
    revs = refs or ["--all"]
    for line in git(repo, "log", "--format=%h %ae %ce", *revs).decode().splitlines():
        commit, *emails = line.split()
        for email in set(emails):
            if not email.endswith("@users.noreply.github.com") and email != "noreply@github.com":
                report.warn(f"commit {commit}", f"author or committer email {email}")
    blobs: dict[str, str] = {}
    for line in git(repo, "rev-list", "--objects", *revs).decode("utf-8", errors="replace").splitlines():
        sha, _, path = line.partition(" ")
        if path:
            blobs.setdefault(sha, path)
    kinds = git(repo, "cat-file", "--batch-check=%(objectname) %(objecttype)",
                data="\n".join(blobs).encode()).decode().split("\n")
    blobs = {sha: blobs[sha] for sha, kind in (k.split() for k in kinds if k) if kind == "blob"}
    for sha, path in blobs.items():
        before = len(report.fails), len(report.warns)
        check_file(path, Path(path).name, git(repo, "cat-file", "blob", sha), game, report)
        if (len(report.fails), len(report.warns)) != before:
            # --find-object lists the commits that add or remove the blob; keep those whose tree holds it
            touched = git(repo, "log", *revs, f"--find-object={sha}", "--format=%h").decode().split()
            commits = [c for c in touched if subprocess.run(["git", "-C", str(repo), "rev-parse", "-q", "--verify",
                                                             f"{c}:{path}"], capture_output=True, text=True
                                                            ).stdout.strip() == sha]
            where = f" [added in {', '.join(commits[:5])}{' ...' if len(commits) > 5 else ''}]"
            for found, start in ((report.fails, before[0]), (report.warns, before[1])):
                found[start:] = [line + where for line in found[start:]]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("targets", nargs="*", type=Path, help="folders, zips or files to check")
    parser.add_argument("--game", type=Path, default=GAME, help="the R.E.P.O. install (default: work/game)")
    parser.add_argument("--no-game", action="store_true", help="skip the comparison with the game's files")
    parser.add_argument("--history", type=Path, help="a git repository: check every blob and commit in its history")
    parser.add_argument("--refs", nargs="+", default=[], help="with --history: the refs to walk (default: all)")
    args = parser.parse_args()
    if not args.targets and not args.history:
        parser.error("nothing to check")

    game = None
    if not args.no_game:
        if not (args.game / "REPO_Data").is_dir():
            parser.error(f"no game install at {args.game} (pass --game or --no-game)")
        game = args.game.resolve()
    files, report = GameFiles(game), Report()
    for target in args.targets:
        check_path(target, files, report)
    if args.history:
        check_history(args.history, args.refs, files, report)

    for line in report.fails + report.warns:
        print(line)
    verdict = "FAIL" if report.fails else "WARN" if report.warns else "PASS"
    print(f"{verdict}: {len(report.fails)} failures, {len(report.warns)} warnings")
    sys.exit(1 if report.fails else 0)


if __name__ == "__main__":
    main()
