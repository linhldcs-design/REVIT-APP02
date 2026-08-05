# Tạo Lưới từ Cad — Revit 2025

## Tổng quan

Lệnh **Tạo Lưới từ Cad** tạo Revit Grid từ các đối tượng `LINE` được người dùng quét
chọn thủ công trong AutoCAD đang mở. Không cần Link/Import CAD vào Revit và không cần
hai Grid neo có sẵn.

Vị trí:

`LDL-STRUCTURAL` → `CAD Tools` → `Tạo Lưới từ Cad`

Phạm vi:

- Revit 2025.
- AutoCAD 2024–2027 đang mở trên cùng máy.
- Chỉ đọc đối tượng `LINE`; bản vẽ AutoCAD không bị sửa.

## Cách sử dụng

1. Mở mặt bằng cần tạo Grid trong Revit 2025.
2. Mở bản vẽ lưới trục trong AutoCAD 2024–2027.
3. Trong Revit, chạy `LDL-STRUCTURAL` → `CAD Tools` → `Tạo Lưới từ Cad`.
4. Revit chuyển sang AutoCAD. Quét chọn thủ công các đường `LINE` lưới trục rồi nhấn
   `Enter`.
5. Trong cửa sổ xem trước, kiểm tra hình học, bật/tắt từng trục và chỉnh tên nếu cần.
   Có thể chọn lại vùng AutoCAD mà không thoát lệnh.
6. Chọn `Tạo Lưới`, sau đó bấm một điểm gốc đặt lưới trong Revit.
7. Add-in giữ nguyên khoảng cách, góc và chiều dài tương đối của các line CAD rồi tạo
   Grid tại điểm gốc đã chọn.

## Quy tắc

- Tọa độ tuyệt đối của DWG không được đưa vào Revit; vùng chọn được dời về gốc tương đối.
- `INSUNITS` của AutoCAD được đổi sang milimét trước khi dựng Grid.
- Hai họ line chính được nhận diện để đề xuất tên số/chữ.
- Line chéo vẫn xuất hiện trong preview và người dùng quyết định có tạo hay không.
- Line ngắn hoặc có độ dài bằng không bị bỏ qua.
- Grid trùng đường với Grid hiện có trong dung sai 1 mm được bỏ qua.
- Toàn bộ Grid mới nằm trong một transaction, vì vậy một lần Undo xóa cả batch.
- Nhấn `Esc` hoặc `Cancel` trước khi xác nhận không sửa model.

## Thành phần

- Ribbon/command: `RevitAPP/Application.cs`,
  `RevitAPP/Commands/CreateGridFromCadCommand.cs`
- Chọn line AutoCAD: `RevitAPP/Services/CadGrid/AutoCadSelectionService.cs`
- Chuẩn hóa và preview: `RevitAPP.Core/Services/CadGridDirectPlacer.cs`,
  `RevitAPP.Core/Services/CadGridPreviewBuilder.cs`
- Tạo Grid: `RevitAPP/Services/CadGrid/CadGridDirectLineBuilder.cs`,
  `RevitAPP/Services/CadGrid/CadGridCreationService.cs`
- Test: `tests/RevitAPP.Tests/CadGridTransferTests.cs`
