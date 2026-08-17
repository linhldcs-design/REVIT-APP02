# Phase 03 — Factory thuần: đai + context bê tông

## Context Links

- Nguồn refactor: `src/BeamRebarPro/Services/Rebar/StirrupCreator.cs` (435 dòng)
- Nguồn refactor: `src/BeamRebarPro/Services/Rebar/AdditionalTieCreator.cs` (170 dòng)
- Nguồn refactor: `src/BeamRebarPro/Services/Rebar/AntiBulgeCreator.cs` (159 dòng)
- Vùng đai theo gối: `src/BeamRebarPro/Services/Rebar/BeamRebarOrchestrator.cs:500-534` (`TryCreateStirrupFrame`)
- Layout math: `RevitAPP.Core/Services/RebarLayoutMath.cs` (từ P0)
- Phase trước: [phase-01](phase-01-geometry-plan-model.md)

## Overview

- **Priority:** P1
- **Status:** pending
- **Effort:** 8h
- **Blockers:** P1
- **Song song được với:** P2

Phase nặng nhất. Sinh **mọi** đai (chính, vùng dày, tăng cường dầm phụ, đai phụ lồng kín, móc C,
đai C lớp 2), thép chống phình, và **context bê tông** (khối dầm + cột đỡ + dầm giao).

## Key Insights

**Hàm THUẦN tách được (đã verify):**

| Hàm | Vị trí | Vai trò |
|---|---|---|
| `StirrupProfile()` | `StirrupCreator.cs:320-346` | Khung đai chữ nhật kín, 4 góc |
| `StirrupProfileNarrow()` | `:350-375` | Đai phụ hẹp giữa 2 thanh chủ |
| `StirrupProfileCHook()` | `:379-397` | 1 đường thẳng đứng (móc C) |
| `BuildSecondaryRanges()` | `:157-188` | Vùng chặn quanh dầm phụ |
| `CreateTwoEnds()` phần tính vùng | `:194-211` | 3 vùng End1/Mid/End2 + scale khi tràn |
| `LateralOfBar()` | `:109` | Vị trí ngang thanh chủ (khớp `LongitudinalBarCreator.Vertical`) |

**Cấu trúc phân vùng đai (phải nhân bản đúng thứ tự):**

1. `TryCreateStirrupFrame` (`Orchestrator:500-534`) **co nhịp** trước: lùi
   `min(halfWidth + firstDistance, span/3)` mỗi đầu có gối. → đai không xuyên cột.
2. `CreateTwoEnds` chia 3 vùng: `[0, endZoneStart]` @SpacingEnd, `[endZoneStart, L−endZoneEnd]`
   @SpacingMid, `[L−endZoneEnd, L]` @SpacingEnd. Mặc định endZone = `L/4` (`:196`).
   Nếu tổng 2 vùng > L → **scale tỉ lệ** (`:206-211`).
3. `CreateZone` **cắt bỏ** phần chồng `blockedRanges` (vùng dầm phụ) rồi rải từng đoạn còn lại
   (`:226-246`). Con trỏ `cursor` nhảy qua `blocked.RightEndFeet`.
4. Mỗi đoạn → `RebarLayoutMath.MaximumSpacingStations` (P0).
5. Đai tăng cường dầm phụ: 2 cụm mỗi dầm phụ, bước 50mm cố định (`:50`), dài `3 × 50mm` (`:63`),
   cụm trái rải `barsOnNormalSide:false` (ngược chiều), cụm phải `true` (`:66-71`).
   → **Cụm trái đi lùi từ `LeftEndFeet`**, phải xử lý dấu đúng.
6. Đai phụ (`CreateAdditionalStirrup:91-155`): chỉ khi `mainBarCount >= 3` (`:101`).
   Lồng kín nới rộng mỗi cạnh `mainBar/2 + addDia/2` (`:127-129`). Móc C dùng 1 vị trí thanh (`:117`).
   Rải theo **cùng vùng** đai chính (`:143-152`).

