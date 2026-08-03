# Xuất Revit Sheets thành một DWG — Revit 2025

## Trạng thái

Tính năng đang ở giai đoạn triển khai và kiểm chứng cho **Revit 2025**. Luồng build và test tự động đã đạt; mixed-scale guard trước đây đã được bỏ nên nút xuất có thể hoạt động với Print Set nhiều tỷ lệ khi các input và đường dẫn đích hợp lệ. Tuy nhiên, bản build mới chưa được deploy và chưa được xem là hoàn tất sản xuất vì chưa có smoke test end-to-end trên Revit/AutoCAD với DWG thực tế.

## Phạm vi hiện tại

Lệnh `Xuất DWG Model` trong nhóm CAD Tools mở hộp thoại cấu hình cho phép người dùng chọn:

- một `Export DWG Setup` đã lưu trong project Revit;
- phiên bản DWG đầu ra: AutoCAD 2007, 2010, 2013 hoặc 2018;
- một `Print Set` đã lưu trong Revit;
- đường dẫn và tên file `.dwg` đầu ra, luôn do người dùng chọn bằng nút `Duyệt`.

Ứng dụng không tự điền sẵn đường dẫn từ project. Đường dẫn ảo như `Autodesk Docs:\...` không phải đường dẫn file Windows mà AutoCAD COM có thể ghi trực tiếp, nên bị từ chối; người dùng phải chọn một thư mục local hoặc thư mục đồng bộ đã được ánh xạ thành đường dẫn Windows hợp lệ.

Danh sách sheet và thứ tự xuất được lấy từ Print Set. Mục tiêu của mỗi lần chạy là tạo **đúng một file DWG cuối cùng** theo đường dẫn người dùng chọn, không tạo một DWG đầu ra riêng cho từng sheet.

Trong file cuối, nội dung các sheet được chuyển vào Model Space và ghép từ trái sang phải theo thứ tự Print Set. Bố cục bên trong từng sheet được lấy từ layout do Revit xuất và làm phẳng bằng `EXPORTLAYOUT`.

## Luồng xử lý

1. Revit đọc DWG Setup, phiên bản DWG, Print Set và đường dẫn đầu ra từ hộp thoại.
2. Mỗi sheet được Revit xuất vào thư mục staging riêng với `MergedViews` bật. Mỗi sheet phải tạo đúng một DWG staging chính.
3. RevitAPP tạo job manifest có phiên bản và kiểm tra đường dẫn/tên file staging.
4. Một phiên **AutoCAD đầy đủ và riêng biệt** được khởi tạo qua Windows COM Automation. Không nạp AutoCAD managed assemblies vào tiến trình Revit.
5. AutoCAD chạy `EXPORTLAYOUT` cho từng DWG staging để đưa nội dung layout vào Model Space.
6. Worker ánh xạ các vùng viewport Revit vào Model Space đã flatten. Hình học không được scale thêm lần nữa vì `EXPORTLAYOUT` đã materialize phép biến đổi viewport; worker chỉ đặt `LinearScaleFactor` cho dimension thuộc từng vùng.
7. Worker so sánh số dimension nguồn với số entity đã normalize cho từng viewport và dừng job nếu số lượng không đủ, tránh publish một DWG có DIMLFAC sai âm thầm.
8. Các DWG đã làm phẳng được chèn, dịch chuyển và ghép từ trái sang phải; file kết quả được lưu trước dưới tên `.partial.dwg`.
9. File tạm chỉ được publish sang đường dẫn người dùng yêu cầu sau khi lưu thành công.
10. Khi thành công, thư mục staging được xóa. Khi có lỗi, staging được giữ lại và đường dẫn được ghi vào log để chẩn đoán.

## Yêu cầu môi trường

