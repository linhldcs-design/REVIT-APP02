# Phase 01 — Model `BeamRebarGeometryPlan` + `PureSpanFrame`

## Context Links

- Mẫu bám theo: `RevitAPP.Core/Models/ColumnRebarGeometryPlan.cs` (72 dòng, toàn record)
- Frame hiện tại (Revit-bound): `src/BeamRebarPro/Services/Rebar/SpanFrame.cs:18-62`
- Model đã thuần sẵn: `src/BeamRebarPro/Models/Point3.cs`, `Span.cs`, `Support.cs`, `BeamRun.cs`, `BeamSegment.cs`
- Phase trước: [phase-00](phase-00-scaffold-and-layout-spec.md)

## Overview

- **Priority:** P1
- **Status:** pending
- **Effort:** 4h
- **Blockers:** P0

Định nghĩa cấu trúc dữ liệu trung gian mà factory sinh ra và cả preview lẫn builder Revit tiêu thụ.
Đây là nơi **chốt ranh giới đơn vị** (rủi ro B) và **tách input cần Revit** (rủi ro E).

## Key Insights

1. `SpanFrame` hiện dựng từ `XYZ` và expose `Along/Across/Up` là `XYZ`
   (`SpanFrame.cs:45-52`). Nhưng logic bên trong **thuần toán**: normalize, cross product với
   `BasisZ`, `AxisTop(t)` nội suy tuyến tính. → Sao chép được sang bản thuần không mất mát.
2. `SpanFrame` có `_lateralOffsetFeet` bù justification dầm (`SpanFrame.cs:34,60`) — **không được
   bỏ sót**, nếu không preview lệch ngang so với bê tông thật.
3. Chiều cao lấy `topElevationFeet - bottomElevationFeet` (`:41`), chiều rộng lấy từ **tham số
   family** chứ không phải bbox (`:37`) — comment giải thích rõ. Bản thuần giữ nguyên quy ước.
4. Bên Cột dùng mm toàn bộ (`GeometryPoint3D(Xmm,Ymm,Zmm)`). Dầm phải theo cùng đơn vị để dùng lại
   được kinh nghiệm/nhất quán và để preview control viết một kiểu.
5. `FindIntersectingBeamStations` (`BeamRebarOrchestrator.cs:216-266`) dùng
   `FilteredElementCollector` + `get_Geometry` → **không thuần hoá được**. Kết quả của nó đã là
   `SecondaryStirrupStation(stationFeet, halfWidthFeet)` — một record thuần. Đây chính là điểm cắt:
   Core nhận danh sách station đã dò sẵn.

### Quy ước đơn vị (chốt — rủi ro B)

| Lớp | Đơn vị | Lý do |
|---|---|---|
| Revit API, `*Creator`, `SpanFrame` | **feet** | Bắt buộc bởi Revit |
| `RevitAPP.Core` (model + factory) | **mm** | Khớp mẫu Cột; số dễ đọc khi debug |
| Preview 2D/3D | **mm** | Đọc thẳng từ Plan |

**Chuyển đổi xảy ra tại đúng MỘT chỗ:** hàm dựng `BeamRebarGeometryContext.FromRevitFrame(...)`
ở lớp BeamRebarPro (P2). Cấm nhân/chia `304.8` bên trong `RevitAPP.Core`.
Hằng số `const double MmPerFoot = 304.8;` khai báo một lần trong adapter.

## Requirements

### Functional
- FR1: `BeamRebarGeometryPlan` mô tả đủ **mọi** loại thép trong scope: thép chủ trên/dưới, gia cường
  lớp 1 + lớp 2, đai chính, vùng đai dày, đai tăng cường dầm phụ, đai phụ (lồng kín + móc C),
  đai C giữ lớp 2, thép chống phình.
- FR2: Plan mang cả **context bê tông**: khối dầm, cột đỡ, dầm giao.
- FR3: `PureSpanFrame` tái tạo được `AxisTop(t)`, `Along/Across/Up`, `Width/Height/Length` không dùng Revit.
- FR4: Mọi input cần Revit (station dầm giao, bề rộng gối) là **tham số truyền vào**, không dò trong Core.

### Non-functional
- NFR1: Toàn bộ là `record` immutable, `file-scoped namespace`, `nullable enable`.
- NFR2: File model < 150 dòng; `PureSpanFrame` < 100 dòng.
- NFR3: Không có dependency nào ngoài BCL.

## Architecture

