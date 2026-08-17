# Phase 00 — Scaffold + ProjectReference + Đặc tả layout Revit

## Context Links

- Mẫu: `RevitAPP.Core/Services/ColumnRebarGeometryFactory.cs`
- Mẫu test: `tests/RevitAPP.Tests/ColumnRebar/ColumnRebarGeometryFactoryTests.cs`
- Điểm rủi ro A: `src/BeamRebarPro/Services/Rebar/LongitudinalBarCreator.cs:484-507`,
  `src/BeamRebarPro/Services/Rebar/StirrupCreator.cs:268-270,290-294,431-432`
- Skill: `/bs:revit-addin`, `/bs:revit-test`

## Overview

- **Priority:** P1 (blocker của mọi phase sau)
- **Status:** pending
- **Effort:** 4h

Chuẩn bị hạ tầng project + **đặc tả bằng văn bản và bằng test** ngữ nghĩa hai API layout của Revit.
Đây là phase quan trọng nhất về rủi ro: nếu ngữ nghĩa sai, preview sẽ vẽ 1 thanh thay vì cả bó và
bước đai sẽ sai trên toàn bộ sản phẩm.

## Key Insights

**Đã verify (đọc code, không suy đoán):**

1. `BeamRebarPro.csproj` hiện chỉ có `ProjectReference` tới `RevitAPP.Licensing`
   (`BeamRebarPro.csproj`, ItemGroup cuối). Chưa ref `RevitAPP.Core` → phải thêm.
2. `RevitAPP.Core` `TargetFrameworks = net48;net8.0` — **không** `-windows`, `UseWPF` không bật.
   → **Core KHÔNG chứa được `FrameworkElement`/`Viewport3D`.** Đây là lý do kỹ thuật bắt buộc
   preview control phải nằm trong `src/BeamRebarPro/Views/` (khớp quyết định đã chốt).
3. `RevitAPP.Core` `Configurations` đã bao gồm `Debug.R22..R27` → ProjectReference resolve được từ
   BeamRebarPro không cần sửa Core csproj.
4. `tests/BeamRebarPro.Tests` đã tồn tại (xunit, net8.0) và dùng cơ chế **link file nguồn** thay vì
   ProjectReference (vì addin target Revit). Test factory Core sẽ đi qua `ProjectReference` tới
   `RevitAPP.Core` — sạch hơn, không cần link.

### Đặc tả ngữ nghĩa layout Revit (rủi ro A)

Hai API được dùng, phải nhân bản CHÍNH XÁC trong factory:

**`SetLayoutAsMaximumSpacing(spacing, arrayLength, barsOnNormalSide, includeFirstBar, includeLastBar)`**
- Dùng tại: `StirrupCreator.cs:270` (vùng đai), `:293` (đai tăng cường dầm phụ), `:432` (đai phụ),
  `AntiBulgeCreator.cs:129` (hàng tie).
- Ngữ nghĩa: Revit **phủ hết** đoạn `arrayLength` với bước **≤** `spacing`, chia **đều**.
  Số khoảng `n = ceil(arrayLength / spacing)`; bước thực `s = arrayLength / n`; số thanh `= n + 1`
  (khi include cả hai đầu). Thanh đầu tại gốc curve, thanh cuối tại `arrayLength`.