- Autodesk Revit 2025 và RevitAPP bản R25.
- Một bản cài **AutoCAD đầy đủ** có đăng ký COM Automation trên Windows. AutoCAD Core Console không được dùng vì `EXPORTLAYOUT` không chạy ổn định trong thử nghiệm hiện tại.
- Project Revit đã có ít nhất một Export DWG Setup và một Print Set chứa sheet hợp lệ.
- Sheet có thể chứa nhiều tỷ lệ viewport; mẫu số lớn nhất trên sheet là tỷ lệ reference theo quy ước của tính năng.

Việc chỉ chọn phiên bản file DWG, ví dụ AutoCAD 2007, không thay thế yêu cầu phải có AutoCAD đầy đủ để xử lý hậu kỳ.

## Biện pháp an toàn

- Chỉ tiếp tục nếu xác nhận được AutoCAD vừa khởi tạo là một process mới, riêng với các phiên AutoCAD đang mở trước đó. Nếu không xác nhận được, lệnh dừng để tránh ảnh hưởng bản vẽ của người dùng.
- Chỉ đóng các document do job mở và chỉ gọi `Quit` với phiên AutoCAD được job sở hữu.
- Dùng file `.partial.dwg` và publish sau cùng để tránh để lại một file đích trông như đã hoàn tất khi job thất bại giữa chừng.
- Kiểm tra job manifest, phần mở rộng, tên staging và đường dẫn tương đối nhằm hạn chế path traversal hoặc đọc nhầm file ngoài staging.
- Giữ staging khi lỗi để có thể kiểm tra DWG trung gian; staging chỉ bị xóa sau khi tạo file cuối thành công.
- Với mixed-scale, dừng job nếu không ánh xạ được vùng viewport ổn định hoặc số dimension đã normalize thấp hơn số dimension nguồn; không publish kết quả thiếu hiệu chỉnh.

## Xử lý sheet nhiều tỷ lệ

Quy tắc đang được triển khai trong production path:

- theo cách gọi của người dùng, tỷ lệ tham chiếu/“tỷ lệ lớn” là tỷ lệ có **mẫu số lớn nhất** trên sheet;
- hệ số hình học của viewport = `mẫu số tham chiếu / mẫu số viewport`;
- hệ số kích thước tuyến tính = `mẫu số viewport / mẫu số tham chiếu`.

Ví dụ với viewport 1:75 và 1:25:

- viewport 1:75 là reference: geometry factor = `1`, DIMLFAC = `1`;
- viewport 1:25: geometry factor = `75/25 = 3`, DIMLFAC = `25/75 = 1/3`.

`geometryFactor` mô tả quan hệ hình học mong đợi giữa các viewport. AutoCAD `EXPORTLAYOUT` đã chuyển đổi hình học theo viewport khi tạo Model Space, nên worker **không nhân geometry factor lần nữa**; nếu scale thêm, viewport 1:25 trong ví dụ có thể bị scale kép thành 9 lần thay vì quan hệ 3 lần.

Worker dùng sheet outline, viewport outline và paper extents để map vùng của từng viewport sang Model Space đã flatten. Với dimension được hỗ trợ có tâm bounding box nằm trong vùng đó, worker đặt `LinearScaleFactor = view.Scale / referenceDenominator` — tức `1` cho 1:75 và `1/3` cho 1:25. Job fail nếu viewport nguồn có dimension nhưng số dimension tìm thấy và normalize không đủ. Guard chặn toàn bộ mixed-scale ở UI/service đã được bỏ, nhưng kết quả runtime vẫn cần được đo trên fixture thật trước khi xem Phase 04 hoàn tất.

## Kết quả kiểm tra tự động

