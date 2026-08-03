---
date: 2026-08-03
session: mixed-scale-dwg-normalization
---

# Journal: 2026-08-03 — Mixed-Scale DWG Normalization

## Context

Sửa lệnh xuất Print Set của Revit 2025 sang đúng một file DWG Model Space. Với sheet có tỷ lệ 1:75 và 1:25, người dùng xác nhận 1:75 là tỷ lệ tham chiếu được ưu tiên.

## What Happened

- Ánh xạ từng viewport Revit vào đúng vùng tương ứng trong DWG đã làm phẳng.
- Chuẩn hóa kích thước AutoCAD bằng `LinearScaleFactor` theo tỷ lệ tham chiếu; 1:25 dưới chuẩn 1:75 nhận hệ số DIMLFAC `25 / 75`.
- Không scale hình học lần hai sau `EXPORTLAYOUT`, vì thao tác này đã biến đổi hình học viewport; scale thêm sẽ gây nhân đôi hệ số.
- Đếm dimension nguồn và dimension đã chuẩn hóa; thiếu hoặc lỗi thì dừng xuất để tránh bàn giao DWG sai âm thầm.
- Đường dẫn đầu ra do người dùng chọn. Quy trình chỉ bàn giao một file DWG cuối cùng.
- Toàn bộ 335 test đã qua. Build/deploy `Debug.R25` hoàn tất với 0 lỗi sau khi Revit đóng.

## Reflection

Giải pháp giữ đúng quy ước tỷ lệ của người dùng và ưu tiên tính an toàn: không tạo kết quả khi không thể chứng minh mọi dimension cần thiết đã được chuẩn hóa. Kiểm thử runtime vẫn cần thực hiện trong Revit và AutoCAD thực tế.

## Decisions Made

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Chọn mẫu số lớn nhất làm tỷ lệ tham chiếu | Người dùng định nghĩa 1:75 là “tỷ lệ lớn” so với 1:25 | Đồng nhất cách tính hệ số cho toàn sheet |
| Chỉ chỉnh `LinearScaleFactor` sau `EXPORTLAYOUT` | Tránh scale hình học hai lần | Giữ kích thước hình học đúng sau flatten |
| Fail closed khi số dimension không khớp | Không bàn giao bản vẽ có DIMLFAC sai hoặc thiếu | Lỗi được báo rõ thay vì xuất sai âm thầm |
| Giữ đường dẫn do người dùng chọn và một DWG cuối | Đúng yêu cầu giao nhận | File staging và xref chỉ là nội bộ |

## Next Steps

- Chờ đóng Revit để deploy bản `Debug.R25` mới.
- Chạy smoke test runtime với Print Set mixed-scale trong Revit 2025 và kiểm tra DWG cuối bằng AutoCAD.
