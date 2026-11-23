# Charlotte Motor Speedway

Concord, North Carolina. NASCAR 1.5-mile asphalt quad oval in Stock Car Manager, track ID 6.

## References

- `Charlotte Track Layout.pdn` is the layered overhead layout supplied through the preceding Charlotte layout work. Its driving footprint was derived from the game layout at that time. Its scenery includes estimated footprints. Use the active Unity track definition and procedural geometry as the controlling source for revisions to existing driving paths.
- The existing track definition specifies 24 degrees of banking in the turns, 5 degrees on the straights, a 15.24 m racing width, 40 stalls, and a 198.771 mph qualifying reference (27.167 seconds at 1.5 miles). Reuse these values for this revision.

## Generation instructions

- Preserve the current racing oval, apron, and pit-road footprint, including the current entry and exit paths.
- Use a continuous banking profile with no staircase in the pavement or car motion. Match connected car travel surfaces at their existing junctions, including the apron, pit connectors and infield garage access.
- Verify the candidate with Stock Car Manager's track checks before delivery.