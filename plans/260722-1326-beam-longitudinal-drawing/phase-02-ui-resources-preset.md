---
phase: 2
title: "UI, resource input và preset"
status: completed
priority: P1
effort: "2 ngày"
dependencies: [0, 1]
---

# Phase 02: UI, resource input và preset

## Overview

Tạo form modal riêng cho tám nhóm input trong ảnh, khung preview trực quan bắt buộc và các offset/tolerance cần để đạt bố cục PDF mẫu.

## Requirements

- Input bắt buộc: Dimension Type, tag thép dọc, tag thép đai, Detail Component, Viewport Type, Section Type dọc, Section Type ngang, Spot Elevation Type.
- Thêm View Template/Scale/Title Block/Sheet Number/Sheet Name và offset annotation để output có thể nghiệm thu.
- Chỉ hiển thị type đúng category/class; thiếu tài nguyên bắt buộc thì không cho Generate.
- Preset JSON version riêng, không làm hỏng preset mặt cắt ngang cũ.
- Preview code-native hiển thị trục local trái→phải, dầm theo thứ tự, cột/gối, mark/chiều dài từng nhịp,
  các station `Gối trái / Giữa nhịp / Gối phải / Gối chung` và warning topology.
- Nút Generate disabled khi chain invalid, preview chưa xác nhận hoặc selection đã thay đổi sau lần xác nhận gần nhất.
- Có nút đảo hướng chuỗi để người dùng chọn chiều đọc bản vẽ; đảo hướng phải tính lại station/label trước khi xác nhận.

## Related Files

- Create: `RevitAPP.Core/Models/BeamLongitudinalDrawing/LongitudinalDrawingSetting.cs`
- Create: `RevitAPP.Core/Services/LongitudinalDrawingSettingFactory.cs`
- Create: `RevitAPP.Core/Services/LongitudinalDrawingSettingValidator.cs`
- Create: `RevitAPP.Core/Services/LongitudinalDrawingPresetStore.cs`
- Create: `RevitAPP/ViewModels/BeamLongitudinalDrawingViewModel.cs`
- Create: `RevitAPP/Views/BeamLongitudinalDrawingWindow.xaml`
- Create: `RevitAPP/Views/BeamLongitudinalDrawingWindow.xaml.cs`
- Create: `RevitAPP/Views/Controls/BeamChainPreviewCanvas.cs`
- Create: `RevitAPP.Core/Models/BeamLongitudinalDrawing/BeamChainPreviewModel.cs`
- Create: `RevitAPP/Services/BeamLongitudinalDrawing/LongitudinalProjectResourceProvider.cs`
- Modify: `RevitAPP/Commands/BeamLongitudinalDrawingCommand.cs`
- Create: `tests/RevitAPP.Tests/BeamLongitudinalDrawing/*Setting*Tests.cs`

## Implementation Steps

1. Chốt schema setting, defaults theo tỷ lệ 1:25 của PDF nhưng không hardcode family/type name.
2. Nạp resource options từ document; có display name `Family: Type` và resolver trả `ElementId` chỉ trong Revit layer.
3. Map `BeamChainModel + SectionStation` sang `BeamChainPreviewModel` thuần; không truyền Revit element vào control.
4. Tạo `BeamChainPreviewCanvas` code-native theo `Theme.xaml`, tự scale theo kích thước control và có legend station.
5. Wire review gate: selection/đảo hướng/thay tolerance làm mất xác nhận; chỉ `Confirm Preview` mới bật Generate.
6. Thêm preset CRUD/import/export vào `%APPDATA%/RevitAPP/beam-longitudinal-drawing-presets.json`.
7. Validate numeric range, resource required, sheet identity và tolerance trước khi đóng dialog.

## Tests

- Factory defaults, validator boundary, JSON roundtrip/version mismatch/corrupt file.
- Manual UI smoke dark/light theme, keyboard, scroll, combobox đúng category.
- Unit test projection chain-distance → canvas coordinate, reverse direction, station labels và confirm-state invalidation.
- Manual preview smoke với 1/2/3 nhịp, gối chung giống/khác, dầm xoay và chain invalid.

## Success Criteria

- [x] Đủ tám input user liệt kê và các field sheet/layout cần thiết.
- [x] Không thể Generate khi thiếu type bắt buộc.
- [x] Preset mới độc lập preset cũ và roundtrip không mất field.
- [x] Dialog mở modal, owner là cửa sổ Revit.
- [x] Preview thể hiện đúng span/support/station và hướng xuất bản vẽ trước khi model bị sửa.
- [x] Generate không thể chạy khi preview invalid hoặc chưa được người dùng xác nhận.

## Risks

- Form quá rộng: nhóm theo `View`, `Annotation`, `Sheet`, dùng scroll và giữ action footer luôn thấy.
