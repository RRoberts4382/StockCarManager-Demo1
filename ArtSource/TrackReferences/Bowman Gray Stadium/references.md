# Bowman Gray Stadium references

The live catalog uses track ID 15 for the 0.25-mile stadium oval. The game models an infield pit road. NASCAR's 2025 Clash used a temporary external garage route behind the fieldhouse and staged qualifying in smaller groups. [NASCAR event logistics](https://www.nascar.com/news-media/2024/12/13/cup-series-2025-bowman-gray-clash-schedule-logistics-inspection/) · [NASCAR qualifying format](https://www.nascar.com/news-media/2025/01/31/format-revealed-for-2025-cook-out-clash-at-bowman-gray-stadium/)

## November 22, 2026 connector revision

- Refit the entry and exit controls within the existing simulated pit layout. The native approach angles at the through-road joins fell from 33.41 and 34.69 degrees to 1.11 and 1.29 degrees. All 989 sampled pavement positions hit within tolerance. The connector endpoint coordinates and 23 painted stall positions are preserved.
- The 104.55 m through road does not have room for a 24th wall-backed bay at 4 m spacing and sufficient clearance for the final car's departure. The automatically assigned extra car overlaps the end of the painted run and blocks the field's release. A revised pit staging design or a verified external route is needed to resolve this capacity limit.
- Evidence is in `TrackVerification/runs/oval-audit-2026-11-21/bowman-gray-diagnostic` in the track-builder workspace.