**Context bê tông:**
- Khối dầm: từ `PureSpanFrame` (start/end center, width, height) — có sẵn.
- Cột đỡ: `Support.Location` + `HalfWidthFeet` (`Support.cs`). **Chỉ có nửa bề rộng theo phương dầm**,
  không có bề rộng ngang và chiều cao cột. → vẽ cột như khối x-ray dùng `HalfWidth*2` cho cả 2 phương,
  chiều cao lấy `2 × heightDầm` (đủ để thấy bối cảnh). Đây là **xấp xỉ có chủ đích**, ghi rõ trong comment.
- Dầm giao: `SecondaryStirrupStation(stationFeet, halfWidthFeet)` — tương tự, xấp xỉ.

## Requirements

### Functional
- FR1: Sinh path cho đai chính đúng số lượng và vị trí theo 3 vùng + vùng chặn.
- FR2: Sinh đai tăng cường dầm phụ (2 cụm/dầm phụ, đúng chiều rải).
- FR3: Sinh đai phụ lồng kín + móc C khi `mainBarCount >= 3`.
- FR4: Sinh đai C giữ thép lớp 2, thép chống phình (thanh dọc + tie).
- FR5: Sinh `BeamRebarContextVolume[]` cho dầm, cột đỡ, dầm giao.
- FR6: Đai kín đánh dấu `IsClosedLoop = true` để renderer nối điểm cuối→đầu.

### Non-functional
- NFR1: **Ngân sách path.** Ném `ArgumentException` khi tổng path > 20 000 (khớp
  `MaxExplicitStirrupPaths` của Cột, `ColumnRebarGeometryFactory.cs:12`). Thông điệp tiếng Việt gợi ý
  tăng bước đai.
- NFR2: Guard **trước** vòng lặp sinh (như Cột `:100-116`), không để tràn bộ nhớ rồi mới chặn.
- NFR3: File < 300 dòng → **tách 2 file**: `BeamRebarStirrupFactory.cs` và
  `BeamRebarContextFactory.cs`.

## Architecture

```
PureSpanFrame + StirrupConfig + Support[] + SecondaryStirrupStation[]
        │
        ▼
BeamRebarStirrupFactory
  ├── StirrupZones(config, length)         → [(from,to,spacing,zone)]   (3 vùng + scale)
  ├── BuildSecondaryRanges(stations)       → vùng chặn
  ├── SubtractBlocked(zone, blocked[])     → đoạn còn lại
  ├── RebarLayoutMath.MaximumSpacingStations(...)   ← P0
  ├── Profile(kind, t)                     → 4 góc | 2 điểm (móc C)
  └── BudgetGuard(total)                   → throw nếu > 20 000
        │
        ▼  BeamRebarPath[] (Kind=Stirrup/StirrupSecondary/AdditionalStirrup*/Layer2Tie/AntiBulge*)

BeamRebarContextFactory
  └── BuildContext(frame, supports, crossBeams) → BeamRebarContextVolume[]
```

## Related Code Files

**Create**
- `RevitAPP.Core/Services/BeamRebarStirrupFactory.cs`
- `RevitAPP.Core/Services/BeamRebarContextFactory.cs`
- `tests/BeamRebarPro.Tests/BeamRebarStirrupFactoryTests.cs`

**Modify** (sau khi test xanh)
- `src/BeamRebarPro/Services/Rebar/StirrupCreator.cs` — dùng factory cho profile + phân vùng
- `src/BeamRebarPro/Services/Rebar/AdditionalTieCreator.cs`
- `src/BeamRebarPro/Services/Rebar/AntiBulgeCreator.cs`

**Delete** — không có.

## Implementation Steps

1. Port `StirrupZones` (3 vùng + logic scale `:206-211`). Test trước.
2. Port `BuildSecondaryRanges` (`:157-188`) — giữ nguyên điều kiện loại bỏ
   `leftStart <= 0 || rightEnd >= spanLength` (`:179`).
3. Viết `SubtractBlocked` tách từ `CreateZone:226-246` — thuần, dễ test.
4. Ghép với `RebarLayoutMath.MaximumSpacingStations` để ra danh sách station đai.
5. Port 3 hàm profile. **Chú ý:** dùng `PureSpanFrame.PointAt` (P1) thay `Corner` cục bộ để DRY.
6. Đai tăng cường dầm phụ: chiều rải ngược cho cụm trái — biểu diễn bằng station **giảm dần** từ
   `LeftEndFeet`, hoặc chuẩn hoá thành `[start, end]` tăng dần. Chọn cách sau cho đơn giản,
   **nhưng phải test** giá trị hai đầu khớp Revit.
