---
title: "Xuất Print Set Revit sang một DWG Model Space"
description: "Thêm lệnh trong RevitAPP để xuất sheet theo DWG setup, chuyển toàn bộ sang Model Space, giữ bố cục view và ghép sheet từ trái sang phải."
status: in-progress
priority: P1
effort: "7-11 ngày"
branch: "main"
tags: [feature, revit, autocad, dwg, export]
blockedBy: []
blocks: []
created: 2026-08-03
mode: hard
---

# Xuất Print Set Revit sang một DWG Model Space

## Tổng quan

Thêm nút `Xuất DWG Model` trong `RevitAPP`. Người dùng chọn một `Export DWG Setup`, phiên bản DWG (ví dụ AutoCAD 2007), một Print Set đã lưu trong hộp thoại `Ctrl+P`, và thư mục/tên file đích. Kết quả mặc định là **một DWG duy nhất**, chỉ dùng Model Space, chứa đầy đủ các sheet theo thứ tự từ trái sang phải.

Đây không phải bài toán chỉ gọi `Document.Export`: khi xuất `ViewSheet`, Revit tạo cấu trúc layout/paper space và có thể tạo xref. Muốn cam kết “mọi thứ ở Model Space” và ghép nhiều sheet vào một file cần bước hậu xử lý bằng AutoCAD. Revit xuất file trung gian, sau đó dùng một phiên AutoCAD đầy đủ, riêng biệt qua COM Automation để chạy `EXPORTLAYOUT` và compose file cuối; thử nghiệm Core Console đã không chạy ổn định lệnh này.

## Kết quả người dùng

- Chọn đúng DWG export setup đang có trong project.
- Chọn đúng Print Set đã lưu, ví dụ `FullKC`; không tự tạo hoặc sửa Print Set.
- Chọn định dạng DWG được Revit API hỗ trợ, gồm `R2007`.
- Xuất một file DWG cuối ở đường dẫn luôn do người dùng chọn bằng `Duyệt`; không tự điền đường dẫn project và từ chối đường dẫn ảo `Autodesk Docs:\...`.
- File cuối mở vào Model Space và không cần xref/file phụ để hiển thị.
- Sheet theo thứ tự Print Set được đặt từ trái sang phải, không chồng lấn.
- Bên trong mỗi sheet, vị trí tương đối, góc xoay, crop và nhãn view bám bố cục Revit.
- Với sheet có nhiều tỷ lệ, theo cách gọi của người dùng, view có “tỷ lệ lớn” là view có mẫu số lớn nhất và được làm chuẩn. Ví dụ 1:75 và 1:25: 1:75 là chuẩn (`DIMLFAC = 1`); view 1:25 dùng hệ số hình học `75/25 = 3` và hệ số dimension `25/75 = 1/3`, nếu spike xác nhận entity xuất ra còn hỗ trợ dimension semantic.

## Phạm vi

### Trong scope

- Ribbon command mới trong assembly `RevitAPP`, có license gate.
- Modal WPF dùng Theme hiện có: DWG setup, DWG version, Print Set, file đích và phần preview/validation.
- Đọc `ExportDWGSettings.ListNames`/`DWGExportOptions.GetPredefinedOptions` và clone setup trước khi override `FileVersion`.
- Đọc `ViewSheetSet`; R23-R27 ưu tiên `OrderedViewList`, R22 dùng fallback được ghi rõ.
- Chỉ nhận `ViewSheet` printable, không nhận view rời trong Print Set ở v1.
- Job manifest versioned trong thư mục staging do RevitAPP sở hữu.
- Phiên AutoCAD đầy đủ chạy riêng qua COM Automation để flatten layout, normalize dimension theo vùng viewport, ghép Model Space và lưu đúng DWG version.
- Pure tests, build Revit theo target, kiểm tra AutoCAD COM Automation và runtime smoke với fixture chuẩn.
- Báo cáo thành công/thất bại theo sheet; file cuối chỉ publish khi toàn bộ job hợp lệ.

### Ngoài scope v1

