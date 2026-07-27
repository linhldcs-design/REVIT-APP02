# Development Roadmap

## IsolatedFootingRebar

Status: complete for Revit 2025 (`Debug.R25`).

- Scaffolded Nice3point add-in under `src/IsolatedFootingRebar`.
- Implemented footing geometry, bottom/top/mid mesh, vertical dowels, and horizontal stirrups.
- Added modeless WPF UI matching the Isolated Footing v1.1 layout: preset bar plus Common/Bottom/Top/Mid/Vertical/Horizontal tabs.
- Added plan/section rebar diagrams to each tab.
- Added xUnit pure-logic tests under `tests/IsolatedFootingRebar.Tests`.

Remaining manual validation: smoke test in Revit 2025 via Add-In Manager reload from `src/IsolatedFootingRebar/bin/Debug.R25/IsolatedFootingRebar.dll`.

## Beam Longitudinal Drawing

Status: in progress for Revit 2025 (`Debug.R25`); Phase 00-02 complete, Phase 03-06 pending.

- Added an independent ribbon command and modal WPF scaffold under `RevitAPP`; the existing transverse beam drawing command is unchanged.
- Added Revit-independent domain logic in `RevitAPP.Core` for beam-chain ordering and validation, section-station planning, rebar fingerprint comparison, and two-tier dimension planning.
- Added the Phase 02 resource/settings workflow, isolated JSON presets, and a themed visual preview of the ordered beam axis, spans, supports, direction, and proposed section stations.
- The command is read-only in this phase and accepts only beams that already host Rebar. It does not create, modify, or delete beam reinforcement.
- Generate remains disabled until resources and preview are valid and the user explicitly confirms the preview. Reversing direction or changing selection/tolerances invalidates that confirmation.
- Added pure-logic and characterization coverage under `tests/RevitAPP.Tests/BeamLongitudinalDrawing`.
- Current verification: 196/196 tests pass and the `Debug.R25` build passes.

No Revit model output is created yet, and this feature has not been released. View generation, annotation,
transverse-section reuse, sheet layout, Revit smoke testing, deployment, and hand-off remain in Phase 03-06.
See [the implementation plan](../plans/260722-1326-beam-longitudinal-drawing/plan.md).
