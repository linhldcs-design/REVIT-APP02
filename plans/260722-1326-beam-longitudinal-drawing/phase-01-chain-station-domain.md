---
phase: 1
title: "Mô hình chuỗi dầm, station và fingerprint"
status: completed
priority: P1
effort: "2-3 ngày"
dependencies: [0]
---

# Phase 01: Mô hình chuỗi dầm, station và fingerprint

## Overview

Xây domain thuần quyết định thứ tự dầm, vị trí cắt, gộp/tách mặt cắt và witness dimension trước khi chạm Revit API.

## Requirements

- Chuỗi là một connected path, không branch/cycle; các span gần đồng trục và đồng cao độ trong tolerance.
- Mỗi span đề xuất left support/mid/right support; gối chung dedupe theo fingerprint.
- Fingerprint không phụ thuộc `ElementId`, dùng số lượng/đường kính/layer/stirrup spacing đã chuẩn hóa tolerance.
- Dimension plan tạo lớp trên (rebar/stirrup zones) và lớp dưới (column/support/span/grid).

## Related Files

- Create: `RevitAPP.Core/Models/BeamLongitudinalDrawing/BeamChainModel.cs`
- Create: `RevitAPP.Core/Models/BeamLongitudinalDrawing/BeamSpanModel.cs`
- Create: `RevitAPP.Core/Models/BeamLongitudinalDrawing/SectionStation.cs`
- Create: `RevitAPP.Core/Models/BeamLongitudinalDrawing/RebarStationFingerprint.cs`
- Create: `RevitAPP.Core/Models/BeamLongitudinalDrawing/LongitudinalDimensionPlan.cs`
- Create: `RevitAPP.Core/Services/BeamChainBuilder.cs`
- Create: `RevitAPP.Core/Services/SectionStationPlanner.cs`
- Create: `RevitAPP.Core/Services/RebarFingerprintComparer.cs`
- Create: `RevitAPP.Core/Services/LongitudinalDimensionPlanner.cs`
- Create: `tests/RevitAPP.Tests/BeamLongitudinalDrawing/*Tests.cs`

## Implementation Steps

1. Định nghĩa input DTO thuần từ endpoint/axis/section/support/rebar sample.
2. Dựng endpoint graph, tìm path và hướng chuẩn trái-sang-phải theo local axis; reject topology không hỗ trợ.
3. Sinh candidate stations với offset khỏi mặt cột và midpoint vùng nhịp.
4. Tạo fingerprint canonical có rounding tolerance; so hai phía gối chung.
5. Quy tắc giảm section: nếu không có tăng cường và stirrup signature chỉ một vùng thì giữ tối thiểu section cần thiết; ghi reason cho mọi station bị gộp/bỏ.
6. Sinh dimension witnesses đã sort, dedupe tolerance và có semantic label.

## Tests

- Một span; hai/ba span đảo thứ tự pick; dầm xoay; endpoint lệch tolerance.
- Branch, cycle, khoảng hở, khác cao độ và khác trục phải bị reject rõ lý do.
- Gối chung fingerprint giống/khác; khác chỉ đường kính, quantity, layer hoặc spacing đai.
- Không tăng cường + một vùng đai; nhiều vùng đai; witness trùng; nhịp rất ngắn.

## Success Criteria

- [x] Output station deterministic, không phụ thuộc thứ tự pick.
- [x] Không gộp hai fingerprint khác nhau.
- [x] Mọi station có `Reason` và `SourceSpanIndex` truy vết được.
- [x] Domain test không reference Revit API và phủ các case Phase 01 trong Acceptance Matrix.

## Completion Evidence

- `BeamChainBuilder` dùng endpoint graph + deterministic union-find; reject empty/invalid/disconnected/branch/cycle,
  lệch trục và lệch cao độ bằng error code có cấu trúc.
- `SectionStationPlanner` rút gọn chỉ khi không có thép tăng cường, đúng một vùng đai và ba fingerprint tương đương;
  luôn giữ hai phía tại transition khác fingerprint.
- Dimension witnesses giữ nhiều semantic roles khi trùng reference; result/warning contract không lộ mutable list.
- `RevitAPP.Tests`: 186/186 pass; Core `net48` build tương thích.

## Risks

- Nghiệp vụ “khác nhau” còn mơ hồ: fingerprint thiết kế mở rộng và mặc định bảo thủ (không chắc thì không gộp).
