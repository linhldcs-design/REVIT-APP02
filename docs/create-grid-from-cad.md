# Model From CAD — Tạo Grid và Structural Column

## Tổng quan

Lệnh **Model From CAD** mở cửa sổ tùy chọn trước, sau đó đọc vùng chọn từ AutoCAD đang mở, nhận line Grid và rectangle cột rồi cho phép duyệt trước trong Revit trước khi tạo model. Không cần Link/Import DWG vào Revit và không sửa bản vẽ AutoCAD.

Vị trí:

`LDL-STRUCTURAL` → `CAD Tools` → `Model From CAD`

Cửa sổ ban đầu chưa có dữ liệu CAD và có hai tab:

- `Create Grid`: có nút `Select From CAD`, duyệt line, chọn/bỏ trục, sửa tên và nhập góc xoay.
- `Create Column`: có nút `Select From CAD`, chọn family, tham số Width/Height, Base/Top Level, offsets và các rectangle cần tạo.

Kết quả quét được nạp vào **Data dùng chung trong cùng cửa sổ**. Sau một lần quét, có thể chuyển giữa Grid và Column để dùng lại dữ liệu; cũng có thể bấm `Select From CAD` từ một trong hai tab để quét lại.

## Cách sử dụng

1. Mở một Floor Plan, Ceiling Plan hoặc Engineering Plan có Level trong project Revit.
2. Mở DWG nguồn trong AutoCAD trên cùng máy.
3. Chạy `LDL-STRUCTURAL` → `CAD Tools` → `Model From CAD`. Cửa sổ tùy chọn rỗng xuất hiện trước; lệnh chưa chuyển sang AutoCAD ở bước này.
4. Chọn tab `Create Grid` hoặc `Create Column`, sau đó bấm `Select From CAD` trong tab đang mở.
5. Trong AutoCAD, quét chọn các `LINE`, polyline kín hoặc block cần đọc rồi nhấn `Enter`.
6. Pick **điểm móc nguồn** trong AutoCAD. Điểm này được lưu theo WCS và kết quả được đưa trở lại Data dùng chung trong cửa sổ.
7. Review dữ liệu trong tab cần tạo; có thể đổi tab mà không quét lại:
   - Với Grid: kiểm tra preview, tên và checkbox từng trục; dùng `Chọn tất cả`, `Bỏ chọn` hoặc `Chỉ trục chính`.
   - Với Column: chọn family, hai tham số kích thước, level/offset; kiểm tra bảng cột và preview `2D / 3D`.
8. Nếu bản CAD và Revit lệch hướng, nhập `Rotation` theo độ. Giá trị này áp dụng cho cả Grid và Column.
9. Bấm `Tạo Grid` hoặc `Tạo Column`, rồi pick **điểm móc đích** tương ứng trong Revit.
10. Xem báo cáo `Đã tạo / Đã tồn tại / Lỗi` sau khi transaction hoàn tất.

`Select From CAD` có trong cả hai tab và cho phép quét lại mà không đóng cửa sổ; các thiết lập family/level còn hợp lệ được giữ lại.

## Quy tắc nhận hình học

- Grid V1 lấy từ entity `LINE` còn lại sau khi loại bốn cạnh đã nhận là rectangle cột.
- Cột nhận từ bốn `LINE` khép kín hoặc `LWPOLYLINE`/2D `POLYLINE` kín có bốn cạnh thẳng.
- Hình học trong `INSERT` được đọc read-only, không gọi Explode; hỗ trợ transform rotate, mirror, scale và block lồng tối đa 5 cấp.
- Chỉ nhận rectangle có mỗi cạnh trong khoảng `100–2000 mm`, gần vuông góc, cạnh đối gần bằng nhau và endpoint khép trong tolerance.
- Bốn cạnh của một candidate phải cùng layer và cùng source block/polyline để tránh ghép nhầm ô lưới thành cột.
- `INSUNITS` được đổi sang milimét trước khi phân tích. Một lần chọn tối đa 20.000 segment.
- Polyline mở, polyline có cung/bulge, polyline 3D, normal không trùng WCS, Xref và proxy geometry bị bỏ qua trong V1.
- Dynamic block chỉ dùng được khi AutoCAD COM trả đúng definition 2D đang hiển thị; chưa có bảo đảm cho mọi dynamic state.

