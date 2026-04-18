"""Convert the legacy FBX sphereLowPoly via Blender headless."""
import bpy
import sys

src = sys.argv[-2]
dst = sys.argv[-1]

# Clear default scene
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
bpy.ops.export_scene.fbx(filepath=dst, path_mode="COPY", embed_textures=False)
print("converted", src, "->", dst)
