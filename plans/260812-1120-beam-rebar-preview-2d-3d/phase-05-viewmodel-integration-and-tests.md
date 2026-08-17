# Phase 05 — Bind ViewModel + live debounce + test hồi quy

## Context Links

- Mẫu debounce: `RevitAPP/ViewModels/ColumnRebarViewModel.cs:23,35-36,49,265-279`
- ViewModel đích: `src/BeamRebarPro/ViewModels/BeamRebarProViewModel.cs` (346 dòng)
- ViewModel Detail: `src/BeamRebarPro/ViewModels/BeamRebarDetailViewModel.cs` (**2335 dòng — đã vượt giới hạn**)
- Build model: `BeamRebarProViewModel.BuildModel():233-301`
- Phase trước: [phase-04](phase-04-preview-controls.md)

## Overview

- **Priority:** P2
- **Status:** pending
- **Effort:** 5h
- **Blockers:** P4

Đấu nối preview vào ViewModel, cập nhật live có debounce ~200ms, và **test hồi quy chốt rủi ro C**
(không làm hỏng chức năng tạo thép production).

## Key Insights

1. **Mẫu debounce của Cột** (`ColumnRebarViewModel.cs:35-36`): `DispatcherTimer` 140ms, `Tick` →
   `Stop()` + `RebuildPreviewNow()`. Yêu cầu đã chốt là **200ms** → dùng 200.
