# North Wilkesboro Speedway

This editable track uses the seven layers of the supplied **North Wilkesboro.pdn**. The track contour comes from the outer red trace. The checker sets the origin and arrow direction, and the race centerline sits half the published pavement width inside the trace. Separate layers set the pit divider, other walls, infield pavement, buildings, stands, and camping placement.

The 725 × 430 layout uses 1.347054221 metres per pixel. Blender X increases to the right of the image, and its Y increases toward the top. Station zero is at source pixel (357.018307, 301.279640) and the race direction is counterclockwise. Unity source data maps those Blender plan coordinates to X and Z. The origin marker checks the exported mesh orientation.

## Specifications and modeling choices

- The official lap is 0.625 mi (1005.84 m). The oval uses 50 ft (15.24 m) of racing pavement and 13° turn banking. The modeled straights use 3° of banking. [North Wilkesboro Speedway repave notes](https://www.northwilkesborospeedway.com/media/race-ready-repave-receives-rave-reviews.html)
- The model descends along the frontstretch and climbs along the backstretch. The 4 m relief and smooth transition at the pit entrance are modeled, since no surveyed elevation profile was available. [NASCAR track history](https://www.nascar.com/news-media/2016/09/29/thunder-in-the-hills-north-wilkesboro-20-years-later/)
- Pit speed is 30 mph. Two vehicle gates break the wall behind the 40 painted stalls. The closest stalls have 6.52 m of usable longitudinal space. [NASCAR pit road chart](https://rbfiles.ndms.nascar.com/2025/02/2025-PIT-ROAD-SPEED-CHART_REVB.pdf), [NASCAR pit stall renovation](https://www.nascar.com/news-media/2022/12/13/construction-at-north-wilkesboro-speedway-underway/)
- The 18.905 s Cup track qualifying record sets the speed benchmark. The 450-lap Cup race uses stage lengths of 80, 185, and 185 laps. [NASCAR timing report](https://www.jayski.com/wp-content/uploads/sites/31/2025/5/16/all-star-openqual-results.pdf), [Cup event schedule](https://www.jayski.com/wp-content/uploads/sites/31/2026/6/19/NORTH-WILKESBORO-SPEEDWAY_NCS_NCTS_CARS-EventSchedule_7_19_2026-created-6-16-2026.pdf)

## Editable source and checks

- Turns 1 and 2 use the shared continuous grandstand component with ten stepped aisles and upper-deck end access. To update the stands, lighting, or frontstretch spectator parking independently, follow the workspace's `VenueComponents/README.md`.

- Edit the scene in `NorthWilkesboroSpeedway.blend` or adjust the points in `wilkesboro_trace.json`. The sampled masks in `mask-1.csv` and `mask-4.csv` keep the trace generator reproducible without Paint.NET.
- Run `python wilkesboro_layout.py` to write the catalog definition. Build the editable and Unity assets with `blender -b --factory-startup --python-exit-code 1 --python build_wilkesboro.py`. Pass `-- --no-render` to skip the review images.
- Run `blender -b --factory-startup --python-exit-code 1 --python verify_wilkesboro.py` to check reimported pavement, pit routes, and walls. Run `verify_wilkesboro_scenery.py` in Blender with `-- --fbx` to check the furnished venue, live scenery anchors, and service routes against the same export.