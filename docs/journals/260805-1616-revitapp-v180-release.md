---
date: 2026-08-05
session: revitapp-v1-8-0-release
version: 1.8.0
status: release-candidate
---

# Journal: 2026-08-05 — RevitAPP v1.8.0 Release Candidate

## Context

Prepared RevitAPP `v1.8.0` for release after extending the former Grid-only CAD bridge into a unified `Model From CAD` workflow. The release focuses on reviewing AutoCAD geometry safely before creating Revit Grids or Structural Columns, while keeping large or complex CAD selections responsive.

## What Happened

- Replaced the direct CAD-selection entry flow with an options window that opens first. Users choose `Create Grid` or `Create Column`, then run `Select From CAD` inside the active tab.
- Kept Grid and Column creation in one `Model From CAD` command, with tab-specific settings, candidate tables, selection controls, and Create-state validation.
- Added AutoCAD source-anchor capture and Revit target-anchor placement so imported geometry preserves relative distances, angles, and layout without requiring a linked or imported DWG.
- Expanded read-only CAD scanning to straight lines, closed 2D polylines, block references, and bounded nested blocks. Unsupported curved, bulged, 3D, proxy, or ambiguous geometry is rejected instead of being approximated silently.
- Added rectangular-column recognition from closed polylines, block geometry, and four independent LINE entities. Ambiguous four-LINE candidates require explicit confirmation before creation.
- Added Column options for family/type, writable Length parameters `b/h`, base/top levels, offsets, and rotation. Duplicate comparison includes geometry and placement configuration instead of location alone.
- Added 2D review with labels, fit, and zoom, plus a 3D column preview with wheel zoom, drag orbit, and camera reset/fit behavior. Toolbar layout was separated from the preview canvas after live UI review exposed overlapping controls.
- Hardened the AutoCAD handoff with COM cleanup, focus restoration, block caching, bounded recursion, entity/time limits, and early anchor selection. Preview rendering is lazy and limited to the active tab to reduce waiting after a large CAD selection.
- Kept Revit model mutation transactional: selected Grids or Columns are created only after review, and a failed batch can roll back rather than leave a partial result.

## Verification and Release Gates

| Gate | Evidence | Status |
|---|---|---|
| Version metadata | `RevitAPP.csproj` declares `1.8.0` | Passed |
| Automated tests | `RevitAPP.Tests` baseline: **357/357** | Passed |
| Multi-version compile | Handoff records successful Release builds for Revit **2022–2027** | Passed |
| Revit 2025 deployment | Feature iterations were deployed and reviewed in Revit 2025 | Passed locally |
| Interactive CAD workflow | Selection, preview responsiveness, 3D zoom/orbit, and toolbar layout were reviewed iteratively | Passed for reported cases; broader CAD variants remain smoke-test scope |
| Release packaging | Workflow must produce installer, `latest.json`, and six R22–R27 ZIP files | Pending workflow evidence |
| GitHub release health | All workflow jobs, eight assets, and public `latest.json` HTTP 200 must be verified | Pending publication |

## Reflection

The feature became more reliable because live review exposed issues that compilation and pure geometry tests could not: AutoCAD appeared to keep spinning while block traversal ran, the 3D preview initially zoomed but did not orbit over empty space, and preview controls could overlap the canvas at the deployed window size. Moving anchor capture earlier, bounding CAD work, making rendering lazy, and treating the whole 3D viewport as an input surface addressed the practical workflow rather than only the underlying geometry.

Separating CAD analysis from Revit creation remains the strongest architectural choice. It supports deterministic tests for transforms and rectangle detection, while the WPF review stage gives users a chance to reject false candidates before any transaction begins. The remaining risk is primarily integration-specific: COM behavior, unusual dynamic blocks, family parameter conventions, display scaling, and application focus vary across real installations.

## Decisions Made

| Decision | Rationale | Impact |
|---|---|---|
| Open the options window before CAD selection | Users need to choose the operation and settings before leaving Revit | Clearer flow; each tab owns its `Select From CAD` action |
| Use source and target anchors | Avoid dependence on shared coordinates or an imported CAD instance | Predictable translation with previewed rotation |
| Read block geometry without exploding CAD | Preserve the DWG and support nested reusable symbols | Unsupported geometry is skipped safely; traversal must remain bounded |
| Require confirmation for ambiguous four-LINE rectangles | Independent CAD lines can accidentally form a valid rectangle | Reduces unintended column creation without removing useful detection |
| Render only the active preview | 2D and 3D construction together caused unnecessary latency | Faster return from AutoCAD and lower UI cost |
| Keep 3D preview interactive but non-model-mutating | Height and orientation need inspection before commit | Orbit/zoom review is available without temporary Revit elements |
| Treat GitHub publication as a separate release gate | Local tests and deployment do not prove package availability | Release is not complete until workflow, assets, and update manifest pass |

## Known Limits

- Grid input is straight-line based; curved grids are outside the v1.8.0 contract.
- Column detection targets rectangular and square sections. Circular, polygonal, bulged, and 3D-polyline profiles are excluded.
- Dynamic or proxy block behavior that cannot be resolved safely through late-bound AutoCAD COM may be skipped.
- Column families must expose suitable writable type parameters with Revit's Length data type for width and height.
- Very large selections are intentionally capped or timed out; users may need to scan the drawing in smaller regions.

## Next Steps

- Run the final clean `Release` test suite and sequential R22–R27 builds with deployment and Revit launch disabled.
- Commit only v1.8.0-related files; exclude local build output, temporary files, unrelated untracked work, and user-specific configuration.
- Push `main`, create and push tag `v1.8.0`, then monitor installer, six Revit build jobs, and the release job to completion.
- Verify all eight GitHub Release assets and confirm `releases/latest/download/latest.json` returns HTTP 200 with version `1.8.0`.
- Preserve license and user preset data during update, then record any post-release AutoCAD/Revit compatibility findings in a follow-up journal.
