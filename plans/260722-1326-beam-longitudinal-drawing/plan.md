---
title: "Triển khai mặt cắt dọc dầm trong RevitAPP"
description: "Tạo lệnh native sinh mặt cắt dọc chuỗi dầm, tag thép, kích thước hai lớp và các mặt cắt gối/nhịp theo PDF mẫu."
status: in-progress
priority: P1
effort: "12-18 ngày kỹ thuật + smoke test Revit"
branch: "main"
tags: [feature, revit, beam, longitudinal-drawing]
blockedBy: []
blocks: []
created: 2026-07-22
mode: hard
---

# Triển khai mặt cắt dọc dầm trong RevitAPP

## Overview

Thêm một lệnh riêng trong `RevitAPP` để người dùng chọn chuỗi dầm đã có thép, cấu hình các loại tag/detail/view/dimension/spot elevation, rồi tạo bản vẽ giống `PDF DAM.pdf`: một mặt cắt dọc liên tục qua các nhịp, tag thép thực tế, kích thước vùng thép và nhịp, cùng các mặt cắt gối/giữa nhịp đặt lên sheet.

Đây là feature **mặt cắt dọc** mới. Kế hoạch `260701-1144-beam-drawing-in-revitapp` và engine hiện tại chủ yếu giải quyết mặt cắt ngang; chỉ tái sử dụng các thành phần đã ổn định (resource resolver, preset, viewport/sheet, annotation helpers) qua interface rõ ràng.

## Scope

### In

- Native trong `RevitAPP`; thêm ribbon command riêng, chạy bằng Add-In Manager khi Revit đang mở.
- Revit 2025 / `.NET 8` / `Debug.R25` và `Release.R25`; WPF modal, không cần `ExternalEvent`.
- Chọn một chuỗi dầm thẳng hoặc gần thẳng, cùng cao độ/tầng, đã có `Rebar`; tự sắp theo trục và nhận dạng cột/gối.
- Hiển thị khung preview trực quan bắt buộc trước khi tạo model: trục chuỗi, thứ tự/hướng nhịp, cột-gối,
  kích thước sơ bộ và mọi station mặt cắt dự kiến; chỉ bật Generate sau khi preview hợp lệ và người dùng xác nhận.
- Tạo một mặt cắt dọc/elevation cho toàn chuỗi dầm đã chọn.
- Với mỗi nhịp, lấy ba station nghiệp vụ: gối trái, giữa nhịp, gối phải; gối chung chỉ sinh một mặt cắt nếu cấu hình thép không đổi.
- Nếu vùng gối hoặc giữa nhịp có cấu hình thép khác nhau thì sinh thêm mặt cắt cần thiết; không sao chép annotation giữa station.
- Tag thép dọc, thép tăng cường và đai theo dữ liệu Rebar thực tế; đặt detail component tại cột/gối trên mặt cắt dọc.
- Hai lớp dimension trên mặt cắt dọc: lớp trên theo các vùng thép/đai và lớp dưới theo cột-gối-nhịp/trục.
- Tái sử dụng engine mặt cắt ngang hiện có để tạo/annotate các section station sau khi API hợp đồng được tách ổn định.
- Tạo sheet/viewports theo PDF mẫu; cảnh báo có cấu trúc khi thiếu family/type.
- Cập nhật hand-off để AI/người khác tiếp tục đúng phase và đúng DLL đang smoke.

### Out

- Tự rải hoặc sửa Rebar.
- Dầm cong, dầm nghiêng lớn, dầm vòng, chuỗi dầm rẽ góc, linked-model beam và nhiều tầng trong một lần chạy ở v1.
- Tự thiết kế title block, tag family hoặc detail component nếu project chưa load.
- Multi-version Revit 2022-2027, batch headless, xuất PDF tự động.
- Sao chép chính xác branding/title block của PDF mẫu.

## Assumptions

- Lệnh thuộc assembly `RevitAPP`, không tạo add-in/solution độc lập.
- V1 chỉ hỗ trợ Revit 2025, UI modal và DI disabled theo pattern hiện tại.
- Chuỗi dầm hợp lệ phải đồng phẳng, trục gần thẳng, đầu dầm gặp nhau trong tolerance và có tối thiểu một nhịp.
- “Ba vị trí mỗi nhịp” là hai vùng gối và một giữa nhịp; tại cột chung, hai nhịp có thể dùng chung một section khi fingerprint thép hai phía tương đương.
- “Khác nhau” được xác định bằng fingerprint có tolerance: kích thước tiết diện, số lượng/đường kính/cao độ lớp thép dọc và đường kính/bước đai tại station.
- PDF mẫu là chuẩn hình học/bố cục; tên family/type cụ thể vẫn do người dùng chọn trong form.

