---
date: 2026-07-20
session: chat-footing-drawing-tools
---

# Journal: 2026-07-20 — Chat Footing Drawing Tools

## Context

Chat AI could open the Bản Vẽ Móng and Mặt Cắt Móng dialogs, but could not directly execute either workflow from a request such as “triển khai bản vẽ móng đang chọn theo cấu hình M1”. The goal was to expose the existing engines as licensed native Chat tools without guessing configuration values.

## What Happened

- Added `draw_footing_drawing` and `draw_footing_section`; the Chat registry now exposes 51 tools.
- Both tools resolve an exact saved preset, validate Structural Foundation IDs, call the existing drawing/section orchestrators directly, report partial errors and warnings, and activate the last generated sheet.
- Added selection injection and batch-draw routing so omitted `footingIds` use the current Revit selection through the existing ExternalEvent flow.
- Updated the system prompt to distinguish direct execution tools from `open_footing_*` dialog adapters.
- Review tightened category validation, exact preset matching, single-footing enforcement for sections, result accounting, and failure reporting.

## Reflection

Reusing the production orchestrators keeps Chat behavior aligned with the ribbon commands and avoids a second drawing implementation. Explicit presets and validated selection make the operation deterministic while preserving existing license and Revit API boundaries.

## Decisions Made

| Decision | Rationale | Impact |
|---|---|---|
| Add native execution tools instead of extending dialog adapters | Opening a window does not fulfill an AI drawing request | Chat can generate output directly |
| Require an exact saved preset | Prevent unsafe configuration inference | Requests fail clearly when a preset is missing |
| Reuse selected foundations through ExternalEvent | Keep Revit API work on the valid context | Same selection behavior as other batch tools |

## Verification

- `dotnet test tests/RevitAPP.Tests/RevitAPP.Tests.csproj -c Release --no-restore`: 155 passed, 0 failed.
- Code review confirmed registry, prompt, selection injection, license gating, and direct orchestrator integration.

## Next Steps

### Combined orchestration and sheet arrangement

- Added `arrange_footing_sheet` and `draw_and_arrange_footing_sheet`; Chat registry now exposes 53 tools.
- Drawing results return `footingId`, `sheetId`, `viewId`, and `viewportId` for deterministic pairing.
- Layout validates matching source footing ids, unique viewport ids, Detail/Section roles, a common sheet, capacity, and collisions with existing sheet-owned content.
- Packing measures viewport boxes together with label outlines, places each plan above its matching section, orders pairs left-to-right, reserves the right title-block and lower note zones, and rejects layouts that do not fit.
- The combined tool retains viewport IDs in C# and wraps drawing plus layout in one outer TransactionGroup, so every created view/viewport rolls back on failure.
- Verified 159/159 tests; release builds target Revit 2022–2027.

- Smoke-test both commands inside Revit with real saved presets, selected isolated footings, generated sheet activation, cancellation, and partial-failure cases.
