---
date: 2026-08-03
session: dwg-worker-soft-hang-fix
---

# DWG worker soft-hang isolation

## Root cause

The first staged drawing opened in a dedicated AutoCAD 2024 instance, but native `EXPORTLAYOUT` produced no flat DWG, no dialog, no COM exception, and almost no CPU activity for more than five minutes. `AutoCadDwgPostProcessor.Compose` was called synchronously inside the Revit external command, so this AutoCAD soft hang also made Revit appear hung. The existing ten-minute command timeout was not process isolation.

## Change

- Added a packaged single-file `RevitAPP.DwgExportWorker.exe`.
- Revit now stages sheets and launches the worker instead of running AutoCAD COM in-process.
- Added per-sheet progress, a five-minute no-progress watchdog, a twelve-minute total ceiling, cancel support, and one retry for transient timeout/COM failures.
- Added an AutoCAD ownership lease with PID plus process start time. Cleanup can terminate only the exact owned process.
- Kept the no-AutoCAD-plugin contract: only native `EXPORTLAYOUT` is used through COM Automation.
- Added worker packaging to the Revit R25 build and deployment output.

## Verification

- Full worker smoke: 34 sheets completed in 172.2 seconds.
- Output: one DWG, 6,645,568 bytes, header `AC1021`.
- Cleanup: no AutoCAD process, worker process, lease, or partial output remained.
- Revit 2025 build/deploy: zero errors.
- Tests: 334/334 passed.
- Installed main DLL and worker EXE hashes matched build artifacts.
