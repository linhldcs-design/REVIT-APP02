---
title: "Preview/Review 2D + 3D cho Vẽ Thép Dầm (BeamRebarPro)"
description: "Tách geometry factory thuần cho thép dầm, dùng chung cho preview 2D/3D và builder Revit, mô phỏng kiến trúc preview của Vẽ Thép Cột."
status: pending
priority: P2
effort: 34h
branch: main
tags: [revit, beam-rebar, preview, wpf, geometry-factory, refactor]
created: 2026-08-12
---

# Preview/Review 2D + 3D — Vẽ Thép Dầm

Rút hình học thép dầm ra khỏi các `*Creator` thành factory thuần (không Revit API) sinh
`BeamRebarGeometryPlan`. Preview 2D/3D và builder Revit dùng CHUNG một nguồn geometry — giống
kiến trúc `ColumnRebarGeometryFactory` + `ColumnRebarPreviewControls`.

## Ràng buộc kiến trúc đã verify

| Điều | Trạng thái |
|---|---|
| `RevitAPP` → `BeamRebarPro` một chiều | verified `RevitAPP/RevitAPP.csproj:59` |
| `BeamRebarPro` KHÔNG ref được `RevitAPP` | verified — sẽ vòng tròn |
| `BeamRebarPro` chưa ref `RevitAPP.Core` | verified — chỉ ref `RevitAPP.Licensing` |
| `RevitAPP.Core` TFM = `net48;net8.0` (KHÔNG `-windows`) | verified `RevitAPP.Core.csproj:4` — **không chứa được WPF control** |
| Baseline build R25 | verified pass (0 errors, `-p:DeployAddin=false`) |
| `Point3/Span/Support/BeamRun/BeamSegment` đã thuần | verified — không dùng `XYZ` |
| `SpanFrame` dùng `XYZ` | verified `SpanFrame.cs:18-32` — cần bản thuần |

## Phases

| # | Tên | Effort | Blockers | Status |
|---|---|---|---|---|
| 0 | [Scaffold + ProjectReference + đặc tả layout Revit](phase-00-scaffold-and-layout-spec.md) | 4h | — | **done** |
| 1 | [Model `BeamRebarGeometryPlan` + `PureSpanFrame`](phase-01-geometry-plan-model.md) | 4h | P0 | **done** |
| 2 | [Factory thuần: thép dọc + gia cường](phase-02-factory-longitudinal.md) | 7h | P1 | **done** |
| 3 | [Factory thuần: đai + context bê tông](phase-03-factory-stirrup-context.md) | 8h | P1 | **done** |
| 4 | [Preview control 2D + 3D (WPF)](phase-04-preview-controls.md) | 6h | P2, P3 | **done** |
| 5 | [Bind ViewModel + live debounce + test hồi quy](phase-05-viewmodel-integration-and-tests.md) | 5h | P4 | **done** (còn F5 smoke test) |

## Trạng thái

Đã dựng xong và build sạch trên `Debug.R25`, 97/97 test pass. **Chưa chạy F5 trong Revit thật** — đây
là bước xác nhận cuối cần người dùng thực hiện, xem mục dưới.

### Điều chỉnh so với kế hoạch ban đầu

- **Creator giữ nguyên cơ chế layout của Revit.** Builder vẫn tạo một thanh rồi để Revit rải; factory
  nhân bản riêng cho bản xem trước. Cả hai dùng chung `RebarLayoutMath` + `PureSpanFrame` nên vẫn đạt
  mục tiêu một nguồn hình học, mà không đụng vào đường đi tới Revit của code production.
- **`StirrupZone` đổi tên thành `BeamStirrupZone`** — tên gốc đã thuộc về vùng đai cột trong
  `RevitAPP.Core.Models`.
- **`BeamRebarProViewModel` tách partial** `BeamRebarProViewModel.Preview.cs`; file gốc chỉ +15 dòng.

### Cần xác nhận trong Revit (F5)

1. Số đai bản xem trước hiện == số đai thật sau khi bấm tạo thép (khoá rủi ro A).
2. Vị trí thanh trong tiết diện khớp mô hình.
3. Mượt khi xoay/zoom với dầm nhiều nhịp thực tế.

## Nguyên tắc xuyên suốt

1. **Không đổi output Revit.** Factory sinh geometry → creator tiêu thụ. Test hồi quy chốt trước/sau.
2. **Ranh giới đơn vị:** feet chỉ tồn tại trong lớp Revit. Factory/Plan dùng **mm**. Chuyển đổi
   đúng một chỗ: adapter tại `BeamRebarGeometryContext` (P1).
3. **Layout Revit phải nhân bản trong factory.** `SetLayoutAsFixedNumber` và
   `SetLayoutAsMaximumSpacing` được đặc tả + khoá bằng unit test (P0/P2/P3).
4. **Dò dầm giao cần Revit** → tách thành input `IReadOnlyList<SecondaryStirrupStation>` truyền vào
   factory, không gọi `FilteredElementCollector` trong Core.

## Rủi ro cấp cao

| ID | Rủi ro | Phase xử lý |
|---|---|---|
| A | Preview vẽ 1 thanh thay vì cả bó (layout Revit) | P0 spec + P2/P3 test |
| B | Lẫn lộn feet/mm rải rác | P1 ranh giới đơn vị |
| C | Refactor làm hỏng tạo thép production | P2/P3 + P5 hồi quy |
| D | Hiệu năng: hàng nghìn đai | P3 budget + P4 LOD |
| E | Dò dầm giao cần Revit | P1 input tách rời |
| F | Warning `[DBG]` rác | P3 — chuyển sang `ILogger` |

## Quyết định đã chốt với user

Các mục dưới đây user đã quyết — **không tự đảo ngược**, kể cả nếu audit sau này thấy YAGNI.

1. **Trung thực hình học là tiêu chí gốc.** Yêu cầu nguyên văn: *"TẤT CẢ CỐ GẮNG GIỐNG THẬT NHƯ
   TRONG REVIT"*. Mọi trade-off nghi ngờ → chọn phía giống thật hơn.
2. **LOD 3D: giống thật tối đa, chấp nhận nặng hơn.** Vẽ đủ mọi đai ở bước thật; giữ ống 7 mặt
   lâu nhất có thể, chỉ giảm ở ngưỡng cao. Vị trí và số lượng đai luôn đúng thật — LOD chỉ được
   phép làm thô mặt cắt ống, không bao giờ bỏ bớt thanh. Xem P4.
3. **Warning `[DBG]`** (`StirrupCreator.cs:214-216`) → chuyển sang `ILogger` (Serilog), bỏ khỏi
   output người dùng. Giữ thông tin chẩn đoán, không hiện cho user. Xem P3.
4. **Xoá 3 hàm chết** trong `LongitudinalBarCreator` — `EvenLaterals:329`, `GetLateralOffsets:266`,
   `IsDefaultPositionSequence:464` (đã verify zero call site toàn repo) — ở **commit riêng**, tách
   khỏi commit refactor. Xem P2.
5. **Theme**: dùng `TryFindResource(...) ?? fallback` hardcode như `ColumnRebarPreviewControls`
   đang làm — không tạo theme riêng. Xem P4.
