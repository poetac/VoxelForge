"""Generate voxelforge logo variants — solid cubes and TPMS-voxelized cubes.

Produces iso-projected SVGs where one or more cubes are subdivided into
sub-voxels filtered by an implicit TPMS function (Schwarz-P / Gyroid).
Run from repo root:  python assets/_gen_logos.py
"""
from __future__ import annotations
import math, os
from contextlib import contextmanager

# --- Palette ---
FB_TOP   = "#0B4F7A"
FB_LEFT  = "#083A5B"
FB_RIGHT = "#05293F"
IG_TOP   = "#D94F00"
IG_LEFT  = "#A83C00"
IG_RIGHT = "#7A2C00"
STROKE   = "#1C2024"

# Dark-mode palette (for future dark variants)
FBD_TOP, FBD_LEFT, FBD_RIGHT = "#4D9FC8", "#357EA4", "#22607F"
IGD_TOP, IGD_LEFT, IGD_RIGHT = "#F28A3F", "#C26A28", "#8F4A18"
DARK_STROKE = "#E6E4DE"

COS30 = math.cos(math.radians(30))  # 0.866

# --- View parameters (bird's-eye iso: camera tilted steeply downward) ---
# Standard isometric is (VIEW_TILT=0.5, VIEW_Z=1.0) — all three axes equal length.
# Raising VIEW_TILT deepens the top-face rhombus; lowering VIEW_Z shortens
# the side faces. Together they make the viewer feel like they're looking
# down ONTO the cubes (bird's-eye) rather than across at them (standard iso).
VIEW_TILT = 0.78
VIEW_Z    = 0.50

def project(wx: float, wy: float, wz: float, ox: float, oy: float, scale: float):
    X = ox + (wx - wy) * COS30 * scale
    Y = oy + (-wz * VIEW_Z + (-wx - wy) * VIEW_TILT) * scale
    return (X, Y)

@contextmanager
def view(tilt: float, zscale: float):
    """Temporarily override VIEW_TILT / VIEW_Z inside a `with` block."""
    global VIEW_TILT, VIEW_Z
    saved = (VIEW_TILT, VIEW_Z)
    VIEW_TILT, VIEW_Z = tilt, zscale
    try:
        yield
    finally:
        VIEW_TILT, VIEW_Z = saved

def fmt(p):
    return f"{p[0]:.2f},{p[1]:.2f}"

def poly(fill: str, pts, stroke: str = STROKE, sw: float = 0.5) -> str:
    return (f'<polygon points="{" ".join(fmt(p) for p in pts)}" '
            f'fill="{fill}" stroke="{stroke}" stroke-width="{sw}" '
            f'stroke-linejoin="round"/>')

def solid_cube(ox, oy, scale, top, left, right, stroke=STROKE, sw=1.25):
    p = lambda wx, wy, wz: project(wx, wy, wz, ox, oy, scale)
    return [
        poly(top,   [p(0,0,1), p(1,0,1), p(1,1,1), p(0,1,1)], stroke, sw),
        poly(left,  [p(0,1,0), p(1,1,0), p(1,1,1), p(0,1,1)], stroke, sw),
        poly(right, [p(1,0,0), p(1,1,0), p(1,1,1), p(1,0,1)], stroke, sw),
    ]

def solid_partial_cube(ox, oy, scale, z0, z1, top, left, right, stroke=STROKE, sw=1.25):
    """Render a box spanning world [0,1] x [0,1] x [z0, z1]."""
    p = lambda wx, wy, wz: project(wx, wy, wz, ox, oy, scale)
    return [
        poly(top,   [p(0,0,z1), p(1,0,z1), p(1,1,z1), p(0,1,z1)], stroke, sw),
        poly(left,  [p(0,1,z0), p(1,1,z0), p(1,1,z1), p(0,1,z1)], stroke, sw),
        poly(right, [p(1,0,z0), p(1,1,z0), p(1,1,z1), p(1,0,z1)], stroke, sw),
    ]

