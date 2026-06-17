# render.py — Blender 4.5 headless rendering for voxelforge-render.
#
# Sprint render (2026-04-25) — Visual elegance / Noyron-parity track.
#
# Invoked by:
#   blender --background --python render.py -- '<JSON payload>'
#
# JSON payload (all paths absolute):
#   {
#     "input_stl": "...",
#     "output_path": "...",
#     "mode": "still" | "turntable",
#     "material_path": ".../materials/copper.json",
#     "resolution": {"width": 1920, "height": 1080, "samples": 256, "engine": "CYCLES"},
#     "frames": 16
#   }
#
# Produces:
#   still     -> PNG at output_path
#   turntable -> PNG sequence at output_path with N frames numbered 0001..N
#                (post-processing into MP4 left to caller via ffmpeg if desired)
#
# Camera: framed 3/4 view auto-fit to bounding box. Turntable rotates the
# OBJECT (easier than orbiting the camera, gives identical lighting on
# every frame).
#
# Lighting: Blender's bundled studio.exr HDRi (datafiles/studiolights/world/).
# No external HDRi download required.
#
# Material: PBR Principled BSDF, parameters from the material_path JSON.

import bpy
import json
import os
import sys
import math


def parse_payload():
    """Extract the JSON payload after the `--` separator on argv."""
    if "--" not in sys.argv:
        raise RuntimeError("render.py: expected '-- <json>' in argv")
    payload_idx = sys.argv.index("--") + 1
    if payload_idx >= len(sys.argv):
        raise RuntimeError("render.py: '--' not followed by JSON payload")
    return json.loads(sys.argv[payload_idx])


def reset_scene():
    """Strip Blender's default cube + lights; start with empty scene."""
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    # Remove any leftover orphan data so re-imports don't merge MAterials.
    for collection in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras):
        for item in list(collection):
            collection.remove(item)


def import_stl(path):
    """Import the STL and return the resulting object. Blender 4.x renamed
    the import op several times; try both common variants."""
    if hasattr(bpy.ops.wm, "stl_import"):
        bpy.ops.wm.stl_import(filepath=path)
    elif hasattr(bpy.ops.import_mesh, "stl"):
        bpy.ops.import_mesh.stl(filepath=path)
    else:
        raise RuntimeError("render.py: no STL importer found in this Blender version")
    obj = bpy.context.selected_objects[0]
    obj.name = "ChamberMesh"
    return obj


def setup_world_hdri(hdri_path=None):
    """Set up HDRi world environment.

    Priority order:
      1. Explicit hdri_path from the JSON payload (fetched by tools/fetch-hdri.ps1).
      2. Blender's own bundled studio.exr (ships with every Blender install).
      3. Grey-blue solid fallback (always produces *something* rather than black).
    """
    world = bpy.data.worlds.new("VoxelforgeWorld") if not bpy.data.worlds else bpy.data.worlds[0]
    bpy.context.scene.world = world
    world.use_nodes = True
    nodes = world.node_tree.nodes
    links = world.node_tree.links
    nodes.clear()

    out = nodes.new("ShaderNodeOutputWorld")
    bg  = nodes.new("ShaderNodeBackground")
    env = nodes.new("ShaderNodeTexEnvironment")

    resolved_path = None

    # 1. Explicit path from payload (Polyhaven fetched, highest quality).
    if hdri_path and os.path.exists(hdri_path):
        resolved_path = hdri_path

    # 2. Blender's own bundled studio.exr — ships with Blender, no external download.
    if resolved_path is None:
        blender_root = os.path.dirname(os.path.dirname(bpy.app.binary_path))
        version = f"{bpy.app.version[0]}.{bpy.app.version[1]}"
        candidate = os.path.join(blender_root, version, "datafiles",
                                 "studiolights", "world", "studio.exr")
        if not os.path.exists(candidate):
            alt = os.path.join(os.path.dirname(bpy.app.binary_path), version, "datafiles",
                               "studiolights", "world", "studio.exr")
            candidate = alt if os.path.exists(alt) else candidate
        if os.path.exists(candidate):
            resolved_path = candidate

    env.image = bpy.data.images.load(resolved_path) if resolved_path else None

    # 1.5x strength makes the copper pop without blowing highlights.
    bg.inputs["Strength"].default_value = 1.5

    if env.image is not None:
        links.new(env.outputs["Color"], bg.inputs["Color"])
    else:
        # 3. Grey-blue solid fallback.
        bg.inputs["Color"].default_value = (0.4, 0.45, 0.5, 1.0)

    links.new(bg.outputs["Background"], out.inputs["Surface"])


