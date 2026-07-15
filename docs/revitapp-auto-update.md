# RevitAPP automatic updates

Nguồn phát hành: `https://github.com/linhldcs-design/REVIT-APP02/releases`.

## Phát hành

1. Cập nhật version và changelog.
2. Push tag, ví dụ `v1.1.0`.
3. GitHub Actions build gói cho Revit 2025–2027, tạo ZIP, SHA-256 và `latest.json`.

> R23/R24 tạm chưa phát hành vì module `FootingDrawing.Core` hiện chỉ target .NET 8.

## Phía người dùng

- License Google phải hợp lệ mới được tải cập nhật.
- Nút Ribbon `Cap Nhat` tải đúng gói theo năm Revit và xác minh SHA-256.
- `RevitAPP.Updater.exe` chờ Revit đóng, backup thư mục cài và thay file.
- Cache license `%AppData%\RevitAPP\license.json` và preset người dùng không bị thay đổi.
