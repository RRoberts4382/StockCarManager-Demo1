# Martinsville Speedway — driving geometry

Track ID 33, Martinsville, Virginia. Built November 17, 2026 using stock-car-track-builder. No previous Martinsville definition was present; no existing track was replaced. The startup Test Practice picker includes this definition through its existing catalog filter.

## Banking rebuild on November 19, 2026

- The straightaways stay at 0° and the turn cores reach 12°. The venue operator specifies 12° in the turns, and NASCAR's event specifications also give 0° for the straightaways. A separate NASCAR track profile lists 11° for the turns. The model uses 12° on the strength of the venue and event specifications. [Venue specifications](https://www.martinsvillespeedway.com/2022/04/06/nascar-hall-of-fame-martinsville-speedway-unveil-75th-anniversary-exhibit/) · [NASCAR event specifications](https://www.nascar.com/2025/cook-out-400) · [NASCAR track profile](https://www.nascar.com/gallery/martinsville-speedway-track-profile/)
- Each transition uses one quintic blend over approximately 60 m. The transition length and level inside-edge pivot are model estimates. They keep the straightaways flat, allow full banking through each turn core, and keep the apron level at the pit handoffs. The lap, pit routes and stalls, qualifying reference, and race marker fractions retain their previous values.
- Blender and Unity evaluate the same four transition intervals by distance around the lap. Unity's layout sampler keeps its original interpolation for tracks without authored transitions. Martinsville car pitch follows the lane over the displayed wheelbase. The racing mesh shares vertices and normals across the asphalt/concrete boundary. Its adjoining apron and outside wall use the same stations and pavement edges.
- Across all four joins, measured outer-edge longitudinal grade peaks at 9.54%, down from about 37%. The straight-to-turn transitions each have one rise and fall in grade. Centerline peak grade is 5.46%, and the inside edge is level. The lap seam closes in height, banking and car pose. The FBX round trip preserved racing vertices within 0.011 mm and confirmed upward-facing pavement and marking triangles. The apron join and outside wall base coincide with the racing edges, and the pit pavement clears the racing surface.
- Offline verification compared Unity's sampled path against the imported FBX at 3,452 lap stations across eight lane offsets. The largest height difference was 5.4 mm. All 31 other catalog tracks matched their baseline samples. Pit checks measured a maximum lane-offset join error of 0.115 ft and 0.622°, with both apron handoffs within one inch and a minimum car footprint clearance of 3.554 ft from the racing edge.
- A headless Unity editor imported and placed the actual resource FBX. The game car placement method was sampled at five lane offsets around the full lap, with 16,940 comparisons to the imported pavement at approximately 0.25 m intervals. Maximum height error was 6.15 mm and peak car pitch was 5.39°. Martinsville remained in the track catalog and a generated season. Interactive Test Practice and live pit traffic await a Play Mode drive.
- A controlled warm-tire VehicleSpeed003 simulation ran 21.233 s versus the previous 21.077 s with the same inputs. The 18.898 s qualifying reference and 1.15 corner calibration remain in the definition.

## Specifications and references