```
INPUT (đã dò bằng Revit ở lớp addin)
  BeamRun (spans + supports)      ─┐
  BeamSegment (top/bottom elev)   ─┤
  SecondaryStirrupStation[]       ─┼──► BeamRebarGeometryFactory (P2/P3)
  QuickSettingModel (config)      ─┤            │
  Support.HalfWidthFeet           ─┘            ▼
                                      BeamRebarGeometryPlan
                                         ├── Paths[]    (thép, mm)
                                         └── Context[]  (bê tông, mm)
                                              │
                            ┌─────────────────┴─────────────────┐
                            ▼                                   ▼
                  BeamRebarPreview2D/3D (P4)          Creator Revit (P5, tiêu thụ)
```

### Kiểu dữ liệu

```csharp
// Điểm 3D mm — TÁI DÙNG GeometryPoint3D đã có của Cột (cùng namespace RevitAPP.Core.Models).
// KHÔNG tạo bản sao: DRY.

public enum BeamRebarPathKind
{
    MainTop, MainBottom,
    AdditionalTop, AdditionalBottom,   // lớp phân biệt bằng field Layer
    Stirrup,                            // đai chính (gồm vùng dày — phân biệt bằng Zone)
    StirrupSecondary,                   // đai tăng cường tại dầm phụ
    AdditionalStirrupClosed,            // đai phụ lồng kín
    AdditionalStirrupCHook,             // đai phụ móc C
    Layer2Tie,                          // đai C giữ thép lớp 2
    AntiBulgeBar, AntiBulgeTie
}

public enum BeamRebarContextKind { Beam, Column, CrossBeam }

public sealed record BeamRebarPath(
    int SpanIndex,
    BeamRebarPathKind Kind,
    double DiameterMm,
    IReadOnlyList<GeometryPoint3D> Points,
    int Layer = 1,
    string? Zone = null,      // "End1"/"Mid"/"End2"/"Secondary" — để 2D tô màu/nhóm
    bool IsClosedLoop = false); // đai kín → nối điểm cuối về đầu khi vẽ

public sealed record BeamRebarContextVolume(
    BeamRebarContextKind Kind,
    GeometryPoint3D StartCenterMm,   // tâm tiết diện đầu
    GeometryPoint3D EndCenterMm,     // tâm tiết diện cuối
    double WidthMm,
    double HeightMm);

public sealed record BeamRebarGeometryPlan(
    IReadOnlyList<BeamRebarContextVolume> Context,
    IReadOnlyList<BeamRebarPath> Paths,
    IReadOnlyList<double> SupportStationsMm,  // để 2D vẽ vạch gối + label nhịp
    double TotalLengthMm)
{
    public IEnumerable<BeamRebarPath> Longitudinal => Paths.Where(p => p.Kind
        is BeamRebarPathKind.MainTop or BeamRebarPathKind.MainBottom
        or BeamRebarPathKind.AdditionalTop or BeamRebarPathKind.AdditionalBottom);
    public IEnumerable<BeamRebarPath> Stirrups => /* các Kind đai */;
}
```

**Ghi chú thiết kế:** Cột dùng `ContextVolume` kiểu tâm+xoay vì cột thẳng đứng. Dầm nằm ngang chạy
theo tuyến → dùng `StartCenter`/`EndCenter` biểu diễn tự nhiên hơn, tránh phải mã hoá góc xoay.

### `PureSpanFrame`

```csharp
public sealed class PureSpanFrame   // sealed, không record vì có logic dựng
{
    public PureSpanFrame(Point3Mm start, Point3Mm end, double widthMm, double heightMm,
                         double topElevationMm, double lateralOffsetMm = 0);
    public GeometryPoint3D AxisTop(double t);       // khớp SpanFrame.AxisTop
    public (double X, double Y, double Z) Along { get; }
    public (double X, double Y, double Z) Across { get; }
    public (double X, double Y, double Z) Up { get; } // luôn (0,0,1)
    public double WidthMm { get; }
    public double HeightMm { get; }
    public double LengthMm { get; }
    public GeometryPoint3D PointAt(double t, double lateralMm, double verticalMm);
}
```
`PointAt` là bản thuần của `LongitudinalBarCreator.PointAt` (`:509-510`) và
`StirrupProfile.Corner` (`:332`) — gộp một chỗ để DRY.

Ném `InvalidOperationException` cùng thông điệp như `SpanFrame:23,30` (dài 0 / gần thẳng đứng)
để hành vi lỗi không đổi.

## Related Code Files

