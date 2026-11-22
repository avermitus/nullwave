"""Blender: turn the fal Rodin 2.5 enemy models into full-detail game meshes.

Rodin models face -Y with +Z up. For each: join, rotate into game axes (x forward, y side, z up; the
game looks down -z), centre, scale to unit radius, keep every triangle (they are drawn from a GPU
buffer), move UVs into this model's tile of the shared model sheets (u in [2, 3]), save the base
colour and normal textures, export an OBJ for Assimp and render a top view for checking.
Usage: blender -b -P blender_convert_rodin.py -- <rodin_dir> <models_out_dir> <tex_out_dir> <preview_dir>
"""
import sys, os, math
import bpy, bmesh
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT_MODELS, OUT_TEX, PREVIEW = args[:4]
for d in (OUT_MODELS, OUT_TEX, PREVIEW):
    os.makedirs(d, exist_ok=True)

# name, tile (3 x 2 grid, row 0 at the bottom), yaw about z, then pitch about y (degrees)
MODELS = [
    # game slot, source file, tile, yaw, pitch: biblically accurate angels (Rodin 2.5)
    ("hunter", "seraph", 0, 90.0, -90.0),      # six wings spread toward the camera
    ("spiraller", "ophan", 1, 90.0, -90.0),    # wheels within wheels face up
    ("pouncer", "cherub", 2, 90.0, 0.0),       # four-faced beast runs forward
    ("pack", "watcher", 3, 90.0, -90.0),       # the great eye stares up at you
    ("juggernaut", "throne", 4, 90.0, -90.0),  # the burning wheel faces up, arms around its rim
    ("choir", "choir", 5, 90.0, -90.0),        # the many-faced seraph looks up at you
]
COLS, ROWS = 3, 2


def linked_image(material, socket_name):
    """The image feeding a Principled BSDF input (through a normal-map node for Normal)."""
    for node in material.node_tree.nodes:
        if node.type != "BSDF_PRINCIPLED":
            continue
        sock = node.inputs.get(socket_name)
        if sock is None or not sock.links:
            return None
        src = sock.links[0].from_node
        if src.type == "NORMAL_MAP" and src.inputs["Color"].links:
            src = src.inputs["Color"].links[0].from_node
        if src.type == "SEPARATE_COLOR" and src.inputs[0].links:
            src = src.inputs[0].links[0].from_node
        return src.image if src.type == "TEX_IMAGE" else None
    return None


for name, source, tile, yaw, pitch in MODELS:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, f"{source}.glb"))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    obj.data.transform(Matrix.Rotation(math.radians(pitch), 4, "Y") @ Matrix.Rotation(math.radians(yaw), 4, "Z"))
    co = [v.co for v in obj.data.vertices]
    lo = Vector((min(v.x for v in co), min(v.y for v in co), min(v.z for v in co)))
    hi = Vector((max(v.x for v in co), max(v.y for v in co), max(v.z for v in co)))
    radius = max(hi.x - lo.x, hi.y - lo.y) / 2
    obj.data.transform(Matrix.Scale(1.0 / radius, 4) @ Matrix.Translation(-(lo + hi) / 2))
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(obj.data); bm.free()

    mat = next((s.material for s in obj.material_slots if s.material and s.material.use_nodes), None)
    saved = []
    for socket, suffix in (("Base Color", ""), ("Normal", "_normal")):
        img = linked_image(mat, socket) if mat else None
        if img is not None:
            img.save_render(os.path.join(OUT_TEX, f"{name}{suffix}.png"))
            saved.append(socket)

    tx, ty = tile % COLS, tile // COLS
    uv_layer = obj.data.uv_layers.active
    for loop in obj.data.loops:
        u, v = uv_layer.data[loop.index].uv
        u = min(max(u, 0.0), 1.0); v = min(max(v, 0.0), 1.0)
        uv_layer.data[loop.index].uv = (2.0 + (tx + u) / COLS, (ty + v) / ROWS)

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.wm.obj_export(filepath=os.path.join(OUT_MODELS, f"enemy_{name}.obj"), export_selected_objects=True,
                          export_uv=True, export_normals=True, export_materials=False,
                          forward_axis="Y", up_axis="Z", apply_modifiers=True)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x = scene.render.resolution_y = 320
    cam_data = bpy.data.cameras.new("cam"); cam_data.type = "ORTHO"; cam_data.ortho_scale = 2.4
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam); scene.camera = cam
    cam.location = (0, 0, 6); cam.rotation_euler = (0, 0, 0)
    scene.render.filepath = os.path.join(PREVIEW, f"top_{name}.png")
    bpy.ops.render.render(write_still=True)
    print(f"CONVERT {name} tile={tile} tris={len(obj.data.polygons)} radius={radius:.3f} textures={saved}")
