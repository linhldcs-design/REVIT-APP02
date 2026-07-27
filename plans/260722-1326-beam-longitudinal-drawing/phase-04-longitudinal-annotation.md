---
phase: 4
title: "Tag, detail component và dimension mặt cắt dọc"
status: pending
priority: P1
effort: "3-4 ngày"
dependencies: [3]
---

# Phase 04: Tag, detail component và dimension mặt cắt dọc

## Overview

Tạo annotation chính của feature: tag đúng từng vùng thép, ký hiệu cột/gối, spot elevation và hai lớp dimension như PDF.

## Requirements

- Chỉ tag Rebar host bởi beam trong chain và giao vùng hiển thị của view.
- Phân loại thép dọc chính/tăng cường trên/dưới và đai; tag type theo setting.
- Detail component đặt tại mỗi support/cột, đúng rotation/flip local view.
- Dimension lớp trên theo các boundary vùng đai/thép; lớp dưới theo mép cột/gối/nhịp/trục.
- Không tạo segment hiển thị `0`; dedupe witness trước và hậu kiểm `DimensionSegment.ValueString` sau regenerate.

## Related Files

- Create: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalRebarClassifier.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalRebarTagPlacer.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/SupportDetailComponentPlacer.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalDimensionPlacer.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalSpotElevationPlacer.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalAnnotator.cs`
- Reuse/read: `RevitAPP/Services/BeamDrawing/RebarTagPlacer.cs`
- Reuse/read: `RevitAPP/Services/BeamDrawing/DimensionPlacer.cs`
- Modify: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalDrawingOrchestrator.cs`
- Create: `tests/RevitAPP.Tests/BeamLongitudinalDrawing/LongitudinalAnnotationMathTests.cs`

## Implementation Steps

1. Xây view-local projection cho mỗi Rebar, lấy curve extents và layer Z/Y thật.
2. Nhóm rebar set theo mark/diameter/shape/layer và vùng station; không gộp chỉ vì cùng cao độ.
3. Tính tag lanes tránh chồng, giữ leader bám rebar; tag đai theo zone spacing.
4. Place detail component tại support; activate symbol và kiểm tra family placement type.
5. Chuyển `LongitudinalDimensionPlan` thành geometric `ReferenceArray/IList<Reference>` ổn định.
6. Tạo upper/lower dimension riêng; hậu kiểm zero/duplicate segments và rebuild best-effort khi cần.
7. Spot elevation tại top beam/support theo type/offset đã chọn.
8. Mỗi nhóm annotation có try/catch riêng, warning chứa view/element/operation; lỗi bắt buộc có thể rollback T2 theo policy.

## Tests

- Phân loại chủ/tăng cường/đai; rebar chồng layer; zone đai a100/a200; hai nhịp.
- Tag lane layout; witness sort/dedupe; zero segment removal; mm-feet conversion.
- Revit smoke đối chiếu tag 3D18/2D18 và D6a100/D6a200 như PDF.

## Success Criteria

- [ ] Mọi tag phản ánh Rebar thực tế, không clone từ station khác.
- [ ] Hai lớp dimension đúng thứ tự, không có đoạn 0 hoặc witness trùng.
- [ ] Detail component đúng tại từng cột/gối.
- [ ] Annotation không dùng world axis và không chồng nghiêm trọng ở tỷ lệ 1:25.

## Risks

- Revit không cung cấp reference cho một số rebar geometry: fallback tag element/subelement; dimension vùng dựa beam/support face, không giả tạo reference model.