## Architecture

```text
LongitudinalBeamDrawingCommand
  -> BeamChainPicker + BeamChainAnalyzer
  -> LongitudinalBeamDrawingWindow/ViewModel
       -> BeamChainPreviewCanvas (review + confirm gate)
  -> LongitudinalDrawingOrchestrator (TransactionGroup)
       T1: create longitudinal view + unique cross views
       commit + Regenerate
       T2: tags + detail components + dimensions + spot elevations
       commit
       T3: sheet + viewport layout
  -> BeamLongitudinalDrawingResult + warnings
```

Pure logic đặt trong `RevitAPP.Core` gồm chain/station model, fingerprint/deduplication, dimension-zone model và sheet layout. Lớp `RevitAPP` chỉ đọc Revit API, resolve `ElementId`, tạo view/annotation và quản lý transaction. Không lưu `ElementId` qua session trong preset.

## Key Contracts

- `BeamChainModel`: danh sách span đã sắp, trục chuẩn, supports, tổng chiều dài và validation warnings.
- `SectionStation`: `SupportLeft | MidSpan | SupportRight`, tọa độ normalized/feet, span index và side tại gối.
- `RebarStationFingerprint`: section size + các layer thép + stirrup signature; dùng để quyết định gộp/tách section.
- `LongitudinalDimensionPlan`: các witness theo zone đai/thép ở lớp trên và support/span/grid ở lớp dưới.
- `LongitudinalDrawingSetting`: resource names, scale, offsets, dedupe tolerances, sheet layout và flags; preset JSON có version riêng.

## Phases

| Phase | Title | Status | Effort | Depends On |
| --- | --- | --- | --- | --- |
| 00 | [Baseline, contracts và ribbon scaffold](phase-00-baseline-scaffold.md) | completed | 1 ngày | [] |
| 01 | [Mô hình chuỗi dầm, station và fingerprint](phase-01-chain-station-domain.md) | completed | 2-3 ngày | [00] |
| 02 | [UI, resource input và preset](phase-02-ui-resources-preset.md) | completed | 2 ngày | [00, 01] |
| 03 | [Tạo mặt cắt dọc và mặt cắt station](phase-03-view-generation.md) | pending | 2-3 ngày | [01, 02] |
| 04 | [Tag, detail component và dimension mặt cắt dọc](phase-04-longitudinal-annotation.md) | pending | 3-4 ngày | [03] |
| 05 | [Tái sử dụng mặt cắt ngang và bố trí sheet](phase-05-cross-sections-sheet.md) | pending | 2-3 ngày | [03, 04] |
| 06 | [Test, smoke, phát hành và hand-off](phase-06-test-deploy-handoff.md) | pending | 2 ngày | [05] |

## Progress

- Hoàn thành: 3/7 phase (43%).
- Verification 2026-07-22: `RevitAPP.Tests` 196/196 pass; `RevitAPP.Core` net48 build pass;
  `RevitAPP` Debug.R25 build + XAML compile + ILRepack pass với deploy/launch tắt.
- Chưa smoke runtime trong Revit; Phase 00 command hiện chỉ mở WPF modal scaffold và không sửa model.

## Acceptance Matrix

| Case | Expected |
| --- | --- |
| Một nhịp, thép đều | 1 mặt cắt dọc + 3 mặt cắt station; annotation và hai lớp dim đầy đủ |
| Hai nhịp, gối chung giống nhau | 1 mặt cắt dọc + 5 mặt cắt station, không nhân đôi section tại cột chung |
| Hai nhịp, hai phía gối chung khác thép | 1 mặt cắt dọc + 6 mặt cắt station; mỗi section tag đúng thép tại phía tương ứng |
| Nhịp không có thép tăng cường và chỉ một vùng đai | Có thể giảm section theo quy tắc đã cấu hình; không sinh tag/dim rỗng |
| Dầm xiên trong mặt bằng nhưng hợp lệ | View và annotation bám hệ trục local của chuỗi, không dùng world X/Y |
| Preview trước khi xuất | Hiển thị đúng thứ tự span, hướng trục, support và station; Generate bị khóa khi chain invalid/chưa xác nhận |
| Thiếu family/type bắt buộc | Dừng trước transaction hoặc cảnh báo rõ resource nào thiếu; không để output nửa vời |
| Một annotation best-effort lỗi | View/sheet vẫn tạo, warning ghi view + element + thao tác lỗi |

