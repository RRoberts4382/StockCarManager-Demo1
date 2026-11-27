# Dover International Speedway

Dover, Delaware

Colored blocks and boxes added to the reference photos are location markers only. Keep them in the reference images and omit them from the track model, textures, and in-game rendering.

## Track shape and pit road

[Original diagram](Track-Shape-and-Pit-Road.png)

- Rob supplied this diagram to show the basic track shape and position of pit road.
- The diagram shows an oval with the frontstretch and start/finish at the bottom. Pit road lies inside that straight between Turns 4 and 1.
- The diagram is labeled Dover Motor Speedway. Use Dover International Speedway as the project track name.

## Supplied track dimensions and facility details

Dover is a symmetrical, high-banked concrete oval known as the Monster Mile. The track opened in 1969 and changed from asphalt to concrete in 1995.

| Feature | Supplied detail |
| --- | --- |
| Lap length | 1.000 mile (1.609 km) |
| Banking | 24° in the turns and 9° on the straightaways |
| Straightaway length | 1,076 ft |
| Racing surface width | 48 ft on the straightaways and 58 ft in the turns |
| Safety apron width | 10 ft on the straightaways and 21 ft in the turns |
| Elevation change | 23.5 ft at the peak of the turns (roughly 2.1 stories) |
| Pit road dimensions | 1,580 ft long and 47 ft wide |
| Pit stalls | 40, equipped with water and electricity |
| Seating | 58,500 in the supplied track facts, with approximately 54,000 permanent seats reported in recent coverage |

### Features to capture in the model

- A smaller harness horse-racing track occupies the infield.
- Two pedestrian walkways cross over the racing surface.
- Dover has no race lighting.

### Supplied source links

