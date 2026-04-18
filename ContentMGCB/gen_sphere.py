"""Generate a low-poly sphere and export as binary FBX."""
import bpy
import sys

dst = sys.argv[-1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=1.0)
bpy.ops.export_scene.fbx(
    filepath=dst,
    use_selection=False,
    path_mode="COPY",
    embed_textures=False,
    bake_space_transform=True,
)
print("wrote", dst)
