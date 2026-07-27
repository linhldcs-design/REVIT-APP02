---
phase: 0
title: "Baseline, contracts và ribbon scaffold"
status: completed
priority: P1
effort: "1 ngày"
dependencies: []
---

# Phase 00: Baseline, contracts và ribbon scaffold

## Overview

Khóa hành vi hiện tại của lệnh mặt cắt ngang, tạo command riêng cho mặt cắt dọc và skeleton không làm thay đổi output cũ.

## Requirements

- Ribbon button riêng, command là `IExternalCommand`/Nice3point `ExternalCommand`, WPF modal.
- Không thêm project/assembly mới, không thêm package mới.
- Command stub chỉ validate document/view và mở dialog placeholder; chưa tạo element.

## Related Files

- Create: `RevitAPP/Commands/BeamLongitudinalDrawingCommand.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/` skeleton
- Create: `RevitAPP.Core/Models/BeamLongitudinalDrawing/` skeleton
- Create: `tests/RevitAPP.Tests/BeamLongitudinalDrawing/ExistingCrossSectionCharacterizationTests.cs`
- Modify: `RevitAPP/Application.cs`
- Modify: `RevitAPP/RevitAPP.csproj` chỉ khi resource mới cần khai báo

## Implementation Steps

1. Ghi snapshot hành vi helper đang tái sử dụng: station filtering, cross section box, resource resolution và sheet placement.
2. Thêm button “Mặt cắt dọc dầm” trỏ tới command mới; giữ nguyên button `Ban Ve Dam` hiện tại.
3. Tạo result/warning envelope dùng xuyên các phase; warning có `Code`, `ViewId?`, `ElementId?`, `Message`.
4. Thêm precondition: document project, active non-template view, không family document, selection không rỗng khi generate.
5. Build Debug.R25 và chạy toàn bộ test baseline.

## Tests

- Characterization tests cho logic thuần hiện có được dùng lại.
- Command validation được tách thành helper thuần nếu có nhánh đáng kể.
- Build XAML/ribbon không lỗi.

## Success Criteria

- [x] Hai ribbon command tồn tại độc lập.
- [x] Lệnh mặt cắt ngang cũ không đổi hành vi.
- [x] Command mới mở được WPF modal placeholder trong Revit 2025, không sửa model.
- [x] Test baseline và Debug.R25 build xanh.

## Completion Evidence

- Ribbon mới: `BeamLongitudinalDrawingCommand` / “Mat Cat Doc Dam”; button `Ban Ve Dam` giữ nguyên.
- WPF modal dùng `Theme.xaml`, owner là Revit main window; không có transaction/model mutation.
- Characterization tests khóa station math, section-box far clip và cross-tag layout hiện hữu.
- Debug.R25 build/XAML compile/ILRepack: 0 error; smoke click ribbon trong Revit còn chờ người dùng.

## Risks

- Trùng tên/assembly identity khi Add-In Manager load: dùng tên command đầy đủ và chưa deploy đè DLL auto-loaded.
