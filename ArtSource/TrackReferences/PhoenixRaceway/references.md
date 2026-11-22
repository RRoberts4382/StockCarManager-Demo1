# Phoenix Raceway

Avondale, Arizona, United States. Modern NASCAR oval with the start/finish line relocated for the November 2018 event.

## Images

- [phoenix-track-diagram.png](phoenix-track-diagram.png) — user-supplied plan. The outer red route establishes the racing shape and direction. The inner red route is pit road. The checker gives the start/finish location.
- [phoenix-overhead.png](phoenix-overhead.png) — user-supplied overhead showing the infield buildings, access, and grandstand placement. Image credit printed on the attachment is Google Earth and Airbus (2025). Use only as a placement reference. The plan governs the new track shape.

## Generation instructions

- Build the racing geometry from the supplied plan and current written specifications. Replace the previous Phoenix catalog definition in place to retain track ID 9 for career compatibility.
- Place a paved apron around the entire inside edge. Use an asphalt infield inside pit road, with concrete pads in the pit work areas.
- Place infield structures, grandstands, access roads, parked cars, and RVs from the user overhead. Include protected fans, working venue and pit lights, pits with tools and crew, the flag stand and officials, and warning lights. Apply the active Stock Car Track Builder guidance.
- Reference annotations identify venue features for modeling. Preserve the user's intended driving layout and keep actual scenery clear of race, pit, garage, and emergency access.

## Written specifications

