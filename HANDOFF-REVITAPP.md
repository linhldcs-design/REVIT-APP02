# HANDOFF REVITAPP

## Câu lệnh bắt đầu cho AI mới

> Đọc toàn bộ `HANDOFF-REVITAPP.md`, kiểm tra trạng thái Git và tiếp tục công việc hiện tại. Không đóng hoặc mở Revit nếu người dùng chưa cho phép.

## Dự án và phát hành

- Thư mục làm việc: `C:\Users\Admin\OneDrive\Desktop\RevitAI`
- Repository: `https://github.com/linhldcs-design/REVIT-APP02`
- Nhánh phát hành: `main`
- Repository đang Public để Installer tải Release không cần đăng nhập GitHub.
- Release đang phát hành: `v1.1.6`
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
- Test gần nhất: `RevitAPP.Tests` 159/159 đạt và bộ test bổ sung 8/8 đạt.
- Chat AI giữ registry **53 tool duy nhất**. Toàn bộ 21 MCP proxy đã được native hóa vào RevitAPP; máy đích không cần `revit_mcp_plugin`, `commandRegistry` hay MCP server ngoài.
- Build xác nhận gần nhất: Revit 2022, 2025 và 2027 đều thành công; bản Revit 2025 đã được triển khai thực tế.
- `send_code_to_revit` giới hạn tối đa 1.200 ký tự và luôn yêu cầu người dùng xác nhận trước khi chạy C#.
- Bộ lọc màu đã được sửa; thao tác tạo kích thước chạy nguyên tử, lỗi giữa chừng không để lại kết quả dở dang.
- GitHub Actions của Release `v1.1.5`: thành công toàn bộ; Release có đủ 8 asset và `latest.json` trả HTTP 200.
- `v1.1.6`: native hóa 21 MCP proxy để đủ 53 tool chạy độc lập trên máy đích; không cần `revit_mcp_plugin`, `commandRegistry.json` hoặc MCP server. Bổ sung chọn toàn bộ tag cột trong view bằng `OST_StructuralColumnTags`, chạy trực tiếp không cần API key.
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