**Create**
- `RevitAPP.Core/Models/BeamRebarGeometryPlan.cs`
- `RevitAPP.Core/Models/PureSpanFrame.cs`
- `tests/BeamRebarPro.Tests/PureSpanFrameTests.cs`

**Modify** — không có (P1 chỉ thêm; đấu nối ở P2).

**Delete** — không có. `SpanFrame.cs` **giữ nguyên** ở phase này; chỉ thành adapter tại P2.

## Implementation Steps

1. Tạo `BeamRebarGeometryPlan.cs` với các enum + record như trên. Tái dùng `GeometryPoint3D` sẵn có
   của `ColumnRebarGeometryPlan.cs:4` — cùng namespace `RevitAPP.Core.Models`, không khai lại.
2. Tạo `PureSpanFrame.cs`. Port từng dòng từ `SpanFrame.cs`:
   - `Along` = normalize(end - start)
   - `Across` = normalize(Along × (0,0,1)); guard length < 1e-6
   - `AxisTop(t)`: `start + Along*(Length*t) + Across*lateralOffset`, rồi ép `Z = topElevationMm`
   - Giữ nguyên quy ước `WidthMm` từ tham số family, `HeightMm = top - bottom`
3. Viết test `PureSpanFrameTests`:
   - Dầm dọc trục X: `Across` phải là `(0,-1,0)` hoặc `(0,1,0)` (chốt dấu theo cross product thực tế
     `Along × BasisZ` — **tính tay và khoá lại**, vì dấu này quyết định chiều rải FixedNumber).
   - `AxisTop(0.5)` = trung điểm, Z = top elevation.
   - `lateralOffset` dịch đúng phương `Across`.
   - Dầm dài 0 → throw; dầm thẳng đứng → throw.
4. Build R25 + `dotnet test`.

## Todo List

- [ ] `BeamRebarGeometryPlan.cs` (enum + record)
- [ ] `PureSpanFrame.cs`
- [ ] Test `PureSpanFrame` (gồm chốt dấu `Across`)
- [ ] Build R25 pass, test pass
- [ ] Xác nhận không nhân/chia 304.8 nào lọt vào Core (grep)

## Success Criteria

- `grep -rn "304.8" RevitAPP.Core/Models/BeamRebarGeometryPlan.cs RevitAPP.Core/Models/PureSpanFrame.cs`
  → **không có kết quả**. (Ranh giới đơn vị sạch.)
- `PureSpanFrame.AxisTop(t)` khớp `SpanFrame.AxisTop(t)` tới sai số 1e-9 khi quy đổi đơn vị —
  kiểm bằng test tính tay với dầm mẫu 6000mm.
- Dấu vector `Across` được khoá bằng assert tường minh, có comment giải thích vì sao dấu này quan trọng.
- Mọi record đều `sealed`, mọi collection đều `IReadOnlyList`.

## Risk Assessment

| Rủi ro | Khả năng | Tác động | Giảm thiểu |
|---|---|---|---|
| Dấu `Across` bị đảo → thép rải ngược ra ngoài tiết diện | **Trung bình** | **Cao** | Test chốt dấu ở step 3; đối chiếu với `FirstLateral = -usableHalf` + `barsOnNormalSide:true` |
| Bỏ sót `lateralOffset` → preview lệch khỏi bê tông | Trung bình | Trung bình | Test riêng cho offset |
| `ContextVolume` kiểu start/end khác Cột gây khó tái dùng code vẽ | Thấp | Thấp | Chấp nhận — dầm và cột có bản chất hình học khác nhau, ép chung sẽ tệ hơn |
| Enum `PathKind` thiếu loại → phải sửa lan man sau | Trung bình | Trung bình | Đã liệt kê từ việc đọc đủ 5 creator; review lại checklist "phạm vi hiển thị TẤT CẢ" trước khi code P2 |

## Security Considerations

Model thuần dữ liệu, không I/O/serialization ở phase này. Nếu sau này Plan được cache/serialize,
cần lưu ý `IReadOnlyList` không đảm bảo immutability sâu — hiện tại chỉ dùng in-memory nên chấp nhận.

## Next Steps

- P2 (thép dọc) và P3 (đai) đều phụ thuộc P1 và **có thể chạy song song** — hai phase chạm file khác
  nhau: P2 sở hữu `BeamRebarLongitudinalFactory.cs`, P3 sở hữu `BeamRebarStirrupFactory.cs`.
- Cả hai cùng đọc (không sửa) `RebarLayoutMath.cs` và `PureSpanFrame.cs`.
