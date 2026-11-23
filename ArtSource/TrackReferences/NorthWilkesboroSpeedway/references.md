# North Wilkesboro Speedway

North Wilkesboro, North Carolina. Modern paved 0.625-mile oval.

## Reference files

- North Wilkesboro.pdn contains the supplied seven-layer Paint.NET layout. It assigns pit and racing pavement, walls, infield surfaces, buildings, grandstands, press boxes, scoreboard, scoring pylon, team hauler parking, and RV parking.
- press-building-trackside.png and press-building-front.png show the press building highlighted in red, including its red base, continuous glass frontage, white fascia, covered viewing gallery, and open rooftop deck.
- scoreboard-infield-view.png shows the backstretch scoreboard viewed from inside the track. Its five car-number fields and lap field sit above a black-and-white checker strip.

## Generation instructions

- Use one continuous curved grandstand through Turns 1 and 2. Join the seating tiers and foundation across every section boundary. Keep ten regularly spaced stepped aisles, a rear upper-deck walkway and end stairs, and keep benches and spectators outside aisle space. The separate frontstretch grandstands remain separate.

- Recreate the venue on the supplied Paint.NET alignment and keep its separately marked backstretch scoreboard and infield scoring pylon.
- Match the press building and track-facing scoreboard to the supplied photographs. Update the five displayed car numbers and the leader lap from active timing.
- Keep the two small garages as scenery. Route a car that returns to the garage to the paved space behind its team hauler.

## Written specifications

- The [track operator's tire-test report](https://www.northwilkesborospeedway.com/media/nascar-cup-series-trio-takes-on-north-wilkesboro-goodyear-tire-test.html) describes the paved 0.625-mile oval, 50 ft pavement and 13 degree turns. The [operator reports a 2023 repave](https://www.northwilkesborospeedway.com/media/race-ready-repave-receives-rave-reviews.html).
- The [NASCAR track history](https://www.nascar.com/news-media/2016/09/29/thunder-in-the-hills-north-wilkesboro-20-years-later/) identifies the downhill frontstretch and uphill backstretch. The authored profile estimates grade from this description and the user layout. It uses the racing centerline as the banking pivot and a separate graded pit road.
- [NASCAR's 2026 event specifications](https://www.nascar.com/north-wilkesboro-speedway-cup-july-2026/) give the 450-lap race with stage ends at laps 80, 265 and 450.
- Use 18.905 seconds at 0.625 mile, or 119.016 mph, as the single-lap Cup reference. [NASCAR's official qualifying report](https://www.jayski.com/wp-content/uploads/sites/31/2025/5/16/all-star-openqual-results.pdf) lists Ernie Irvan's record.
- The [track operator's renovation notes](https://www.northwilkesborospeedway.com/media/north-wilkesboro-speedway-renovation-update.html) describe the original leaderboard.

## Build measurements and verification

- The Blender elevation model uses 4 m of relief and estimates 3 degrees of straight banking. The pit road and travel lanes were derived from the original Paint.NET layers.
- Unity 6000.6.8f1 ran 1,353 exported-pavement measurements across the racing surface, apron and both pit approaches. The largest error between the FBX and the native car-position sampler was 0.0162 m.
- Native sessions completed a normal pit stop and a park-and-release visit behind the player team's hauler. The race probe confirmed live running order and leader lap, stage flags, alternating yellow lights, dark warning lights at one to go, a controlled tow recovery, overtime restart, checkered flag, and complete end-of-session parking.
- The delivered 450-lap event definition uses stages of 80, 185 and 185 laps. The controlled verification race was shorter and went to overtime.
