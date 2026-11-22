# Martinsville Speedway references

The catalog uses track ID 33 for the 0.526-mile asphalt and concrete oval in Martinsville, Virginia. The archived geometry notes in the track-builder workspace contain the original design, dimensions, and qualifying reference. The editable model is at `ArtSource/MartinsvilleSpeedway/MartinsvilleSpeedway.blend`. [Facility map](https://www.nascar.com/wp-content/uploads/sites/7/2024/04/02/Martinsville-2024.pdf)

## November 21, 2026 oval verification

- The practice car retained its pit through-lane offset up to the apron and snapped sideways on exit. The live car position now tapers that offset over the authored final fifth of the exit connector. The measured heading change dropped from 54.50 degrees to 1.11 degrees. The car completed its stop and was within 1.87 degrees of parallel when parked.
- Unity reflected the finish-centered FBX horizontally. It now uses the asymmetric restart-start paint as the horizontal orientation anchor. The strict preflight passed 16,979 probes in Unity, the reopened editable scene, and the clean FBX import with the alignment correction. It checked pavement, wall clearance, the finish stripe, and both restart endpoints.
- Keep the first `Restart zone endpoint` mesh at the authored start and its `.001` counterpart at the authored end when regenerating the FBX. The recorded restart location remains a model estimate.
- Track-builder evidence is in `TrackVerification/runs/layout-39m3dt8g` and `TrackVerification/runs/verification-hzz8bhhh`. The source measurements before this audit are in `MartinsvilleSpeedway/geometry-notes.md` in the same workspace.