def subdivided_cube(ox, oy, scale, N, fill_fn, top, left, right, stroke=STROKE, sw=0.4):
    """Render an N^3-subdivided cube with only voxels where fill_fn returns True."""
    filled = [[[fill_fn(i, j, k, N) for k in range(N)] for j in range(N)] for i in range(N)]
    p = lambda wx, wy, wz: project(wx, wy, wz, ox, oy, scale)
    u = 1.0 / N
    out = []
    # Painter's order: viewer at (+inf, +inf, +inf). Draw smallest (i+j+k) first.
    for i in range(N):
        for j in range(N):
            for k in range(N):
                if not filled[i][j][k]:
                    continue
                x0, x1 = i * u, (i + 1) * u
                y0, y1 = j * u, (j + 1) * u
                z0, z1 = k * u, (k + 1) * u
                # Top face (normal +z)
                if k == N - 1 or not filled[i][j][k + 1]:
                    out.append(poly(top, [p(x0,y0,z1), p(x1,y0,z1), p(x1,y1,z1), p(x0,y1,z1)], stroke, sw))
                # Right face (normal +x)
                if i == N - 1 or not filled[i + 1][j][k]:
                    out.append(poly(right, [p(x1,y0,z0), p(x1,y1,z0), p(x1,y1,z1), p(x1,y0,z1)], stroke, sw))
                # Left face (normal +y)
                if j == N - 1 or not filled[i][j + 1][k]:
                    out.append(poly(left, [p(x0,y1,z0), p(x1,y1,z0), p(x1,y1,z1), p(x0,y1,z1)], stroke, sw))
    return out

# --- TPMS samplers ---
def schwarz_p(thresh: float = 0.0):
    def fn(i, j, k, N):
        a = (i + 0.5) / N * 2 * math.pi
        b = (j + 0.5) / N * 2 * math.pi
        c = (k + 0.5) / N * 2 * math.pi
        return math.cos(a) + math.cos(b) + math.cos(c) > thresh
    return fn

def gyroid(thresh: float = 0.0):
    def fn(i, j, k, N):
        a = (i + 0.5) / N * 2 * math.pi
        b = (j + 0.5) / N * 2 * math.pi
        c = (k + 0.5) / N * 2 * math.pi
        return (math.sin(a)*math.cos(b) + math.sin(b)*math.cos(c) + math.sin(c)*math.cos(a)) > thresh
    return fn

# --- Composition: standard L-stack of 3 cubes ---
def l_stack(L: float, cube1_origin):
    ox1, oy1 = cube1_origin
    cube2_origin = project(1, 0, 0, ox1, oy1, L)   # world (1,0,0)
    cube3_origin = project(0, 0, 1, ox1, oy1, L)   # world (0,0,1)
    return cube1_origin, cube2_origin, cube3_origin

def svg_wrap(body: str, w: int = 240, h: int = 240) -> str:
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" '
            f'role="img" aria-label="voxelforge">\n'
            f'  <title>voxelforge</title>\n'
            f'{body}\n</svg>\n')

# --- Variants ---
def variant_tpms_top(outfile, tpms_fn, top_c=IG_TOP, left_c=IG_LEFT, right_c=IG_RIGHT, N=6, sw=0.4):
    """L-stack; the top cube is TPMS-voxelized (Ignition accent)."""
    L = 44
    c1, c2, c3 = l_stack(L, (90, 200))
    parts = []
    parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
    parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
    parts += subdivided_cube(*c3, L, N, tpms_fn, top_c, left_c, right_c, sw=sw)
    body = "  " + "\n  ".join(parts)
    return svg_wrap(body)

def variant_tpms_base(outfile, tpms_fn, N=6, sw=0.4):
    """L-stack; the base-right cube is TPMS-voxelized (Forge Blue); top keeps Ignition."""
    L = 44
    c1, c2, c3 = l_stack(L, (90, 200))
    parts = []
    parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
    parts += subdivided_cube(*c2, L, N, tpms_fn, FB_TOP, FB_LEFT, FB_RIGHT, sw=sw)
    parts += solid_cube(*c3, L, IG_TOP, FB_LEFT, FB_RIGHT)
    body = "  " + "\n  ".join(parts)
    return svg_wrap(body)

def variant_single(outfile, tpms_fn, N=8, sw=0.4):
    """Minimalist: one voxelized cube in Forge Blue."""
    L = 120
    origin = (120, 200)
    parts = subdivided_cube(*origin, L, N, tpms_fn, FB_TOP, FB_LEFT, FB_RIGHT, sw=sw)
    body = "  " + "\n  ".join(parts)
    return svg_wrap(body, w=240, h=240)

def variant_mono(outfile, tpms_fn, N=6, sw=0.4):
    """Monochromatic L-stack: all Forge Blue, top voxelized (no Ignition)."""
    L = 44
    c1, c2, c3 = l_stack(L, (90, 200))
    parts = []
    parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
    parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
    parts += subdivided_cube(*c3, L, N, tpms_fn, FB_TOP, FB_LEFT, FB_RIGHT, sw=sw)
    body = "  " + "\n  ".join(parts)
    return svg_wrap(body)