## Risks

- **Critical — chọn chuỗi sai thứ tự hoặc trộn hai chuỗi.** Mitigation: graph theo endpoint, bắt buộc một connected path, reject branch/cycle và preview thứ tự span trước khi Generate.
- **Critical — gộp nhầm hai mặt cắt có thép khác nhau.** Mitigation: fingerprint từ Rebar giao station với tolerance có test; mặc định ưu tiên không gộp khi không chắc.
- **High — reference cho dimension/tag không ổn định sau join cột-sàn-dầm.** Mitigation: lấy stable geometric references trong view, `Regenerate`, tạo từng nhóm annotation độc lập và log reference count.
- **High — section box sai hướng cho dầm không song song trục X.** Mitigation: mọi phép tính dùng chain-local basis; test thuần cho transform và smoke với dầm xoay.
- **High — PDF có quy tắc bố cục chưa thể suy ra hoàn toàn.** Mitigation: khóa các offset/scale trong setting, nghiệm thu bằng overlay/checklist thay vì hardcode pixel.
- **Medium — thay đổi helper mặt cắt ngang làm regression feature cũ.** Mitigation: chỉ extract interface nhỏ, giữ test cũ xanh và smoke lệnh cũ trước phát hành.
- **Medium — Add-In Manager cache assembly `RevitAPP`.** Mitigation: build không deploy, tạo assembly identity smoke riêng khi Revit đã load, ghi hash DLL vào hand-off.

## Verification

- `dotnet test tests/RevitAPP.Tests/RevitAPP.Tests.csproj -c Release`
- `dotnet build RevitAPP/RevitAPP.csproj -c Debug.R25 -p:DeployAddin=false`
- `dotnet build RevitAPP/RevitAPP.csproj -c Release.R25 -p:DeployAddin=false`
- Smoke trong Revit 2025 bằng model có ít nhất: một nhịp, hai nhịp gối giống, hai nhịp gối khác và dầm xoay.
- So sánh PDF mẫu theo checklist: trục/cột, đường bao dầm, rebar/stirrup, tag, spot elevation, dimension trên/dưới, section mark, viewport và title.
- Regression smoke lệnh mặt cắt ngang hiện có.

## Red-Team Findings Applied

1. Không cho phép “pick nhiều dầm bất kỳ”; Phase 01 phải xác minh graph là một path duy nhất.
2. Không dedupe chỉ bằng vị trí cột; phải so fingerprint thép hai phía và fail-safe sang tạo riêng.
3. Không tạo tất cả trong một transaction; view, annotation và sheet tách transaction trong một `TransactionGroup`.
4. Không sửa trực tiếp engine mặt cắt ngang trước khi có characterization tests.
5. Không đánh dấu hoàn thành khi chỉ build xanh; Phase 06 bắt buộc smoke model thật và cập nhật hand-off/hash DLL.

## Open Questions / Validation Gate

- V1 có cần đặt toàn bộ chuỗi nhiều nhịp vào **một** mặt cắt dọc như PDF hay cho phép tách mỗi dầm thành một view khi tổng chiều dài vượt khổ sheet? Kế hoạch mặc định một view, tự cảnh báo khi vượt vùng đặt.
- Khi một nhịp hoàn toàn không có thép tăng cường và chỉ một khoảng đai, yêu cầu trong ảnh nói giảm còn một mặt cắt; mặc định áp dụng giảm cho nhịp đó, nhưng vẫn giữ hai gối nếu fingerprint hai đầu khác nhau.
- Detail component tại cột trên mặt cắt dọc là family do người dùng chọn; cần chốt orientation/flip bằng smoke trên family thực tế.
- Sheet có dùng title block hiện tại của project hay tạo trên sheet đã chọn? Mặc định tạo sheet mới bằng Title Block được chọn.

## Next Gate

Xác nhận các giả định ở `Open Questions`, sau đó triển khai Phase 00-01. Không bắt đầu Revit API view generation trước khi test thuần cho chain ordering, station dedupe và dimension plan đều xanh.
