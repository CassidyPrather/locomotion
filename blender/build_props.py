"""Builds the locomotion props: the flight cloud, the stepladder and the Zs of a nap.

    blender -b --python blender/build_props.py [-- --only STEM] [-- --previews DIR]

Each prop is modelled from the code below, saved as `blender/<stem>.blend`,
checked against the wirenook prop contract and exported to the package's
`Models/<stem>.fbx`. See README.md beside this file.
"""

import pathlib
import sys

sys.dont_write_bytecode = True

REPO = pathlib.Path(__file__).resolve().parents[1]
MODELS = REPO / "Packages" / "net.wirenook.locomotion" / "Models"
LIBRARY = REPO.parent / "wirenook-assets" / "tools" / "blender"
if not (LIBRARY / "wirenook_props.py").is_file():
    raise SystemExit(f"The shared prop kit is missing: {LIBRARY / 'wirenook_props.py'}\n"
                     "Clone wirenook-assets beside this repository and run again.")
sys.path.insert(0, str(LIBRARY))

from mathutils import Vector

from wirenook_props import FORWARD, UP, PropSpec, Puff, chamfered_rect, cloud, cloud_tones, main_build, ring_points

X = Vector((1.0, 0.0, 0.0))
BACK = -FORWARD

FLIGHT_CLOUD = PropSpec("FlightCloud", "flight-cloud", triangle_budget=350)


def build_flight_cloud(mesh):
    # Everything hangs below the hold point, so feet placed on the root bone
    # stand on the crown. Puffs shrink towards the back into a short tail,
    # which gives the cloud a direction of travel.
    puff = mesh.part()
    cloud(puff, [
        Puff((0.0, -0.02, -0.085), (0.25, 0.23, 0.085)),
        Puff((-0.21, -0.08, -0.085), (0.14, 0.13, 0.075), rings=4),
        Puff((0.21, -0.08, -0.085), (0.14, 0.13, 0.075), rings=4),
        Puff((0.0, -0.24, -0.09), (0.15, 0.11, 0.07), rings=4),
        Puff((-0.17, 0.14, -0.09), (0.13, 0.12, 0.07), rings=4),
        Puff((0.17, 0.14, -0.09), (0.13, 0.12, 0.07), rings=4),
        Puff((0.0, 0.27, -0.095), (0.10, 0.08, 0.055), segments=6, rings=4),
    ], floor=-0.15, tones=cloud_tones(-0.125, -0.065))
    return {}


# --------------------------------------------------------------------------
# Stepladder
# --------------------------------------------------------------------------

LADDER_STEPS = 8
LADDER_STEP_HEIGHT = 0.36

LADDER = PropSpec("Ladder", "ladder", triangle_budget=460,
                  parts={"Base": None, **{f"S{step}": None for step in range(1, LADDER_STEPS + 1)}})


def build_ladder(mesh):
    """A ladder that telescopes: a base on the ground and eight steps, each its own part.

    Unity spreads the steps evenly between the ground and the wearer's feet,
    so here they stand at full stretch, one step height apart. Each step
    carries the length of rail below it. Closer together than this the
    rails overlap, which a flat colour hides; the top step is the platform
    the feet stand on, with its top face at its bone.
    """
    half_width = 0.24

    def slab(part, centre, half_x, half_y, thickness, side, top):
        rings = [chamfered_rect(Vector(centre) - UP * drop, X, BACK, half_x, half_y, 0.012) for drop in (thickness, 0.0)]
        part.loft(rings, side, cap_start=side, cap_end=top)

    # The root bone carries nothing. Unity hides the ladder by scaling each
    # part to nothing, and cannot scale the root: the steps hang from it.
    base = mesh.part("Base")
    slab(base, UP * 0.025, 0.32, 0.20, 0.025, "ink", "violet")

    heads = {"Base": UP * 0.0}
    for step in range(1, LADDER_STEPS + 1):
        name = f"S{step}"
        origin = UP * (step * LADDER_STEP_HEIGHT)
        part = mesh.part(name)
        last = step == LADDER_STEPS
        if last:
            slab(part, origin, 0.31, 0.23, 0.035, "magenta", "panel")
        else:
            slab(part, origin, half_width + 0.03, 0.085, 0.028, "lavender", "ice")
        for side in (-1.0, 1.0):
            rail = [ring_points(origin + X * (side * half_width) - UP * drop, X, BACK, 0.017, 0.017, 4, 0.5)
                    for drop in (LADDER_STEP_HEIGHT + 0.02, 0.03)]
            part.loft(rail, "violet", cap_start="violet")
        heads[name] = origin
    return heads


# --------------------------------------------------------------------------
# Zzz
# --------------------------------------------------------------------------

ZZZ = PropSpec("Zzz", "zzz", triangle_budget=220, parts={"Z1": None, "Z2": None, "Z3": None})


def letter_z(centre, size, depth):
    """The outline of a Z, `size` square, as two rings `depth` apart for a loft."""
    a, bar, slant = size / 2.0, size * 0.24, size * 0.34
    outline = [(-a, a), (a, a), (a, a - bar), (-a + slant, -a + bar), (a, -a + bar),
               (a, -a), (-a, -a), (-a, -a + bar), (a - slant, a - bar), (-a, a - bar)]
    return [[Vector(centre) + X * x + UP * z + FORWARD * side for x, z in outline] for side in (depth / 2.0, -depth / 2.0)]


def build_zzz(mesh):
    """Three Zs rising from a sleeper's head, smallest first. Each is its own part, so they can drift."""
    heads = {}
    for name, centre, size, swatch in (
            ("Z1", (0.07, 0.0, 0.06), 0.07, "pink"),
            ("Z2", (0.17, 0.0, 0.19), 0.10, "lavender"),
            ("Z3", (0.30, 0.0, 0.36), 0.14, "ice")):
        part = mesh.part(name)
        part.loft(letter_z(centre, size, size * 0.22), "ink", cap_start=swatch, cap_end=swatch)
        heads[name] = centre
    return heads


PROPS = [
    (FLIGHT_CLOUD, build_flight_cloud),
    (LADDER, build_ladder),
    (ZZZ, build_zzz),
]

if __name__ == "__main__":
    main_build(PROPS, REPO / "blender", MODELS)
