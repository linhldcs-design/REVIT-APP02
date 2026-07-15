# Journal — Point Cloud Display Features (2026-06-09)

## Bối cảnh
User muốn clone panel hiển thị Point Cloud của Qbitec (slider Point size / Brightness / Contrast / Transparency / X-ray). Thực hiện trong add-in `RevitAPP` (Nice3point, R25, .NET 8, CommunityToolkit.Mvvm).

## Hai feature đã giao

### 1. Native display panel (plan 260606-pointcloud-display-panel-addin)
Dockable panel + ExternalEvent, đổi `PointCloudColorMode` (RGB/Elevation/Intensity/Normals/FixedColor) + scan visibility qua Revit override API. Chỉ áp dụng point cloud `.rcp/.rcs` (`SupportsOverrides=true`).

### 2. Custom render + sliders (plan 260606-pointcloud-custom-render-sliders)
Tự render point cloud qua **DirectContext3D** (billboard quad camera-facing) → kiểm soát point size / brightness / transparency / color mà Revit API native không cho. Áp dụng cho MỌI point cloud (kể cả engine third-party).

## Quyết định kỹ thuật + lý do

- **Revit API KHÔNG hỗ trợ point size/brightness/contrast/transparency/halftone** cho point cloud (verify bằng tài liệu Autodesk chính thức). → buộc dùng DirectContext3D tự render.
- **Point size:** `PrimitiveType.PointList` cố định 1px → phải vẽ **billboard quad** (mỗi điểm = 2 tam giác camera-facing).
- **16-bit index limit:** `IndexBuffer` Revit dùng index 16-bit (max 65535 vertex) → **chunk ≤16.000 điểm/buffer**, index cục bộ. Đây là bug nghiêm trọng (point cloud >16K điểm ra hình rác) — chỉ lộ khi render nhiều điểm, không bắt được lúc POC ít điểm.
- **Transparency:** per-vertex alpha KHÔNG đủ → phải `EffectInstance.SetTransparency()` + gate `DrawContext.IsTransparentPass()`.
- **Contrast + X-ray (Tomographic): KHÔNG khả thi** qua DirectContext3D (cần custom pixel shader) → slider disabled + tooltip.

## Bài học reverse-engineering Qbitec (qua Revit MCP)
Đọc ExtensibleStorage thật của Qbitec trong model:
- Họ lưu display settings **per-view** dạng JSON (schema `QbitecStorage`): PointSizePercentage/Brightness/Contrast/TomographicContrast/Opacity/RenderMode.
- Render bằng **custom PointStreamEngine** riêng (non-file engine) — point size/contrast/x-ray là tính năng engine, không phải Revit.
- Family `qbitec360cam` = camera panorama 360° (schema `PanoStorage_v2`), KHÔNG liên quan slider.
- **Áp dụng:** thêm `PointCloudSettingsStore` lưu render state per-view qua ExtensibleStorage (schema `RevitAppPointCloudRenderState`) — chuyển view/lưu file giữ nguyên setting.

## POC gate (quan trọng)
Phase 0 verify trong Revit thật TRƯỚC khi đầu tư: `GetPoints()` đọc được point cloud third-party (PointStreamEngine, SupportsOverrides=false) → 20.000 điểm. Rủi ro lớn nhất loại bỏ sớm.

## Lỗi pass-CI-nhưng-crash-runtime (code-reviewer bắt)
1. GPU buffer leak mỗi frame (camera xoay) → `RenderChunk:IDisposable` + dispose trước rebuild.
2. 16-bit index overflow → chunking.
3. `_enabled=true` trước khi API throw → state desync.
4. Slider last-wins mất giá trị cuối → handler re-raise.
5. Per-tick Transaction làm đầy undo stack → chỉ lưu khi Enable+Disable.

→ Build xanh ≠ chạy đúng. Smoke F5 + review là bắt buộc.

## Kết quả
- Build R25 deploy OK, 55/55 xUnit pass.
- Smoke F5 verify: point size to/nhỏ ✓, transparency mờ thật ✓, brightness ✓, không lag ✓, per-view persistence ✓.
- Không regression 4 command cũ + native panel.

## Out of scope (v2)
- Contrast + X-ray (cần custom engine — effort hàng tháng).
- Multi-version R24/R26/R27 (R27 cần migrate Rebar API: RebarHookOrientation bỏ).
- LOD nâng cao cho point cloud rất lớn.
