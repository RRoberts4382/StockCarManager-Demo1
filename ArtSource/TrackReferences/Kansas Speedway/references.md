# Kansas Speedway references

The catalog uses track ID 32 for the post-repave Kansas City, Kansas tri-oval. The operator lists 1.5 miles of asphalt, 17 to 20 degrees of progressive banking in the turns, 9 to 11 degrees on the frontstretch, and 5 degrees on the backstretch. [Kansas Speedway track history](https://www.kansasspeedway.com/track-history/)

The stored benchmark is Kevin Harvick's 27.304-second Cup qualifying lap on October 3, 2014, equivalent to 197.773220 mph over 1.5 miles. [NASCAR qualifying records](https://www.nascar.com/gallery/nascar-all-time-qualifying-records-by-track/)

The original modeled Blender layout, the shipped FBX, and the catalog definition were saved in the track-builder workspace. The November banking audit records the existing roll and lane sampling decisions in `BankingAuditStage/geometry-audit.md`. Use the saved pit path and the existing estimated restart stations when refining the pavement and barriers.

## November 21, 2026 oval verification

- Unity follows the authored Kansas pit splines and bottom-lane attachments. A stopped through-lane car can separate slowly from an already overlapping car directly behind it while the swept checks continue to enforce clearance ahead. The final practice test stopped at 55.32 seconds and completed its exit at 103.28 seconds, with all 3,460 native pavement samples within tolerance.
- The asymmetric restart mark corrects horizontal FBX placement. The checker pattern is centered on the saved finish station. The original restart ticks remain on the recorded start and end stations. Both pit pavement joins now share exact vertices across the 18.288 m road. The shortened crew-side wall clears both garage mouths, and the track-facing divider stays outside the measured apron and driving envelopes.
- Corrected winding on the saved racing surface, apron, painted markings and three infield barriers. Verified the reopened scene and clean FBX import with backface culling. The final layout passed 44,027 Unity, Blender and FBX checks in `TrackVerification/runs/layout-ahq67enm` in the track-builder workspace.
