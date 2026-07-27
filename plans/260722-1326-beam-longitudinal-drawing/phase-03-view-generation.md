---
phase: 3
title: "Tạo mặt cắt dọc và mặt cắt station"
status: pending
priority: P1
effort: "2-3 ngày"
dependencies: [1, 2]
---

# Phase 03: Tạo mặt cắt dọc và mặt cắt station

## Overview

Đọc model Revit thành domain, rồi tạo mặt cắt dọc toàn chuỗi và các mặt cắt ngang unique theo station plan.

## Requirements

- Selection filter chỉ nhận `OST_StructuralFraming`; xác minh Rebar host và cột/support gần endpoint.
- Orchestrator chỉ nhận snapshot chain/station đã được preview-confirm; nếu selection/document thay đổi thì yêu cầu review lại.
- Mọi transform dùng local chain axis; không giả định world X/Y.
- Crop mặt cắt dọc bao toàn chuỗi, dầm, cột/gối và vùng annotation; depth đủ thấy thép nhưng không hút element xa.
- Cross section dùng đúng station/side và type ngang đã chọn.

## Related Files

- Create: `RevitAPP/Services/BeamLongitudinalDrawing/BeamChainPicker.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/RevitBeamChainReader.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/RevitRebarStationSampler.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalSectionBoxCalculator.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalViewBuilder.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/StationCrossViewBuilder.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalDrawingOrchestrator.cs`
- Reuse/read: `RevitAPP/Services/BeamDrawing/SectionPlaneCalculator.cs`
- Modify only if extraction is needed: `RevitAPP/Services/BeamDrawing/SectionViewBuilder.cs`

## Implementation Steps

1. Map FamilyInstance/LocationCurve/support/rebar sang input DTO Phase 01.
2. Hiển thị validation summary trong preview khi chain bị reject hoặc có beam thiếu rebar; không mở transaction.
3. Trước generate, đối chiếu fingerprint của confirmed preview snapshot với selection/document hiện tại; mismatch thì dừng và review lại.
4. Trong `TransactionGroup`, T1 tạo longitudinal view và unique cross views với tên collision-safe.
5. Áp Section Type, scale, template; set crop/far clip và `SetUnobscuredInView` cho rebar liên quan.
6. Commit T1, gọi `doc.Regenerate()`, trả `ViewBeamChainContext` cho annotation.
7. Nếu một view lỗi, rollback toàn T1; không để view orphan.

## Tests

- Unit test math cho local basis, crop extents và station transform.
- Revit smoke: một span, hai span, dầm xoay 30-45°, cột khác kích thước.
- Verify số view đúng station plan và tên không collision khi chạy lại.

## Success Criteria

- [ ] Mặt cắt dọc nhìn đủ chuỗi dầm/cột/thép, đúng hướng đọc.
- [ ] Cross views đúng station và không trùng gối khi fingerprint giống.
- [ ] View type/template/scale đúng input.
- [ ] Lỗi T1 rollback sạch.

## Risks

- Bounding box rebar/beam sau join không phản ánh solid thật: ưu tiên geometry/face projection, bbox chỉ fallback có warning.