- Comment tại `StirrupCreator.cs:265-267` xác nhận đúng ý đồ này ("chia đều PHỦ HẾT vùng ... đai
  đầu tại `from` và đai cuối tại `to`").
- Hướng rải: `barsOnNormalSide` — tại `:270,:432` là `true`; tại `:293` là tham số
  (`false` cho cụm trái, `true` cho cụm phải — xem `StirrupCreator.cs:66-71`).
- **Edge case bắt buộc test:** `arrayLength <= spacing` → chỉ 2 thanh (đầu + cuối).
  `arrayLength <= 1e-6` → sớm return, 0 thanh (`CreateZoneUnblocked:252`).

**`SetLayoutAsFixedNumber(count, arrayLength, barsOnNormalSide:true, includeFirstBar:true, includeLastBar:true)`**
- Dùng tại: `LongitudinalBarCreator.cs:500`.
- Ngữ nghĩa: đúng `count` thanh, rải đều trên `arrayLength`, bước `= arrayLength / (count - 1)`.
- Thanh gốc dựng tại `FirstLateral(usableHalf, count)` = `-usableHalf` khi `count > 1`, `0` khi
  `count == 1` (`LongitudinalBarCreator.cs:461-462`). Normal khi `CreateFromCurves` là
  `frame.Across` (`:369,:410,:431`) → rải theo chiều `+Across`, đi từ mép trái vào trong tiết diện.
- `arrayLength` = `layoutDistanceFeet ?? usableHalf * 2` (`:492`).
- **Guard:** `count <= 1 || usableHalf <= 1e-6` → không set layout, chỉ 1 thanh (`:487`).

**Kết luận cho factory:** hàm thuần cần sinh:
```
MaximumSpacingStations(arrayLengthMm, spacingMm) -> IReadOnlyList<double>
FixedNumberOffsets(count, arrayLengthMm) -> IReadOnlyList<double>
```
Hai hàm này là **điểm chân lý duy nhất**, được cả preview lẫn (sau này) creator dùng.

## Requirements

### Functional
- FR1: `BeamRebarPro` tham chiếu được `RevitAPP.Core`, build pass cả 6 config R22–R27.
- FR2: Có class thuần `RebarLayoutMath` trong `RevitAPP.Core/Services/` expose 2 hàm trên.
- FR3: Test project chạy được test cho `RebarLayoutMath`.

### Non-functional
- NFR1: Không đổi bất kỳ hành vi runtime nào ở phase này (chỉ thêm, không sửa creator).
- NFR2: `RebarLayoutMath` < 120 dòng.

## Architecture

```
RevitAPP.Core (net48;net8.0, KHÔNG WPF)
└── Services/RebarLayoutMath.cs      [MỚI]  ← chân lý layout
        ▲                    ▲
        │ (P2/P3 factory)    │ (P5 creator tiêu thụ)
src/BeamRebarPro  ────ProjectReference────┘
```

Không có data flow runtime ở phase này — chỉ dựng đường dẫn tham chiếu và hằng số toán.

## Related Code Files

**Modify**
- `src/BeamRebarPro/BeamRebarPro.csproj` — thêm `ProjectReference` tới `..\..\RevitAPP.Core\RevitAPP.Core.csproj`
- `tests/BeamRebarPro.Tests/BeamRebarPro.Tests.csproj` — thêm `ProjectReference` tới `RevitAPP.Core`

**Create**
- `RevitAPP.Core/Services/RebarLayoutMath.cs`
- `tests/BeamRebarPro.Tests/RebarLayoutMathTests.cs`

**Delete** — không có.

## Implementation Steps

1. Thêm `ProjectReference` `RevitAPP.Core` vào `BeamRebarPro.csproj` (ItemGroup cùng chỗ Licensing).
2. Build `dotnet build src/BeamRebarPro/BeamRebarPro.csproj -c Debug.R25 -p:DeployAddin=false`.
   Nếu lỗi TFM/config → kiểm tra `Configurations` của Core đã có `Debug.R25` (đã verify là có).
3. Tạo `RebarLayoutMath` với 2 static method, `file-scoped namespace`, `static class`:
   - `MaximumSpacingStations(double arrayLengthMm, double spacingMm)`:
     guard `spacingMm <= 0` → throw `ArgumentException`; `arrayLengthMm <= 1e-6` → mảng rỗng;
     `n = (int)Math.Ceiling(arrayLengthMm / spacingMm - 1e-9)`; trả `i * arrayLengthMm / n` với
     `i ∈ [0..n]`.
   - `FixedNumberOffsets(int count, double arrayLengthMm)`:
     `count <= 0` → rỗng; `count == 1` → `[0]`; ngược lại `i * arrayLengthMm / (count - 1)`.
4. Viết test khoá ngữ nghĩa (xem Success Criteria).
5. Chạy `dotnet test tests/BeamRebarPro.Tests`.
6. Build lại R25 + spot-check R22 và R27 (2 biên của ma trận version).

## Todo List

- [ ] Thêm ProjectReference Core → BeamRebarPro
- [ ] Build R25 pass
- [ ] Tạo `RebarLayoutMath.cs`
- [ ] Thêm ProjectReference Core → test project
- [ ] Viết `RebarLayoutMathTests.cs`
- [ ] `dotnet test` pass
- [ ] Build R22 + R27 pass

## Success Criteria

Test bắt buộc pass (đây là hợp đồng khoá rủi ro A):

| Case | Input | Expect |
|---|---|---|
| Chia chẵn | `arrayLength=1000, spacing=250` | 5 station: 0,250,500,750,1000 |
| Không chia chẵn → bước co lại | `arrayLength=1000, spacing=300` | 5 station bước 250 (**không** phải 4 station bước 300) |
| Vùng ngắn hơn bước | `arrayLength=200, spacing=300` | 2 station: 0, 200 |
| Vùng rỗng | `arrayLength=0` | rỗng |
| Spacing không hợp lệ | `spacing=0` | throw `ArgumentException` |
| FixedNumber nhiều thanh | `count=4, arrayLength=300` | 0,100,200,300 |
| FixedNumber 1 thanh | `count=1` | `[0]` (khớp `FirstLateral` trả 0) |
| FixedNumber 0 | `count=0` | rỗng |

- `dotnet build -c Debug.R25 -p:DeployAddin=false` → 0 errors.
- Không có file `.cs` mới nào vượt 300 dòng.

## Risk Assessment

| Rủi ro | Khả năng | Tác động | Giảm thiểu |
|---|---|---|---|
| Ngữ nghĩa MaximumSpacing hiểu sai | Trung bình | **Cao** — sai toàn bộ đai | Test bảng trên; xác minh lại bằng F5 đếm đai thực tế ở P5 |
| Core ref kéo `Newtonsoft.Json` vào addin làm phình ILRepack | Thấp | Trung bình | `IsRepackable=false` đã set sẵn trong BeamRebarPro.csproj — xác nhận size DLL sau build |
| Xung đột `Configurations` giữa Core và BeamRebarPro | Thấp | Trung bình | Đã verify Core có đủ R22–R27 |

**Chưa verify được bằng code tĩnh:** ngữ nghĩa chính xác của `SetLayoutAsMaximumSpacing` là hành vi
runtime của Revit. Đặc tả trên dựa vào comment của tác giả tại `StirrupCreator.cs:265-267` + suy luận
từ tên tham số. **Phải xác nhận bằng F5 đếm đai thật ở P5** trước khi coi là đóng.

## Security Considerations

Không có bề mặt tấn công mới — thuần toán học, không I/O, không input người dùng chưa kiểm chứng.
Guard chia cho 0 và giá trị âm/NaN là yêu cầu đúng đắn (đã nêu ở step 3).

## Next Steps

- Mở khoá P1 (model Plan) — cần `RebarLayoutMath` để P2/P3 dùng.
- Câu hỏi mở #3 (ngưỡng LOD) chưa cần trả lời tới P3.
