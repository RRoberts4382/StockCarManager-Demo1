# Michigan International Speedway references

Venue locality used in this project: Irish Hills, Michigan. Track ID 11.

## Source layout and generation instructions

- `MichiganTrackLayout.pdn` is Rob's 669 by 400 Paint.NET source, dated November 22, 2026. Preserve the layered original. The flattened image and measured color regions are in the build reference folder.
- Recreate the full layout with its service roads, racing surface, outside wall and catch fence, continuous grandstand, press box, grass, pit divider, infield asphalt, the two pit-facing glass buildings, three garages, two RV areas, and the hauler lot.
- Use a continuously curved D-Oval frontstretch and a straight backstretch. Ease Turn 2 exit and Turn 3 entry into the backstretch with a single curvature transition at each join. Run counterclockwise, matching the previous Michigan definition and existing race direction.

## Dimensions and estimates

- NASCAR documents a 2-mile asphalt D-Oval, a 3,600 ft frontstretch, a 2,242 ft backstretch, and 18-degree turns. The operator lists 73 ft turn width. The build fits the supplied schematic and normalizes the runtime datum to 3,218.688 m. Stretch endpoints in the procedural model are estimated from the diagram rather than survey stations. [NASCAR dimensions](https://www.nascar.com/news-media/2019/06/05/michigan-101-rules-package-goodyear-tires-live-coverage/) · [Venue operator](https://www.mispeedway.com/about-us/)
- Retain 12-degree frontstretch and 5-degree backstretch banking from the game baseline, with 18 degrees in the turns. Later NASCAR coverage supports the 12-degree curved frontstretch. Earlier NASCAR statistics list different straightaway angles. The flat apron and elevations pivot at the inside racing edge, with zero datum where the drawing provides no survey grades. [Later banking reference](https://www.nascar.com/news-media/2020/08/05/michigan-101-tv-times-key-statistics-revised-procedures-and-more/) · [Earlier banking reference](https://www.nascar.com/news-media/2015/08/10/stats-advance-pure-michigan-400/)
- Retain the measured 34.857-second, 206.558 mph NASCAR Cup qualifying reference set by Jeff Gordon on August 15, 2014. The modern Cup pit limit is 55 mph. The pit lane width, 40 stalls, apron width, and estimated restart endpoints use the prior game dimensions where the PDN has no scale annotations. [Qualifying record](https://www.nascar.com/news-media/2014/08/15/jeff-gordon-wins-coors-light-pole-award-at-michigan/) · [NASCAR pit speed chart](https://rbfiles.ndms.nascar.com/2025/02/2025-PIT-ROAD-SPEED-CHART_REVB.pdf)

## Prior game build

Track ID 11 uses the modeled 2-mile asphalt oval. The November 22, 2026 pit revision keeps the authored layout and qualifies normal player pit calls against the lane at the exact crossing. It arms the request before lane planning, allows enough time to settle on the apron, and retains a committed approach as speed changes.

The final Unity release suite passed. Evidence is in `TrackVerification/runs/verification-i3155ab_` in the track-builder workspace.

## Recreated venue

- The editable scene and generator are in `ArtSource/MichiganSpeedway`. The exported venue loads as `Assets/Startup/Resources/Startup/MichiganSpeedway.fbx` using catalog track 11. The existing 006 runtime draws and follows the magenta preferred line.
- The native spline, editable scene, and reimported FBX passed the final layout preflight across 55,332 mesh and clearance samples. The measured pit handoffs had no gap. The second tangent difference was 0.02 degrees.
- Unity measured no backstretch datum deviation or lateral reversal in 006's preferred path. A clear-running car tracked the sampled line at racing speed. The driven practice check completed a pit entry at 59.70 seconds, stop at 69.52 seconds, and exit at 123.27 seconds. The imported venue check found 30 seating sections, 96,380 seating positions, 40 live haulers, and 78 switchable venue lights.
- Evidence in the track-builder workspace is under `TrackVerification/runs/layout-k0vi06a1` and `TrackVerification/runs/verification-bmsgcfcg`. The historic Michigan frontstretch fixture represents the earlier layout and remains available for comparison. The recreated route differs from that fixture and awaits approval before replacement.