- 0.526-mile paperclip oval, 55-foot racing width, 800-foot straightaways, 12-degree turns. [Track operator specifications](https://www.martinsvillespeedway.com/2022/08/24/martinsville-speedway-to-celebrate-75th-anniversary-of-first-race-with-martinsville-speedway-day-on-sept-7/) and [track history](https://www.martinsvillespeedway.com/track-history/).
- Concrete inside half of both turns, asphalt outside half and straightaways, as requested. Pit pavement is concrete and the apron asphalt, based on the aerial reference. Surface boundaries are approximations, not a survey.
- User confirmed the real Turn 3 entry → Turn 2 exit route, superseding the original Turn 1 → Turn 4 request. Pit road follows the inside of turns 3–4, the frontstretch, and turns 1–2. The approach and rejoin use the backstretch apron. [Official facility map](https://www.nascar.com/wp-content/uploads/sites/7/2024/04/02/Martinsville-2024.pdf), [pit-route description](https://speedwaydigest.com/index.php/news/nascar-cup-series-news/30347-stp-500-at-martinsville-preview/).
- Pit-road speed 30 mph. [NASCAR pit-road speed chart](https://rbfiles.ndms.nascar.com/2025/02/2025-PIT-ROAD-SPEED-CHART_REVB.pdf).
- Qualifying benchmark: Joey Logano, March 28, 2014, 18.898 seconds / 100.201079 mph, Cup single-lap record. [NASCAR all-time qualifying records](https://www.nascar.com/gallery/nascar-all-time-qualifying-records-by-track/). Racing-Reference was searched but did not supply a better matching record. The benchmark is stored in JSON and Blender metadata; it is reference pace, not a guaranteed simulated lap.
- Aerial reference downloaded as reference-aerial.jpg: [ESPN photograph](https://a.espncdn.com/photo/2018/0320/r344047_1296x729_16-9.jpg), [associated article](https://www.espn.com/racing/nascar/story/_/id/25080386/2018-nascar-cup-series-race-martinsville-speedway-brings-unpredictability-playoffs-says-ricky-craven).

## Estimates and implementation

The published lap distance is represented by the game centerline; the source does not provide a surveyed measurement line. The final Catmull-Rom centerline measures 846.5149 meters (0.526 mile). Straight/turn joins have a short curvature blend; both straight cores remain straight. Banking blends independently into the turns.

Estimated dimensions: 26.2-foot apron, 39.4-foot pit pavement, 3.5-foot walls, 43 stall locations approximately 30.9 feet apart on the inside stall baseline. Pit through lane is separate from the stall lane. The restart zone spans approximately 141 to 39 feet before the frontstretch timing stripe; exact endpoints were not verified. The 500-lap, 130/130/240-stage definition is a development-event default, not a claim about every modern race distance.

Blender source, JSON and FBX share coordinates and route controls. Three editable pit sections form one connected travel route. Explicit endpoint handles preserve matching headings in Unity's open Catmull-Rom interpolator. The game uses these authored curves for Martinsville instead of replacing an entire connector with a chord. This avoids cutting across its paperclip ends. Physical-space pit avoidance and collision checks use the same geometry. Rear-axle steering is retained. Bristol and all existing track definitions are unchanged. The existing five-lap practice pit test remains enabled.

No scenery structures were generated, in accordance with the narrowed skill scope. Walls, apron, racing/pit surfaces, timing stripe, restart zone and pit markings are included. Asphalt and concrete receive matte textured runtime materials.

## Validation

Follow-up: user completed a Unity test with successful pit entries/exits and no stack-ups or collisions. The pavement was invisible because its ribbon faces pointed down; the two-sided Blender preview hid this error. The generator now orients all pavement and marking polygons upward and asserts their normals. Reimporting the exported FBX in Blender verified all 4,582 pavement polygons face upward. Source and exported mesh were replaced in the project.

Martinsville-only VehicleSpeed003 corner calibration is 1.15, applied before setup/tires/load/weather penalties and capped at the existing straightaway demand. A controlled warm-tire run (200°F, full fuel, recorded short-track baseline setup, no traffic or off-line penalty) changed minimum speed from 52.1 to 60.5 mph. The same harness with preferred-line tracking also exhibits slower transient speeds when the car is off line; this penalty remains intentional and was not removed. The 60 mph target is a healthy, on-line reference, not a universal speed floor. VehicleSpeed001/002, braking limits, other tracks and pit paths are unchanged. Runtime rendering and pace after these corrections await the next Unity test.

- Saved Blender reopened and overhead/oblique renders inspected.
- Final Unity scripts compile using the project's compilation references, without launching Unity.
- TrackDefinition validation and Test Practice picker eligibility pass.
- Actual Unity TrackLayout/TrackPitSpline geometry checked offline, including lane-offset joins, full 9.5-by-5.9-foot car footprint clearance from the racing surface, and level pit/apron handoffs.
- Pit section joins: maximum offset-position difference 0.115 foot, heading difference 0.622 degree. Both apron handoffs within one inch. Minimum sampled moving-car footprint clearance from racing edge 3.554 feet.
- Runtime material import, dynamic occupied-stall maneuvers, complete pit transit, lap timing and frame rate remain pending the user's Unity test. Offline geometry checks do not establish collision-free operation in Play Mode.

Rebuild with Blender 5.3: `blender --background --python build_martinsville.py`. The importer script lives in the workspace delivery folder. The editable source is also copied into the project's ArtSource/MartinsvilleSpeedway folder.
