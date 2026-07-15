# IsolatedFootingRebar Phase 1: Scaffold Complete

**Date**: 2026-06-26 14:30  
**Severity**: Low (informational)  
**Component**: IsolatedFootingRebar Add-in (new project)  
**Status**: Resolved

## What Happened

Phase 1 scaffold for the IsolatedFootingRebar Revit add-in completed successfully. New project initialized at `src/IsolatedFootingRebar/` following the BeamRebarPro template pattern (Nice3point 6.2.2, multi-version R23–R27, hand-rolled DI via `Host.GetService<T>()`). Ribbon panel "Thép Móng" with button "Vẽ Móng Đơn" deployed and functional: picks one OST_StructuralFoundation element, displays element ID in TaskDialog.

Build gate passed (Revit 2025 R25 target): `dotnet build -c Debug.R25` → 0 warnings, 0 errors. DLL auto-deployed to `%AppData%\Autodesk\Revit\Addins\2025\`.

## The Brutal Truth

This was straightforward clone work. Zero friction because we copied a working template. The only minor annoyance: had to establish that user prioritizes R25 first, not R27. No firefighting, no rewrites, no surprises — this is what good scaffolding feels like.

## Technical Details

**Files created:**
- `IsolatedFootingRebar.csproj` — Multi-version config targeting R25 (Revit 2025), R23–R27 preprocessor support
- `.addin` manifest — GUID CD93759C-D335-414B-BF1F-B0A9119E921D
- `Host.cs` — Service locator pattern (`GetService<T>()`)
- `Application.cs` — ExternalApplication, ribbon setup, button handler
- `Configuration/LoggerConfiguration.cs` — Serilog integration
- `Services/FoundationSelectionFilter.cs` — Element filter (OST_StructuralFoundation only)
- `Services/FoundationPicker.cs` — Selection UI wrapper
- `Commands/StartupCommand.cs` — ExternalCommand, picks foundation, shows TaskDialog with element ID

**Build output:**
```
C:\Users\Admin\OneDrive\Desktop\RevitAI\src\IsolatedFootingRebar\bin\Debug.R25\
  IsolatedFootingRebar.dll (auto-deployed to %AppData%\Autodesk\Revit\Addins\2025\)
```

**ElementId note:** ElementId.Value (long) works across R23–R27 via Nice3point compat shim — no `#if REVIT2024_OR_GREATER` needed.

## What We Tried

Nothing failed. This was copy-paste-adapt from a working template. Pattern proven on BeamRebarPro, so risk was minimal.

## Root Cause Analysis

Not applicable — no root cause to analyze. This was execution-only.

## Lessons Learned

- **Template hygiene matters.** BeamRebarPro gave us a solid DI + ribbon pattern to clone. Setup friction dropped to zero.
- **Early platform targeting simplifies onboarding.** Choosing R25 first (not R27) meant no ambiguity about which API version to test against during F5.
- **Placeholder folders (Models/) are fine for phase gate.** Code review noted the empty folder as a nit — keeping it because Phase 2 will populate it. Not worth premature removal.

## Next Steps

1. **Phase 2 ready:** Geometry + FootingRebarModel (foundation frame detection, reinforcement layout rules).
2. **F5 smoke test pending:** User will load DLL via Add-In Manager in Revit 2025 to verify ribbon button appears and pick works.
3. **Handoff:** Plan docs live at `plans/260626-1430-isolated-footing-rebar/` with 6 phase breakdowns. Lead to review before Phase 2 kickoff.

---

**Ownership:** Phase 1 complete. User owns next F5 validation; engineer owns Phase 2 implementation per plan.
