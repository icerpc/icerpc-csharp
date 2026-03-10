#!/usr/bin/env python3
# Copyright (c) ZeroC, Inc.

"""Synchronize vendored Slice files (*.ice, *.slice) based on slice.toml.

Usage:
    python tools/sync-slice.py           # sync files from upstream
    python tools/sync-slice.py --verify  # verify files match upstream (for CI)

slice.toml format
-----------------

The file contains one or more [source.<name>] sections.  Each section describes
an upstream git repository and the files to vendor from it.

    [source.<name>]
    repo    = "<git-clone-url>"      # required — upstream repository URL
    rev     = "<tag|branch|commit>"  # required — git rev to check out
    source  = "<subdir>"             # optional — subdirectory within the repo
                                     #   to treat as the file root (default: repo root)
    dest    = "<subdir>"             # required — local directory to copy files into,
                                     #   relative to the repository root
    include = ["<glob>", ...]        # required — glob patterns matched against paths
                                     #   relative to 'source'; supports ** for recursion

When multiple sources write to the same dest, their include patterns should not
overlap.  The script warns when the same destination path is matched by more
than one source.

Sync mode (default) clones every source, removes stale .ice/.slice files from
dest that are no longer in the manifest, and copies the matched files.

Verify mode (--verify) clones every source and checks that the local files in
dest are identical to upstream.  It reports missing, modified, and extra files
and exits with a non-zero status on any mismatch.
"""

from __future__ import annotations

import argparse
import filecmp
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
import tomllib


def find_repo_root() -> Path:
    """Return the root of the git repository."""
    result = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        capture_output=True,
        text=True,
        check=True,
    )
    return Path(result.stdout.strip())


def collect_files(base_dir: Path, patterns: list[str]) -> list[str]:
    """Return sorted relative paths under *base_dir* matching any glob pattern."""
    matches: set[str] = set()
    for pattern in patterns:
        for path in base_dir.glob(pattern):
            if path.is_file():
                matches.add(str(path.relative_to(base_dir)))
    return sorted(matches)


def fetch_source(name: str, config: dict, tmp_base: Path) -> tuple[Path, list[str]]:
    """Clone a source and return (source_dir, matched_relpaths)."""
    repo = config["repo"]
    rev = config["rev"]
    source = config.get("source", "")
    patterns = config["include"]

    clone_dir = tmp_base / name
    print(f"[{name}] Fetching {repo} @ {rev} ...")
    subprocess.run(["git", "init", str(clone_dir)], capture_output=True, text=True, check=True)
    subprocess.run(
        ["git", "-C", str(clone_dir), "remote", "add", "origin", repo],
        capture_output=True, text=True, check=True,
    )
    result = subprocess.run(
        ["git", "-C", str(clone_dir), "fetch", "--depth", "1", "origin", rev],
        capture_output=True, text=True,
    )
    if result.returncode != 0:
        sys.exit(f"Error: failed to fetch {repo} @ {rev}\n{result.stderr.strip()}")
    subprocess.run(
        ["git", "-C", str(clone_dir), "checkout", "FETCH_HEAD"],
        capture_output=True, text=True, check=True,
    )

    source_dir = clone_dir / source if source else clone_dir
    if not source_dir.is_dir():
        sys.exit(f"Error: source '{source}' not found in {repo}")

    files = collect_files(source_dir, patterns)
    print(f"  Matched {len(files)} file(s)")
    return source_dir, files


def sync(sources: dict, repo_root: Path) -> bool:
    """Download all sources, clean stale files, and copy fresh ones."""
    # Maps dest-relative path -> absolute source path.
    expected: dict[str, Path] = {}

    with tempfile.TemporaryDirectory(prefix="sync-slice-") as tmp:
        tmp_base = Path(tmp)
        for name, config in sources.items():
            dest = config["dest"]
            source_dir, files = fetch_source(name, config, tmp_base)
            for relpath in files:
                dest_rel = os.path.join(dest, relpath)
                if dest_rel in expected:
                    print(f"  Warning: {dest_rel} matched by multiple sources", file=sys.stderr)
                expected[dest_rel] = source_dir / relpath

        if not expected:
            print("No files matched any patterns.", file=sys.stderr)
            return False

        # Remove stale .ice/.slice files that are no longer in the manifest.
        dest_dirs = {config["dest"] for config in sources.values()}
        for dest_dir in dest_dirs:
            abs_dest = repo_root / dest_dir
            if not abs_dest.is_dir():
                continue
            for ext in ("*.ice", "*.slice"):
                for existing in abs_dest.rglob(ext):
                    rel = str(existing.relative_to(repo_root))
                    if rel not in expected:
                        print(f"  Removing stale: {rel}")
                        existing.unlink()

        # Copy files from upstream.
        for dest_rel, src_abs in sorted(expected.items()):
            dst = repo_root / dest_rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src_abs, dst)

    print(f"\nSynced {len(expected)} file(s).")
    return True


def verify(sources: dict, repo_root: Path) -> bool:
    """Verify that committed files match what slice.lock specifies."""
    issues: list[str] = []
    expected: dict[str, Path] = {}

    with tempfile.TemporaryDirectory(prefix="sync-slice-") as tmp:
        tmp_base = Path(tmp)
        for name, config in sources.items():
            dest = config["dest"]
            source_dir, files = fetch_source(name, config, tmp_base)
            for relpath in files:
                dest_rel = os.path.join(dest, relpath)
                expected[dest_rel] = source_dir / relpath

                dst = repo_root / dest_rel
                if not dst.exists():
                    issues.append(f"Missing: {dest_rel}")
                elif not filecmp.cmp(str(source_dir / relpath), str(dst), shallow=False):
                    issues.append(f"Modified: {dest_rel}")

        # Check for extra .ice/.slice files not tracked by any source.
        dest_dirs = {config["dest"] for config in sources.values()}
        for dest_dir in dest_dirs:
            abs_dest = repo_root / dest_dir
            if not abs_dest.is_dir():
                continue
            for ext in ("*.ice", "*.slice"):
                for existing in abs_dest.rglob(ext):
                    rel = str(existing.relative_to(repo_root))
                    if rel not in expected:
                        issues.append(f"Extra: {rel}")

    if issues:
        print("\nVerification FAILED:")
        for issue in sorted(issues):
            print(f"  {issue}")
        return False

    print(f"\nVerification passed: {len(expected)} file(s) match.")
    return True


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Synchronize vendored Slice/Ice files based on slice.toml.",
    )
    parser.add_argument(
        "--verify",
        action="store_true",
        help="Verify committed files match slice.toml (for CI).",
    )
    parser.add_argument(
        "--config",
        default=None,
        help="Path to slice.toml (default: <repo-root>/slice.toml).",
    )
    args = parser.parse_args()

    repo_root = find_repo_root()
    config_path = Path(args.config) if args.config else repo_root / "slice.toml"

    if not config_path.exists():
        sys.exit(f"Error: {config_path} not found.")

    with open(config_path, "rb") as f:
        config = tomllib.load(f)

    sources = config.get("source", {})
    if not sources:
        sys.exit("Error: no [source.*] sections found in slice.toml.")

    required_keys = ("repo", "rev", "dest", "include")
    for name, source in sources.items():
        missing = [k for k in required_keys if k not in source]
        if missing:
            sys.exit(f"Error: [source.{name}] is missing required key(s): {', '.join(missing)}")
        if not isinstance(source["include"], list) or not source["include"]:
            sys.exit(f"Error: [source.{name}] 'include' must be a non-empty array.")

    ok = verify(sources, repo_root) if args.verify else sync(sources, repo_root)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