- Chạy không cần cài một bản AutoCAD đầy đủ có COM Automation.
- AutoCAD LT/Mac, cloud conversion hoặc thư viện DWG bên thứ ba.
- Xuất view rời không thuộc sheet, 3D perspective hoặc sheet placeholder.
- Tự sửa Export DWG Setup, Print Set hoặc model Revit.
- Tái tạo font/CTB/SHX bị thiếu ngoài khả năng exporter hiện có.
- Chỉnh tay khoảng cách/tạo nhiều hàng sheet; v1 luôn một hàng trái sang phải.

## Kiến trúc đề xuất

```text
RevitAPP command (Revit API context, read-only)
  -> load DWG setups + saved ViewSheetSets
  -> validate sheets/viewports/scales/output path/AutoCAD Automation
  -> build immutable DwgExportJob manifest
  -> Document.Export each sheet to owned staging folder
  -> launch a dedicated full AutoCAD instance through COM Automation

Dedicated AutoCAD COM automation
  -> open each staged sheet DWG
  -> flatten active sheet layout to a temporary Model Space DWG
  -> map flattened regions back to source viewports
  -> preserve geometry already transformed by EXPORTLAYOUT
  -> set per-dimension LinearScaleFactor and verify normalized counts
  -> measure model extents
  -> clone sheet entities into final database at increasing X offsets
  -> audit, save temp output at requested DWG version

RevitAPP
  -> validate worker result + final file
  -> atomically publish final DWG
  -> cleanup only the owned job folder
  -> show summary/log
```

### Hợp đồng tỷ lệ

Trong hợp đồng của tính năng này, `View.Scale` là mẫu số và cách gọi “tỷ lệ lớn” của người dùng nghĩa là **mẫu số lớn nhất**. Với mỗi sheet:

- `referenceDenominator = max(printableViewport.View.Scale)`.
- `geometryFactor(view) = referenceDenominator / view.Scale`.
- `dimensionLinearFactor(view) = view.Scale / referenceDenominator`.
- View chuẩn có cả hai hệ số bằng `1`.

Đây là **acceptance contract**. Sau `EXPORTLAYOUT`, hình học đã được AutoCAD biến đổi theo viewport nên không áp dụng `geometryFactor` thêm lần nữa, nếu không sẽ scale kép (ví dụ 1:25 có thể thành 9 lần thay vì 3 lần tương đối so với 1:75). Worker ánh xạ tâm/bounding box của dimension trong Model Space đã flatten về vùng viewport nguồn, đặt `LinearScaleFactor` theo `dimensionLinearFactor`, và dừng job nếu số dimension nguồn của bất kỳ viewport nào không được normalize đủ; không được âm thầm xuất sai DIM.

### Bảo toàn bố cục

- Mỗi sheet được flatten độc lập để title block, sheet annotation, viewport crop, rotation và label đi cùng nhau.
- Việc compose chỉ transform cả sheet như một nhóm bằng phép tịnh tiến X; không scale/rotate cả sheet ở bước ghép.
- Khoảng hở dùng một giá trị vật lý cố định (đề xuất 100 mm) được đổi sang `TargetUnit` của DWG setup.
- Thứ tự nguồn là Print Set; nếu API không cung cấp thứ tự (R22), fallback là `SheetNumber` tự nhiên và UI phải hiển thị trước khi chạy.

## Thành phần và file dự kiến

| Khu vực | File chính |
| --- | --- |
| Ribbon/command | Modify `RevitAPP/Application.cs`; create `RevitAPP/Commands/ExportSheetsToDwgCommand.cs` |
| WPF | Create `RevitAPP/Views/DwgExportWindow.xaml(.cs)`, `RevitAPP/ViewModels/DwgExportViewModel.cs` |
| Revit services | Create `RevitAPP/Services/DwgExport/PrintSetProvider.cs`, `RevitDwgExportService.cs`, `DwgPostProcessorLauncher.cs` |
| Core contracts/math | Create `RevitAPP.Core/Models/DwgExport/*`, `RevitAPP.Core/Services/DwgSheetLayoutPlanner.cs`, `DwgExportJobStore.cs` |
| AutoCAD automation | `RevitAPP/Services/DwgExport/AutoCadDwgPostProcessor.cs` dùng COM late binding, không load AutoCAD managed DLL vào Revit |
| Tests | Create `tests/RevitAPP.Tests/DwgExport/*`; add worker-side integration fixture/tests where host permits |
| Assets/docs | Create icon files; create `docs/export-revit-sheets-to-dwg.md` after behavior is verified |