# --- Combined variants (original + F aesthetic) ---
def variant_combo_a(N=4, sw=0.5):
    """Option A: Cube 3 = solid blue bottom 75% + chunky voxel top 25%.
       Reads as the original cube with a thin voxel 'crown' replacing the top layer."""
    L = 44
    c1, c2, c3 = l_stack(L, (90, 200))
    parts = []
    parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
    parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
    # Cube 3: bottom 75% solid blue
    parts += solid_partial_cube(*c3, L, 0.0, 0.75, FB_TOP, FB_LEFT, FB_RIGHT)
    # Top layer (k=N-1) voxelized Ignition
    fill_fn = schwarz_p(0.0)
    top_layer_only = lambda i, j, k, N: fill_fn(i, j, k, N) and k == N - 1
    parts += subdivided_cube(*c3, L, N, top_layer_only, IG_TOP, IG_LEFT, IG_RIGHT, sw=sw)
    return svg_wrap("  " + "\n  ".join(parts))

def variant_combo_b(N=4, sw=0.5):
    """Option B: Cube 3 split 50/50 — solid blue bottom half + voxelized Ignition top half."""
    L = 44
    c1, c2, c3 = l_stack(L, (90, 200))
    parts = []
    parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
    parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
    # Cube 3 bottom half solid blue
    parts += solid_partial_cube(*c3, L, 0.0, 0.5, FB_TOP, FB_LEFT, FB_RIGHT)
    # Top half voxelized Ignition (k in [N/2, N))
    fill_fn = schwarz_p(0.0)
    top_half = lambda i, j, k, N: fill_fn(i, j, k, N) and k >= N // 2
    parts += subdivided_cube(*c3, L, N, top_half, IG_TOP, IG_LEFT, IG_RIGHT, sw=sw)
    return svg_wrap("  " + "\n  ".join(parts))

def tee_stack(L: float, cube1_origin, top_scale: float = 0.7):
    """Two base cubes side-by-side along +x plus a smaller centered top cube.
       Base cubes at world (0,0,0) and (1,0,0) with side L. Top cube side
       top_scale*L, centered in x on the seam (world x=1), centered in y on
       the base's mid-depth (world y=0.5), sitting on z=1.
       Returns (c1, c2, (c3_origin, c3_scale))."""
    ox1, oy1 = cube1_origin
    c1 = cube1_origin                               # world (0,0,0)
    c2 = project(1, 0, 0, ox1, oy1, L)              # world (1,0,0)
    # Top cube's local (0,0,0) is at world (ax, ay, 1):
    #   ax = 1 - top_scale/2  → center x = 1 (seam)
    #   ay = 0.5 - top_scale/2 → center y = 0.5 (mid-depth)
    ax = 1.0 - top_scale / 2.0
    ay = 0.5 - top_scale / 2.0
    c3_origin = project(ax, ay, 1.0, ox1, oy1, L)
    c3_scale = top_scale * L
    return c1, c2, (c3_origin, c3_scale)

def v_positions(offset=(0, 0, 0)):
    """V as 9 unit cubes in a 5-wide × 5-tall pixel-font grid (X-Z plane)."""
    pattern = {
        4: [0, 4],
        3: [0, 4],
        2: [1, 3],
        1: [1, 3],
        0: [2],
    }
    ox, oy, oz = offset
    return [(ox + x, oy, oz + z) for z, xs in pattern.items() for x in xs]

def f_positions(offset=(0, 0, 0)):
    """F as 10 unit cubes in a 4-wide × 5-tall pixel-font grid (X-Z plane)."""
    pattern = {
        4: [0, 1, 2, 3],   # top bar
        3: [0],
        2: [0, 1, 2],      # middle bar
        1: [0],
        0: [0],
    }
    ox, oy, oz = offset
    return [(ox + x, oy, oz + z) for z, xs in pattern.items() for x in xs]

