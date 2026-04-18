"""Convert XNA 4.0 .contentproj to MonoGame .mgcb.

Reads ../DwarfCorpContent/DwarfCorpXNAContent.contentproj and emits Content.mgcb
in the current directory, mapping EffectProcessor -> FxcEffectProcessor so FNA
can load the compiled shader .xnb.
"""

import os
import xml.etree.ElementTree as ET

NS = {"ms": "http://schemas.microsoft.com/developer/msbuild/2003"}
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CONTENTPROJ = os.path.join(REPO, "DwarfCorpContent", "DwarfCorpXNAContent.contentproj")
OUT = os.path.join(REPO, "DwarfCorpContent", "Content.mgcb")

HEADER = """
#----------------------------- Global Properties ----------------------------#

/outputDir:../ContentMGCB/bin/Content
/intermediateDir:../ContentMGCB/obj/Content
/platform:DesktopGL
/config:
/profile:Reach
/compress:False

#-------------------------------- References --------------------------------#

/reference:../ContentMGCB/lib/ContentPipeline.dll

#---------------------------------- Content ---------------------------------#

""".lstrip()


def main():
    tree = ET.parse(CONTENTPROJ)
    root = tree.getroot()
    lines = [HEADER]
    n = 0
    for compile_node in root.iter("{http://schemas.microsoft.com/developer/msbuild/2003}Compile"):
        include = compile_node.attrib["Include"].replace("\\", "/")
        importer = compile_node.find("ms:Importer", NS)
        processor = compile_node.find("ms:Processor", NS)
        imp = importer.text if importer is not None else None
        proc = processor.text if processor is not None else None

        if proc == "EffectProcessor":
            proc = "FxcEffectProcessor"

        params = []
        for child in compile_node:
            tag = child.tag.split("}", 1)[-1]
            if tag.startswith("ProcessorParameters_"):
                key = tag[len("ProcessorParameters_") :]
                params.append(f"/processorParam:{key}={child.text}")

        lines.append(f"#begin {include}")
        if imp:
            lines.append(f"/importer:{imp}")
        if proc:
            lines.append(f"/processor:{proc}")
        for p in params:
            lines.append(p)
        lines.append(f"/build:{include}")
        lines.append("")
        n += 1

    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print(f"wrote {n} entries to {OUT}")


if __name__ == "__main__":
    main()
