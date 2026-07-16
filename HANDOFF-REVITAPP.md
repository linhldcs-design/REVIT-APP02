# HANDOFF REVITAPP

## Câu lệnh bắt đầu cho AI mới

> Đọc toàn bộ `HANDOFF-REVITAPP.md`, kiểm tra trạng thái Git và tiếp tục công việc hiện tại. Không đóng hoặc mở Revit nếu người dùng chưa cho phép.

## Dự án và phát hành

- Thư mục làm việc: `C:\Users\Admin\OneDrive\Desktop\RevitAI`
- Repository: `https://github.com/linhldcs-design/REVIT-APP02`
- Nhánh phát hành: `main`
- Repository đang Public để Installer tải Release không cần đăng nhập GitHub.
- Release đang phát hành: `v1.0.2`
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
- Test gần nhất: 140/140 đạt.
- GitHub Actions của Release `v1.0.0`: thành công toàn bộ.
- Thay đổi cho `v1.0.1`: xóa nút `Cap Nhat` khỏi Ribbon; Installer vẫn kiểm tra cập nhật.
- Thay đổi cho `v1.0.2`: thêm tùy chọn bẻ móc thép tường vào trong/ra ngoài độc lập cho đầu trên và dưới; bản Debug không tự thay bằng Release khi khởi động.
- Các commit phát hành gần nhất:
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