def render_cube_group(positions, ox, oy, scale, top, left, right, sw=0.8):
    """Render a set of unit cubes at integer world positions with face culling.
       Skips any face shared with an adjacent cube in the group. Painter's
       order: ascending (x + y + z) so cubes further from the +x+y+z camera
       draw first."""
    pos_set = set(positions)
    sorted_pos = sorted(positions, key=lambda p: p[0] + p[1] + p[2])
    parts = []
    for (x, y, z) in sorted_pos:
        # Capture (x, y, z) as defaults to avoid closure-over-loop-var bug.
        p = lambda wx, wy, wz, cx=x, cy=y, cz=z: project(cx + wx, cy + wy, cz + wz, ox, oy, scale)
        if (x, y, z + 1) not in pos_set:
            parts.append(poly(top,   [p(0,0,1), p(1,0,1), p(1,1,1), p(0,1,1)], STROKE, sw))
        if (x, y + 1, z) not in pos_set:
            parts.append(poly(left,  [p(0,1,0), p(1,1,0), p(1,1,1), p(0,1,1)], STROKE, sw))
        if (x + 1, y, z) not in pos_set:
            parts.append(poly(right, [p(1,0,0), p(1,1,0), p(1,1,1), p(1,0,1)], STROKE, sw))
    return parts

def _vf_monogram(v_colors, f_colors, aligned_baseline=False):
    """Core VF renderer. v_colors/f_colors are (top, left, right) tuples.
       If aligned_baseline=True, F's screen origin is shifted down so V and F
       share a common visual baseline (kills the inter-letter iso staircase)."""
    with view(tilt=0.5, zscale=1.0):
        L = 17
        ox_v, oy_v = 54, 209
        # F center Y = V center Y when oy_f = oy_v + 2.5*L (derived from the
        # difference in letter-center wx+wy contribution to the iso tilt).
        # 2.5*L = 42.5 px vertical shift.
        ox_f = ox_v
        oy_f = oy_v + 2.5 * L if aligned_baseline else oy_v
        v_pos = v_positions(offset=(0, 0, 0))
        f_pos = f_positions(offset=(6, 0, 0))
        parts = []
        parts += render_cube_group(v_pos, ox_v, oy_v, L, *v_colors, sw=0.8)
        parts += render_cube_group(f_pos, ox_f, oy_f, L, *f_colors, sw=0.8)
        return svg_wrap("  " + "\n  ".join(parts))

def variant_combo_d1():
    """D1: V in Forge Blue, F in Ignition. Iso staircase between letters."""
    return _vf_monogram((FB_TOP, FB_LEFT, FB_RIGHT),
                        (IG_TOP, IG_LEFT, IG_RIGHT),
                        aligned_baseline=False)

def variant_combo_d2():
    """D2: Both letters in Forge Blue — unified monogram. Iso staircase."""
    return _vf_monogram((FB_TOP, FB_LEFT, FB_RIGHT),
                        (FB_TOP, FB_LEFT, FB_RIGHT),
                        aligned_baseline=False)

def variant_combo_d3():
    """D3: V blue + F orange, aligned baseline (F origin shifted down)."""
    return _vf_monogram((FB_TOP, FB_LEFT, FB_RIGHT),
                        (IG_TOP, IG_LEFT, IG_RIGHT),
                        aligned_baseline=True)

def variant_combo_d4():
    """D4: Unified Forge Blue + aligned baseline. Cleanest monogram read."""
    return _vf_monogram((FB_TOP, FB_LEFT, FB_RIGHT),
                        (FB_TOP, FB_LEFT, FB_RIGHT),
                        aligned_baseline=True)

