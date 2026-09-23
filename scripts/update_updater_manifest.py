#!/usr/bin/env python3
# Copyright (C) 2026 NVDS5 Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

ASSETS = {
    "win-x64": lambda nightly, version: (
        "nvds5-nightly-win-x64.zip" if nightly else f"nvds5-{version}-win-x64.zip"
    ),
    "linux-x64": lambda nightly, version: (
        "nvds5-nightly-linux-x64.tar.gz" if nightly else f"nvds5-{version}-linux-x64.tar.gz"
    ),
    "osx-x64": lambda nightly, version: (
        "nvds5-nightly-osx-x64.zip" if nightly else f"nvds5-{version}-osx-x64.zip"
    ),
}

def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--channel", choices=("stable", "nightly"), required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--sha", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--assets-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    data = {"schema": 1}
    if args.output.exists():
        data = json.loads(args.output.read_text(encoding="utf-8"))

    nightly = args.channel == "nightly"
    assets = {}
    for rid, name_factory in ASSETS.items():
        name = name_factory(nightly, args.version)
        path = args.assets_dir / name
        if not path.is_file():
            raise SystemExit(f"missing updater asset: {path}")

        assets[rid] = {
            "name": name,
            "url": f"https://github.com/NVDEMU/nvd123-ps5/releases/download/{args.tag}/{name}",
            "size": path.stat().st_size,
            "sha256": sha256(path),
        }

    data[args.channel] = {
        "version": args.version,
        "sha": args.sha,
        "tag": args.tag,
        "assets": assets,
    }
    args.output.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
