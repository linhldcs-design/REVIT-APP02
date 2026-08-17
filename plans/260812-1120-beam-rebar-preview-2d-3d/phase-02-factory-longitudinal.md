# Phase 02 — Factory thuần: thép dọc + thép gia cường

## Context Links

- Nguồn refactor: `src/BeamRebarPro/Services/Rebar/LongitudinalBarCreator.cs` (519 dòng)
- Nguồn refactor: `src/BeamRebarPro/Services/Rebar/AdditionalBarCreator.cs` (105 dòng)
- Điều phối: `src/BeamRebarPro/Services/Rebar/BeamRebarOrchestrator.cs:377-498`
- Mẫu: `RevitAPP.Core/Services/ColumnRebarGeometryFactory.cs:212-355` (`AddMainBars`)
- Phase trước: [phase-01](phase-01-geometry-plan-model.md)

## Overview

- **Priority:** P1
- **Status:** pending
- **Effort:** 7h
- **Blockers:** P1
- **Song song được với:** P3 (file không giao nhau)

Rút toán hình học thép dọc ra khỏi `LongitudinalBarCreator` thành factory thuần. Creator giữ lại
**chỉ** phần gọi Revit API (`CreateFromCurves`, hook type, layout accessor).

## Key Insights

**Đã verify — các hàm THUẦN tách được nguyên vẹn:**

| Hàm | Vị trí | Ghi chú |
|---|---|---|
| `Vertical()` | `LongitudinalBarCreator.cs:443-459` | Trả `(vertical, usableHalf)`. Thuần toán trên `_cover` + `frame`. |
| `GetGapOffsets()` | `:225-253` | Vị trí ngang thép gia cường theo khe thanh chủ |
| `EvenInteriorOffsets()` | `:255-264` | Fallback chia đều trong lòng |
| `FirstLateral()` | `:461-462` | `count==1 ? 0 : -usableHalf` |
| `ClampSegmentInsideHost()` | `:512-518` | Co đoạn theo cover |
| `EvenLaterals()` | `:329-335` | **Hiện không được gọi ở đâu** — xem Risk |
| `GetLateralOffsets()` | `:266-283` | **Hiện không được gọi ở đâu** — xem Risk |
| `IsDefaultPositionSequence()` | `:464-482` | **Hiện không được gọi ở đâu** — xem Risk |

**Nhánh tạo hình (phải nhân bản chính xác):**

1. **Thẳng** (`:310-317`): 1 rebar tại `FirstLateral`, `SetFixedNumberLayout(count, usableHalf*2)`.
   → factory sinh `count` path song song, offset theo `FixedNumberOffsets`.
2. **Bẻ xuống đầu** (`:319-325` → `TryCreateMainBentEndSet:337-378`): polyline 2–3 đoạn.
   `bendDirection = atTop ? -Up : +Up` (`:352`); `maxDownFeet` clamp theo
   `height - coverTop - coverBottom - diameter` (`:344`). Nếu cả hai đầu ≤ 1e-6 → **rơi về nhánh thẳng** (`:347-348`).
3. **Gia cường có bẻ** (`TryCreateSegmentWithEndBendsSet:380-420`): giống (2) nhưng
   `bendDirection` luôn là `-Up` (`:398,:404`) **bất kể `atTop`** — khác biệt tinh tế, dễ sai.
   Có `layoutDistanceFeet` tuỳ chọn (`:412`).
4. **Gia cường theo khe** (`CreateSegment:118-155`): nếu `gapOffsets.Count > 0` và `count >= 2` →
   1 set tại `ordered[0]` với `layoutDistance = ordered[^1] - ordered[0]` (`:132`).
   Nếu `count == 1` → tạo **từng thanh riêng** (`:139-147`), không layout.
   `forceFixedNumberAcrossWidth` (dùng cho Layer ≥ 2, `AdditionalBarCreator.cs:83,93,99,102`)
   → bỏ qua gap, dùng `FirstLateral` + fixed number.

