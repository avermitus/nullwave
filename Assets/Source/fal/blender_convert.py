"""Blender: turn the fal (Tripo) enemy models into game meshes.

For each model: join, rotate into game axes (x forward, y side, z up; the game looks down -z), centre,
scale to unit radius, decimate, re-map its UVs into its tile of the shared model sheet, save its base
colour texture, export an OBJ that Assimp loads, and render a top view for checking.
Model-sheet UVs live at u in [2, 3] so the mesh shader can tell them from the material atlas.
Usage: blender -b -P blender_convert.py -- <src_dir> <models_out_dir> <tex_out_dir> <preview_dir>
"""
import sys, os, math
import bpy, bmesh
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT_MODELS, OUT_TEX, PREVIEW = args[:4]
for d in (OUT_MODELS, OUT_TEX, PREVIEW):
    os.makedirs(d, exist_ok=True)

# name, tile index (3 x 2 grid, row 0 at the bottom), yaw about z, then pitch about y (degrees), target faces
MODELS = [
    ("hunter", 0, 45.0, 0.0, 900),
    ("spiraller", 1, None, None, 900),     # None: lay the flattest axis along +z (the halo faces the camera)
    ("pouncer", 2, 45.0, 0.0, 1000),
    ("pack", 3, -20.0, -90.0, 450),        # face turned up toward the camera
    ("juggernaut", 4, -30.0, -35.0, 1100),  # tipped forward so the chest core shows
    ("choir", 5, -30.0, -90.0, 1600),       # the saint's face looks up at you
]
COLS, ROWS = 3, 2


def flattest_axis_rotation(obj):
    """Rotation that sends the axis of least spread (the face normal of a flat object) to +z."""
    verts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    c = sum(verts, Vector()) / len(verts)
    cov = [[0.0] * 3 for _ in range(3)]
    for v in verts:
        d = v - c
        for i in range(3):
            for j in range(3):
                cov[i][j] += d[i] * d[j]
    m = Matrix(cov)
    # power iteration on (trace*I - cov) finds the smallest-variance axis
    shift = Matrix.Identity(3) * (m[0][0] + m[1][1] + m[2][2]) - m
    axis = Vector((0.3, 0.5, 0.8))
    for _ in range(200):
        axis = (shift @ axis).normalized()
    return axis.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4()


for name, tile, yaw, pitch, target in MODELS:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, f"model_{name}.glb"))
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

    # orientation
    if yaw is None:
        rot = flattest_axis_rotation(obj)
    else:
        rot = Matrix.Rotation(math.radians(pitch), 4, "Y") @ Matrix.Rotation(math.radians(yaw), 4, "Z")
    obj.data.transform(rot)

    # centre and unit radius (largest half-extent across x / y)
    co = [v.co for v in obj.data.vertices]
    lo = Vector((min(v.x for v in co), min(v.y for v in co), min(v.z for v in co)))
    hi = Vector((max(v.x for v in co), max(v.y for v in co), max(v.z for v in co)))
    centre = (lo + hi) / 2
    radius = max(hi.x - lo.x, hi.y - lo.y) / 2
    obj.data.transform(Matrix.Scale(1.0 / radius, 4) @ Matrix.Translation(-centre))

    # decimate
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if tris > target:
        mod = obj.modifiers.new("decimate", "DECIMATE")
        mod.ratio = target / tris
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(obj.data); bm.free()

    # base colour texture of the model
    image = None
    for slot in obj.material_slots:
        if slot.material and slot.material.use_nodes:
            for node in slot.material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image is not None:
                    image = node.image
                    break
        if image:
            break
    if image is not None:
        image.save_render(os.path.join(OUT_TEX, f"{name}.png"))

    # UVs into this model's tile of the shared sheet (u offset by 2 marks the model sheet)
    tx, ty = tile % COLS, tile // COLS
    uv_layer = obj.data.uv_layers.active
    for loop in obj.data.loops:
        u, v = uv_layer.data[loop.index].uv
        u = min(max(u, 0.0), 1.0); v = min(max(v, 0.0), 1.0)
        uv_layer.data[loop.index].uv = (2.0 + (tx + u) / COLS, (ty + v) / ROWS)

    # export in game axes (Blender z up = game z up), no axis conversion
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.wm.obj_export(filepath=os.path.join(OUT_MODELS, f"enemy_{name}.obj"), export_selected_objects=True,
                          export_uv=True, export_normals=True, export_materials=False,
                          forward_axis="Y", up_axis="Z", apply_modifiers=True)
    final_tris = len(obj.data.polygons)

    # top-view preview as the game sees it (camera above, looking down -z)
    for uv in obj.data.uv_layers:
        pass
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "OBJECT"
    scene.render.resolution_x = scene.render.resolution_y = 256
    cam_data = bpy.data.cameras.new("cam"); cam_data.type = "ORTHO"; cam_data.ortho_scale = 2.6
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam); scene.camera = cam
    cam.location = (0, 0, 6); cam.rotation_euler = (0, 0, 0)
    arrow = bpy.data.objects.new("fwd", None); scene.collection.objects.link(arrow)
    scene.render.filepath = os.path.join(PREVIEW, f"top_{name}.png")
    bpy.ops.render.render(write_still=True)
    print(f"CONVERT {name} tile={tile} tris {tris}->{final_tris} radius={radius:.3f} texture={'yes' if image else 'NO'}")
