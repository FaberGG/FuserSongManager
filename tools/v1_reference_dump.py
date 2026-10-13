"""Dump the seven song fields as read by the v1 parser, for parity checks against v2.

Usage:
    python v1_reference_dump.py <v1 src dir> <paks dir> <output.json>

Requires PyPAKParser==1.2.0 (the v1 dependency). Output is consumed by
`FuserSongManager.Tools compare <output.json> <paks dir>`.
"""
import glob
import json
import os
import sys


def main():
    if len(sys.argv) != 4:
        print(__doc__)
        return 1
    v1_src, paks_dir, output = sys.argv[1:]
    sys.path.insert(0, os.path.abspath(v1_src))
    import loader  # v1 module, imported from v1_src

    results = []
    for pak in sorted(glob.glob(os.path.join(glob.escape(paks_dir), "**", "*.pak"), recursive=True), key=str.lower):
        short_name = os.path.basename(pak)[:-6]
        try:
            song = loader.get_song_info(pak, True, False)
            results.append({
                "short_name": short_name,
                "title": song.song_name,
                "artist": song.artist,
                "year": song.year,
                "genre": song.genre.name,
                "key": song.key.name,
                "mode": song.mode.name,
                "bpm": song.bpm,
            })
        except Exception as ex:
            results.append({"short_name": short_name, "error": f"{type(ex).__name__}: {ex}"})

    with open(output, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2, ensure_ascii=False)
    print(f"{len(results)} paks written to {output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