- [Dover Motor Speedway media and track facts](https://www.dovermotorspeedway.com/media/)
- [Dover Motor Speedway history and facility overview](https://www.dovermotorspeedway.com/about/about-us/)
- [Dover Motor Speedway on Wikipedia](https://en.wikipedia.org/wiki/Dover_Motor_Speedway)

## Annotated view from outside Turn 3

[Original annotated photo](Turn-3-View-Annotated.png)

- **Red box** — Pedestrian walkway over the track at the entrance to Turn 3. The second walkway crosses at the exit of Turn 2.
- **Blue box** — Grandstands outside Turns 1 and 2.
- **Yellow box** — Three-story building outside the entrance to Turn 1.

## Turns 3 and 4 grandstands and infield

[Original Turns 3 and 4 photo](Turns-3-and-4-Grandstands-and-Infield.png)

- Use the photo as the height reference for the tall, tiered grandstands outside Turns 3 and 4.
- Preserve the curved grass strip between the apron and the infield wall through these turns.
- On the infield side of the wall, use the photo as a reference for the asphalt area and the light-roofed infield building visible at the left.

## Annotated overhead layout

[Original overhead photo](Overhead-Layout-Annotated.png)

- The frontstretch and start/finish line are in the upper half of this photo.
- Correct for the camera angle in this photograph. Dover's racing surface and outside edge form a smooth, symmetrical oval. Use the photo to place surrounding features.
- **Red block** — The smaller infield harness track.
- **Blue block** — The stock-car racing surface.
- **Green box** — The frontstretch grandstands.
- Both pedestrian walkways are visible at the bottom of the photo, at the Turn 2 exit and the Turn 3 entrance.
- Use this view to place the infield buildings, trace the roads, and locate the asphalt pads. Preserve the grass areas between and around the paved sections.
- Tan and brown patches within the grass are exposed dirt where the grass has died. Keep those areas distinct from the asphalt pads and roads.
- Use concrete for the stock-car racing surface and asphalt for the other paved surfaces, including the apron, pit road, infield roads, and pads.

## Hauler parking

[Original hauler parking annotation](Overhead-Hauler-Parking-Annotated.png)

- The purple line locates the curved row of hauler parking on the right side of the overhead photo, between the harness oval and the infield buildings.
- Use the white stall stripes in the photo to trace the actual spaces. The purple line is a reference annotation only.

## Emergency vehicle access

[Original emergency access annotation](Overhead-Emergency-Access-Annotated.png)

- The yellow box at lower left identifies the infield emergency vehicle access by Turn 2, close to the Turn 2 pedestrian overpass.
- The yellow box at lower right identifies the access at the Turn 3 end, above and to the right of the pedestrian overpass in the photo.
- Trace the pavement and wall gaps at these openings. Keep the full width of each route clear. The yellow boxes only locate the openings in the reference.
- Give the infield wall smooth, flowing curves. Preserve the grass setback and the emergency access gaps.

## Written specifications and qualifying benchmark

- The operator confirms the 1.000-mile concrete oval, 1,076-foot straightaways, 9-degree straightaway and 24-degree turn banking, 48/58-foot racing widths, 10/21-foot apron widths, and 1,580-foot pit road with 40 positions. The operator lists asphalt for the apron and pit road and concrete pit boxes. This preview uses asphalt outside the racing surface as requested. [Dover Motor Speedway track facts](https://www.dovermotorspeedway.com/media/)
- Denny Hamlin set the NASCAR Cup single-lap qualifying benchmark in a Toyota on October 5, 2019, with 21.559 seconds at 166.984 mph. The speed calculated from the timing and 1.000-mile length is 166.983626 mph. [NASCAR qualifying report](https://www.nascar.com/news-media/2019/10/05/hamlin-scores-busch-pole-award-at-dover-for-500th-cup-start/)
- NASCAR documented a 140-foot (42.672 m) restart zone before the start/finish stripe at Dover in October 2015. The model uses that length, with estimated endpoints positioned on the Turn 4 approach to start/finish. [NASCAR restart-zone notice](https://www.nascar.com/news-media/2015/10/01/nascar-doubles-restart-zone-for-dover/)

## Generation instructions

- Extend the Turn 1 grandstands continuously through Turn 2 to the pedestrian bridge at the exit. Maintain the same grandstand height as Turn 1 throughout the extension.
- Begin the Turn 4 grandstand just after the Turn 3 entrance pedestrian bridge in race direction. Continue it through Turn 3 toward Turn 4.

## Blender model review

- Built the symmetrical concrete oval, asphalt apron and pit road, 40 pit stalls, curved inside and outside barriers, and both pedestrian overpasses. The Turn 1–2 stands extend at constant seating height to the Turn 2 bridge. The Turn 3–4 tiers start just beyond the other bridge.
- Used the rectified aerial to place the harness oval, infield buildings, exposed dirt, roads, pads, two emergency accesses, and 32 hauler bays. The garage has 20 doors on each long side.
- Modeled 48/58 ft as the width measured across the banked surface. The inside edge and apron share a 0.06 m elevation datum. Banking is blended over 81 m at each tangent and produces a 7.19 m maximum rise across the pavement. Building heights, bank transition stations, and the restart-zone position are estimates. The full modeled pit path including tapers is about 488.8 m. The operator lists 481.6 m for pit road.
- Kept the new preferred line as a proposed, separate Blender curve. Offline checks measured minimum tire clearances of 0.28 m at the outside edge and 0.18 m at the inside edge for the nominal 1.8 m tread. Both straight joins reach zero curvature, and sampled heading never required a rightward turn.
- Verified the saved Blender scene and fresh FBX import with 53,026 upward-facing pavement and marking triangles each. The height comparison used 3,200 racing and apron samples per asset and measured a maximum FBX error of 2.36 mm. Another 725 samples per asset passed on pit and emergency routes. Back-face-culling views and wall clearance checks passed.
- The editable model, FBX, geometry report, day previews, and proposed line image are saved under `outputs/Dover International Speedway` in the shared workspace.

## Stock Car Manager integration

- Installed as track 36. Dover is available in Test Practice and in newly generated Cup schedules. The venue is daytime only and uses the one-mile concrete oval, 40 pit stalls, the approved grandstand extents, and both pedestrian bridges.
- The new preferred line follows the path reviewed with the model. Unity checked 6,438 positions around the lap, measured 0.608 m minimum tire clearance, and found no rightward steering. The qualifying benchmark is 21.559 seconds.
- The pace car uses the protected bay off the infield side of pit road. The approved emergency openings are connected by paved access routes. The response uses the Turn 2 gate for dispatch and loaded entry, and the Turn 3 gate for empty exit from the recovery area. Local asphalt connections join the existing paved areas.
- The final layout passed 27,041 Unity, Blender, and FBX probes. The full seven-check Unity suite also passed, including the catalog, solo test scenery, pit practice, full-field rollout, and a simulated wreck that verified tow coupling, delivery, return to the bays, and a green restart.
- The [editable Blender model](../../DoverInternationalSpeedway/DoverInternationalSpeedway.blend) matches the installed game export. The captured Chase, Spotter, and overhead images are in the shared workspace under `outputs/Dover International Speedway`.

## Pit entry, Turn 1 wall, and pit promenade corrections

- Start the pit-entry blend earlier in Turns 3–4. The revised entry begins about 66.5 m earlier and is 119.3 m long. Fit the divider and perimeter opening to the same smooth approach. Preserve the approved racing line and pit-exit route.
- Shape the Turn 1 infield wall tip to the user's red guide in `Turn-1-Wall-Target.png`. Relative to `Turn-1-Wall-Before.png`, the tip extends 2.68 m toward pit exit and sits 1.72 m farther infield. The curve rejoins the normal wall without a kink. The widened wall setback adds apron clearance through Turns 1–2.
- Keep spectators off the entire pit entry and exit pavement. Two imported spectators moved behind the Turn 4 wall. The infield operations building and pad moved away from the pit-exit approach.
- Use a visibly distinct 10 ft concrete walkway behind the pit work areas, interrupted at the existing infield crossing. Keep signed concessions and trash barrels on the protected side of the walkway. The revised promenade provides three stands and ten barrels.
- Offline route, car footprint, pavement, wall, and scenery checks passed against the corrected Blender and FBX assets. The Unity session verification for this revision is pending. Reports and Blender overhead views are in `DoverGameStage/Corrections` and `DoverGameStage/route-revision` in the shared workspace.

## Turn 2 apron narrowing

The first wall correction cleared the nominal apron center but left the game tracking it at the fixed lane-change rate. Through the narrowing at Turn 2 exit, cars could lag infield and cross the wall. Dover now carries the apron center's geometric motion through each actual travel step while retaining the existing steering rate for intentional lateral movement. The update applies to pit-entry and pit-exit cars while on the apron.

Offline 60 Hz checks against the editable Blender model and the installed FBX reproduced the old wall crossings. With the corrected lane update, the 3.10 m by 2.16 m test car cleared the wall and stayed on pavement at 22 to 75 m/s, including starting offsets up to 0.35 m either side of center. The C# compiled against the project references. A Unity drive-through is pending. See `DoverGameStage/ApronTracking` in the workspace.