- Build/deploy `Debug.R25` của `RevitAPP.csproj`: thành công, **0 lỗi biên dịch** sau khi Revit đóng.
- Test suite: **343/343 test đạt**, bao gồm layout planner, tỷ lệ reference theo mẫu số lớn nhất, geometry/dimension factor, ánh xạ vùng viewport, kiểm tra bao phủ DIM CAD, tạo tên DIMSTYLE/Text Style không trùng, job manifest và validation đường dẫn đầu ra.
- Smoke cuối: **34/34 sheet**, hoàn tất ngay lần chạy đầu với job id mới; file tổng không để lại AutoCAD/worker chạy nền.
- Kiểm tra DWG: **1.695/1.695 DIM annotative**; **1.695/1.695 Text Style** annotative, font Arial Narrow, height 2.5, width factor 0.8; `AUDIT` báo 0 lỗi.
- Bản mixed-scale mới đã được deploy vào Addins 2025; vẫn cần runtime smoke Revit–AutoCAD trước khi coi là hoàn tất production.
- Các cảnh báo build còn lại thuộc mã có sẵn hoặc bước đóng gói; không có lỗi build liên quan tới lệnh xuất DWG mới.

Kết quả trên chỉ xác nhận compile và logic tự động. Chưa có smoke test end-to-end được ghi nhận với một project Revit 2025 và AutoCAD thực tế, nên Phase 04 và Phase 05 vẫn `in-progress` và không được xem là xác nhận hoàn tất runtime.

## Checklist smoke test thủ công

Chuẩn bị một project Revit 2025 thử nghiệm có DWG Setup đã lưu và Print Set gồm ít nhất hai sheet: một sheet đơn tỷ lệ và một sheet mixed-scale có viewport 1:75 và 1:25.

1. Đóng các bản vẽ thử không cần thiết nhưng giữ nguyên bất kỳ phiên AutoCAD người dùng đang làm việc để kiểm tra cơ chế cô lập process.
2. Mở Revit 2025, project thử nghiệm và chạy `CAD Tools > Xuất DWG Model`.
3. Kiểm tra danh sách DWG Setup, phiên bản DWG và Print Set khớp dữ liệu đã lưu trong project.
4. Chọn Print Set và xác nhận preview giữ đúng thứ tự sheet.
5. Xác nhận ô file đích không được tự điền từ đường dẫn project; bấm `Duyệt` và chọn một đường dẫn `.dwg` Windows hợp lệ. Thử nhập `Autodesk Docs:\...` và xác nhận UI từ chối.
6. Xác nhận chỉ có **một file DWG cuối cùng** tại đường dẫn đã chọn; không còn `.partial.dwg` sau khi thành công.
7. Mở file bằng AutoCAD và xác nhận `TILEMODE=1`, nội dung nằm trong Model Space, các sheet xếp trái sang phải đúng thứ tự Print Set và không chồng lấn.
8. So sánh title block, text, dimension, linework, hatch và vị trí tương đối của view trong từng sheet với Revit.
9. Dùng `DIST` hoặc một kích thước chuẩn đã biết để kiểm tra đơn vị và kích thước hình học.
10. Kiểm tra bản vẽ đang mở trong phiên AutoCAD có trước khi chạy không bị đóng, ẩn hoặc sửa đổi.
11. Chạy lại với file đích đã tồn tại và xác nhận hành vi publish/ghi đè đúng mong đợi, không để file đích hỏng nếu job thất bại.
12. Gây một lỗi có kiểm soát, ví dụ chọn thư mục không có quyền ghi, rồi xác nhận staging được giữ và log có đường dẫn chẩn đoán.
13. Chọn Print Set có sheet 1:75 và 1:25; xác nhận 1:75 được nhận là reference và nút xuất được bật khi setup/version/output path hợp lệ.
14. Trong DWG sau `EXPORTLAYOUT`, đo quan hệ hình học giữa hai viewport để xác nhận 1:25 thể hiện tương đối theo factor `3` mà không bị scale kép; kiểm tra dimension của vùng 1:75 có `LinearScaleFactor/DIMLFAC = 1` và vùng 1:25 là `1/3`.
15. Tạo case dimension không thể map/normalize và xác nhận job dừng, giữ staging, không publish file cuối có thể sai.

Chỉ sau khi fixture 1:75/1:25 vượt qua kiểm tra mapping, hình học, DIMLFAC, bố cục và `AUDIT` mới được đánh dấu Phase 04/05 hoàn tất.