7. Port đai C lớp 2 + chống phình.
8. `BeamRebarContextFactory` — dầm + cột + dầm giao (xấp xỉ như Key Insights).
9. Thêm `BudgetGuard` **trước** mọi vòng sinh.
10. Đấu nối creator (giữ nguyên `SetLayoutAsMaximumSpacing` — Revit vẫn tự nhân bản như production).
11. Build R25 + test.

## Todo List

- [ ] `StirrupZones` + test (gồm case scale tràn)
- [ ] `BuildSecondaryRanges` + test
- [ ] `SubtractBlocked` + test
- [ ] Ghép station đai + test đếm số đai
- [ ] 3 profile (kín / hẹp / móc C) + test
- [ ] Đai tăng cường dầm phụ (2 cụm, đúng chiều) + test
- [ ] Đai C lớp 2 + chống phình
- [ ] `BeamRebarContextFactory` + test
- [ ] `BudgetGuard` + test ném đúng ngưỡng
- [ ] Đấu nối 3 creator
- [ ] Build R25 pass, test pass

## Success Criteria

| Test | Expect |
|---|---|
| Uniform L=6000, spacing=200 | 31 đai (30 khoảng), đầu tại 0, cuối tại 6000 |
| TwoEnds L=8000, endZone mặc định | vùng `[0,2000]`, `[2000,6000]`, `[6000,8000]` |
| endZoneStart+End > L | scale tỉ lệ, tổng đúng bằng L |
| 1 dầm phụ giữa nhịp | vùng chặn bị cắt khỏi đai chính; 2 cụm tăng cường 4 đai mỗi cụm |
| `mainBarCount = 2` | **0** đai phụ (guard `:101`) |
| Đai lồng kín | cạnh nới ra `mainBar/2 + addDia/2` mỗi bên |
| Móc C | path 2 điểm, không `IsClosedLoop` |
| Đai kín | path 4 điểm + `IsClosedLoop = true` |
| Vượt ngân sách | throw `ArgumentException`, thông điệp gợi ý tăng bước |

- Số đai factory sinh = số đai Revit tạo (đối chiếu F5 ở P5).
- Cả 2 file factory < 300 dòng.
- `dotnet build -c Debug.R25 -p:DeployAddin=false` → 0 errors.

## Risk Assessment

| Rủi ro | Khả năng | Tác động | Giảm thiểu |
|---|---|---|---|
| **A — sai số đai do hiểu sai MaximumSpacing** | Trung bình | **Cao** | P0 đã đặc tả + test; **F5 đếm đai thật ở P5 là gate bắt buộc** |
| **D — hiệu năng: hàng nghìn đai** | **Cao** | Trung bình | `BudgetGuard` 20 000; LOD ở P4; đo thời gian sinh với dầm 5 nhịp bước 100mm |
| Chiều rải cụm đai tăng cường trái bị đảo | **Cao** | Trung bình | Test tường minh 2 đầu cụm trái (step 6) |
| Context cột/dầm giao xấp xỉ sai lệch thị giác | Trung bình | Thấp | Vẽ x-ray wireframe (không đặc) → sai lệch không che thép; ghi rõ là xấp xỉ |
| **C — hỏng production** | Trung bình | **Cao** | Giữ nguyên cơ chế layout Revit; test hồi quy P5 |
| Đai phụ rải sai vùng khi có vùng chặn | Trung bình | Trung bình | `CreateNarrowZone:401-417` có logic chặn riêng — port và test riêng |

## Security Considerations

`BudgetGuard` là biện pháp chống **DoS tự gây** (người dùng nhập bước đai = 1mm → sinh hàng triệu
path, treo UI). Đây là yêu cầu an toàn thực chất, không chỉ hiệu năng — phải guard **trước** vòng lặp.
Guard chia 0 cho `spacing <= 0` đã có ở P0.

## Next Steps

- P4 (preview control) cần cả P2 + P3.
- Trả lời câu hỏi mở #3 (ngưỡng LOD) trước khi vào P4.
