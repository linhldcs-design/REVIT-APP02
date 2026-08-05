# HANDOFF REVITAPP

## Câu lệnh bắt đầu cho AI mới

> Đọc toàn bộ `HANDOFF-REVITAPP.md`, kiểm tra trạng thái Git và tiếp tục công việc hiện tại. Không đóng hoặc mở Revit nếu người dùng chưa cho phép.

## Dự án và phát hành

- Thư mục làm việc: `C:\Users\Admin\OneDrive\Desktop\RevitAI`
- Repository: `https://github.com/linhldcs-design/REVIT-APP02`
- Nhánh phát hành: `main`
- Repository đang Public để Installer tải Release không cần đăng nhập GitHub.
- Release đang chuẩn bị phát hành: `v1.6.0`
- Workflow: `.github/workflows/release-revitapp.yml`
- Installer trên Desktop: `C:\Users\Admin\Desktop\RevitAPP-Installer\RevitAPP.Installer.exe`
- Installer đã cài: `%LocalAppData%\Programs\RevitAPP Installer\RevitAPP.Installer.exe`

## Cơ chế cập nhật

- Installer và Add-in đọc:
  `https://github.com/linhldcs-design/REVIT-APP02/releases/latest/download/latest.json`
- Mỗi Release phải có:
  - `RevitAPP.Installer.exe`
  - `latest.json`
  - `RevitAPP-R22-<version>.zip`
  - `RevitAPP-R23-<version>.zip`
  - `RevitAPP-R24-<version>.zip`
  - `RevitAPP-R25-<version>.zip`
  - `RevitAPP-R26-<version>.zip`
  - `RevitAPP-R27-<version>.zip`
- License và preset người dùng phải được giữ nguyên khi cập nhật.

## Trạng thái kỹ thuật