def _combo_e_parts(ox_big, oy_big, L,
                    cube_palette=(FB_TOP, FB_LEFT, FB_RIGHT),
                    vf_palette=(IG_TOP, IG_LEFT, IG_RIGHT),
                    stroke=STROKE, cube_sw=1.25, pixel_sw=0.5):
    """Shared geometry for variant E: Forge-Blue solid cube + Ignition VF relief
       on the +y face, extruded outward by one pixel thickness.

       ox_big, oy_big — screen origin of the big cube's world (0,0,0).
       L — big cube side length in screen units.
       cube_palette, vf_palette — (top, left, right) colors.
       Returns a list of polygon strings in correct painter's order."""
    with view(tilt=0.5, zscale=1.0):
        c_top, c_left, c_right = cube_palette
        v_top, v_left, v_right = vf_palette
        parts = []
        # Big cube (solid). Rendered first so the inscription draws on top.
        parts += solid_cube(ox_big, oy_big, L, c_top, c_left, c_right,
                             stroke=stroke, sw=cube_sw)

        # VF inscription on the +y face, extruded outward in +y.
        # Font grid: V fills x=[0..4]×z=[0..4], F fills x=[6..9]×z=[0..4];
        # total 10 wide × 5 tall.
        px = 0.08                         # pixel size in cube-local units
        thickness = px                    # extrusion depth (y: 1 → 1+px)
        x_off = (1.0 - 10 * px) / 2.0     # center V+F horizontally on the face
        z_off = (1.0 - 5 * px) / 2.0      # center vertically
        y0, y1 = 1.0, 1.0 + thickness

        v_pts = v_positions(offset=(0, 0, 0))
        f_pts = f_positions(offset=(6, 0, 0))
        grid = v_pts + f_pts
        pos_set = set(grid)
        # Painter's order: cubes with smaller (fx + fz) are further from the
        # +x+y+z camera, so draw them first.
        grid_sorted = sorted(grid, key=lambda p: p[0] + p[2])

        for (fx, _, fz) in grid_sorted:
            x0 = x_off + fx * px
            x1 = x0 + px
            z0 = z_off + fz * px
            z1 = z0 + px
            p = lambda wx, wy, wz: project(wx, wy, wz, ox_big, oy_big, L)
            # +z face (top strip of relief) — cull if pixel above exists
            if (fx, 0, fz + 1) not in pos_set:
                parts.append(poly(v_top,
                    [p(x0,y0,z1), p(x1,y0,z1), p(x1,y1,z1), p(x0,y1,z1)],
                    stroke, pixel_sw))
            # +y face (outward face of relief — what the camera sees most)
            parts.append(poly(v_left,
                [p(x0,y1,z0), p(x1,y1,z0), p(x1,y1,z1), p(x0,y1,z1)],
                stroke, pixel_sw))
            # +x face (right-side wall of relief) — cull if pixel to right exists
            if (fx + 1, 0, fz) not in pos_set:
                parts.append(poly(v_right,
                    [p(x1,y0,z0), p(x1,y1,z0), p(x1,y1,z1), p(x1,y0,z1)],
                    stroke, pixel_sw))
        return parts

def variant_combo_e():
    """E (FINAL): Large Forge Blue solid cube with an orange VF monogram
       raised as unit voxels on its left (+y) face. VF extrudes outward so
       the letters read as a 3D relief inscription on the cube."""
    parts = _combo_e_parts(120, 220, 110)
    return svg_wrap("  " + "\n  ".join(parts))

def variant_combo_e_dark():
    """Dark-mode E: lighter palette + light stroke, transparent background
       (so the host page's dark bg shows through)."""
    parts = _combo_e_parts(
        120, 220, 110,
        cube_palette=(FBD_TOP, FBD_LEFT, FBD_RIGHT),
        vf_palette=(IGD_TOP, IGD_LEFT, IGD_RIGHT),
        stroke=DARK_STROKE)
    return svg_wrap("  " + "\n  ".join(parts))

def variant_combo_c(N=3, sw=0.7):
    """Reset v3: T-shape. Two solid Forge Blue base cubes side-by-side on
       the ground; a smaller fully-filled N^3 Ignition sub-voxel cube sits
       centered on top of the seam between them. Smaller top cube preserves
       the two distinct base cube silhouettes without occluding them."""
    L = 44
    c1, c2, (c3_origin, c3_scale) = tee_stack(L, (101, 178), top_scale=0.7)
    always = lambda i, j, k, N: True
    parts = []
    parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
    parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
    parts += subdivided_cube(*c3_origin, c3_scale, N, always,
                             IG_TOP, IG_LEFT, IG_RIGHT, sw=sw)
    return svg_wrap("  " + "\n  ".join(parts))

# --- Gallery: all variants in a 2x4 grid, rendered inline ---
def gen_variant_parts(name: str, offset: tuple):
    """Return the list of polygon strings for a named variant, with cube origins offset."""
    ox, oy = offset
    L = 44
    c1_origin = (90 + ox, 200 + oy)
    c1, c2, c3 = l_stack(L, c1_origin)
    parts = []
    if name == "original":
        parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += solid_cube(*c3, L, IG_TOP, FB_LEFT, FB_RIGHT)
    elif name == "a":  # Schwarz-P top, Ignition
        parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += subdivided_cube(*c3, L, 6, schwarz_p(0.0), IG_TOP, IG_LEFT, IG_RIGHT)
    elif name == "b":  # Gyroid top, Ignition
        parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += subdivided_cube(*c3, L, 6, gyroid(0.0), IG_TOP, IG_LEFT, IG_RIGHT)
    elif name == "c":  # Schwarz-P base-right, Blue; top stays solid Ignition
        parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += subdivided_cube(*c2, L, 6, schwarz_p(0.0), FB_TOP, FB_LEFT, FB_RIGHT)
        parts += solid_cube(*c3, L, IG_TOP, FB_LEFT, FB_RIGHT)
    elif name == "d":  # single voxelized, N=8
        L_single = 120
        origin = (120 + ox, 200 + oy)
        parts += subdivided_cube(*origin, L_single, 8, schwarz_p(0.0), FB_TOP, FB_LEFT, FB_RIGHT)
    elif name == "e":  # mono all blue, top voxelized
        parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += subdivided_cube(*c3, L, 6, schwarz_p(0.0), FB_TOP, FB_LEFT, FB_RIGHT)
    elif name == "f":  # chunky N=4 Ignition top
        parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += subdivided_cube(*c3, L, 4, schwarz_p(0.0), IG_TOP, IG_LEFT, IG_RIGHT)
    elif name == "g":  # denser Schwarz-P (lower threshold) Ignition top
        parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
        parts += subdivided_cube(*c3, L, 6, schwarz_p(-0.3), IG_TOP, IG_LEFT, IG_RIGHT)
    return parts

