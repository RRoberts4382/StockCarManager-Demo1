# Reusable venue components

Richmond and North Wilkesboro share Blender mesh construction, venue locators,
vehicle shapes, and floodlight assemblies. Each venue retains its own layout,
materials, placements, and live Unity locator names. The continuous grandstand
builder follows a curved station range and leaves stepped aisles between seats.

Use the component rebuild when changing grandstands, frontstretch spectator
parking, venue lighting, or North Wilkesboro's catch fence. It opens an existing editable scene, regenerates
only the selected component, checks the rest of the scene for changes, and writes
a separate candidate Blender file, FBX, and verification report. The full FBX
is re-exported so Unity can import one model using the normal resource path.

```powershell
blender -b --factory-startup --python-exit-code 1 --python VenueComponents/rebuild_component.py -- --track north-wilkesboro --component grandstands --blend NorthWilkesboroRebuild/geometry/NorthWilkesboroSpeedway.blend --out VenueComponents/runs/north-grandstands
```

Use `richmond` or `north-wilkesboro` with `grandstands`, `spectator-parking`,
or `lighting`. North Wilkesboro also supports `catch-fence` to rebuild posts and
rails on the existing wall cap. At North Wilkesboro the grandstand scope replaces the curved
Turns 1 and 2 run. The installed scripts also work from `ArtSource/VenueComponents`
with the editable scene under the track's ArtSource folder.

For component verification, check the rebuilt object names and the
component-verification.json output. For the new curved stand, run
`verify_curved_stands.py` in Blender with the candidate FBX and an output JSON path
after `--`. It measures adjacent section seams and aisle treads on both decks.
Use the shared Unity runner for affected checks during iteration and its full
suite before delivery. Use the layout preflight if the underlying driving layout
changes.
