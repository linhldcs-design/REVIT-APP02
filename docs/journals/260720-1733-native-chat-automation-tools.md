---
date: 2026-07-20
topic: Native hóa 21 Chat AI automation tools
status: completed
---

# Native hóa 21 Chat AI automation tools

## Bối cảnh

Chat AI từng gọi 21 lệnh Revit qua assembly MCP cài ngoài, `commandRegistry.json` và lớp proxy phản chiếu. Cách này làm tính năng phụ thuộc trạng thái cài đặt MCP dù Chat AI đã chạy bên trong RevitAPP.

## Những gì đã thực hiện

- Thay 21 proxy bằng `IChatTool` native chạy trực tiếp qua Revit API.
- Loại bỏ `NativeMcpCommandHost`, `RevitMcpProxyTool` và bước khởi tạo command registry khi mở Chat.
- Giữ nguyên tên tool công khai để không phá luồng hội thoại hiện có.
- `send_code_to_revit` biên dịch C# trong process bằng Roslyn thay vì tải command assembly bên ngoài.
- Registry giữ đúng 53 tool, không trùng tên.

## Quyết định và bảo mật

- Tool thay đổi model phải qua license gate và xác nhận người dùng trước khi marshal vào Revit API context.
- Tool xóa phần tử và chạy C# được đánh dấu nguy hiểm; hộp xác nhận hiển thị tool cùng payload rút gọn.
- Transaction nằm trong bridge hoặc trong tool chuyên biệt, tránh transaction lồng nhau.
- Không còn runtime dependency tới MCP socket, localhost, assembly MCP hay `commandRegistry.json`.

## Kiểm chứng

- Test Release: 159/159 đạt.
- Registry tĩnh: 53 tên, 53 tên duy nhất; đủ 21 tool native.
- Build tuần tự Release.R22, Release.R25 và Release.R27: 0 lỗi với `DeployAddin=false`, `LaunchRevit=false`.
- Bản Revit 2025 đã được deploy để kiểm thử thực tế.

## Kết luận

Chat AI hiện tự chứa toàn bộ 21 automation tool trong RevitAPP, giảm điểm lỗi cài đặt và giữ chốt xác nhận cho các thao tác có tác động tới model.