def build_gallery():
    """2 rows x 4 cols gallery of all variants, each labelled."""
    cell_w, cell_h = 260, 300
    cols = 4
    cells = [
        ("original", "Current: 3 solid cubes"),
        ("a", "A: Schwarz-P top, Ignition"),
        ("b", "B: Gyroid top, Ignition"),
        ("c", "C: Schwarz-P base-right, Blue"),
        ("d", "D: Single voxelized, N=8"),
        ("e", "E: Monochrome, Blue top-TPMS"),
        ("f", "F: Chunky N=4, Ignition"),
        ("g", "G: Denser Schwarz-P, Ignition"),
    ]
    w = cols * cell_w
    h = 2 * cell_h + 40
    body_parts = [f'<rect width="{w}" height="{h}" fill="#F5F3EE"/>']
    body_parts.append(
        f'<text x="{w/2}" y="28" font-family="ui-sans-serif, system-ui, -apple-system, \'Segoe UI\', sans-serif" '
        f'font-size="18" font-weight="600" fill="#1C2024" text-anchor="middle">voxelforge — logo variants</text>'
    )
    for idx, (name, label) in enumerate(cells):
        col = idx % cols
        row = idx // cols
        # Place cell origin at (col * cell_w, 40 + row * cell_h).
        # Each variant's local origin (90, 200) sits inside a 240x240 viewBox;
        # we translate by cell position - the local origin, then offset to center inside the cell.
        cell_ox = col * cell_w
        cell_oy = 40 + row * cell_h
        # The variant's bounding box is roughly x in [52,166], y in [68, 200] for L-stacks
        # For the single voxelized it's larger. We use a consistent cell origin by wrapping in <g transform>.
        # Simpler: render parts with an offset so the variant's internal (90,200) maps into the cell center.
        # Cell internal center target: (cell_ox + cell_w/2, cell_oy + cell_h/2 - 20) (room for label at bottom).
        target_x = cell_ox + cell_w / 2
        target_y = cell_oy + cell_h / 2 - 20
        # For L-stacks, the geometric centroid of the composition in local coords is about (109, 134).
        # For variant d (single), centroid about (120, 156) with its settings.
        if name == "d":
            cx, cy = 120, 156
        else:
            cx, cy = 109, 134
        off_x = target_x - cx
        off_y = target_y - cy
        parts = gen_variant_parts(name, (off_x, off_y))
        body_parts.append(f'<g>\n    ' + '\n    '.join(parts) + '\n  </g>')
        # Label
        label_y = cell_oy + cell_h - 14
        body_parts.append(
            f'<text x="{cell_ox + cell_w/2}" y="{label_y}" '
            f'font-family="ui-sans-serif, system-ui, -apple-system, \'Segoe UI\', sans-serif" '
            f'font-size="13" fill="#1C2024" text-anchor="middle">{label}</text>'
        )
        # Hex code for which file
        if name != "original":
            body_parts.append(
                f'<text x="{cell_ox + cell_w/2}" y="{label_y + 16}" '
                f'font-family="ui-monospace, \'SF Mono\', Menlo, monospace" '
                f'font-size="10" fill="#6B7280" text-anchor="middle">logo-tpms-{name}.svg</text>'
            )
        else:
            body_parts.append(
                f'<text x="{cell_ox + cell_w/2}" y="{label_y + 16}" '
                f'font-family="ui-monospace, \'SF Mono\', Menlo, monospace" '
                f'font-size="10" fill="#6B7280" text-anchor="middle">logo.svg</text>'
            )
    body = "  " + "\n  ".join(body_parts)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" '
            f'role="img" aria-label="voxelforge logo variants">\n{body}\n</svg>\n')

