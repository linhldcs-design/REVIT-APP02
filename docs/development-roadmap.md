# Development Roadmap

## IsolatedFootingRebar

Status: complete for Revit 2025 (`Debug.R25`).

- Scaffolded Nice3point add-in under `src/IsolatedFootingRebar`.
- Implemented footing geometry, bottom/top/mid mesh, vertical dowels, and horizontal stirrups.
- Added modeless WPF UI matching the Isolated Footing v1.1 layout: preset bar plus Common/Bottom/Top/Mid/Vertical/Horizontal tabs.
- Added plan/section rebar diagrams to each tab.
- Added xUnit pure-logic tests under `tests/IsolatedFootingRebar.Tests`.

Remaining manual validation: smoke test in Revit 2025 via Add-In Manager reload from `src/IsolatedFootingRebar/bin/Debug.R25/IsolatedFootingRebar.dll`.
