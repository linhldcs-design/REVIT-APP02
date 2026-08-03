---
title: DWG export final safety verification
date: 2026-08-03
tags: [revit-2025, autocad-2024, dwg, dimensions]
---

# DWG Export Final Safety Verification

## Context

Finalize the Revit 2025 Print Set to one Model Space DWG workflow after independent review.

## What happened

- Changed viewport normalization from an unreliable Revit/CAD object-count equality check to full coverage of actual CAD DIM candidates.
- Added a hard failure when Revit reports dimensions but `EXPORTLAYOUT` produces no CAD DIM candidate.
- Made generated DIMSTYLE/Text Style names collision-safe.
- Ensured an unverified AutoCAD COM instance receives `Quit` before RCW release.
- Raised the large annotation batch timeout to four minutes.

## Verification

- 343/343 tests passed.
- Revit `Debug.R25` build/deploy: 0 errors.
- 34/34-sheet AutoCAD 2024 worker smoke: attempt 1 completed.
- 1,695/1,695 DIM annotative; 1,695/1,695 text styles satisfy Arial Narrow, height 2.5, width factor 0.8.
- `AUDIT`: 0 errors; no worker/AutoCAD process remained.

## Next

User visual acceptance of sheet order, layout, and viewport anchor remains the final gate.
