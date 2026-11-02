#!/usr/bin/env python3
"""Package the current worktree, including uncommitted sources, without local secrets."""
import argparse
import hashlib
import io
import json
import subprocess
import tarfile
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", required=True, type=Path, help="Private directory outside the repository")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    output = args.output_dir.resolve()
    if output == root or root in output.parents:
        parser.error("Keep release artifacts outside the repository.")
    output.mkdir(parents=True, exist_ok=True, mode=0o700)
    names = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=root
    ).decode().split("\0")
    examples = {".env.example", ".env.staging.example"}
    excluded = {".git", ".ssh", ".codex", ".agents", "node_modules", "bin", "obj", "dist", "TestResults", "artifacts", "backups"}
    files = {}
    for name in sorted(set(names)):
        if not name:
            continue
        relative = Path(name)
        if any(part in excluded for part in relative.parts):
            continue
        if any(part.startswith(".env") and part not in examples for part in relative.parts):
            continue
        if relative.suffix.lower() in {".dump", ".backup", ".pfx", ".p12", ".key", ".pem"}:
            continue
        path = root / relative
        if path.is_symlink():
            parser.error(f"Review symlink before packaging: {relative}")
        if path.is_file():
            files[name] = path.read_bytes()
    if "SOURCE-MANIFEST.json" in files:
        parser.error("SOURCE-MANIFEST.json is reserved for the release manifest.")
    required = {"backend/Trade.Api/Dockerfile", "frontend/Dockerfile", "compose.production.yaml", "compose.staging.yaml", "Caddyfile", "AGENTS.md"}
    if not required.issubset(files):
        parser.error("Required deployment sources are missing from the worktree.")
    manifest = {name: {"sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)} for name, data in files.items()}
    manifest_bytes = (json.dumps(manifest, sort_keys=True, indent=2) + "\n").encode()
    release_id = hashlib.sha256(manifest_bytes).hexdigest()[:16]
    archive = output / f"trade-system-staging-{release_id}.tar.gz"
    try:
        with tarfile.open(archive, "x:gz") as tar:
            for name, data in {**files, "SOURCE-MANIFEST.json": manifest_bytes}.items():
                info = tarfile.TarInfo("trade-system/" + name)
                info.size, info.mode, info.mtime = len(data), 0o644, 0
                tar.addfile(info, io.BytesIO(data))
    except FileExistsError:
        parser.error("This release archive already exists; refusing to overwrite it.")
    archive.chmod(0o600)
    checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
    sidecar = Path(str(archive) + ".sha256")
    sidecar.write_text(f"{checksum}  {archive.name}\n", encoding="utf-8")
    sidecar.chmod(0o600)
    print(json.dumps({"releaseId": release_id, "archive": str(archive), "sha256": checksum, "sourceFiles": len(files)}, indent=2))


if __name__ == "__main__":
    main()
