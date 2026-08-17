# Phase 04 — Preview control 2D + 3D (WPF)

## Context Links

- Mẫu bám theo: `RevitAPP/Views/ColumnRebarPreviewControls.cs` (363 dòng — cả 2D và 3D trong 1 file)
- Layout mẫu: `RevitAPP/Views/ColumnRebarView.xaml:446-486` (khung REVIEW)
- Theme tham chiếu: `RevitAPP/Resources/Themes/ColumnRebarPreviewTheme.xaml`
- Phase trước: [phase-02](phase-02-factory-longitudinal.md), [phase-03](phase-03-factory-stirrup-context.md)

## Overview

- **Priority:** P2
- **Status:** pending
- **Effort:** 6h
- **Blockers:** P2, P3

Hai `FrameworkElement`/`Grid` control vẽ `BeamRebarGeometryPlan`. Bố cục 2D: **mặt cắt dọc toàn
tuyến làm trục chính + inset mặt cắt ngang góc phải** (đã chốt).

## Key Insights

1. **Ràng buộc vị trí file đã verify:** `RevitAPP.Core` TFM `net48;net8.0` không có `-windows`,
   `UseWPF` không bật → không chứa được `FrameworkElement`. `BeamRebarProView.xaml` ở
   `src/BeamRebarPro/Views/` và không thấy assembly `RevitAPP`. → Control **bắt buộc** ở
   `src/BeamRebarPro/Views/`. (Khớp quyết định đã chốt.)
2. **Không dùng lại được** `ColumnRebarPreviewControls.cs` vì nó ở `RevitAPP` (BeamRebarPro không ref
   ngược được). Chấp nhận trùng lặp **cơ chế** (orbit camera, `AddTube`) — đây là trùng lặp bắt buộc
   bởi ranh giới assembly, không phải vi phạm DRY do cẩu thả. Ghi rõ trong comment đầu file.
3. **Khác biệt hình học so với Cột:** cột thẳng đứng, dầm nằm ngang chạy dài. 2D của Cột map
   `(x|y, z)`; dầm phải map `(station dọc trục, z)` — tức **chiếu lên mặt phẳng chứa trục dầm**,
   không phải mặt phẳng thế giới XZ (dầm có thể xiên trong mặt bằng).
4. Tỉ lệ dầm rất lệch (dài 8000mm × cao 600mm) → nếu fit đều 2 phương thì thép dẹt không đọc được.
   **Cần hệ số phóng đại phương đứng** hoặc fit độc lập 2 trục. Chọn: fit độc lập, có nút khoá tỉ lệ 1:1.
5. Cột giảm mặt tube khi `>2500` path (`ColumnRebarPreviewControls.cs:294`). Dầm sẽ vượt xa → cần LOD
   nhiều bậc.

## Requirements

### Functional
- FR1: 2D vẽ mặt cắt dọc: bê tông dầm, vạch gối, cột đỡ, dầm giao, toàn bộ thép.
- FR2: 2D có inset mặt cắt ngang góc phải (thanh chủ + gia cường + đai tại station chọn).
- FR3: 2D hỗ trợ zoom (wheel) + pan (drag) + `Fit()`.
- FR4: 3D dựng tube theo đường kính thật, orbit/pan/zoom, double-click = fit.
- FR5: Đai kín (`IsClosedLoop`) được nối điểm cuối→đầu.
- FR6: Màu phân biệt theo `PathKind` (thép chủ / gia cường / đai) — không phải một màu đỏ duy nhất.

### Non-functional
- NFR1: **LOD** — `sides` của tube giảm theo số path: `>8000 → 3`, `>2500 → 4`, còn lại `7`.
- NFR2: Vẽ lại 2D < 100ms với 3000 path (dầm 3 nhịp điển hình).
- NFR3: Mỗi file < 300 dòng → **tách 2 file**: `BeamRebarPreview2D.cs`, `BeamRebarPreview3D.cs`.
- NFR4: XAML thêm vào view < 500 dòng tổng.

## Architecture

```
BeamRebarProView.xaml
  └── TabControl [2D | 3D]
        ├── <v:BeamRebarPreview2D Plan="{Binding PreviewPlan}"
        │        SelectedSpanIndex="{Binding SelectedSpanIndex}"/>
        └── <v:BeamRebarPreview3D Plan="{Binding PreviewPlan}"/>

BeamRebarPreview2D : FrameworkElement
  OnRender(dc):
    1. tính bounding (station 0..TotalLengthMm) × (min..max Z)
    2. Map(station, z) → Point màn hình   [fit độc lập 2 trục]
    3. vẽ Context (x-ray rect)
    4. vẽ vạch gối + nhãn nhịp
    5. vẽ Paths (màu theo Kind, dày theo DiameterMm)
    6. DrawSectionInset(góc phải)

BeamRebarPreview3D : Grid
  Rebuild(): Model3DGroup ← lights + context wireframe + tube mesh (LOD)
  camera yaw/pitch/distance, giống mẫu Cột
```

