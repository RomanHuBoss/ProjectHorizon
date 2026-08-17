# Third-party art inputs

## Kenney — Space Kit 1.0

TASK-216 uses a small reviewed subset of **Kenney Space Kit 1.0** as authoring inputs. Upstream: `https://kenney.nl/assets/space-kit`; reviewed mirror source: `https://github.com/ETdoFresh/kenney.nl/tree/master/space-kit-1.0/Models`. The checked-in upstream `License.txt` declares **Creative Commons Zero (CC0)** and permits personal, educational and commercial use.

Source authoring files under `tools/content/vendor/kenney_space_kit/`:

- `spaceCraft1.obj` — normalized into small ship equipment/avionics detail modules;
- `satelliteDish.obj` — normalized into the station communications dish;
- `metalStructure.obj` — normalized into station service truss details;
- `License.txt` — upstream license notice.

These OBJ files are **not loaded by the game at runtime**. `tools/content/generate-production-glb.py` consumes them offline, discards upstream material references, rematerializes the resulting geometry into the Project Horizon material palette and exports self-contained GLB assets. Shipping collision and gameplay identity remain Project Horizon-authored.
