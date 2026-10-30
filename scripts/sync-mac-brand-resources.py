#!/usr/bin/env python3
"""Copy approved, committed Lingrove assets; package unchanged PNGs as Windows ICO frames.

The source Git checkout is read-only. No renderer, image generator or network is used.
"""
import argparse
import hashlib
import pathlib
import struct
import subprocess

REFERENCE = "3b53402f2cbc196b0398d133177fd80006d165b2"
ROOT = pathlib.Path(__file__).resolve().parents[1]
COPIES = {
    "assets/branding/lingrove-logo.svg": "resources/Branding/lingrove-logo.svg",
    "assets/branding/lingrove-activation-indicator.svg": "resources/Branding/lingrove-activation-indicator.svg",
    "Lingrove/Assets.xcassets/LingroveLogo.imageset/LingroveLogo@3x.png": "src/Cotabby.App/Assets/LingroveLogo.png",
    ".github/assets/readme/logo.png": "docs/images/logo.png",
}


def read(repo, path):
    return subprocess.check_output(["git", "-C", str(repo), "show", f"{REFERENCE}:{path}"])


def icon(repo):
    # ICO supports PNG-compressed frames since Vista. Keep approved pixels byte-for-byte;
    # the directory merely describes each frame's dimensions and offset.
    frames = [(size, read(repo, f"Lingrove/Assets.xcassets/AppIcon.appiconset/{size}.png"))
              for size in (16, 32, 64, 128, 256)]
    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = len(header) + 16 * len(frames)
    directory = bytearray()
    for size, data in frames:
        if data[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", data[16:24]) != (size, size):
            raise ValueError("Unexpected approved icon frame")
        directory += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    return header + directory + b"".join(data for _, data in frames)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mac-repo", required=True, type=pathlib.Path)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    outputs = {target: read(args.mac_repo, source) for source, target in COPIES.items()}
    outputs["src/Cotabby.App/Assets/Lingrove.ico"] = icon(args.mac_repo)
    for relative, data in outputs.items():
        target = ROOT / relative
        if args.verify:
            if not target.is_file() or target.read_bytes() != data:
                raise ValueError(f"Brand resource differs from the committed reference: {relative}")
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        print(f"{relative} {hashlib.sha256(data).hexdigest()}")


if __name__ == "__main__":
    main()
