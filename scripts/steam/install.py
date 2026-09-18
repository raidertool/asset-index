#!/usr/bin/env python3
"""Install the pinned Linux x64 SteamDepotFS release without authentication."""
import hashlib
from pathlib import Path, PurePosixPath
import shutil
import sys
import tarfile
import tempfile
import urllib.request

RELEASE = "SteamDepotFS-0.2.0-linux-x64"
URL = f"https://github.com/raidertool/SteamDepotFS/releases/download/v0.2.0/{RELEASE}.tar.gz"
SHA256 = "625050772692108513141831c6fc2f7aba93753d55600503282a7a50fd0afa4c"
MAX_ARCHIVE_BYTES = 64 * 1024 * 1024
MAX_EXTRACTED_BYTES = 256 * 1024 * 1024


def unpack(archive: Path, destination: Path) -> None:
    """Reject links, special files and paths outside the release directory."""
    total = 0
    with tarfile.open(archive, "r:gz") as package:
        for member in package:
            path = PurePosixPath(member.name)
            if path.parts[:1] != (RELEASE,) or ".." in path.parts:
                raise ValueError("Invalid archive path")
            if not member.isfile() and not member.isdir():
                raise ValueError("Only ordinary files and directories are allowed")
            target = destination.joinpath(*path.parts[1:])
            if member.isdir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            total += member.size
            if member.size < 0 or total > MAX_EXTRACTED_BYTES:
                raise ValueError("Archive is too large")
            target.parent.mkdir(parents=True, exist_ok=True)
            with package.extractfile(member) as source, target.open("xb") as output:
                shutil.copyfileobj(source, output)
            target.chmod(0o755 if member.mode & 0o111 else 0o644)


def install(destination: Path) -> None:
    if destination.exists() or destination.is_symlink():
        raise ValueError("Installation destination must not exist")
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="steam-depotfs-", dir=destination.parent) as directory:
        archive = Path(directory) / "release.tar.gz"
        digest = hashlib.sha256()
        total = 0
        with urllib.request.urlopen(URL, timeout=30) as response, archive.open("xb") as output:
            while block := response.read(1024 * 1024):
                total += len(block)
                if total > MAX_ARCHIVE_BYTES:
                    raise ValueError("Download is too large")
                digest.update(block)
                output.write(block)
        if digest.hexdigest() != SHA256:
            raise ValueError("Release checksum does not match")
        staged = Path(directory) / "install"
        staged.mkdir()
        unpack(archive, staged)
        executable = staged / "SteamDepotFs"
        if not executable.is_file():
            raise ValueError("Release executable is missing")
        executable.chmod(0o755)
        staged.rename(destination)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit("Usage: install.py DESTINATION")
    try:
        install(Path(sys.argv[1]).absolute())
    except (OSError, ValueError, tarfile.TarError):
        sys.exit("SteamDepotFS installation failed; no unverified executable was installed.")
    print("Installed verified SteamDepotFS v0.2.0.")