- RevitAPP đã build thành công cho Revit 2022–2027.
- Revit 2022 có fallback cho `Viewport.GetProjectionToSheetTransform`.
- Revit 2022–2024 bỏ qua Rebar Bending Detail vì API chưa hỗ trợ.
- Test gần nhất: `RevitAPP.Tests` 343/343 đạt.
- Chat AI giữ nguyên registry **55 tool duy nhất** và vẫn hoạt động từ cửa sổ Chat trên Ribbon. Cùng registry đó được mở thêm qua MCP Streamable HTTP tích hợp tại `http://127.0.0.1:8765/mcp`; không cần `revit_mcp_plugin`, `commandRegistry` hay MCP server ngoài. Cả Chat AI và MCP dùng chung `ExternalEvent`, license gate và transaction ownership hiện có.
- Build xác nhận gần nhất: đủ Revit 2022–2027 đều thành công; bản Revit 2025 đã được triển khai thực tế.
- `send_code_to_revit` giới hạn tối đa 1.200 ký tự và luôn yêu cầu người dùng xác nhận trước khi chạy C#.
- Bộ lọc màu đã được sửa; thao tác tạo kích thước chạy nguyên tử, lỗi giữa chừng không để lại kết quả dở dang.
- GitHub Actions của Release `v1.1.5`: thành công toàn bộ; Release có đủ 8 asset và `latest.json` trả HTTP 200.
- `v1.1.6`: native hóa 21 MCP proxy để đủ 53 tool chạy độc lập trên máy đích; không cần `revit_mcp_plugin`, `commandRegistry.json` hoặc MCP server. Bổ sung chọn toàn bộ tag cột trong view bằng `OST_StructuralColumnTags`, chạy trực tiếp không cần API key.
- GitHub Actions Release `v1.1.6` (run `29736265914`) thành công; 8 assets đã phát hành và `latest.json` v1.1.6 trả HTTP 200.
- `v1.2.0`: thêm lệnh triển khai mặt cắt dọc dầm theo một chuỗi dầm liên tục, preview trục/vị trí cắt, mặt cắt ngang gối–nhịp, tag/dim/detail thật và phát hành lên sheet có sẵn. Kiểm thử local đạt 196 + 15 test; build Release R22–R27 thành công.
- `v1.2.1`: thiết kế bộ icon riêng nền trong suốt cho toàn bộ lệnh; tách Ribbon thành `Commands`, `Rebar` và `Drawing Rebar`; đổi tên `Bản Vẽ Dầm` thành `Mặt Cắt Ngang Dầm`, `Bản Vẽ Móng` thành `Mặt Bằng Móng`.
- `v1.2.2`: đổi tên tab Ribbon hiển thị từ `RevitAPP` thành `LDL-STRUCTURAL`; giữ nguyên assembly, manifest, namespace, pack URI và toàn bộ logic lệnh để bảo đảm tương thích cập nhật.
- `v1.2.3`: hoàn thiện bộ icon nền trong suốt cho các lệnh trên Ribbon.
- `v1.2.4`: sửa lệnh Mặt Cắt Ngang Dầm chỉ sắp xếp viewport vừa tạo; giữ nguyên viewport cũ trên sheet, tránh nội dung hiện hữu và vẫn giữ viewport mới để người dùng chỉnh tay khi không còn vùng trống.
- `v1.3.0`: thêm 2 Chat AI tool tìm preset và triển khai mặt cắt dọc dầm hàng loạt lên sheet có sẵn. Hỗ trợ số dầm mỗi sheet do người dùng chọn, chia dependent view tại lưới gần trung điểm, đặt nét cắt cách lưới ưu tiên 500 mm, xếp mặt cắt ngang 1–2 hàng và luôn giữ nguyên tỷ lệ. Khi sheet thiếu chỗ, tool vẫn đặt view để người dùng tiếp tục sắp xếp tay thay vì rollback vì kích thước.
- `v1.3.1`: thêm lệnh `Lưới 3D/2D` trong panel `Commands` để đồng bộ cả hai đầu của toàn bộ lưới trục đang hiển thị trong mặt bằng. Nếu còn đầu lưới ở 3D, lệnh chuyển toàn bộ sang 2D; khi tất cả đã ở 2D, lệnh chuyển ngược lại 3D. Mỗi lưới được xử lý trong sub-transaction riêng và lưới không thể chỉnh sửa được bỏ qua có báo cáo.
- `v1.3.2`: mở rộng lệnh `Lưới 3D/2D` để sử dụng trong mặt đứng, mặt cắt và Detail/Callout dạng `ViewSection`, ngoài mặt bằng; thông báo kết quả dùng tên view hiện tại.
- `v1.4.0`: phát hành toàn bộ 55 Chat AI tool qua MCP Streamable HTTP tích hợp, đồng thời giữ nguyên cửa sổ Chat AI. Endpoint loopback yêu cầu bearer token 256-bit tại `%LocalAppData%\RevitAPP\mcp-access-token.txt`, dùng MCP ổn định `2025-11-25`, hàng đợi worker có giới hạn, kết quả được liên kết riêng theo từng request và mọi tool thay đổi model đều yêu cầu xác nhận trong Revit; license gate và transaction ownership hiện có được giữ nguyên.
- `v1.5.0`: thêm lệnh `Xuất DWG Model` trong panel `CAD Tools`. Lệnh chọn DWG Export Setup, phiên bản DWG và Print Set đã lưu; xuất toàn bộ sheet thành một DWG Model Space tự chứa qua AutoCAD 2024 Automation riêng. DIM và DIMSTYLE được chuyển annotative, Text Style dùng Arial Narrow cao 2.5 và width factor 0.8, giữ DIMLFAC theo viewport; worker có ownership/timeout/retry và được đóng gói self-contained trong mỗi gói R22–R27. Runtime smoke Revit 2025 đạt 34/34 sheet, 1.695/1.695 DIM và Text Style đúng contract, `AUDIT` 0 lỗi.
- `v1.5.1`: sửa workflow clean-run để restore `DwgExportWorker` trước khi publish; `v1.5.0` chỉ là tag build thất bại và không tạo GitHub Release.
- `v1.5.2`: chuẩn hóa DIMLFAC cho mọi sheet có viewport, kể cả sheet chỉ có một tỷ lệ. View tham chiếu (mẫu số tỷ lệ lớn nhất trên sheet) luôn có DIMLFAC = 1; các view còn lại dùng `view.Scale / referenceScale` (ví dụ 1:25 so với 1:75 là 25/75). `v1.5.1` đã bị hủy trong lúc chạy workflow và không tạo GitHub Release vì phát hiện lỗi này trước khi phát hành.
- `v1.5.3`: sửa đầu DIM/tick bị nhỏ sau khi chuyển annotative. Revit ghi override DIMASZ theo inch; worker chuyển riêng từng override sang đơn vị DWG đích sau ANNOUPDATE để giữ đúng kích thước riêng của từng kiểu đầu DIM mà không làm chậm bước regenerate. Kiểm chứng trực tiếp 1.695/1.695 DIM: DIMASZ đổi đúng inch sang mm, giữ nguyên REVIT XData và trạng thái annotative.
- `v1.6.0`: thêm lệnh `Tạo Lưới từ Cad` cho Revit 2025 tại `LDL-STRUCTURAL > CAD Tools`. Người dùng chạy lệnh trong Revit, quét chọn thủ công các đối tượng `LINE` trong AutoCAD 2024–2027 đang mở, xem/chọn/chỉnh tên các trục trong preview rồi bấm một điểm gốc đặt lưới. Add-in giữ nguyên khoảng cách, góc và chiều dài tương đối của line CAD, hỗ trợ cả trục chéo, bỏ qua Grid trùng và tạo toàn bộ Grid mới trong một transaction; không cần CAD link/import hoặc hai Grid neo có sẵn.
- Thay đổi cho `v1.0.1`: xóa nút `Cap Nhat` khỏi Ribbon; Installer vẫn kiểm tra cập nhật.
- Thay đổi cho `v1.0.2`: thêm tùy chọn bẻ móc thép tường vào trong/ra ngoài độc lập cho đầu trên và dưới; bản Debug không tự thay bằng Release khi khởi động.
- Thay đổi cho `v1.1.0`: thêm Chat AI 47 tool, trí nhớ mã hóa, điều khiển toàn bộ nút RevitAPP và đọc Excel.
- Thay đổi cho `v1.1.1`: sửa chọn toàn bộ phần tử bằng tool native và áp dụng license gate cho mọi nút chức năng RevitAPP; nút License vẫn mở để kích hoạt/gia hạn.
- Thay đổi cho `v1.1.2`: Chat AI hỗ trợ chọn ảnh, dán ảnh từ clipboard và kéo thả ảnh; ảnh được chuẩn hóa trước khi gửi và chuyển đúng định dạng vision cho OpenAI, Anthropic và Gemini.
- Thay đổi cho `v1.1.3`: phát hành Chat AI 49 tool; sửa Gemini tool schema; đọc bảng Excel đang mở đúng cả khi UsedRange không bắt đầu tại A1; vẽ dầm theo Instance Mark và cấu hình Excel; gọi hệ cột theo Instance Mark/cấu hình add-in; giảm số lần Regenerate để tránh lag; không báo thành công khi không tạo được thép.
- Thay đổi cho `v1.1.4`: công cụ Vẽ Móng Đơn bỏ qua solid bê tông lót ở dưới cùng khi đọc hình học family; ưu tiên Material/Subcategory và có nhận dạng hình học dự phòng cho family không gán metadata.
- Thay đổi cho `v1.1.5`: Chat AI có 53 tool; thêm vẽ mặt bằng/mặt cắt móng trực tiếp và điều phối C# nguyên tử để giữ đúng viewport ID, xếp mặt bằng trên/mặt cắt dưới, căn tên view, kiểm tra sức chứa/va chạm nội dung sheet và rollback toàn bộ khi lỗi.
- Các commit phát hành gần nhất:
  - `72012ff` — Chat AI 53 tool và tự động triển khai bản vẽ móng lên sheet; phát hành v1.1.5
  - `dd37339` — bỏ qua bê tông lót khi đọc hình học móng đơn; phát hành v1.1.4
  - `aa1d25a` — attach installer executable to releases
  - `37f6391` — publish standalone installer in releases
  - `eb200ef` — release RevitAPP installer for Revit 2022-2027

