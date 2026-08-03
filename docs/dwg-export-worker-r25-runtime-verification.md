# DWG Export Worker R25 - Runtime Verification

This note supersedes older DWG-export documentation that says runtime smoke testing was pending, that AutoCAD post-processing ran directly inside Revit, or that a one-to-one Revit/CAD dimension entity count was required.

## Production behavior

- AutoCAD post-processing runs in the packaged `RevitAPP.DwgExportWorker.exe`, outside the Revit process.
- It does not install or load an AutoCAD add-in, bundle, ARX, managed DLL, or LISP. It drives the native `EXPORTLAYOUT` command through Windows COM Automation in a dedicated AutoCAD instance.
- Revit shows modal progress while the external worker runs.
- Each AutoCAD command has a four-minute ceiling. A transient timeout/COM failure causes exactly one retry with a fresh dedicated AutoCAD instance.
- The Revit-side watchdog stops a worker after five minutes without progress or twelve minutes total.
- Cleanup requires an ownership lease containing both the AutoCAD PID and its exact process start time. A pre-existing/user AutoCAD process is never terminated by process name.
- The final DWG is published atomically only after every sheet completes.

## Verification on 2026-08-03

- Full external-worker smoke after the final safety fixes: 34/34 sheets completed on the first attempt with a fresh job id.
- Output: one 6,645,568-byte DWG with header `AC1021` (AutoCAD 2007).
- Cleanup: zero remaining AutoCAD processes, worker processes, lease files, and `.partial.dwg` files.
- Revit 2025 build/deploy: zero errors.
- Automated suite: 343/343 tests passed.
- Final DWG verification: 1,695/1,695 dimensions are annotative; 1,695/1,695 dimension text styles are annotative Arial Narrow with height 2.5 and width factor 0.8.
- AutoCAD `AUDIT` reported 0 errors. No AutoCAD or worker process remained after completion.
- Revit and CAD dimension object counts are diagnostic only because `EXPORTLAYOUT` may split, crop, or omit Revit dimension objects. The runtime gate instead requires every CAD dimension candidate mapped to a viewport to receive its DIMLFAC; a Revit viewport reporting dimensions but producing no CAD candidate fails the job.
- Installed `RevitAPP.dll` and `RevitAPP.DwgExportWorker.exe` hashes matched the build artifacts.