2. **Xử lý input tạm không hợp lệ** (`:272-279`): khi factory ném, **giữ lại `PreviewPlan` cũ** và
   set `PreviewValidationMessage`. Comment gốc nói rõ chủ đích ("typed input can be temporarily
   invalid"). Bắt buộc theo mẫu này — nếu xoá Plan khi gõ dở, preview sẽ nhấp nháy.
3. **`BeamRebarProViewModel` là 346 dòng, giới hạn ViewModel là 250.** Thêm preview vào sẽ vượt xa.
   → **Tách `BeamRebarPreviewCoordinator`** (service) giữ timer + logic rebuild; ViewModel chỉ expose
   `PreviewPlan`, `PreviewValidationMessage`, `FitPreviewCommand`, `SelectedSpanIndex`.
4. **Preview cần dữ liệu hình học dầm thật** (`BeamSegment`, `Support`, station dầm giao) — chỉ có
   sau khi user pick dầm (`SetPickedBeams:97-103`). Trước đó Plan = null → hiện thông điệp
   "Chọn dầm để xem preview". `PickedSpans` đã có sẵn (`:84`).
5. **Dữ liệu Revit cần cache lại lúc pick.** `FindIntersectingBeamStations` cần `Document` — không
   gọi được khi user gõ số. → Lúc pick dầm, **cache** `SecondaryStirrupStation[]` + `Support[]` +
   `BeamSegment[]` vào VM; rebuild preview chỉ dùng cache + config mới. Đây là hệ quả trực tiếp của
   rủi ro E.
6. `BeamRebarDetailViewModel` đã 2335 dòng — **không thêm gì vào file này**. Nếu Detail cũng cần
   preview, tái dùng `BeamRebarPreviewCoordinator` qua composition.

## Requirements

### Functional
- FR1: Preview vẽ lại tự động khi đổi bất kỳ thông số nào, debounce 200ms.
- FR2: Input không hợp lệ → giữ Plan hợp lệ gần nhất + hiện cảnh báo.
- FR3: Nút Fit reset camera/zoom cả 2D lẫn 3D.
- FR4: Chưa pick dầm → thông điệp hướng dẫn, không crash.
- FR5: **Chức năng tạo thép không đổi hành vi.**

### Non-functional
- NFR1: `BeamRebarProViewModel` sau thay đổi vẫn < 250 dòng *(hiện 346 — xem Risk/câu hỏi mở)*.
- NFR2: `BeamRebarPreviewCoordinator` < 200 dòng.
- NFR3: Rebuild preview không chặn UI > 100ms.

## Architecture

```
User gõ thông số
      │ (PropertyChanged)
      ▼
BeamRebarProViewModel.OnAnyConfigChanged()
      │
      ▼
BeamRebarPreviewCoordinator.RequestRebuild()   ── DispatcherTimer 200ms ──┐
                                                                          │
      ┌───────────────────────────────────────────────────────────────────┘
      ▼
  BuildModel()  →  QuickSettingModel
  + cache (BeamSegment[], Support[], SecondaryStirrupStation[])   ← cache lúc pick dầm
      │
      ▼
  BeamRebarGeometryFactory.Create(...)        [P2 + P3, thuần]
      │
      ├── OK    → PreviewPlan = plan;  PreviewValidationMessage = null
      └── throw → giữ PreviewPlan cũ;  PreviewValidationMessage = ex.Message
      │
      ▼
  Binding → BeamRebarPreview2D / 3D  (P4)
```

**Điểm cắt Revit rõ ràng:** mọi thứ sau "cache" đều thuần → rebuild an toàn, không cần
`ExternalEvent`, không đụng `Document`.

## Related Code Files

**Create**
- `src/BeamRebarPro/Services/BeamRebarPreviewCoordinator.cs`
- `RevitAPP.Core/Services/BeamRebarGeometryFactory.cs` — façade gộp P2 + P3
- `tests/BeamRebarPro.Tests/BeamRebarGeometryFactoryTests.cs` — test hồi quy

**Modify**
- `src/BeamRebarPro/ViewModels/BeamRebarProViewModel.cs` — expose preview property + gọi coordinator
- `src/BeamRebarPro/Services/RebarCreationHandler.cs` — cache dữ liệu Revit lúc pick

**Delete** — không có.

## Implementation Steps

1. Tạo `BeamRebarGeometryFactory` façade: gọi `BeamRebarLongitudinalFactory` (P2) +
   `BeamRebarStirrupFactory` (P3) + `BeamRebarContextFactory` (P3), gộp thành 1 `BeamRebarGeometryPlan`.
2. Cache dữ liệu Revit lúc pick dầm: trong `RebarCreationHandler` (nơi đã có `Document`), sau khi
   đọc segment/support/dầm giao, đẩy vào VM qua callback (theo mẫu `OnSupportsSelected:206-214`).
3. Tạo `BeamRebarPreviewCoordinator`: `DispatcherTimer` 200ms, `RequestRebuild()`, `RebuildNow()`,
   sự kiện `PlanChanged`. Bắt exception → giữ Plan cũ (mẫu `ColumnRebarViewModel:272-279`).
4. VM: thêm `[ObservableProperty] BeamRebarGeometryPlan? _previewPlan`,
   `string? _previewValidationMessage`, `int _selectedSpanIndex`, `[RelayCommand] FitPreview`.
   Gọi `RequestRebuild()` từ `OnXxxChanged` partial method của các property ảnh hưởng hình học.
   *(Cân nhắc: đăng ký `PropertyChanged` chung thay vì viết ~30 partial method — gọn hơn nhiều.)*
5. Bind XAML (đã dựng khung ở P4).
6. **Test hồi quy (gate rủi ro C)** — xem Success Criteria.
7. **F5 smoke test (gate rủi ro A)** — bắt buộc, xem dưới.

### F5 smoke test — gate bắt buộc

Rủi ro A và ngữ nghĩa `SetLayoutAsMaximumSpacing` **không thể đóng bằng unit test** (là hành vi
runtime Revit). Quy trình:

1. Mở Revit 2025, dựng dầm 6000mm, cấu hình đai TwoEnds bước 150/200.
2. Tạo thép thật → **đếm số đai Revit tạo ra** trong từng vùng.
3. So với số path preview sinh cùng cấu hình.
4. **Khớp** → rủi ro A đóng. **Lệch** → sửa `RebarLayoutMath` (P0) theo số thực đo, chạy lại toàn bộ test.

## Todo List

- [ ] Façade `BeamRebarGeometryFactory`
- [ ] Cache dữ liệu Revit lúc pick dầm
- [ ] `BeamRebarPreviewCoordinator` + debounce 200ms
- [ ] VM property + command + trigger rebuild
- [ ] Bind XAML
- [ ] Test hồi quy trước/sau refactor
- [ ] Build R25 pass, `dotnet test` pass
- [ ] **F5 smoke: đếm đai thật vs preview**
- [ ] Build R22 + R27 pass

## Success Criteria

### Test hồi quy (chốt rủi ro C)
Trước khi refactor creator (P2 step 5 / P3 step 10), ghi lại **golden output**: với 3 cấu hình mẫu
(1 nhịp đơn giản / 3 nhịp có dầm phụ / có gia cường 2 lớp + chống phình), ghi lại toạ độ mọi curve
mà creator dựng (log ra file JSON). Sau refactor, chạy lại và **so khớp từng điểm ≤ 1e-6 feet**.

| Kiểm tra | Expect |
|---|---|
| Golden output trước/sau refactor | Trùng khớp tuyệt đối |
| `RebarCreationResult` counts | Không đổi |
| Warning list | Không đổi (trừ `[DBG]` nếu user quyết xoá) |
| F5: số đai Revit vs preview | Bằng nhau |
| Debounce | Gõ liên tục 10 ký tự → rebuild đúng 1 lần |
| Input rác (bước đai = 0) | Plan cũ giữ nguyên + cảnh báo, không crash |
| Chưa pick dầm | Không crash, có hướng dẫn |

- Build R25 + R22 + R27 pass.
- `BeamRebarProViewModel` < 250 dòng (hoặc quyết định của user — xem Risk).

## Risk Assessment

| Rủi ro | Khả năng | Tác động | Giảm thiểu |
|---|---|---|---|
| **C — hỏng tạo thép production** | Trung bình | **Cao** | Golden-output regression (step 6) là gate cứng; giữ layout Revit nguyên vẹn |
| **A — sai ngữ nghĩa layout** | Trung bình | **Cao** | F5 smoke (step 7) là gate cứng |
| VM vượt 250 dòng | **Cao** | Thấp | Tách coordinator; nếu vẫn vượt → **hỏi user**, không tự ý tách VM lớn |
| Rebuild chặn UI với dầm lớn | Trung bình | Trung bình | Factory thuần → chuyển `Task.Run` nếu đo thấy > 100ms |
| Cache Revit lỗi thời khi model đổi ngoài | Trung bình | Thấp | Preview là công cụ xem trước, không phải nguồn chân lý; rebuild lúc pick lại |
| `[DBG]` warning lọt vào UI | Chắc chắn (đang xảy ra) | Thấp | **Câu hỏi mở #1 — chờ user** |

## Security Considerations

- Cache giữ tham chiếu dữ liệu hình học, **không giữ `Document`/`Element`** → tránh memory leak và
  truy cập object đã chết sau khi user đóng model.
- `DispatcherTimer` phải `Stop()` khi View đóng, tránh rebuild trên VM đã hỏng.
- Exception trong rebuild phải bắt hết (`catch (Exception)`) — preview lỗi **không được** làm sập
  dialog đang có config chưa lưu của user.

## Next Steps

- Cập nhật `docs/project-changelog.md` + `docs/development-roadmap.md` (theo
  `documentation-management.md`).
- Chạy `/bs:code-review` trước merge.
- Cân nhắc đưa preview vào `BeamRebarDetailView` (tái dùng coordinator) — **ngoài scope**, cần user quyết.