## Quy trình khi người dùng nói “PHÁT HÀNH BẢN MỚI”

1. Đọc yêu cầu và kiểm tra `git status -sb`; không stage file không liên quan.
2. Sửa code và kiểm tra không làm mất thay đổi của người dùng.
3. Chạy test phù hợp, tối thiểu:
   `dotnet test tests/RevitAPP.Tests/RevitAPP.Tests.csproj -c Release`
4. Build đủ sáu bản với `DeployAddin=false` và `LaunchRevit=false`:
   `dotnet build RevitAPP/RevitAPP.csproj -c Release.R22 -p:DeployAddin=false -p:LaunchRevit=false`
   và lặp lại cho R23, R24, R25, R26, R27.
5. Tăng version theo SemVer; không dùng lại tag đã tồn tại.
6. Commit có chủ đích và push lên `origin/main`.
7. Tạo tag, ví dụ `v1.0.1`, rồi push tag để chạy workflow.
8. Theo dõi GitHub Actions đến khi job `installer`, sáu job build và job `release` đều thành công.
9. Kiểm tra Release có đủ tám asset liệt kê phía trên.
10. Kiểm tra URL `releases/latest/download/latest.json` trả HTTP 200.

## Lưu ý an toàn

- Không chạy build với deploy/launch mặc định vì có thể mở hoặc khóa Revit.
- Không đóng Revit nếu người dùng chưa yêu cầu.
- Không commit `bin`, `obj`, `artifacts`, bundle MCP, file tạm hoặc cấu hình local.
- Worktree có một số thư mục chưa tracked thuộc công việc khác; không tự ý `git add -A`.
- Không hiển thị OAuth client, shared secret hay token trong log hoặc câu trả lời.
