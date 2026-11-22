"""Blender: render top / front / side views of each fal model so its orientation can be checked.
Usage: blender -b -P blender_views.py -- <out_dir> <model.glb> [<model.glb> ...]"""
import sys, os, math
import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
out_dir, models = args[0], args[1:]
os.makedirs(out_dir, exist_ok=True)

for path in models:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
    tris = 0
    for o in meshes:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
        tris += sum(len(p.vertices) - 2 for p in o.data.polygons)
    center = (lo + hi) / 2; size = max(hi - lo)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x = scene.render.resolution_y = 256
    cam_data = bpy.data.cameras.new("cam"); cam_data.type = "ORTHO"; cam_data.ortho_scale = size * 1.2
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam); scene.camera = cam
    name = os.path.splitext(os.path.basename(path))[0]
    # top: looking down -Z (game camera); front: looking along +Y from -Y; side: looking along -X from +X
    views = {
        "top": ((center.x, center.y, center.z + size * 2), (0, 0, 0)),
        "front": ((center.x, center.y - size * 2, center.z), (math.pi / 2, 0, 0)),
        "side": ((center.x + size * 2, center.y, center.z), (math.pi / 2, 0, math.pi / 2)),
    }
    for v, (loc, rot) in views.items():
        cam.location = loc; cam.rotation_euler = rot
        scene.render.filepath = os.path.join(out_dir, f"{name}_{v}.png")
        bpy.ops.render.render(write_still=True)
    print(f"VIEWS {name} tris={tris} size=({hi.x-lo.x:.2f},{hi.y-lo.y:.2f},{hi.z-lo.z:.2f})")