# --- Combos gallery: just the three original+F combinations ---
def build_combos_gallery():
    cell_w, cell_h = 320, 340
    cols = 3
    cells = [
        ("combo-a", "A — Voxel crown",     "Solid blue + top-layer voxels"),
        ("combo-b", "B — Half-and-half",   "Bottom half solid, top half voxel"),
        ("combo-c", "C — Two-size stack",  "Big Forge Blue voxels + fine Ignition sub-voxels"),
    ]
    w = cols * cell_w
    h = cell_h + 50
    out = [f'<rect width="{w}" height="{h}" fill="#F5F3EE"/>']
    out.append(
        f'<text x="{w/2}" y="30" font-family="ui-sans-serif, system-ui, sans-serif" '
        f'font-size="20" font-weight="600" fill="#1C2024" text-anchor="middle">'
        f'voxelforge — original + F combined</text>'
    )
    L = 44
    for idx, (name, label, sub) in enumerate(cells):
        cell_ox = idx * cell_w
        cell_oy = 50
        target_x = cell_ox + cell_w / 2
        target_y = cell_oy + cell_h / 2 - 30
        cx, cy = 109, 134  # centroid of an L-stack at origin (90, 200)
        off_x = target_x - cx
        off_y = target_y - cy
        parts = []
        if name == "combo-c":
            # T-shape layout: two base cubes side-by-side + one smaller centered top cube.
            c1t, c2t, (c3t_origin, c3t_scale) = tee_stack(L, (90 + off_x, 200 + off_y), top_scale=0.7)
            parts += solid_cube(*c1t, L, FB_TOP, FB_LEFT, FB_RIGHT)
            parts += solid_cube(*c2t, L, FB_TOP, FB_LEFT, FB_RIGHT)
            always = lambda i, j, k, N: True
            parts += subdivided_cube(*c3t_origin, c3t_scale, 3, always, IG_TOP, IG_LEFT, IG_RIGHT, sw=0.7)
        else:
            c1, c2, c3 = l_stack(L, (90 + off_x, 200 + off_y))
            parts += solid_cube(*c1, L, FB_TOP, FB_LEFT, FB_RIGHT)
            parts += solid_cube(*c2, L, FB_TOP, FB_LEFT, FB_RIGHT)
            if name == "combo-a":
                parts += solid_partial_cube(*c3, L, 0.0, 0.75, FB_TOP, FB_LEFT, FB_RIGHT)
                fill_fn = schwarz_p(0.0)
                top_layer = lambda i, j, k, N: fill_fn(i, j, k, N) and k == N - 1
                parts += subdivided_cube(*c3, L, 4, top_layer, IG_TOP, IG_LEFT, IG_RIGHT)
            elif name == "combo-b":
                parts += solid_partial_cube(*c3, L, 0.0, 0.5, FB_TOP, FB_LEFT, FB_RIGHT)
                fill_fn = schwarz_p(0.0)
                top_half = lambda i, j, k, N: fill_fn(i, j, k, N) and k >= N // 2
                parts += subdivided_cube(*c3, L, 4, top_half, IG_TOP, IG_LEFT, IG_RIGHT)
        out.append("<g>\n    " + "\n    ".join(parts) + "\n  </g>")
        out.append(
            f'<text x="{target_x}" y="{cell_oy + cell_h - 34}" '
            f'font-family="ui-sans-serif, system-ui, sans-serif" '
            f'font-size="15" font-weight="600" fill="#1C2024" text-anchor="middle">{label}</text>'
        )
        out.append(
            f'<text x="{target_x}" y="{cell_oy + cell_h - 14}" '
            f'font-family="ui-sans-serif, system-ui, sans-serif" '
            f'font-size="12" fill="#6B7280" text-anchor="middle">{sub}</text>'
        )
    body = "  " + "\n  ".join(out)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" '
            f'role="img" aria-label="voxelforge combined variants">\n{body}\n</svg>\n')

