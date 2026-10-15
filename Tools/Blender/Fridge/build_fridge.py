"""Builds a realistic bottom-freezer stainless fridge and exports FBX + textures.
Run: blender -b --factory-startup --python build_fridge.py -- <out_dir> <preview_png>
Blender axes: X = width, -Y = front, Z = up. Origin = floor, centre of footprint.
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_DIR, PREVIEW = argv[0], argv[1]
os.makedirs(OUT_DIR, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ---------------------------------------------------------------- dimensions
W, D, H = 0.70, 0.65, 2.00          # overall width / depth / height
DOOR_T = 0.055                      # door thickness
FRONT = -D / 2                      # front plane of doors (y)
BODY_FRONT = FRONT + DOOR_T         # front plane of cabinet body
GAP = 0.006
KICK = 0.09                         # toe-kick height
SPLIT = 0.74                        # z of freezer/fridge split
HINGE_X = -W / 2

# ---------------------------------------------------------------- textures
def save_png(name, arr):
    h, w = arr.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=True)
    rgba = np.ones((h, w, 4), np.float32)
    rgba[..., :3] = arr[..., None] if arr.ndim == 2 else arr
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = os.path.join(OUT_DIR, name + ".png")
    img.file_format = "PNG"
    img.save()
    return img

rng = np.random.default_rng(7)
N = 1024
def blur_v(a, k):  # box blur along rows (v axis), wrapping -> long vertical streaks
    c = np.cumsum(np.concatenate([a[-k:], a, a[:k]], 0), 0)
    return (c[2 * k:] - c[:-2 * k])[:a.shape[0]] / (2 * k)
streak = blur_v(rng.random((N, N)), 90)
streak = (streak - streak.mean()) / streak.std()
cols = np.convolve(rng.random(N + 8), np.ones(5) / 5, "same")[4:N + 4]
cols = (cols - cols.mean()) / cols.std()
brushed = np.clip(0.90 + 0.035 * streak + 0.025 * cols[None, :], 0, 1)
save_png("T_Fridge_Brushed_Albedo", np.stack([brushed, brushed, brushed * 1.01], -1).clip(0, 1).astype(np.float32))
# URP metallic map: R = metallic, A = smoothness (brushed streaks vary the gloss)
mg = np.ones((N, N, 4), np.float32)
mg[..., 3] = np.clip(0.70 + 0.06 * streak + 0.03 * cols[None, :], 0, 1)
img = bpy.data.images.new("T_Fridge_Brushed_MetalGloss", N, N, alpha=True)
img.pixels.foreach_set(mg.ravel())
img.filepath_raw = os.path.join(OUT_DIR, "T_Fridge_Brushed_MetalGloss.png")
img.file_format = "PNG"; img.save()

# ---------------------------------------------------------------- materials
def mat(name, color, metal=0.0, rough=0.5, emit=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = 4.0
    return m

M_STEEL = mat("M_Fridge_Steel", (0.72, 0.72, 0.71), 1.0, 0.32)
M_SIDE = mat("M_Fridge_Side", (0.42, 0.43, 0.44), 0.8, 0.45)
M_CHROME = mat("M_Fridge_Handle", (0.85, 0.85, 0.85), 1.0, 0.12)
M_RUBBER = mat("M_Fridge_Gasket", (0.02, 0.02, 0.02), 0.0, 0.85)
M_PLASTIC = mat("M_Fridge_Plastic", (0.03, 0.03, 0.035), 0.0, 0.55)
M_GLASS = mat("M_Fridge_DisplayGlass", (0.005, 0.005, 0.006), 0.0, 0.05)
M_GLOW = mat("M_Fridge_DisplayGlow", (0.6, 0.9, 1.0), 0.0, 0.4, emit=(0.45, 0.8, 1.0))

# ---------------------------------------------------------------- helpers
def box_uv(obj, scale=1.0):
    """World-aligned box mapping; brushed streaks (texture v) run vertically on doors."""
    me = obj.data
    bm = bmesh.new(); bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()
    mw = obj.matrix_world
    for f in bm.faces:
        n = (mw.to_3x3() @ f.normal)
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            p = mw @ l.vert.co
            if ax == 0:   u, v = p.y, p.z
            elif ax == 1: u, v = p.x, p.z
            else:         u, v = p.x, p.y
            l[uv].uv = (u * scale, v * scale)
    bm.to_mesh(me); bm.free()

def cube(name, x0, x1, y0, y1, z0, z1, material, bevel=0.0, seg=3, parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.active_object
    o.name = o.data.name = name
    o.location = ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
    o.scale = (x1 - x0, y1 - y0, z1 - z0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(material)
    if bevel > 0:
        b = o.modifiers.new("Bevel", "BEVEL")
        b.width, b.segments, b.limit_method = bevel, seg, "ANGLE"
        b.harden_normals = True
    box_uv(o)
    if parent: set_parent(o, parent)
    return o

def cyl(name, p0, p1, r, material, bevel=0.0, parent=None, verts=24):
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d.length,
                                        location=(p0 + p1) / 2)
    o = bpy.context.active_object
    o.name = o.data.name = name
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    if bevel > 0:
        b = o.modifiers.new("Bevel", "BEVEL")
        b.width, b.segments, b.limit_method = bevel, 3, "ANGLE"
    box_uv(o)
    if parent: set_parent(o, parent)
    return o

def set_parent(child, parent):
    bpy.context.view_layer.update()
    mw = child.matrix_world.copy()
    child.parent = parent
    child.matrix_world = mw

def empty(name, loc, parent=None):
    o = bpy.data.objects.new(name, None)
    scene.collection.objects.link(o)
    o.location = loc
    if parent: set_parent(o, parent)
    return o

# ---------------------------------------------------------------- build
root = empty("Fridge", (0, 0, 0))

# cabinet body (sides slightly inset from doors, back flush)
body = cube("Body", -W / 2 + 0.002, W / 2 - 0.002, BODY_FRONT, D / 2, KICK - 0.01, H - 0.004,
            M_SIDE, bevel=0.006, parent=root)
# dark front face of the body visible in door gaps
cube("Body_FrontTrim", -W / 2 + 0.004, W / 2 - 0.004, BODY_FRONT - 0.002, BODY_FRONT + 0.004,
     KICK, H - 0.006, M_PLASTIC, parent=root)

# toe-kick grille with horizontal ribs
cube("ToeKick", -W / 2 + 0.03, W / 2 - 0.03, BODY_FRONT + 0.02, D / 2 - 0.05, 0.0, KICK,
     M_PLASTIC, parent=root)
grille = cube("ToeKick_Grille", -W / 2 + 0.02, W / 2 - 0.02, BODY_FRONT + 0.012, BODY_FRONT + 0.03,
              0.012, KICK - 0.004, M_PLASTIC, bevel=0.003, parent=root)
for i in range(5):
    z = 0.024 + i * 0.012
    cube(f"ToeKick_Rib_{i}", -W / 2 + 0.05, W / 2 - 0.05, BODY_FRONT + 0.006, BODY_FRONT + 0.014,
         z, z + 0.005, M_RUBBER, parent=grille)
# levelling feet
for sx in (-1, 1):
    cyl(f"Foot_{'L' if sx < 0 else 'R'}", (sx * (W / 2 - 0.06), BODY_FRONT + 0.05, 0.0),
        (sx * (W / 2 - 0.06), BODY_FRONT + 0.05, 0.012), 0.016, M_PLASTIC, parent=root)

def door(name, z0, z1):
    # pivot sits on the hinge axis so the door can swing open in Unity (rotate local Z in Blender / Y in Unity)
    pivot = empty(name, (HINGE_X, FRONT + DOOR_T / 2, z0), parent=root)
    panel = cube(name + "_Panel", -W / 2, W / 2, FRONT, FRONT + DOOR_T - 0.004, z0, z1,
                 M_STEEL, bevel=0.012, seg=5, parent=pivot)
    # rubber gasket frame on the inside face of the door
    t = 0.018
    for n_, a in (("Top", (-W / 2 + 0.01, W / 2 - 0.01, z1 - 0.01 - t, z1 - 0.01)),
                  ("Bottom", (-W / 2 + 0.01, W / 2 - 0.01, z0 + 0.01, z0 + 0.01 + t)),
                  ("Left", (-W / 2 + 0.01, -W / 2 + 0.01 + t, z0 + 0.01, z1 - 0.01)),
                  ("Right", (W / 2 - 0.01 - t, W / 2 - 0.01, z0 + 0.01, z1 - 0.01))):
        cube(f"{name}_Gasket_{n_}", a[0], a[1], FRONT + DOOR_T - 0.006, FRONT + DOOR_T + 0.002,
             a[2], a[3], M_RUBBER, bevel=0.004, parent=pivot)
    return pivot, panel

fridge_door, _ = door("Door_Fridge", SPLIT + GAP / 2, H - 0.002)
freezer_door, _ = door("Door_Freezer", KICK + 0.004, SPLIT - GAP / 2)

# bar handles (stainless, on the side opposite the hinges)
HY = FRONT - 0.048   # handle bar centre plane
hx = W / 2 - 0.065
cyl("Handle_Fridge_Bar", (hx, HY, SPLIT + 0.10), (hx, HY, SPLIT + 0.82), 0.0125, M_CHROME,
    bevel=0.004, parent=fridge_door)
for z in (SPLIT + 0.14, SPLIT + 0.78):
    cyl(f"Handle_Fridge_Post_{z:.2f}", (hx, FRONT + 0.002, z), (hx, HY, z), 0.009, M_CHROME,
        parent=fridge_door)
hz = SPLIT - 0.075
cyl("Handle_Freezer_Bar", (-W / 2 + 0.11, HY, hz), (W / 2 - 0.11, HY, hz), 0.0125, M_CHROME,
    bevel=0.004, parent=freezer_door)
for x in (-W / 2 + 0.15, W / 2 - 0.15):
    cyl(f"Handle_Freezer_Post_{x:+.2f}", (x, FRONT + 0.002, hz), (x, HY, hz), 0.009, M_CHROME,
        parent=freezer_door)

# smart display panel on the fridge door
dz0, dz1 = 1.52, 1.60
dx0, dx1 = -0.10, 0.10
cube("Display_Glass", dx0, dx1, FRONT - 0.0025, FRONT + 0.002, dz0, dz1, M_GLASS, bevel=0.004,
     parent=fridge_door)

def label(txt, x, z, size):
    bpy.ops.object.text_add(location=(x, FRONT - 0.0028, z), rotation=(math.radians(90), 0, 0))
    t = bpy.context.active_object
    t.data.body = txt
    t.data.size = size
    t.data.align_x, t.data.align_y = "CENTER", "CENTER"
    t.data.extrude = 0.0003
    bpy.ops.object.convert(target="MESH")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    t.name = t.data.name = "Display_" + txt.replace("°", "deg").replace("-", "m")
    t.data.materials.clear(); t.data.materials.append(M_GLOW)
    box_uv(t)
    set_parent(t, fridge_door)
    return t

label("4°", -0.045, (dz0 + dz1) / 2, 0.042)
label("-18°", 0.045, (dz0 + dz1) / 2, 0.034)
# small status LED strip
cube("Display_LED", dx0 + 0.02, dx1 - 0.02, FRONT - 0.003, FRONT - 0.0026, dz0 + 0.006, dz0 + 0.009,
     M_GLOW, parent=fridge_door)

# hinge covers (left side, top + between doors)
cube("Hinge_Top", HINGE_X, HINGE_X + 0.09, FRONT + 0.005, BODY_FRONT + 0.03, H - 0.004, H + 0.008,
     M_PLASTIC, bevel=0.004, parent=root)
cube("Hinge_Mid", HINGE_X - 0.003, HINGE_X + 0.03, FRONT + 0.012, BODY_FRONT, SPLIT - 0.003,
     SPLIT + 0.003, M_PLASTIC, parent=root)

# ---------------------------------------------------------------- preview render
scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in
    bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE_NEXT"
scene.render.resolution_x, scene.render.resolution_y = 700, 1000
world = bpy.data.worlds.new("W"); scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
sky = world.node_tree.nodes.new("ShaderNodeTexGradient")  # soft studio gradient
bg.inputs["Color"].default_value = (0.55, 0.57, 0.6, 1); bg.inputs["Strength"].default_value = 1.0
cam_data = bpy.data.cameras.new("Cam"); cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam); scene.camera = cam
cam.location = (1.6, -3.0, 1.5)
cam.rotation_euler = (Vector((0, 0, 1.0)) - cam.location).to_track_quat("-Z", "Y").to_euler()
cam_data.lens = 45
for loc, e in (((2.5, -2.5, 3.0), 400), ((-2.5, -1.5, 2.0), 150)):
    ld = bpy.data.lights.new("L", "AREA"); ld.energy = e; ld.size = 2
    lo = bpy.data.objects.new("L", ld); scene.collection.objects.link(lo); lo.location = loc
    lo.rotation_euler = (Vector((0, 0, 1)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
floor = cube("PreviewFloor", -3, 3, -3, 3, -0.01, 0, mat("Floor", (0.3, 0.3, 0.3)))
scene.render.filepath = PREVIEW
bpy.ops.render.render(write_still=True)
bpy.data.objects.remove(floor)
for o in [o for o in scene.objects if o.type in ("CAMERA", "LIGHT")]:
    bpy.data.objects.remove(o)

# ---------------------------------------------------------------- export
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "Fridge_source.blend"))
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT_DIR, "Fridge.fbx"),
    use_selection=True, object_types={"EMPTY", "MESH"},
    use_mesh_modifiers=True, mesh_smooth_type="FACE",
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
    bake_space_transform=False, add_leaf_bones=False, path_mode="AUTO")
print("EXPORT_OK", OUT_DIR)