Không mở rộng `src/AutoCadGridBridge`: worker DWG là một executable/plugin boundary riêng để tránh ghép vòng đời của tính năng xuất bản với Grid Bridge đang dang dở. Cả hai chỉ tái dùng pattern build/reference AutoCAD đã có.

## Phases

| Phase | Tên | Status | Effort | Depends On |
| --- | --- | --- | --- | --- |
| 00 | [Calibration spike và chốt hợp đồng DWG](phase-00-calibration-contract.md) | in-progress | 1-2 ngày | [] |
| 01 | [Core contracts và layout planner](phase-01-core-contract-layout.md) | completed | 1 ngày | [00] |
| 02 | [Revit command, UI và staging export](phase-02-revit-command-export.md) | in-progress | 2 ngày | [01] |
| 03 | [AutoCAD flatten và compose Model Space](phase-03-autocad-flatten-compose.md) | in-progress | 2-3 ngày | [00, 01] |
| 04 | [Mixed-scale và dimension normalization](phase-04-scale-dimension-normalization.md) | in-progress | 1-2 ngày | [00, 03] |
| 05 | [Test matrix, deploy và hand-off](phase-05-test-deploy.md) | in-progress | 1 ngày | [02, 03, 04] |

## Acceptance criteria tổng

- UI liệt kê đúng setup và Print Set từ document hiện tại; `FullKC` chọn được nếu tồn tại.
- Đổi `FileVersion` không làm thay đổi setup đã lưu trong RVT.
- Cancel/validation failure không tạo file cuối và không sửa model.
- Print Set chứa sheet không printable, placeholder hoặc view rời bị chặn trước export với danh sách lỗi.
- File cuối có đúng một Model Space chứa đối tượng; không phụ thuộc xref/file staging; layout phụ không chứa nội dung cần thiết.
- N sheet tạo N cụm không chồng lấn, theo đúng thứ tự đã preview, tăng dần theo trục X.
- Với fixture 1:75 + 1:25, 1:75 là reference; view 1:25 đạt geometry factor `3` và DIMLFAC `1/3`; không chỉ kiểm tra bằng mắt mà đo khoảng cách và giá trị dimension trong AutoCAD.
- Title block, text, hatch, viewport crop, rotation và label khớp ảnh/PDF baseline trong tolerance đã quy định.
- Output R2007 mở được trên AutoCAD đích và `AUDIT` không báo lỗi nghiêm trọng.
- Worker crash/timeout không ghi đè file DWG hợp lệ có sẵn; staging được giữ để chẩn đoán hoặc dọn có kiểm soát.
- Pure tests pass; Revit build R22-R27 pass; worker build/smoke được báo cáo chính xác theo AutoCAD version thực sự có trên máy.

## Red-team findings đã đưa vào plan

1. **Critical — Revit API đơn lẻ không cam kết Model Space:** thêm worker AutoCAD và gate kiểm tra dependency trước khi export.
2. **Critical — không map ổn định entity về viewport thì DIMLFAC có thể sai:** Phase 00 là blocking gate; không cho phép heuristic âm thầm ở production.
3. **High — `EXPORTLAYOUT` có thể explode dimension/wipeout/xref:** fixture phải kiểm tra loại entity và visual regression; ghi rõ giới hạn Autodesk.
4. **High — partial output/ghi đè file cũ:** output vào file tạm, audit rồi atomic replace; không xóa file đích trước.
5. **High — sở hữu nhầm AutoCAD instance:** xác định process mới do job mở; chỉ đóng document/instance thuộc job và dừng nếu không chứng minh được ownership.
6. **High — thứ tự Print Set R22 không truy cập được như R23+:** UI preview fallback và acceptance riêng cho R22.
7. **Medium — đường dẫn/tên sheet có ký tự đặc biệt:** truyền qua manifest JSON + script file do ứng dụng tạo, canonicalize đường dẫn, không ghép lệnh shell từ input.
8. **Medium — worktree đang bẩn, `Application.cs` đang có sửa đổi:** implementation phải patch tối thiểu và không overwrite thay đổi hiện có.

## Verification chính