def setup_material(obj, material_path):
    """Build a PBR Principled BSDF material from the JSON spec and apply to obj."""
    with open(material_path, "r") as f:
        spec = json.load(f)

    mat = bpy.data.materials.new(name=spec.get("name", "VoxelforgeMaterial"))
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    links = mat.node_tree.links

    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")

    bsdf.inputs["Base Color"].default_value = tuple(spec["base_color"])
    bsdf.inputs["Metallic"].default_value = spec["metallic"]
    bsdf.inputs["Roughness"].default_value = spec["roughness"]
    # Blender 4.x renamed several Principled BSDF inputs. Apply
    # defensively — if a slot doesn't exist, skip it.
    _safe_set(bsdf.inputs, "IOR Level", spec.get("specular_ior_level"))
    _safe_set(bsdf.inputs, "Specular IOR Level", spec.get("specular_ior_level"))
    _safe_set(bsdf.inputs, "Anisotropic", spec.get("anisotropic"))
    _safe_set(bsdf.inputs, "Anisotropic Rotation", spec.get("anisotropic_rotation"))
    _safe_set(bsdf.inputs, "Coat Weight", spec.get("clearcoat"))
    _safe_set(bsdf.inputs, "Coat Roughness", spec.get("clearcoat_roughness"))

    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    # Smooth shading on the mesh — STLs come in flat-shaded by default
    # which makes faceted-looking renders. PBR + smooth shading mimics
    # the polished surface the material spec describes.
    # Note: `use_auto_smooth` was removed from bpy.types.Mesh in Blender 4.1+;
    # the modern equivalent is the "Smooth by Angle" modifier, but for our
    # smoke test setting per-face smooth on every polygon is sufficient.
    if hasattr(obj.data, "use_auto_smooth"):
        obj.data.use_auto_smooth = False
    for poly in obj.data.polygons:
        poly.use_smooth = True

    obj.data.materials.clear()
    obj.data.materials.append(mat)


def _safe_set(inputs, slot_name, value):
    if value is None: return
    if slot_name in inputs:
        inputs[slot_name].default_value = value


