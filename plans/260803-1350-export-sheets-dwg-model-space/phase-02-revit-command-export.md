---
phase: 2
title: "Revit command, UI và staging export"
status: in-progress
priority: P1
effort: "2 ngày"
dependencies: [1]
---

# Phase 02: Revit command, UI và staging export

## Overview

Thêm modal command vào RevitAPP để chọn cấu hình, preview thứ tự sheet, export staging và điều phối worker mà không sửa document.

## Requirements

- `IExternalCommand`/Nice3point `ExternalCommand`, `TransactionMode.Manual`, nhưng không mở transaction.
- License gate chạy trước mọi workflow.
- Modal UI; không cần `ExternalEvent` vì mọi Revit API call nằm trong command context.
- Clone predefined options và chỉ override `FileVersion`; setup gốc giữ nguyên.

## Related Files

- Modify: `RevitAPP/Application.cs`
- Modify: `RevitAPP/RevitAPP.csproj` (chỉ khi cần project/asset include)
- Create: `RevitAPP/Commands/ExportSheetsToDwgCommand.cs`
- Create: `RevitAPP/ViewModels/DwgExportViewModel.cs`
- Create: `RevitAPP/Views/DwgExportWindow.xaml`
- Create: `RevitAPP/Views/DwgExportWindow.xaml.cs`
- Create: `RevitAPP/Services/DwgExport/PrintSetProvider.cs`
- Create: `RevitAPP/Services/DwgExport/RevitDwgExportService.cs`
- Create: `RevitAPP/Services/DwgExport/DwgPostProcessorLauncher.cs`
- Create: `RevitAPP/Resources/Icons/DwgExportIcon16.png`
- Create: `RevitAPP/Resources/Icons/DwgExportIcon32.png`

## Implementation Steps

1. Đăng ký button ở panel `CAD Tools`, patch tối thiểu quanh thay đổi hiện có trong `Application.cs`.
2. Liệt kê setup và map enum `ACADVersion` sang label UI; chọn version của setup làm mặc định.
3. Liệt kê saved `ViewSheetSet`, lọc/validate members, giữ `OrderedViewList` trên R23+; preprocessor/fallback R22.
4. UI hiển thị thứ tự sheet, scale các viewport, cảnh báo mixed-scale/R22 fallback và final path. Final path luôn rỗng cho tới khi người dùng bấm `Duyệt`; từ chối `Autodesk Docs:\...` vì đây không phải đường dẫn file Windows ghi được.
5. Kiểm tra output không nằm trong staging, quyền ghi, file đang khóa và AutoCAD đầy đủ có COM Automation tương thích.
6. Tạo owned staging folder + manifest; export từng sheet với tên nội bộ dựa trên stable ordinal/id, không dựa vào tên user.
7. Launch worker bằng argument/script file đã quote an toàn; capture exit code/stdout/stderr, support cancel và timeout.
8. Chỉ publish/summary khi result schema/job id/output đều hợp lệ; log path staging khi thất bại.

## Tests

- ViewModel validation/pure state nếu tách khỏi Revit types.
- Manual: setup missing giữa lúc dialog mở; Print Set rỗng/mixed views; sheet unprintable; cancel; output locked.
- Build R22-R27 để bắt API/preprocessor differences.

## Success Criteria

- [x] User chọn được setup, version, Print Set và output bằng `Duyệt`; không tự điền fallback từ đường dẫn project.
- [x] Preview phản ánh đúng thứ tự thực thi theo `OrderedViewList` của Revit 2025.
- [ ] Revit document không dirty sau thành công hoặc cancel.
- [x] Staging export từng sheet đầy đủ trước khi AutoCAD automation chạy.
- [ ] Lỗi có sheet/path/worker context, không chỉ báo exception chung.

## Risks

- `ViewSheetSet.OrderedViewList` không có trên R22.
- Revit `Document.Export` yêu cầu mọi view printable và folder tồn tại.
- Chờ process lâu trên UI thread; v1 cần progress/cancel hợp lý nhưng không gọi Revit API từ background thread.
