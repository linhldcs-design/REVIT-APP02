---
date: 2026-08-18
session: zero-wait-ribbon-licensing
---

# Journal: 2026-08-18 — Zero-wait Ribbon licensing

## Context

Các lệnh Ribbon có độ trễ ngắn trước khi chạy vì license gate thực hiện HTTP đồng bộ trên UI thread của Revit. Mục tiêu là loại bỏ thời gian chờ khi bấm lệnh nhưng vẫn giữ máy chủ làm nguồn cấp quyền duy nhất và fail closed khi không còn bằng chứng xác minh mới.

## What Happened

- Root cause: `EnsureValid` chờ online verification ngay trên đường chạy command, khiến mọi lệnh Ribbon phụ thuộc độ trễ mạng.
- Phương án dùng disk cache làm nguồn cấp quyền bị bác ở mức P0: file local có thể bị sửa và không đủ thẩm quyền để mở khóa command.
- Gate cuối chỉ đọc snapshot đã được máy chủ xác minh trong RAM; đọc snapshot không thực hiện HTTP và refresh chạy nền.
- Worker warm-up khi add-in khởi động, refresh mỗi 60 giây và snapshot quá 3 phút bị chặn ngay.
- Các online verification được serialize để tránh kết quả cũ ghi đè kết quả mới; sign-out vô hiệu hóa snapshot đang có và chặn commit muộn.
- Gate cuối: `RevitAPP.Licensing.Tests` 26/26 và `RevitAPP.Tests` 582/582 đạt, 0 fail/skip.
- Review cuối cho phạm vi Ribbon kết luận 0 P0/P1.
- Build Release R22–R27 đều đạt với 0 lỗi (`DeployAddin=false`, `LaunchRevit=false`). Đã được duyệt để phát hành v1.14.3 qua workflow public; không deploy trực tiếp vào Revit đang chạy.

## Reflection

Zero-wait không đồng nghĩa với tin dữ liệu local. Tách command gate khỏi network I/O, nhưng chỉ giữ quyền trong snapshot RAM ngắn hạn, đạt phản hồi tức thì mà không hạ chuẩn server-authoritative. Khoảng 3 phút là cửa sổ fail-closed có chủ đích để chịu một nhịp scheduler chậm, không phải grace period ngoại tuyến.

## Decisions Made

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Command gate chỉ đọc snapshot RAM | Không chặn UI và không tin file local | Lệnh Ribbon chạy ngay khi snapshot còn mới |
| Bác disk cache authority ở mức P0 | Disk cache có thể bị giả mạo | File cache chỉ giữ identity/trạng thái hiển thị |
| Refresh 60 giây, fail closed sau 3 phút | Cân bằng scheduler jitter và khả năng thu hồi quyền | Mất refresh kéo dài sẽ khóa command tự động |
| Serialize online verification | Ngăn race và stale result thắng kết quả mới | Snapshot phản ánh thứ tự xác minh xác định |
| Phát hành qua updater, không deploy trực tiếp | Giữ nguyên Revit đang chạy và dùng đường cập nhật chuẩn | Khách nhận v1.14.3 qua release public |

## Next Steps

- Theo dõi workflow v1.14.3, xác minh đủ 8 asset, manifest và gói R25 sau khi phát hành.
- Smoke test trong Revit sau khi người dùng cho phép đóng/mở hoặc cập nhật add-in.
