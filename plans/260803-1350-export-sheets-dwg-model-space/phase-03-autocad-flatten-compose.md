---
phase: 3
title: "AutoCAD flatten và compose Model Space"
status: in-progress
priority: P1
effort: "2-3 ngày"
dependencies: [0, 1]
---

# Phase 03: AutoCAD flatten và compose Model Space

## Overview

Xây worker chạy ngoài Revit để chuyển từng sheet layout thành Model Space và clone các sheet vào một database cuối theo trục X.

## Requirements

- Process riêng; tuyệt đối không load `AcMgd/AcDbMgd` vào Revit process.
- Không load AutoCAD managed assemblies vào Revit; automation dùng COM late binding trong một AutoCAD đầy đủ, riêng biệt.
- Không dùng input người dùng tương tác; job chỉ sở hữu/đóng document và AutoCAD instance do chính nó mở.
- File cuối self-contained, không xref staging.

## Related Files

- Create: `RevitAPP/Services/DwgExport/AutoCadDwgPostProcessor.cs`
- Modify: `RevitAPP/Services/DwgExport/RevitDwgExportService.cs`
- Modify: `RevitAPP.Core/Models/DwgExport/DwgExportJob.cs`

## Implementation Steps

1. Khởi tạo AutoCAD đầy đủ bằng COM late binding, xác định PID/process mới và không dùng instance người dùng đang mở.
2. Đọc job plan, verify schema/job id và canonical paths.
3. Mở từng staged DWG, chọn đúng non-model layout và chạy `EXPORTLAYOUT` bằng route đã chứng minh ở Phase 00.
4. Bind/resolve required xrefs; từ chối unresolved dependency thay vì tạo output thiếu.
5. Tính extents sau flatten, normalize base point, clone toàn bộ Model Space entities vào database final với displacement planner.
6. Giữ layer, linetype, text/dim style, block table record và draw order; resolve duplicate symbols bằng policy deterministic đã test.
7. Xóa layout phụ hoặc đảm bảo toàn bộ nội dung cần thiết nằm trong Model Space.
8. Save file tạm đúng requested `DwgVersion`, publish sau cùng; restore biến hệ thống, đóng document do job mở và chỉ `Quit` instance do job sở hữu.

## Tests

- 1, 2 và nhiều sheet; sheet landscape/portrait; extents âm; duplicate layer/block/style names.
- Xref missing; corrupt DWG; unsupported object; output exists/locked; worker killed giữa job.
- Mở output không có unresolved xref và entity count/extents hợp lý.

## Success Criteria

- [x] Mỗi staged sheet thành một cụm Model Space self-contained.
- [x] Cụm sheet tăng dần theo X, đúng order và không overlap.
- [x] Không load AutoCAD assemblies trong Revit host.
- [x] Failure không publish file final và result chỉ đúng job hiện tại.
- [ ] R2007 output mở/audit thành công.

## Risks

- Symbol name collision khi clone nhiều DWG.
- `EXPORTLAYOUT` có thể tạo anonymous blocks/explode custom objects.
- COM ProgID/runtime AutoCAD khác nhau theo bản cài đặt; phải fail rõ nếu không khởi tạo/xác định được instance riêng.
