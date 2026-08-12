# Create Wall từ AutoCAD

Thêm tab `Create Wall` vào cửa sổ `Model From CAD`, bên cạnh `Create Grid`, `Create Column`,
`Create Beam`, `Create Slab`.

**Trạng thái:** đang thực hiện — Phase 01–03 xong; fix nối qua cửa đạt 542/542 test. Debug.R25 và Release.R22–R27 build final đã qua với deploy tắt; còn deploy R25 và người dùng thử runtime.

## Quyết định của người dùng

| Điểm | Chốt |
|---|---|
| Nhận tường | **Hai line song song**, như tab Beam |
| Bề dày | **Đo khoảng cách hai line**, không đọc text |
| Chiều cao | **Base Level → Top Level**, chọn trong bản tùy chọn |
| Wall Type | **Dò type có sẵn trước; không có mới sinh mới** |
| Cửa đi/cửa sổ | Chỉ nối khi **cả hai bridge dọc** tiếp tục đúng hai mặt tường, trên chính layer tường người dùng chọn; không suy luận từ jamb/end-cap ngang |

## Vì sao làm được nhanh

Ba thứ khó nhất đã có sẵn và đã kiểm chứng trên bản vẽ thật:

| Đã có | Dùng cho |
|---|---|
| `CadBeamAnalyzer` — ghép hai rail song song thành tiết diện | Nhận cặp line thành tường |
| `CadArcChords` + đọc `ARC` (30 test) | Tường cong |
| `CadSlabTypeNaming` + `BuiltLike` (13 test) | Dò/sinh Wall Type |
| `AutoCadModelSelectionService` | Quét CAD, đã xử COM, block lồng, đơn vị |

Việc còn lại chủ yếu là nối chúng lại, không phải phát minh.

## Các phase

| # | Phase | Nội dung |
|---|---|---|
| 01 | [Phân tích](phase-01-analyzer.md) | `CadWallAnalyzer` — cặp rail → trục tường + bề dày |
| 02 | [Dựng tường](phase-02-creation.md) | `CadWallCreationService` — `Wall.Create`, dò/sinh Wall Type |
| 03 | [Giao diện](phase-03-ui.md) | Tab, bảng review, preview 2D/3D |
| 04 | [Kiểm chứng](phase-04-verify.md) | Test, build R22–R27, thử bản vẽ thật |

## Phụ thuộc

Phase 01 → 02 → 03. Phase 04 chạy suốt, không để cuối.

## Ràng buộc

- Test trước, sửa sau — bài học `v1.11.0`, mò dấu công thức tám vòng không hội tụ
- Không lọc theo tên layer — bài học `v1.11.0`, `S-GRID` bị lọc mất làm hỏng bước quét lưới
- Build đủ R22–R27 trước khi phát hành; `double.IsFinite` không có trên net48
- Không tự đóng/mở Revit
