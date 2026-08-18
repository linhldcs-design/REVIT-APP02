---
date: 2026-08-18
session: revitapp-license-hotfix-v1.14.2
---

# Journal: 2026-08-18 — RevitAPP License Hotfix v1.14.2

## Context

Sau khi khách tự cập nhật lên v1.14.1, đăng nhập Google có thể báo thành công trên trình duyệt nhưng Revit vẫn không kích hoạt. Mục tiêu của v1.14.2 là khôi phục xác thực bản quyền và bảo đảm các bản lỗi vẫn tự cập nhật được mà khách không phải cài lại thủ công.

## What Happened

- Secret nhúng trong add-in bị thừa ký tự BOM ở đầu, nên máy chủ từ chối dù phần nội dung còn lại đúng.
- Bản Installer độc lập trước đó không được nhúng secret phát hành.
- Hai đường kiểm tra cập nhật bị chặn bởi kiểm tra license; một bản bị lỗi license vì vậy không thể tự tải bản vá.
- Workflow phát hành mới sinh source secret không BOM, kiểm tra định dạng nghiêm ngặt và xác minh secret trong DLL thành phẩm.
- Add-in và Installer đều được build với secret phát hành; lệnh cập nhật và kiểm tra cập nhật lúc khởi động không còn phụ thuộc vào trạng thái license.
- Trang callback OAuth được sửa nội dung để không tuyên bố kích hoạt hoàn tất trước khi Revit kiểm tra license với máy chủ.
- Gate local đạt 600/600 tests; build và xác minh R22–R27 đều đạt; Installer publish và xác minh đạt.

## Reflection

Lỗi không nằm ở OAuth mà ở dữ liệu secret được đóng gói trong artifact. Việc kiểm tra source hoặc biến CI là chưa đủ: cần xác minh trực tiếp DLL cuối cùng sau mọi bước đóng gói. Đồng thời, cơ chế tự cập nhật phải luôn là đường cứu hộ độc lập với license để tránh khóa khách trong một phiên bản lỗi.

## Decisions Made

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Chỉ chấp nhận secret phát hành đúng định dạng và không có BOM/khoảng trắng | Ngăn dữ liệu vô hình làm hỏng artifact | Workflow dừng sớm thay vì phát hành bản lỗi |
| Xác minh metadata của DLL thành phẩm | Bắt lỗi phát sinh sau build hoặc repack | Cả add-in và Installer có bằng chứng secret được nhúng đúng |
| Bỏ license gate khỏi đường cập nhật | Cho phép bản lỗi hoặc hết hạn nhận bản vá | Khách có thể tự phục hồi mà không cài lại thủ công |
| Máy chủ tạm tương thích BOM đầu chuỗi | Giải cứu ngay các máy đang chạy v1.14.1 | v1.14.1 có thể xác thực lại và nhận v1.14.2 |

## Next Steps

- P0: triển khai phiên bản Web App Apps Script mới có tương thích BOM đầu chuỗi.
- Chạy hai probe với secret chuẩn và secret có BOM; cả hai phải cho cùng kết quả xác thực.
- Chỉ sau khi P0 đạt mới tag, phát hành public v1.14.2 và kiểm tra toàn bộ asset/SHA.
- Cập nhật handoff với run phát hành và kết quả xác minh artifact cuối cùng.