- One-mile asphalt oval, 1,609.344 m. Turns 1 and 2 bank 8–9 degrees, turns 3 and 4 bank 10–11 degrees. The turn numbering follows the post-2018 start/finish relocation. NASCAR lists the frontstretch at 1,551 ft and backstretch at 1,179 ft. [Phoenix Raceway](https://www.phoenixraceway.com/about-phoenix-raceway/) and [NASCAR 2020 Phoenix 101](https://www.nascar.com/news-media/2020/11/04/phoenix-101-tv-schedule-playoff-stats-goodyear-info-odds-more/)
- The earlier frontstretch widening from 52 to 62 ft gives a useful width range for fitting the supplied visual reference. [NASCAR layout history](https://www.nascar.com/gallery/flashback-phoenix-raceway-changes-through-the-years/)
- Use 45 mph pit-road limit. [NASCAR pit-road speed limits](https://www.nascar.com/news-media/2021/01/26/nascar-pit-road-speed-limits-for-2021-season/)
- Qualifying reference for the relocated start/finish layout: Ryan Blaney, NASCAR Cup, March 8, 2019, 25.480 s over 1.000 mile, 141.287284 mph calculated. NASCAR lists 141.287 mph. NASCAR's 25.147 s track record in the 2020 worksheet used the pre-relocation timing line in 2015. [2019 qualifying](https://www.nascar.com/news-media/2019/03/08/ryan-blaney-earns-busch-pole-award-at-ism-raceway/) and [2020 NASCAR statistics worksheet](https://www.nascar.com/wp-content/uploads/sites/7/2020/03/06/12004_QUALORD-Phoenix.pdf)
- The restart zone is before the start/finish line in the current counterclockwise direction. Exact endpoints, survey coordinates, elevations, apron widths, and service-road dimensions are estimated from the references. [NASCAR on the relocated line and restarts](https://www.nascar.com/news-media/2017/02/01/drivers-expect-plenty-of-action-with-new-start-finish-line-at-phoenix/)

## Delivered geometry and assumptions

- The work uses the supplied diagram for centerline, pit road and direction, and the supplied aerial for the relationship of grandstands, building groups, paddock, and parking. No old Phoenix layout or scenery was used. ID 9 preserves lookup for older career schedules.
- The modeled route is 1,609.344 m along the game centerline. The apron is continuous, about 6.10–49.00 m wide, with the greatest width at the dogleg. The apron shares the inside edge as the pivot and the elevation datum is flat where no survey was supplied. Race width is 15.85–18.90 m.
- The 461.32 m pit through lane has 40 marked, wall-backed stalls approximately 10.03 m apart. The adjoining pit work area is concrete. The large paved infield has 40 garage bays and 24 separate recovery bays.
- Frontstretch is 9 degrees nominal, backstretch is 3 degrees, and the dogleg varies within the modern range. [ARCA banking profile](https://www.arcaracing.com/2022/03/08/track-profile-phoenix-raceway/)
- Estimated restart brackets are 1,527.27–1,581.99 m along the lap from the start/finish seam, or 0.949–0.983 lap.
- Observed runtime qualifying lap pace is distinct from the 25.480 s NASCAR reference. The stored 141.287284 mph is read by the configured VehicleSpeed003 reference and lap-speed profile.

## Verification

- Blender and reimported FBX: 18,036 pavement/marking triangles checked and no inverted or degenerate faces. Full pit pavement sampled across width with no unplanned racing-surface overlap or outside-wall crossing. Maximum measured pit handoff tangent difference 0.0094 degrees, estimated outer-edge lap-seam grade step 0.0031 percentage points.
- Isolated Unity practice: loaded the authored Phoenix FBX through TrackPreview. 1,353 across-width probes covered the racing pavement, full lap apron and each pit segment, with 0.0076 m maximum height difference. A normal player pit request completed entry, stall service, departure and merge.
- Isolated Unity venue checks: 8,442 patrons in race seating and 4,221 in qualifying. Grandstand visibility toggled seating with the structures. Day/night switching exercised 49 venue fixtures and 24 occupied pit lights. Initial pacing and green exercised the flag stand and both green safety lamps. The pylon reported lap 1.
- Isolated Unity race recovery: two simultaneous injected wrecks exercised concurrent dispatch, coupling, hauling, release into separate bays, holding caution, and returning responders to their exact parking positions. Complete swept-envelope clearance around every scenery object and the full white/stage-finish/checkered flag matrix were not verified.

- A full native Unity project copy accepted a single Phoenix catalog entry. The game's regular Test Practice picker included it, and a new Cup career schedule included it.
- A fresh Windows player built successfully with the Phoenix FBX and runtime sources. Its track definition is byte-identical to the installed project track. The previous Windows player folder remains its earlier release.
- Verification artifacts: source geometry metrics and FBX winding reports in the model folder, and UnityCore.log, runtime/PhoenixVenueQA.log, UnityNative.log and WindowsBuild.log in the rebuild workspace.

## Grandstands, advertising, and race-state check

- Add upper seating decks, raise the exterior grandstand sections to at least twice their original height, and rake them for comfortable views from the track. Keep most of the seating and frontage visible. Increase the number of track-facing billboards just outside the wall where their full footprint fits and the stands stay visible.
- Blender reopening and FBX reimport measured every section at 2.37x or more of its former height, with final heights 23.71–25.67 m. Lower and upper decks each have 402 seat-row anchors. The geometry verification still found 18,036 upward-facing pavement and marking triangles.
- Unity’s billboard clearance grid now reads the individual imported faces rather than a venue-wide fixture bounding box. Verified 29 unique automotive ads at 4.5 m from the outside racing edge; the previous import produced zero.
- Unity sightline sampling checked 17 grandstand sections from three lanes and three stations at driver and broadcast heights (306 viewpoints). Mean visible row coverage by section was 75–88 percent. Two viewpoints at the flag stand/safety fixtures had partial obstruction. Billboard geometry did not account for either low-coverage point.
- Fixed the warning boards turning dark at the final checkered. They now stay green for a normal cooldown, or amber if the race ends under caution, and turn red when the field has parked. A separate restart cleanup corrected a two-way gate deadlock, ghost blockers from recovered wrecks, and a Phoenix cooldown pack repeatedly missing the apron entry.
- Unity race verification used 24 trucks in clean and late-caution 18-lap runs. All 24 received the rendered stage and finish flags; warning boards alternated both amber banks, returned to green on restart, and turned red after parking. A 30-lap collision stress race exercised seven concurrent/queued recoveries; all 17 active trucks received the stage and checkered signals, the field restarted, and all remaining trucks returned to pit stalls. Tests used the game’s native race-state, fixed-step driving, flag-stand meshes, and warning-lamp materials.
- The Unity-generated live scoring pylon is on the protected infield walkway about 70.5 m from the start/finish centerline. Spotter provides a clear view near the stripe. The default Player framing can leave it outside the view.

## November 21, 2026 oval verification

- The runtime now follows the authored 6.25 m apron offset at the pit approach and departure. It eases onto the regular apron within 70 m. The initial practice check measured about 60 degrees of heading change at entry and 52 degrees at exit. After correction, practice had at most 0.10 degrees of heading change and qualifying had at most 0.13 degrees. Both sessions completed their pit routing.
- Passed all three release checks and 29,580 strict Unity, editable Blender and FBX preflight probes. Full release evidence is in `TrackVerification/runs/verification-72rncrch`, layout evidence in `TrackVerification/runs/layout-wct3nf8e`, and qualifying telemetry in `TrackVerification/runs/oval-audit-2026-11-21/phoenix-diagnostic` in the workspace.
- A continuous divider at the current pit edge conflicts with the live apron and recovery envelope. Moving the first stalls downstream would clear the nominal crossing, but the downstream recovery pass and pickup envelopes still overlap the through lane. Clearances require a coordinated pit and apron design revision.
