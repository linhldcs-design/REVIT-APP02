---
phase: 4
title: "Mixed-scale và dimension normalization"
status: in-progress
priority: P1
effort: "1-2 ngày"
dependencies: [0, 3]
---

# Phase 04: Mixed-scale và dimension normalization

## Overview

Ánh xạ từng vùng viewport nguồn vào Model Space sau `EXPORTLAYOUT`, giữ nguyên hình học đã được AutoCAD transform và chuẩn hóa `LinearScaleFactor` cho dimension mà không âm thầm giao file sai.

## Requirements

- Theo cách gọi của người dùng, “tỷ lệ lớn” = mẫu số lớn nhất làm reference.
- View reference có geometry factor và dimension factor bằng 1.
- Các view còn lại dùng cặp factor nghịch đảo theo yêu cầu; ví dụ 1:75/1:25 có geometry factor khái niệm là 3 và dimension factor là 1/3.
- Không scale geometry thêm sau `EXPORTLAYOUT`, vì lệnh này đã materialize phép biến đổi viewport; scale lại sẽ gây nhân đôi hệ số.
- Dimension phải được map vào đúng vùng viewport và đặt `LinearScaleFactor`; nếu số entity được normalize thấp hơn số dimension nguồn thì dừng job.
- Không sửa text hiển thị dimension bằng string replacement.

## Related Files

- Create: `RevitAPP.Core/Services/DwgViewportScaleRegionPlanner.cs`
- Modify: `RevitAPP/Services/DwgExport/AutoCadDwgPostProcessor.cs`
- Modify: `RevitAPP/Services/DwgExport/RevitDwgExportService.cs`
- Modify: `RevitAPP.Core/Models/DwgExport/DwgExportJob.cs`
- Modify: `tests/RevitAPP.Tests/DwgExport/DwgSheetLayoutPlannerTests.cs`
- Create: `tests/RevitAPP.Tests/DwgExport/DwgViewportScaleRegionPlannerTests.cs`

## Implementation Steps

1. Ghi sheet outline, viewport outline, scale denominator và số dimension nguồn vào job plan.
2. Lấy paper extents của layout và ánh xạ hình học sheet/viewport Revit sang vùng Model Space đã flatten, có guard tỷ lệ X/Y.
3. Giữ nguyên geometry sau `EXPORTLAYOUT`; không transform viewport group thêm lần nữa.
4. Duyệt các loại linear/radial/diametric/ordinate/arc dimension được hỗ trợ, map tâm bounding box vào vùng viewport và đặt `LinearScaleFactor` theo `view.Scale / referenceDenominator`.
5. Theo dõi số entity đã normalize cho từng viewport. Nếu viewport nguồn có dimension nhưng số normalize không đủ, fail sheet/job với lỗi định danh thay vì publish kết quả có thể sai.
6. Compose các sheet theo extents sau flatten và log factor/count áp dụng cho từng view/sheet.

## Tests

- 1:75 + 1:25; 1:100 + 1:50 + 1:20; tất cả cùng scale.
- Unit test ánh xạ vùng viewport, reference mẫu số lớn nhất, geometry/dimension factor và reject mapping X/Y không đồng nhất.
- Rotated/cropped viewport; dimension trong block; dimension crossing crop; text override.
- Đo geometry và displayed dimension sau khi mở DWG.
- Visual comparison với baseline bố cục.

## Success Criteria

- [ ] Runtime fixture 1:75 có `DIMLFAC = 1`; fixture 1:25 giữ geometry tương đối do `EXPORTLAYOUT` tạo ra và đạt dimension factor `25/75 = 1/3` theo contract.
- [ ] Anchor không đổi ngoài tolerance.
- [ ] Không sửa nhầm dimension của title block/sheet annotation/view khác.
- [ ] Unsupported mapping/entity dừng với lỗi định danh sheet/view.

## Risks

- Scale view lên có thể gây overlap dù tâm được giữ; đây là xung đột nội tại giữa “giữ bố cục tuyệt đối” và “normalize tỷ lệ”. Cần user chốt ưu tiên tại gate Phase 00.
- Dimension có thể bị exporter/flatten biến thành primitive và mất `DIMLFAC` semantic.

## Trạng thái kiểm chứng

- Mixed-scale guard trong UI/service đã được bỏ; lệnh có thể chạy khi output path hợp lệ.
- Pure test suite đạt **335/335**, gồm kiểm tra tự khớp chiều giấy ngang/dọc và không dùng plot offset làm tọa độ layout.
- Build/deploy `Debug.R25` đạt **0 lỗi** sau khi Revit đóng.
- Bản build mới đã vào Addins 2025 nhưng chưa có runtime smoke trên DWG thật. Phase này vẫn `in-progress`.
