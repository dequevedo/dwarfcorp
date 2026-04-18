"""Regenerate DwarfCorpContent/Content.mgcb by scanning the filesystem.

The XNA .contentproj was deleted during cleanup, so we derive MGCB build
entries directly from file extensions. PremultiplyAlpha overrides from the
old .contentproj are preserved via a hard-coded hint list.
"""
import os

ROOT = "D:/Workspace/dwarfcorp/DwarfCorp/Content"
OUT = os.path.join(ROOT, "Content.mgcb")

# Copy-paste from the original .contentproj — textures that set PremultiplyAlpha=False.
NO_PREMULT = {
    "newgui/pointers.png",
    "Sky/day_sky.dds",
    "Sky/night_sky.dds",
    "Terrain/water_normal.jpg",
}

HEADER = """\
#----------------------------- Global Properties ----------------------------#

/outputDir:../../ContentMGCB/bin/Content
/intermediateDir:../../ContentMGCB/obj/Content
/platform:DesktopGL
/config:
/profile:Reach
/compress:False

#-------------------------------- References --------------------------------#

/reference:../../ContentMGCB/lib/ContentPipeline.dll

#---------------------------------- Content ---------------------------------#
"""


def entry(path: str) -> list[str] | None:
    ext = os.path.splitext(path)[1].lower()
    name = path.replace("\\", "/")

    if ext in {".png", ".jpg", ".jpeg", ".dds", ".bmp"}:
        # .bmp is never an asset for us (those are font sources), skip.
        if ext == ".bmp":
            return None
        lines = [f"#begin {name}",
                 "/importer:TextureImporter",
                 "/processor:TextureProcessor"]
        if name in NO_PREMULT:
            lines.append("/processorParam:PremultiplyAlpha=False")
        lines.append(f"/build:{name}")
        return lines

    if ext == ".fx":
        return [f"#begin {name}",
                "/importer:EffectImporter",
                "/processor:FxcEffectProcessor",
                f"/build:{name}"]

    if ext == ".fbx":
        return [f"#begin {name}",
                "/importer:FbxImporter",
                "/processor:ModelProcessor",
                f"/build:{name}"]

    if ext == ".wav":
        # WAVs under Music/ are long loops/intros — load as Song, not SoundEffect.
        if name.startswith("Music/"):
            return [f"#begin {name}",
                    "/importer:WavImporter",
                    "/processor:SongProcessor",
                    f"/build:{name}"]
        return [f"#begin {name}",
                "/importer:WavImporter",
                "/processor:SoundEffectProcessor",
                f"/build:{name}"]

    if ext == ".mp3":
        return [f"#begin {name}",
                "/importer:Mp3Importer",
                "/processor:SongProcessor",
                f"/build:{name}"]

    if ext == ".ogg":
        # Leave out — FNA reads ogg directly at runtime from file system,
        # MGCB's OggImporter is for MonoGame's SongProcessor which FNA rejects.
        return None

    return None


def main():
    entries = []
    count_by_ext: dict[str, int] = {}
    for dirpath, _, files in os.walk(ROOT):
        for f in files:
            abs_path = os.path.join(dirpath, f)
            rel = os.path.relpath(abs_path, ROOT).replace("\\", "/")
            # Skip our own generated files.
            if rel.startswith(("bin/", "obj/", "Content.mgcb")):
                continue
            e = entry(rel)
            if e is None:
                continue
            ext = os.path.splitext(f)[1].lower()
            count_by_ext[ext] = count_by_ext.get(ext, 0) + 1
            entries.append("\n".join(e))

    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(HEADER)
        f.write("\n\n")
        f.write("\n\n".join(entries))
        f.write("\n")

    total = sum(count_by_ext.values())
    print(f"wrote {total} entries to {OUT}")
    for ext, n in sorted(count_by_ext.items()):
        print(f"  {ext:6s} {n}")


if __name__ == "__main__":
    main()