**Chiếu 2D (khác Cột — điểm cốt lõi):**
Với path point `p` và frame gốc `(start, Along)`:
`station = dot(p − start, Along)`, `elevation = p.Zmm`.
→ Dầm xiên trong mặt bằng vẫn duỗi thẳng đúng trên mặt cắt dọc.
Vì Plan lưu điểm ở toạ độ thế giới (mm), Plan **phải kèm** `start` + `Along` để chiếu.
→ **Bổ sung vào `BeamRebarGeometryPlan` ở P1**: `GeometryPoint3D OriginMm` +
`(double X, double Y, double Z) AlongAxis`. *(Ghi chú ngược cho P1 — cần thêm 2 field này.)*

## Related Code Files

**Create**
- `src/BeamRebarPro/Views/BeamRebarPreview2D.cs`
- `src/BeamRebarPro/Views/BeamRebarPreview3D.cs`

**Modify**
- `src/BeamRebarPro/Views/BeamRebarProView.xaml` — thêm khung REVIEW
- `RevitAPP.Core/Models/BeamRebarGeometryPlan.cs` — thêm `OriginMm` + `AlongAxis` (xem trên)

**Delete** — không có.

## Implementation Steps

1. Bổ sung `OriginMm` + `AlongAxis` vào Plan (sửa P1).
2. Viết `BeamRebarPreview2D`:
   - `DependencyProperty` `Plan`, `SelectedSpanIndex` với `AffectsRender`.
   - Chiếu station/elevation như trên.
   - Fit độc lập 2 trục; giữ zoom/pan như mẫu Cột (`:62-65`).
   - Vẽ context trước, thép sau (thép không bị che).
   - `IsClosedLoop` → thêm đoạn cuối→đầu.
3. Viết `DrawSectionInset`: chọn station theo `SelectedSpanIndex` (giữa nhịp), lấy path cắt qua
   station đó, chiếu lên mặt cắt ngang `(lateral, vertical)`. Thép dọc vẽ chấm tròn; đai vẽ khung.
4. Viết `BeamRebarPreview3D`: port cấu trúc camera + `AddTube` từ mẫu Cột, thay context box bằng
   khối dầm/cột/dầm giao dạng wireframe.
5. Áp LOD theo NFR1.
6. Thêm khung REVIEW vào `BeamRebarProView.xaml` (theo mẫu `ColumnRebarView.xaml:446-486`):
   TabControl 2D/3D, nút Fit, dòng chú giải, overlay `PreviewValidationMessage`.
7. Build R25 + kiểm thị giác bằng F5.

## Todo List

- [ ] Thêm `OriginMm`/`AlongAxis` vào Plan
- [ ] `BeamRebarPreview2D` — chiếu + context + thép
- [ ] Inset mặt cắt ngang
- [ ] Zoom/pan/Fit
- [ ] `BeamRebarPreview3D` — tube + camera + LOD
- [ ] Khung REVIEW trong XAML
- [ ] Build R25 pass
- [ ] F5 kiểm thị giác

## Success Criteria

- Dầm 3 nhịp hiển thị đủ: bê tông, cột đỡ, vạch gối, thép chủ trên/dưới, gia cường 2 lớp, đai
  (thấy rõ vùng dày ở gối và thưa ở giữa), đai phụ, thép chống phình.
- Đai kín hiển thị **khép kín** (không hở một cạnh).
- Vùng đai dày ở gối nhìn thấy rõ dày hơn vùng giữa nhịp — đây là kiểm tra thị giác xác nhận
  rủi ro A không xảy ra.
- 3D orbit mượt (không giật) với dầm 3 nhịp.
- Không có `StaticResource` cho màu; dùng `DynamicResource` hoặc fallback (xem Risk).
- Mỗi file < 300 dòng; XAML < 500 dòng.

## Risk Assessment

| Rủi ro | Khả năng | Tác động | Giảm thiểu |
|---|---|---|---|
| **D — hiệu năng 3D** | **Cao** | Trung bình | LOD 3 bậc; `BudgetGuard` P3 chặn 20 000; đo với dầm 5 nhịp |
| Tỉ lệ dầm quá lệch → thép không đọc được | **Cao** | Trung bình | Fit độc lập 2 trục (step 2) |
| 2D chiếu sai khi dầm xiên trong mặt bằng | Trung bình | **Cao** | Chiếu theo `AlongAxis`, không theo trục thế giới; test bằng dầm xiên 45° |
| Trùng lặp code với `ColumnRebarPreviewControls` | Chắc chắn | Thấp | Bắt buộc bởi ranh giới assembly; ghi comment giải thích |
| Theme không resolve trong BeamRebarPro | **Cao** | Thấp | Dùng `TryFindResource(...) as Brush ?? fallback` (**câu hỏi mở #2**) |
| Vẽ lại đồng bộ trên UI thread gây khựng | Trung bình | Trung bình | Debounce ở P5; nếu vẫn khựng → sinh Plan off-thread (factory thuần nên an toàn) |

## Security Considerations

Không có input không tin cậy — Plan sinh từ config nội bộ. Rủi ro chính là **DoS tự gây** qua số
path quá lớn, đã chặn bằng `BudgetGuard` (P3) và LOD. Control phải chịu được `Plan == null` và
`Paths` rỗng mà không ném (mẫu Cột guard tại `:75`).

## Next Steps

- P5 đấu nối ViewModel + debounce + test hồi quy.
- Cần trả lời câu hỏi mở #2 (theme) trước khi code step 2.
