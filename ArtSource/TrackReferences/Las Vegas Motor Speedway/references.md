# Las Vegas Motor Speedway

Las Vegas, Nevada. Modern NASCAR asphalt oval, game track ID 26.

## Source layout

- `Las Vegas.pdn` is the user's 1024 by 768, twelve-layer Paint.NET drawing supplied November 26, 2026. Its byte-for-byte copy is the design reference. `Layout.png` and the numbered PNGs under `Layers` extract its composite and individual layers.
- The black perimeter, central checker, straight backstretch, curved pit road, blue pit wall, and magenta infield wall define the layout. The northeast arrow is the north indicator. NASCAR traffic runs counterclockwise.
- Preserve the marked grandstands and press box, infield paving, all marked building and garage footprints, hauler and car parking, both RV and access-road networks, and exterior backstretch RV spaces. Keep both the marked infield wall and its access breaks.
- Add concrete pit work areas and equipment, pit crews, and a continuous 10 ft (3.048 m) rear walkway with concessions, trash barrels, restrooms and spectators on its protected side. Place golf carts, grills and campers near the RVs. Include spectators on the protected backstretch exterior near those RVs and behind the infield wall. Keep service routes, wall openings, and emergency access clear.

## Written specifications

- The operator specifies a 1.5-mile tri-oval with 20-degree turns and 9–12-degree straightaways. NASCAR describes 9-degree front and back straightaways. Use 9 degrees on the straight approaches and backstretch, with continuous transitions to the 20-degree corners. https://www.lvms.com/tracks/ and https://www.nascar.com/news-media/2020/02/19/las-vegas-101-tv-times-goodyear-tire-information-key-stats-more/
- The operator lists Kurt Busch's 2016 Cup qualifying record as 27.505 s (196.328 mph). NASCAR lists 27.498 s (196.378 mph), also printed as the track record on the 2016 official timing sheet. Use 27.498 s and 196.378 mph for the qualifying reference. https://www.lvms.com/media/track-history/ and https://www.nascar.com/gallery/nascar-all-time-qualifying-records-by-track/ and https://www.jayski.com/wp-content/uploads/sites/31/2019/05/03lvms2016qualresults.pdf
- NASCAR specifies a 45 mph pit-road limit and a 55 mph caution-vehicle reference for Las Vegas. https://rbfiles.ndms.nascar.com/2025/02/2025-PIT-ROAD-SPEED-CHART_REVB.pdf
- The existing game width is 55 ft (16.764 m). Use it as an estimate until a survey or venue specification establishes the measurement. Use the drawing's checker for start/finish and retain the old definition's restart distances upstream of that stripe as estimates. The drawing gives no longitudinal elevation datum, so begin with a level inside racing edge and bank outward.

## Venue corrections

- Curve the press box with the frontstretch grandstand above start/finish.
- Park cars and infield RVs nose-first toward their adjoining roads. Point exterior RVs toward the track. Use the apron asphalt material for the infield roads, car parking, paddock, and pit road with its connectors.
- Leave a pedestrian aisle between spectator groups in the grandstand.
- Run the ten-foot concrete walkway directly behind the pit work area, from the first stall boundary to the last. End the walkway flush with the work area at each end.
- Fill the marked RV pads densely, with approximately 5 ft (1.524 m) between rigs and clear access roads. Infield RV pads are grass.
- Include a live flag stand and fence-mounted warning lights. Develop a fresh preferred line for the rebuilt geometry and present it for review.

## Verification

The pre-scenery layout passed the Unity, Blender, and FBX preflight, including 41,315 probes. The current live Las Vegas configuration separately passed its four baseline Unity checks. The user approved the full-lap preferred line. The driven practice stop passed, with a maximum handoff travel of 0.520 m, no vertical discontinuity, and a parked heading 0.07 degrees from the pit tangent. The initial line needed a local numerical correction in Unity, within 0.102 m of the approved preview. The final Unity suite passed all seven Las Vegas checks. Forty race cars and the pace vehicle completed the rolling start with at least 2.486 m of lateral separation. The largest pace-bay departure rotation was 3.78 degrees per simulation step. The final matching Unity, Blender, and reimported FBX passed 41,315 layout probes. The edited scene contains 923 RVs, with a measured minimum footprint gap of 1.541 m. Dedicated wreck recovery routing and a standalone player verification remain pending.


Evidence in the Stock Car Manager chat workspace: TrackVerification/runs/verification-036r9me2/summary.json and TrackVerification/runs/layout-_6fo94x4/custom-result.json.

## Approved backstretch line

The driver approved the extended wall-side line after testing it in game. It reaches the wall 30 m later off Turn 2 and starts the mirrored Turn 3 entry 30 m earlier than the previous preview. The line holds 380 m of straight running with 0.26 m nominal right-tire clearance. The approval reference updates only changed Las Vegas rows using offline float32 reprojection anchored to the previous Unity capture. The short-chord float32 approximation bottomed out at -0.00233 degrees. A new Unity probe was not run. See `PreferredLineApproval.json` and `LasVegas-Preferred-Line.png` in `ArtSource/LasVegasSpeedway`.

## Frontstretch apron, infield paving, and pace bay

The frontstretch apron is 5.4 m wide between tapered approaches. The original width and attachment points remain at both pit connectors and elsewhere on the lap. Infield asphalt fills gaps against the pit divider and infield walls and joins the pit road at both ends. The infield access roads use continuous outlines fitted to the layout. Their three separate components and three grass islands were preserved.

The pace car parks in a pull-through recess beyond the last Turn 1 pit stall. Both directions join pit road before its exit. The old apron bay and its infield loop were removed. Main-grandstand seat rows follow the bench axes and face the adjacent racing surface.

The editable Blender scene and the reimported FBX passed the apron, pavement seam, and pace vehicle swept-footprint checks. The C# source compiled against the Unity project references. A Unity play session was not run for this revision.

The apron, pit road, and 32 infield and pit paving meshes share one asphalt material in the Blender source and reimported FBX. The 48 spectator bays place their 25 rows above the same aisles. A full-race seat layout leaves 1.21–1.95 m clear across the 1,175 aisle crossings.
