---
date: 2026-07-20
session: filter-bottom-blinding-concrete
---

# Journal: 2026-07-20 — Filter Bottom Blinding Concrete

## Context

Isolated-footing families may include a thin, wider blinding-concrete solid at the lowest elevation. Including that solid in geometry extents makes reinforcement use the blinding slab's footprint and bottom level instead of the structural footing.

## What Happened

- Geometry collection now keeps individual solids, removes solids explicitly identified as blinding concrete by subcategory or dominant face material, then calculates footing extents from the remaining structural solids.
- For families without usable metadata, a conservative fallback removes only the lowest separate slab when it is thin, wider than the solid immediately above, and vertically adjacent within configured tolerances.
- If filtering would remove every solid, the reader falls back to the original solids so geometry extraction remains available.

## Validation

- `Debug.R25` build: 0 warnings, 0 errors.
- Automated tests: 24/24 passed, including name normalization and geometric-classification coverage.

## Reflection

Metadata is the safest signal; the geometry heuristic is necessary for legacy families but must remain deliberately strict. Keeping the original-solid fallback limits regression risk for unusual or incorrectly authored footing families.

## Decisions Made

| Decision | Rationale | Impact |
|---|---|---|
| Prefer material/subcategory filtering | Explicit family intent is more reliable than proportions | Correctly named blinding solids are excluded deterministically |
| Apply geometry fallback only when no explicit solid was removed | Avoid stacking multiple filters and accidentally excluding structural concrete | Legacy families are supported with bounded risk |
| Treat the lowest thin, oversized, adjacent solid as blinding | Matches the observed family construction | Reinforcement extents start at the structural footing rather than the bottom slab |

## Next Steps

- Smoke-test in Revit 2025 using the shown family: generate bottom/top/middle bars and starters, then verify bar cover, plan extents, and host validity are based on the structural footing while the bottom blinding slab is ignored.