```powershell
dotnet test tests\RevitAPP.Tests\RevitAPP.Tests.csproj -c Debug
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R22
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R23
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R24
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R25
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R26
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R27
```

Worker build và runtime smoke dùng đúng cấu hình AutoCAD được định nghĩa ở Phase 05; AutoCAD 2024 đã được kiểm chứng runtime, các phiên bản chưa cài không được tuyên bố runtime pass.

## Cơ sở kỹ thuật đã kiểm tra

- Autodesk Revit cho phép lấy predefined setup qua `DWGExportOptions.GetPredefinedOptions`, chọn `FileVersion` (có `R2007`) và export một collection view/sheet bằng `Document.Export`.
- Autodesk mô tả `MergedViews`/“Export views on sheets and links as external references” chỉ quyết định merge hay tạo tham chiếu; nó không thay thế bước chuyển layout sang Model Space.
- Autodesk AutoCAD xác nhận `EXPORTLAYOUT` tạo biểu diễn trực quan của layout trong Model Space của một DWG mới, đồng thời cảnh báo một số dimension/xref/custom object có thể bị explode hoặc đổi loại. Vì vậy Phase 00 là gate bắt buộc.
- Tài liệu tham chiếu: [Revit Export to DWG/DXF](https://help.autodesk.com/cloudhelp/2024/ENU/Revit-DocumentPresent/files/GUID-42C75024-4D71-4831-8910-2747168624A3.htm), [Revit DWGExportOptions](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/3e510f02-1a4c-3e4f-f923-e96972d03862.htm), [AutoCAD Export Layout to Model Space](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-653D3843-AFE5-4569-959F-E06F3866D7D7.htm).

## Assumptions và quyết định đã xác nhận

1. **User-confirmed 2026-08-03:** tạo đúng **một DWG tổng cho cả Print Set**, không bàn giao DWG/xref phụ.
2. Kết quả giữ cả title block và sheet annotation, không chỉ riêng model view.
3. Chấp nhận yêu cầu máy chạy có một bản AutoCAD đầy đủ đăng ký COM Automation; v1 không dùng Core Console hoặc thư viện DWG trả phí.
4. Khoảng cách giữa hai sheet mặc định 100 mm theo đơn vị vật lý của setup.
5. **User-confirmed 2026-08-03:** vòng triển khai đầu tiên chỉ nhắm Revit 2025; R22-R24/R26-R27 để vòng sau.
6. Cần xác nhận “tuân thủ cách sắp xếp view” nghĩa là giữ tâm/góc xoay/crop của viewport sau khi chuẩn hóa tỷ lệ; khi scale view 1:25 lên 3 lần theo reference 1:75, biên view có thể lớn hơn vùng ban đầu và phải có quy tắc anchor đã chốt trong spike.

## Rollback/mitigation

- Feature mới đứng sau một ribbon command độc lập; rollback là bỏ đăng ký button và các file feature.
- Không có migration dữ liệu và không sửa RVT.
- Worker chỉ được xóa thư mục có job id hợp lệ nằm dưới root staging của RevitAPP.
- Nếu Phase 00 không chứng minh được mixed-scale an toàn, dừng ở review gate thay vì hạ tiêu chuẩn đầu ra.

## Next gate

External-worker smoke cuối trên staging thật của Revit 2025 đã hoàn tất **34/34 sheet ngay lần chạy đầu** với job id mới và tạo đúng một DWG tổng, không để lại AutoCAD/worker. Bộ test hiện đạt **343/343**; build/deploy `Debug.R25` đạt **0 lỗi**. File cuối có **1.695/1.695 DIM annotative** và **1.695/1.695 Text Style** annotative Arial Narrow, height 2.5, width factor 0.8; `AUDIT` báo 0 lỗi. Worker nay dừng nếu một viewport Revit có DIM nhưng không tạo DIM CAD nào, hoặc nếu bất kỳ DIM CAD ứng viên nào không được normalize; chênh lệch tổng số Revit/CAD chỉ ghi chẩn đoán vì `EXPORTLAYOUT` có thể tách/crop đối tượng. Phase 04 và Phase 05 vẫn `in-progress` cho tới khi người dùng mở file cuối và xác nhận trực quan sheet order/layout/anchor.
