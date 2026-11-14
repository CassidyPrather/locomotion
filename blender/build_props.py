"""Builds the locomotion prop: the cloud that carries a flying avatar.

    blender -b --python blender/build_props.py [-- --only STEM] [-- --previews DIR]

The prop is modelled from the code below, saved as `blender/<stem>.blend`,
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

from wirenook_props import PropSpec, Puff, cloud, cloud_tones, main_build

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


PROPS = [(FLIGHT_CLOUD, build_flight_cloud)]

if __name__ == "__main__":
    main_build(PROPS, REPO / "blender", MODELS)
