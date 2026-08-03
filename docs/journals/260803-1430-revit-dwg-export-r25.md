---
date: 2026-08-03
session: revit-dwg-export-r25
---

# Journal: 2026-08-03 — Revit DWG Export R25

## Context

Xây dựng lệnh RevitAPP xuất các sheet sang đúng một file DWG, ưu tiên Revit 2025. Mục tiêu là đưa toàn bộ bản vẽ vào Model Space, xếp sheet từ trái sang phải và giữ bố cục viewport như Revit.

## What Happened

- Người dùng xác nhận đầu ra chỉ có một DWG và phiên bản đầu tiên chỉ cần Revit 2025.
- Thử nghiệm `EXPORTLAYOUT` bằng AutoCAD Core Console bị treo; kiến trúc chuyển sang một phiên AutoCAD đầy đủ, riêng biệt, được điều khiển qua COM.
- Đã triển khai contract/job store, bộ tính bố cục, UI và command Revit, xuất DWG tạm, ghép sheet và publish atomically; build/deploy Debug.R25 thành công, 326/326 test thuần đã qua.
- Review phát hiện DWG do `EXPORTLAYOUT` tự mở chưa được theo dõi và rủi ro COM tác động nhầm phiên AutoCAD; các điểm này đã được sửa bằng quản lý document/session rõ ràng và fail-safe khi không bảo đảm phiên riêng.
- Revit MCP bridge không khả dụng, nên chưa thể lấy fixture Revit/AutoCAD thực tế để kiểm tra ánh xạ viewport sang entity.

## Reflection

Luồng xuất một file và biên an toàn của tiến trình đã rõ hơn sau spike và review. Tuy vậy, mixed-scale là phần nhạy cảm nhất: công thức 1:50/1:100 đúng ở lớp kế hoạch chưa đủ chứng minh DIM trong DWG hoạt động đúng. Giữ fail-closed là lựa chọn thận trọng hơn xuất ra một file có vẻ hợp lệ nhưng sai kích thước.

## Decisions Made

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Chỉ giao R25 ở vòng này | Đúng phạm vi người dùng chốt | Giảm ma trận build/runtime; các bản Revit khác để sau |
| Một DWG cuối cùng | Yêu cầu đầu ra bắt buộc | Mọi sheet được flatten, xếp trái sang phải và ghép trước khi publish |
| Dùng dedicated full AutoCAD COM | Core Console treo tại `EXPORTLAYOUT` | Cần AutoCAD desktop tương thích và phải cô lập session |
| Mixed-scale fail-closed | Chưa chứng minh mapping entity và DIM semantics trên fixture thật | Sheet 1:50/1:100 chưa được phép xuất như đã hoàn tất |

## Next Steps

- Mở Revit 2025 cùng một project fixture có sheet mixed-scale 1:50/1:100 và khôi phục MCP bridge.
- Chạy smoke test end-to-end trong AutoCAD, xác minh viewport-to-entity mapping, `DIMLFAC` và kích thước đo thực tế.
- Chỉ gỡ fail-closed sau khi fixture chứng minh kết quả ổn định và có regression evidence.