def build_social_preview():
    """1280x640 social-share image with the variant-E logo on the left
       and voxelforge wordmark + tagline on the right. The logo geometry
       is generated from _combo_e_parts (single source of truth)."""
    w, h = 1280, 640
    # Logo: center at (260, 320), big-cube side L=320.
    # _combo_e_parts draws the big cube's world (0,0,0) at (ox_big, oy_big);
    # world (0,0,0) is the bottom-front-left corner. The cube's projected
    # bounding box spans [-L*COS30, +L*COS30] in X and [-L*2, 0] in Y around
    # that origin (for VIEW_TILT=0.5, VIEW_Z=1.0). So to center vertically
    # we place oy at (bbox center) + half_height.
    L = 320
    ox = 260
    oy = 540   # bottom-front-left near y=540 centers the tall box vertically
    logo_parts = _combo_e_parts(ox, oy, L)

    body = [
        f'<rect width="{w}" height="{h}" fill="#F5F3EE"/>',
        f'<rect width="{w}" height="{h}" fill="url(#grid)"/>',
        '<g>',
        '  ' + '\n  '.join(logo_parts),
        '</g>',
        '<g font-family="ui-sans-serif, system-ui, -apple-system, \'Segoe UI\', Inter, sans-serif">',
        f'  <text x="580" y="305" font-size="112" font-weight="700" fill="#1C2024" letter-spacing="-3">voxelforge</text>',
        f'  <text x="584" y="365" font-size="28" fill="#1C2024" opacity="0.72">Computational engineering for voxel-based additive manufacturing</text>',
        '</g>',
        '<g font-family="ui-monospace, \'SF Mono\', Menlo, Consolas, monospace">',
        f'  <text x="584" y="425" font-size="20" fill="#6B7280" letter-spacing="0.6">.NET 9   ·   PicoGK   ·   LOX/CH4 regen cooling   ·   TPMS   ·   SA optimiser</text>',
        '</g>',
        f'<line x1="85" y1="555" x2="1195" y2="555" stroke="#1C2024" stroke-opacity="0.12"/>',
        f'<text x="85" y="595" font-family="ui-monospace, \'SF Mono\', Menlo, Consolas, monospace" font-size="18" fill="#6B7280">github.com/poetac/voxelforge</text>',
        f'<rect x="1145" y="581" width="50" height="6" fill="#D94F00"/>',
    ]
    defs = (
        '  <defs>\n'
        '    <pattern id="grid" x="0" y="0" width="40" height="40" patternUnits="userSpaceOnUse">\n'
        '      <path d="M 40 0 L 0 0 0 40" fill="none" stroke="#1C2024" stroke-opacity="0.055" stroke-width="0.5"/>\n'
        '    </pattern>\n'
        '  </defs>\n'
    )
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" '
        f'width="{w}" height="{h}" role="img" '
        f'aria-label="voxelforge — Computational engineering for voxel-based additive manufacturing">\n'
        f'  <title>voxelforge — Computational engineering for voxel-based additive manufacturing</title>\n'
        + defs
        + '  ' + '\n  '.join(body) + '\n</svg>\n'
    )

if __name__ == "__main__":
    out_dir = os.path.join(os.path.dirname(__file__))
    variants = {
        "logo-tpms-a.svg":  variant_tpms_top(None, schwarz_p(0.0), N=6),
        "logo-tpms-b.svg":  variant_tpms_top(None, gyroid(0.0),    N=6),
        "logo-tpms-c.svg":  variant_tpms_base(None, schwarz_p(0.0), N=6),
        "logo-tpms-d.svg":  variant_single(None, schwarz_p(0.0), N=8),
        "logo-tpms-e.svg":  variant_mono(None, schwarz_p(0.0), N=6),
        "logo-tpms-f.svg":  variant_tpms_top(None, schwarz_p(0.0), N=4),
        "logo-tpms-g.svg":  variant_tpms_top(None, schwarz_p(-0.3), top_c=IG_TOP, left_c=IG_LEFT, right_c=IG_RIGHT, N=6),
        "logo-variants-gallery.svg": build_gallery(),
        "logo-combo-a.svg": variant_combo_a(),
        "logo-combo-b.svg": variant_combo_b(),
        "logo-combo-c.svg": variant_combo_c(),
        "logo-combo-d1.svg": variant_combo_d1(),
        "logo-combo-d2.svg": variant_combo_d2(),
        "logo-combo-d3.svg": variant_combo_d3(),
        "logo-combo-d4.svg": variant_combo_d4(),
        "logo-combo-e.svg":  variant_combo_e(),
        "logo.svg":          variant_combo_e(),
        "logo-dark.svg":     variant_combo_e_dark(),
        "social-preview.svg": build_social_preview(),
        "logo-combos-gallery.svg": build_combos_gallery(),
    }
    for name, svg in variants.items():
        with open(os.path.join(out_dir, name), "w", encoding="utf-8") as f:
            f.write(svg)
        print(f"wrote {name}")