def setup_camera_for_object(obj):
    """3/4 framed camera, auto-fit to object bounding box."""
    # Compute object bounding-sphere radius (approx) from world-space corners.
    obj_bbox = [obj.matrix_world @ vert.co for vert in obj.data.vertices]
    if not obj_bbox:
        # Fallback: use object's bound_box (8 corners of the BB).
        import mathutils
        obj_bbox = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    cx = sum(v.x for v in obj_bbox) / len(obj_bbox)
    cy = sum(v.y for v in obj_bbox) / len(obj_bbox)
    cz = sum(v.z for v in obj_bbox) / len(obj_bbox)
    radius = max(((v.x - cx) ** 2 + (v.y - cy) ** 2 + (v.z - cz) ** 2) ** 0.5 for v in obj_bbox)

    cam_data = bpy.data.cameras.new("VoxelforgeCam")
    cam_obj  = bpy.data.objects.new("VoxelforgeCam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    bpy.context.scene.camera = cam_obj

    # 3/4 framed view: position the camera off-axis.
    # Distance = 3 × radius so the object fits comfortably with margin.
    distance = max(radius * 3.0, 0.1)
    # Azimuth 30° from +X, elevation 20° above horizon.
    az_rad = math.radians(30)
    el_rad = math.radians(20)
    cam_obj.location = (
        cx + distance * math.cos(el_rad) * math.cos(az_rad),
        cy + distance * math.cos(el_rad) * math.sin(az_rad),
        cz + distance * math.sin(el_rad),
    )

    # Point camera at the object's centre via track-to constraint.
    target = bpy.data.objects.new("VoxelforgeCamTarget", None)
    target.location = (cx, cy, cz)
    bpy.context.collection.objects.link(target)
    track = cam_obj.constraints.new("TRACK_TO")
    track.target = target
    track.track_axis = "TRACK_NEGATIVE_Z"
    track.up_axis    = "UP_Y"

    return cam_obj, target, (cx, cy, cz)


def configure_render(scene, resolution):
    """Set engine, sample count, resolution, output format."""
    scene.render.resolution_x = resolution["width"]
    scene.render.resolution_y = resolution["height"]
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False  # solid background from HDRi

    engine = resolution["engine"]
    samples = resolution["samples"]
    if engine == "CYCLES":
        scene.render.engine = "CYCLES"
        scene.cycles.samples = samples
        scene.cycles.use_denoising = True
        # GPU first if available.
        try:
            prefs = bpy.context.preferences.addons["cycles"].preferences
            prefs.compute_device_type = "OPTIX"  # NVIDIA preferred
            prefs.get_devices()
            scene.cycles.device = "GPU"
        except Exception:
            scene.cycles.device = "CPU"
    else:
        # Eevee Next (Blender 4.2+) for low-resolution fast preview.
        scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in {e.identifier for e in scene.render.bl_rna.properties["engine"].enum_items} else "BLENDER_EEVEE"
        # Eevee uses different sample property.
        if hasattr(scene.eevee, "taa_render_samples"):
            scene.eevee.taa_render_samples = samples


def render_still(scene, output_path):
    scene.render.filepath = output_path
    bpy.ops.render.render(write_still=True)


def render_turntable(scene, obj, output_path, frames):
    """Rotate the object around its own Z axis through 360°, render each frame.

    Output convention: <output_path>_0001.png ... <output_path>_NNNN.png.
    If output_path ends in .png, strip the extension before the numeric suffix.
    """
    base, ext = os.path.splitext(output_path)
    if not ext: ext = ".png"
    out_dir = os.path.dirname(base) or "."
    os.makedirs(out_dir, exist_ok=True)

    for i in range(frames):
        angle = 2 * math.pi * i / frames
        obj.rotation_euler = (0, 0, angle)
        scene.render.filepath = f"{base}_{i+1:04d}{ext}"
        bpy.ops.render.render(write_still=True)


def main():
    payload = parse_payload()
    print(f"render.py: payload = {json.dumps(payload, indent=2)}")

    reset_scene()
    setup_world_hdri(hdri_path=payload.get("hdri_path"))
    obj = import_stl(payload["input_stl"])
    setup_material(obj, payload["material_path"])
    cam_obj, target, center = setup_camera_for_object(obj)
    configure_render(bpy.context.scene, payload["resolution"])

    if payload["mode"] == "still":
        render_still(bpy.context.scene, payload["output_path"])
        print(f"render.py: wrote still -> {payload['output_path']}")
    elif payload["mode"] == "turntable":
        # For turntable, target stays at object centre; the OBJECT rotates.
        # Reset object origin to its own centre first so rotation is around itself.
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="MEDIAN")
        render_turntable(bpy.context.scene, obj, payload["output_path"], payload["frames"])
        print(f"render.py: wrote {payload['frames']}-frame turntable -> {payload['output_path']}_NNNN.png")
    else:
        raise RuntimeError(f"render.py: unknown mode '{payload['mode']}'")


if __name__ == "__main__":
    # Blender's --background mode swallows Python exceptions and exits 0
    # by default (it considers "the script ran" success). For correct
    # exit-code propagation back to voxelforge-render, wrap main() and
    # explicitly call sys.exit(1) on any exception.
    try:
        main()
    except Exception as e:
        import traceback
        traceback.print_exc()
        print(f"render.py: FATAL: {e}", file=sys.stderr)
        sys.exit(1)
