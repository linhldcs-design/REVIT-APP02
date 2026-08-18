---
date: 2026-08-18
session: license-server-authoritative-v1.14.1
---

# Journal: 2026-08-18 — License server-authoritative v1.14.1

## Context

Khách vẫn chạy được lệnh sau khi ngày hết hạn trên Google Sheet bị đổi vì client còn tin cache hợp lệ trong thời gian grace. Bản vá v1.14.1 chuyển quyết định cấp quyền về server cho từng lần chạy lệnh bảo vệ.

## What Happened

- `GetStateAsync` luôn xác minh online; cache chỉ nhớ phiên đăng nhập và trạng thái hiển thị.
- Offline hoặc timeout bị chặn theo fail-closed, không còn quyền dùng từ cache 7 ngày.
- Cache dùng mutex, ghi atomic và `sessionId` để phản hồi cũ không thể ghi đè sau đăng xuất/đăng nhập đồng thời.
- Bổ sung hồi quy cho thu hồi/gia hạn tức thời, cache lỗi, cache legacy và các race đăng xuất.
- Gate hiện tại đạt 17/17 test licensing, 582/582 test ứng dụng; build R22–R27 không lỗi và không deploy/khởi động Revit.

## Reflection

Nguyên nhân không nằm ở Google Sheet hay Apps Script mà ở chính sách cache phía client. Việc bỏ grace làm thu hồi có hiệu lực ở lần bấm lệnh kế tiếp, đổi lại người dùng phải có mạng và có thể chờ tới timeout khi server không phản hồi.

## Decisions Made

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Server quyết định quyền ở mỗi lệnh | Thu hồi/rút ngắn/gia hạn phải có hiệu lực ngay | Không cần đăng xuất để nhận ngày mới |
| Offline fail-closed | Cache offline có thể giữ quyền đã bị thu hồi | Mất mạng thì lệnh bảo vệ bị chặn |
| Tombstone + generation cho phiên | Ngăn race và lỗi ABA phục hồi phiên cũ | Trạng thái đăng xuất không bị response trễ ghi đè |

## Next Steps

- Phát hành v1.14.1 và xác minh đủ artifact cùng manifest.
- Khách nhận auto-update, sau đó đóng/mở Revit một lần; không cần cài lại thủ công.
