#!/usr/bin/env python3
"""Brings a track's original textures into the Unity project as PNG.

The KTRK meshes have always named the texture `track.1s` gave their material;
the pixels live in the extracted theme archives of the recovery workspace as
DDS (DXT1/DXT3) and PNG. This resolves one against the other and writes what
Unity can actually import.

Unity's texture importer reads PNG but not DDS, and its compressed formats
cover DXT1 and DXT5 but not the DXT3 that 106 of these use, so decoding here
rather than at import time is the only path that keeps every texture. The
decoders come from the recovery workspace's own packer, which is the
implementation the original renderer was verified against.

Nothing is downscaled. `pack_track_textures.py` caps images at 64x64 because a
software rasterizer sampled them; Unity samples the real thing.

    python Tools/AssetPipeline/import_track_textures.py --track village_R01

Writes Assets/_Project/Art/Tracks/<Track>/Textures/*.png and a manifest the
material builder reads for the alpha flag.
"""

import argparse
import json
import os
import struct
import sys
import zlib

# The theme lookup order the original resolution uses: the track's own theme
# first, then the two shared archives.
SHARED_SOURCES = ("theme_{theme}", "theme_common", "track_common")

KTRK_MAGIC = b"KTRK"
KTRK_NAME_BYTES = 96


def load_decoders(workspace):
    """Borrows decode_dds/decode_png from the recovery workspace's packer."""
    pipeline = os.path.join(workspace, "DeveloperTools", "AssetPipeline")
    if not os.path.isdir(pipeline):
        raise SystemExit(f"No asset pipeline at {pipeline}")
    sys.path.insert(0, pipeline)
    import pack_track_textures  # noqa: E402

    return pack_track_textures.decode_dds, pack_track_textures.decode_png


def read_ktrk_textures(path):
    """The distinct texture names a KTRK's meshes reference, in file order."""
    with open(path, "rb") as handle:
        data = handle.read()

    if data[:4] != KTRK_MAGIC:
        raise SystemExit(f"{path} is not a KTRK export")

    version, mesh_count = struct.unpack_from("<II", data, 4)
    if version < 2:
        raise SystemExit(f"{path} is KTRK v{version}; re-export at v2 or later")

    offset = 4 + 4 + 4 + 4 + 4 + 12 + 12
    names = []
    seen = set()
    for _ in range(mesh_count):
        offset += KTRK_NAME_BYTES  # mesh name
        raw = data[offset:offset + KTRK_NAME_BYTES]
        offset += KTRK_NAME_BYTES
        _flags, vertex_count, index_count = struct.unpack_from("<III", data, offset)
        offset += 12
        offset += vertex_count * 20  # x, y, z, u, v
        offset += index_count * 4

        name = raw.split(b"\x00", 1)[0].decode("latin-1").strip()
        if name and name.lower() not in seen:
            seen.add(name.lower())
            names.append(name)
    return names


def index_sources(workspace, shared_directory, theme):
    """Maps a lower-case texture stem to its file, honouring the source order."""
    index = {}
    for pattern in SHARED_SOURCES:
        source = pattern.format(theme=theme)
        root = os.path.join(workspace, shared_directory, source)
        if not os.path.isdir(root):
            continue
        for directory, _, files in os.walk(root):
            for name in sorted(files):
                stem = os.path.splitext(name)[0].lower()
                # First source to carry a name wins, which is what the original
                # resolution does.
                index.setdefault(stem, (source, os.path.join(directory, name)))
    return index


def write_png(path, width, height, rgba):
    """A minimal RGBA8 writer, so the tool needs no imaging library."""
    stride = width * 4
    raw = b"".join(
        b"\x00" + bytes(rgba[y * stride:(y + 1) * stride]) for y in range(height))

    def chunk(tag, payload):
        body = tag + payload
        return (struct.pack(">I", len(payload)) + body
                + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF))

    with open(path, "wb") as handle:
        handle.write(
            b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--track", default="village_R01")
    parser.add_argument(
        "--workspace", default=os.path.join("..", "KartriderDemoPhysics"),
        help="The physics recovery workspace holding the extracted archives.")
    parser.add_argument("--shared-directory", default=os.path.join("Assets", "Tracks", "shared"))
    parser.add_argument(
        "--project", default=".", help="The Unity project root.")
    parser.add_argument(
        "--art-directory", default=None,
        help="Defaults to Assets/_Project/Art/Tracks/<CamelCaseTrack>.")
    arguments = parser.parse_args()

    workspace = os.path.abspath(arguments.workspace)
    project = os.path.abspath(arguments.project)
    theme = arguments.track.split("_")[0]

    if arguments.art_directory:
        art = os.path.join(project, arguments.art_directory)
    else:
        folder = "".join(part.capitalize() for part in arguments.track.split("_"))
        art = os.path.join(project, "Assets", "_Project", "Art", "Tracks", folder)

    ktrk = os.path.join(art, f"track_{arguments.track}.ktrk")
    if not os.path.isfile(ktrk):
        raise SystemExit(f"No KTRK at {ktrk}")

    decode_dds, decode_png = load_decoders(workspace)
    references = read_ktrk_textures(ktrk)
    index = index_sources(workspace, arguments.shared_directory, theme)

    textures = os.path.join(art, "Textures")
    os.makedirs(textures, exist_ok=True)

    entries = []
    unmatched = []
    for name in references:
        found = index.get(name.lower())
        if found is None:
            unmatched.append(name)
            continue
        source, path = found

        with open(path, "rb") as handle:
            data = handle.read()
        try:
            if path.lower().endswith(".dds"):
                width, height, pixels, has_alpha = decode_dds(data)
            elif path.lower().endswith(".png"):
                width, height, pixels, has_alpha = decode_png(data)
            else:
                unmatched.append(name)
                continue
        except ValueError as error:
            sys.stderr.write(f"skipping {name}: {error}\n")
            unmatched.append(name)
            continue

        # `has_alpha` only says the source carried a channel. What decides
        # whether the material needs alpha clipping is whether any texel is
        # actually see-through.
        transparent = any(pixels[i] != 255 for i in range(3, len(pixels), 4))

        write_png(os.path.join(textures, f"{name}.png"), width, height, pixels)
        entries.append({
            "name": name,
            "width": width,
            "height": height,
            "hasAlpha": bool(has_alpha),
            "transparent": transparent,
            "source": source,
            "sourceFile": os.path.basename(path),
        })

    manifest = {
        "track": arguments.track,
        "theme": theme,
        "referenced": len(references),
        "written": len(entries),
        "unmatched": sorted(unmatched),
        "textures": sorted(entries, key=lambda entry: entry["name"].lower()),
    }
    with open(os.path.join(textures, "textures.json"), "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2, ensure_ascii=False)

    print(f"{arguments.track}: {len(entries)}/{len(references)} textures written to {textures}")
    if unmatched:
        print("unmatched (drawn untextured): " + ", ".join(sorted(unmatched)))


if __name__ == "__main__":
    main()