Transform dùng cùng một công thức cho Grid và Column:

`P_revit = Anchor_revit + Rotate(P_cad - Anchor_cad, Rotation)`

Z của CAD không được dùng. Grid đặt theo mặt bằng đang mở; Column dùng Base/Top Level và offsets đã chọn.

## Preview Column

- 2D: hiển thị rectangle cột, overlay line Grid, nhãn kích thước và phần tử đang chọn; hỗ trợ wheel zoom, kéo pan và fit-to-view. Các nút điều khiển nằm trên một toolbar riêng phía trên canvas để không chồng lấn vùng vẽ.
- 3D: dựng prism tạm theo `b × h`, góc CAD cộng Rotation, và chiều cao từ level/offset. Lăn con lăn để zoom; giữ chuột trái và kéo để orbit. Thao tác orbit bắt đầu được cả trên mô hình lẫn vùng nền trống của host preview.
- Preview chỉ là WPF geometry, không tạo hoặc sửa Revit element.
- Checkbox trong bảng quyết định candidate nào được tạo. Click rectangle trên canvas đồng bộ dòng đang chọn trong bảng.

## Tạo Structural Column

- Chỉ liệt kê `FamilySymbol` thuộc `Structural Columns` và các type parameter có kiểu Length.
- Với mỗi kích thước, lệnh reuse type phù hợp hoặc duplicate symbol gốc rồi gán hai tham số Width/Height.
- Instance đặt tại tâm rectangle, xoay theo cạnh local cộng Rotation, sau đó gán Base/Top Level và offsets.
- Toàn bộ type/column của một lần chạy nằm trong `TransactionGroup`; lỗi hard gate rollback batch.
- Duplicate chỉ được xem là “đã tồn tại” khi vị trí, family, b/h, base/top level, offsets và rotation cùng khớp trong tolerance.

## Giới hạn và trạng thái xác minh

- V1 chỉ hỗ trợ Grid thẳng và cột chữ nhật/vuông point-based; không hỗ trợ Grid cong, cột tròn/L/T/polygon, cột nghiêng, shared coordinates hoặc scale calibration hai điểm.
- Chưa có chế độ tự pick/căn theo một Grid Revit; hiện dùng ô `Rotation` thủ công.
- Chưa hiển thị lý do reject riêng cho từng candidate ambiguous/skipped; warning hiện tại chủ yếu ở mức tổng quát/log.
- `RevitAPP.Tests` hiện đạt **357/357**; nhóm test feature đã đạt **74/74** ở checkpoint trước. Build `Release.R22` đến `Release.R27` đều thành công với deploy, Revit launch và publish tắt tại lần xác minh ngày 2026-08-05.
- Runtime cục bộ đã xác minh luồng cửa sổ tùy chọn xuất hiện trước, độ phản hồi khi quét CAD, toolbar 2D không đè canvas, wheel zoom và left-drag orbit trong preview 3D.
- Chưa có smoke end-to-end đầy đủ được ghi nhận cho tạo Grid/Column, xử lý duplicate khi chạy lại và Undo. v1.8.0 vẫn là **Release Candidate** cho đến khi hoàn tất xác minh này và GitHub Release thành công.

## Thành phần chính

- Ribbon/command: `RevitAPP/Application.cs`, `RevitAPP/Commands/ModelFromCadCommand.cs`
- AutoCAD COM: `RevitAPP/Services/CadStructure/AutoCadModelSelectionService.cs`
- Core geometry: `RevitAPP.Core/Models/CadStructure/CadStructureModels.cs`, `RevitAPP.Core/Services/CadStructureAnalyzer.cs`
- UI/MVVM: `RevitAPP/Views/ModelFromCadWindow.xaml(.cs)`, `RevitAPP/ViewModels/ModelFromCadViewModel.cs`
- Revit placement: `RevitAPP/Services/CadGrid/CadGridDirectLineBuilder.cs`, `RevitAPP/Services/CadStructure/CadColumnCreationService.cs`
- Tests: `tests/RevitAPP.Tests/CadStructureAnalyzerTests.cs` và các regression test CadGrid hiện có
