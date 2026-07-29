---
date: 2026-07-29
session: beam-cross-section-sheet-layout-ownership-fix
---

# Journal: 2026-07-29 — Sửa ownership layout mặt cắt ngang dầm

## Context

Khi bổ sung mặt cắt ngang dầm vào sheet đã có viewport được sắp xếp, lệnh vô tình layout lại toàn bộ mặt cắt ngang hiện hữu và làm mất bố cục người dùng đã chỉnh trước đó.

## What Happened

- Root cause nằm ở `SheetBuilder`: danh sách ID viewport vừa tạo bị thay bằng toàn bộ viewport MCN trên sheet trước khi chạy layout.
- Việc thay danh sách này khiến planner xem cả viewport cũ là đối tượng thuộc quyền di chuyển của lần chạy hiện tại.
- Luồng mới giữ nguyên tập ID vừa tạo; viewport hiện hữu chỉ được đưa vào tập vùng đã chiếm để tránh chồng lấn.
- Nếu không còn vùng trống, lệnh giữ viewport mới tại vị trí đặt ban đầu để người dùng chỉnh tay thay vì rollback.
- Tách phép tính bố trí thành pure planner và bổ sung test cho ownership, vùng chiếm chỗ và trường hợp thêm viewport vào sheet đã có nội dung.

## Reflection / Decision

Layout tăng dần phải phân biệt rõ hai vai trò: nội dung mới là **movable**, nội dung hiện hữu là **occupied**. Quyết định giữ ownership theo từng lần chạy giúp bảo toàn chỉnh sửa thủ công trên sheet, đồng thời vẫn tìm vị trí hợp lệ cho viewport mới.

## Verification

- Test suite: **217/217 pass**.
- Build `Debug.R25`: **0 errors**; chỉ còn các warning hiện hữu.

## Next

- Smoke-test trong Revit bằng cách thêm nhiều đợt mặt cắt ngang vào cùng một sheet.
- Xác nhận viewport cũ giữ nguyên tọa độ và viewport mới không chồng lên nội dung hiện hữu.