**Đoạn dọc `[startT, endT]`:**
- Thép chủ chạy suốt host (`BeamRebarOrchestrator.cs:391-413`), **không** dừng ở gối trong.
- Thép gia cường trên nằm quanh gối (`:424-487`); dưới nằm giữa nhịp tính trên **L thông thủy**
  (`AdditionalBarCreator.cs:33-84`) với 3 nhánh: anchor tường minh / `LengthMm` cố định / mặc định 1/8–7/8.

## Requirements

### Functional
- FR1: `BeamRebarLongitudinalFactory` sinh `BeamRebarPath[]` cho: thép chủ trên, chủ dưới,
  gia cường trên lớp 1+2, gia cường dưới lớp 1+2.
- FR2: Số path sinh ra **bằng đúng** số thanh Revit thực tế tạo (đã nhân bản layout).
- FR3: Toạ độ path (quy đổi mm→feet) khớp toạ độ curve mà creator dựng, sai số ≤ 1e-6 feet.
- FR4: Nhánh bẻ đầu sinh polyline đúng số đỉnh (2 hoặc 3 đoạn).

### Non-functional
- NFR1: File factory < 300 dòng. Nếu vượt → tách `BeamRebarLongitudinalMath.cs` (offset ngang) riêng.
- NFR2: Không tham chiếu `Autodesk.*` trong `RevitAPP.Core`.

## Architecture

```
QuickSettingModel + PureSpanFrame + Support widths
              │
              ▼
BeamRebarLongitudinalFactory.CreatePaths(...)
   ├── Vertical(atTop, layerOffset)      → (verticalMm, usableHalfMm)
   ├── LateralOffsets(...)               → gap | fixed-number | even
   │      └── RebarLayoutMath.FixedNumberOffsets  (từ P0)
   ├── LongitudinalSpanT(config, ...)    → (startT, endT)
   └── BuildPolyline(bend...)            → GeometryPoint3D[]
              │
              ▼
      IReadOnlyList<BeamRebarPath>
```

**Data flow rõ ràng:** vào là config + frame thuần; ra là danh sách path đã nhân bản. Không có state,
toàn `static` hoặc instance immutable → an toàn gọi off-thread cho preview debounce (P5).

## Related Code Files

**Create**
- `RevitAPP.Core/Services/BeamRebarLongitudinalFactory.cs`
- `tests/BeamRebarPro.Tests/BeamRebarLongitudinalFactoryTests.cs`

**Modify** (chỉ sau khi test factory xanh)
- `src/BeamRebarPro/Services/Rebar/LongitudinalBarCreator.cs` — thay phần tính toạ độ bằng gọi
  factory; giữ nguyên phần Revit API.

**Delete** — không có.

## Implementation Steps

1. **Viết factory trước, chưa đụng creator.** Port `Vertical`, `GetGapOffsets`,
   `EvenInteriorOffsets`, `FirstLateral`, `ClampSegmentInsideHost` sang factory, đổi đơn vị sang mm.
2. Thêm `LateralOffsets(...)` gom 3 nhánh chọn vị trí ngang (gap / force-fixed / even) — đây là chỗ
   dễ sai nhất, viết test cho từng nhánh.
3. Thêm `BuildPolyline` xử lý bẻ đầu. **Chú ý:** thép chủ dùng `atTop ? -Up : +Up`; thép gia cường
   luôn `-Up`. Truyền `bendDirection` như tham số tường minh, không suy trong hàm — để khác biệt này
   hiển ngôn thay vì ẩn.
4. Viết test đối chiếu (xem Success Criteria).
5. **Sau khi test xanh:** sửa `LongitudinalBarCreator` gọi factory. Chiến lược an toàn:
   - Giữ chữ ký public không đổi.
   - Trong `CreateBars`/`CreateSegment*`, thay đoạn tính `p0/p1` bằng gọi factory rồi map
     `GeometryPoint3D` → `XYZ` qua adapter (mm→feet).
   - **Giữ nguyên** lời gọi `SetFixedNumberLayout` — Revit vẫn nhân bản như cũ. Factory chỉ nhân
     bản **cho preview**. Creator vẫn tạo 1 rebar + layout.
6. Build R25 + test hồi quy (P5 định nghĩa đầy đủ; ở đây chạy test unit hiện có).

