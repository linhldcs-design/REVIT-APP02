---
date: 2026-08-03
session: dwg-export-path-scale-fix
---

# Journal: 2026-08-03 — DWG export path and scale fix

## Context

Investigated the Revit 2025 DWG export failure shown in the user screenshot and clarified the intended mixed-scale rule.

## What Happened

- The command failed with `Value does not fall within the expected range.`
- Revit journal evidence showed that no staging output was created.
- The UI had prefilled an Autodesk Docs virtual path, which is unsafe as a local `SaveFileDialog`/DWG destination.
- Output now starts empty and requires the user to choose a path with **Browse**. The picker, ViewModel, and export service each validate the selected path.
- Removed validation side effects from `CanExecute`, eliminating reentrancy risk during WPF command reevaluation.
- User clarified that the reference scale is the largest denominator: for 1:75 and 1:25, use 1:75 as reference, geometry factor `75 / 25 = 3`, and `DIMLFAC = 25 / 75 = 1/3`.
- Red-green tests were added. The suite passes `331/331`; `Debug.R25` builds with 0 errors.
- After Revit closed, the new DLL was deployed to Addins 2025 and its hash matched the publish artifact.

## Reflection

The failure originated before DWG staging, so AutoCAD automation was not the immediate cause. Treating a cloud display path as a writable filesystem path was an invalid assumption. Layered path validation now fails earlier and explains the required user action. Mixed-scale production handling remains deliberately fail-closed until its runtime transformation is verified.

## Decisions Made

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Require user-selected local output path | Autodesk Docs virtual paths are not reliable `SaveFileDialog` destinations | Prevents invalid prefilled paths from reaching export |
| Validate at picker, ViewModel, and service boundaries | UI state alone cannot guarantee a safe runtime path | Defensive failure with clearer diagnostics |
| Use maximum scale denominator as reference | Matches the clarified rule: 1:75 takes precedence over 1:25 | Geometry factor is 3; `DIMLFAC` is 1/3 |
| Keep mixed-scale production export fail-closed | Runtime DWG normalization is not yet verified | Avoids silently producing incorrect dimensions |

## Next Steps

- Reopen Revit 2025 and verify Browse-only path selection plus single-scale export.
- Validate mixed-scale DWG transformation before removing the production guard.
