---
phase: 0
title: "Calibration spike và chốt hợp đồng DWG"
status: in-progress
priority: P1
effort: "1-2 ngày"
dependencies: []
---

# Phase 00: Calibration spike và chốt hợp đồng DWG

## Overview

Chứng minh pipeline trên dữ liệu thật trước khi thiết kế worker production. Phase này là blocking gate vì hành vi `Document.Export` và `EXPORTLAYOUT` đối với viewport/dimension không thể suy ra an toàn chỉ từ API signature.

## Requirements

- Tạo/nhận fixture RVT có title block, text, hatch, một view 1:75 và một view 1:25 trên cùng sheet; 1:75 là reference theo contract người dùng.
- Dùng một DWG setup thật và xuất R2007.
- Kiểm tra cả `MergedViews = true` và `false` trên bản clone option, không sửa setup gốc.
- Ghi inventory Model/Paper Space, layout viewport, xref/block, dimension entity và extents.

## Related Files

- Create: `plans/260803-1350-export-sheets-dwg-model-space/evidence/` (chỉ report/text/screenshot cần thiết, không commit DWG lớn nếu repo không cho phép).
- Create: prototype tạm dưới `tmp/` nếu cần; không đưa prototype vào production source.

## Implementation Steps

1. Chốt fixture và expected sheet order/layout bằng ảnh hoặc PDF baseline.
2. Xuất sheet bằng Revit UI và API với cùng setup để đối chiếu.
3. Thử `EXPORTLAYOUT` trong Core Console và AutoCAD đầy đủ; ghi nhận Core Console bị treo/không ổn định và chốt AutoCAD COM Automation trong một instance riêng làm pipeline production.
4. Đo entity trước/sau flatten: loại dimension, displayed value, measured value, `Dimlfac`, block/xref ownership.
5. Thử công thức 1:75/1:25: reference denominator 75, view 1:25 có geometry factor 3 và DIMLFAC 1/3; xác định group mapping theo viewport và anchor scale an toàn.
6. Kiểm tra crop, rotation, annotation crop, view label, title block, raster/wipeout/hatch.
7. Chọn pipeline chính thức và ghi contract version 1; nếu thất bại, nêu chính xác giới hạn và dừng plan để review.

## Tests

- So sánh bounding boxes và các điểm neo đã biết giữa Revit sheet và DWG.
- Đo ít nhất một dimension trong mỗi view trước/sau normalize.
- Mở R2007 output bằng AutoCAD và chạy `AUDIT`.
- Chạy cùng script hai lần để kiểm tra idempotency/tên file tạm.

## Success Criteria

- [ ] Có bằng chứng pipeline tự động chạy được qua một AutoCAD đầy đủ do job sở hữu qua COM Automation.
- [ ] Xác định được cách map entity/group về từng viewport mà không dùng tên file heuristic mơ hồ.
- [ ] Chốt công thức, anchor và hành vi dimension cho mixed-scale.
- [ ] Chốt fallback rõ ràng cho entity bị explode.
- [ ] Không còn unknown Critical trước Phase 01.

## Risks

- `EXPORTLAYOUT` thay đổi/explode một số loại entity theo tài liệu Autodesk.
- Revit exporter có thể đặt view geometry/xref khác nhau tùy `MergedViews` và setup.
- Core Console không chạy `EXPORTLAYOUT` ổn định trong thử nghiệm; runtime smoke phải dùng một AutoCAD đầy đủ, riêng biệt và không ảnh hưởng instance người dùng.
