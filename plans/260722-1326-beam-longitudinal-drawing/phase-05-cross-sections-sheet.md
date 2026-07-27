---
phase: 5
title: "Tái sử dụng mặt cắt ngang và bố trí sheet"
status: pending
priority: P1
effort: "2-3 ngày"
dependencies: [3, 4]
---

# Phase 05: Tái sử dụng mặt cắt ngang và bố trí sheet

## Overview

Annotate các mặt cắt station bằng engine hiện có qua adapter an toàn, rồi bố trí mặt cắt dọc và các mặt cắt ngang lên sheet.

## Requirements

- Mỗi station đọc Rebar thật tại station/side; không reuse danh sách tag của station khác.
- Regression của command mặt cắt ngang hiện có phải giữ nguyên.
- Sheet layout ưu tiên mặt cắt dọc ở trên, cross sections theo hàng dưới như PDF; tự wrap hàng và cảnh báo overflow.
- Viewport Type, title, sheet number/name và title block theo setting.

## Related Files

- Create: `RevitAPP/Services/BeamLongitudinalDrawing/CrossSectionAnnotationAdapter.cs`
- Create: `RevitAPP.Core/Services/BeamLongitudinalSheetLayoutCalculator.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalSheetBuilder.cs`
- Modify: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalDrawingOrchestrator.cs`
- Modify narrowly if required: `RevitAPP/Services/BeamDrawing/BeamAnnotator.cs`
- Modify narrowly if required: `RevitAPP/Services/BeamDrawing/SheetBuilder.cs`
- Create: `tests/RevitAPP.Tests/BeamLongitudinalDrawing/BeamLongitudinalSheetLayoutCalculatorTests.cs`

## Implementation Steps

1. Tách adapter/interface nhỏ quanh cross annotation; không truyền setting mặt cắt dọc vào toàn bộ legacy orchestrator.
2. Annotate từng cross view theo `SectionStation` và fingerprint/side đã lên kế hoạch.
3. Tính outline view/viewport theo scale; pack longitudinal view và N cross views vào vùng title block khả dụng.
4. T3 tạo sheet/viewport, đổi viewport type, set Title on Sheet và center theo layout thuần.
5. Kiểm tra overlap/overflow sau Revit regenerate; cảnh báo và rollback T3 nếu không thể đặt hợp lệ.
6. Chạy test và smoke regression lệnh cũ.

## Tests

- Layout 3/5/6/9 cross views, title block ngang, view title height và overflow.
- Adapter map support/mid đúng tag/dim rules.
- Regression test/smoke command `BeamDrawingCommand` hiện tại.

## Success Criteria

- [ ] Số cross view đúng station planner và tag đúng station.
- [ ] Không viewport overlap; overflow có thông báo hành động được.
- [ ] Sheet name/number/title block/viewport type đúng input.
- [ ] Feature mặt cắt ngang cũ vẫn build, test và smoke xanh.

## Risks

- Outline viewport chỉ chính xác sau placement: dùng layout hai bước (ước lượng, place/regenerate, chỉnh final) trong cùng T3.