**Quyết định quan trọng ở step 5:** creator **không** chuyển sang tạo N rebar riêng lẻ. Giữ cơ chế
layout của Revit (đúng như production hiện tại) → rủi ro C giảm mạnh, và vẫn đạt mục tiêu "chung
một nguồn geometry" vì cả hai đều lấy vị trí thanh gốc + offset từ cùng factory.

## Todo List

- [ ] Port `Vertical` → mm, có test
- [ ] Port `LateralOffsets` (3 nhánh), có test từng nhánh
- [ ] Port `ClampSegmentInsideHost`, có test
- [ ] `BuildPolyline` bẻ đầu (2 hướng bend), có test
- [ ] `CreatePaths` ghép thép chủ + gia cường
- [ ] Test đối chiếu toạ độ với creator
- [ ] Đấu nối creator → factory (giữ layout Revit)
- [ ] Build R25 pass, test pass

## Success Criteria

| Test | Expect |
|---|---|
| Thép chủ 3 cây, `usableHalf=200mm` | 3 path tại lateral −200, 0, +200 |
| Thép chủ 1 cây | 1 path tại lateral 0 (khớp `FirstLateral`) |
| Gia cường 2 cây, main 3 cây, position mặc định | 2 path tại tâm khe 1 và khe 2 |
| Gia cường `forceFixedNumberAcrossWidth=true` | bỏ qua gap, rải fixed-number từ `−usableHalf` |
| Gia cường main ≤ 2 | rơi về `EvenInteriorOffsets` |
| Bẻ đầu trái 300mm, atTop | polyline 3 đỉnh, đỉnh đầu thấp hơn 300mm |
| Bẻ cả hai đầu = 0 | polyline 2 đỉnh (nhánh thẳng) |
| Bẻ vượt `maxDown` | clamp về `height − coverTop − coverBottom − d` |
| Thép dưới giữa nhịp mặc định | `startT/endT` = 1/8 và 7/8 **L thông thủy**, không phải L tim-tim |

- Với một dầm mẫu cố định, toạ độ factory (đổi sang feet) khớp toạ độ creator tính bằng tay ≤ 1e-6.
- `dotnet build -c Debug.R25 -p:DeployAddin=false` → 0 errors.
- File < 300 dòng.

## Risk Assessment

| Rủi ro | Khả năng | Tác động | Giảm thiểu |
|---|---|---|---|
| **C — refactor làm hỏng tạo thép production** | **Trung bình** | **Cao** | Step 5 giữ nguyên cơ chế layout Revit; chữ ký public không đổi; test đối chiếu toạ độ trước khi đấu nối; F5 smoke ở P5 |
| Nhầm hướng bend giữa thép chủ và gia cường | **Cao** | Trung bình | Truyền `bendDirection` tường minh (step 3) + test cả hai |
| 3 hàm chết (`EvenLaterals`, `GetLateralOffsets`, `IsDefaultPositionSequence`) gây nhầm khi port | Trung bình | Thấp | **Không port** 3 hàm này. Xác nhận lại bằng grep trước khi xoá; nếu xoá thì làm ở commit riêng, tách khỏi refactor |
| Sai lệch tích luỹ khi đổi feet↔mm | Thấp | Trung bình | Chuyển đổi tại đúng 1 adapter (P1); test sai số 1e-6 |
| `layoutDistance` cho nhánh gap tính sai | Trung bình | Trung bình | Test riêng case `count>=2` với gap: `layoutDistance = ordered[^1] − ordered[0]` |

**Lưu ý về 3 hàm chết:** đã grep xác nhận không có caller trong `LongitudinalBarCreator`. Nhưng
**chưa xoá trong phase này** — xoá code không dùng là thay đổi độc lập, gộp vào refactor sẽ làm
diff khó review và khó bisect nếu hồi quy. Đề xuất tách commit riêng, cần user xác nhận.

## Security Considerations

`GetGapOffsets` parse chuỗi `positionInSection` từ UI (`:236-240`). Bản port phải giữ
`int.TryParse` + `Math.Clamp` (`:240,:244`) — không được đổi sang `int.Parse` (crash trên input rác).
Không có I/O, không có deserialization không tin cậy.

## Next Steps

- P3 chạy song song (file riêng biệt).
- P4 cần cả P2 và P3 xong mới vẽ đủ.
