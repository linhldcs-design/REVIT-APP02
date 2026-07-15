# Hướng dẫn setup License Google cho RevitAPP (Phase 0)

Tài liệu này giúp bạn tạo **2 thứ** cần cho tính năng license:
1. **Google OAuth Client ID** — để người dùng đăng nhập Google trong addin.
2. **Google Sheet + Apps Script** — nơi lưu danh sách email được phép + ngày hết hạn.

Kết quả cuối: bạn có **3 chuỗi cấu hình** để điền vào addin ở Phase 1:
- `ClientId`
- `AppsScriptUrl`
- `SharedSecret`

> ⚠️ 3 chuỗi này **KHÔNG commit giá trị thật lên git**. Điền vào code lúc build, hoặc để placeholder trong repo.

---

## Phần A — Tạo OAuth Client ID (Desktop app)

1. Vào https://console.cloud.google.com → đăng nhập tài khoản Google của bạn (tài khoản quản trị, không phải của khách).
2. Trên cùng, bấm dropdown project → **New Project** → đặt tên `RevitAPP License` → **Create**. Đợi tạo xong, chọn project đó.
3. Menu trái → **APIs & Services** → **OAuth consent screen**:
   - User type: **External** → **Create**.
   - App name: `RevitAPP`. User support email: email của bạn. Developer contact: email của bạn. → **Save and Continue**.
   - **Scopes**: bấm **Add or remove scopes** → tick `.../auth/userinfo.email`, `.../auth/userinfo.profile`, `openid` → **Update** → **Save and Continue**.
   - **Test users**: bấm **Add users** → thêm các email khách hàng sẽ dùng thử (giai đoạn "Testing" giới hạn 100 user). → **Save and Continue** → **Back to Dashboard**.

   > Nếu bán rộng (>100 khách): sau khi test xong, ở màn OAuth consent screen bấm **Publish App**. Vì chỉ dùng scope `email/profile` (không nhạy cảm) nên **thường không cần Google review**.

4. Menu trái → **Credentials** → **+ Create Credentials** → **OAuth client ID**:
   - Application type: **Desktop app**.
   - Name: `RevitAPP Desktop`.
   - **Create** → hộp thoại hiện **Client ID** (dạng `xxxx.apps.googleusercontent.com`). **Copy** lại.

   > Desktop app dùng PKCE, không cần giữ bí mật client secret — an toàn để nhúng Client ID vào addin.

➡️ **Ghi lại:** `ClientId = <chuỗi vừa copy>`

---

## Phần B — Tạo Google Sheet danh sách license

1. Vào https://sheets.google.com → **Blank** (tạo sheet mới). Đặt tên file, ví dụ `RevitAPP Licenses`.
2. Đổi tên tab (dưới cùng) thành đúng chữ **`Licenses`**.
3. Hàng 1 nhập 3 cột tiêu đề: **A1** = `email`, **B1** = `expiry`, **C1** = `note`.
4. Nhập vài dòng mẫu (từ hàng 2):

   | email | expiry | note |
   |-------|--------|------|
   | khach1@gmail.com | 2026-12-31 | goi 1 nam |
   | khach2@gmail.com | 2026-09-30 | dung thu |

   > **Định dạng cột B (expiry):** dùng `yyyy-mm-dd`. Nếu Google tự nhận dạng thành Date thì vẫn OK (Apps Script tự xử lý cả 2 kiểu).

---

## Phần C — Deploy Apps Script web app

1. Trong Google Sheet vừa tạo: menu **Extensions** → **Apps Script**.
2. Xóa code mẫu, dán toàn bộ nội dung file **`tools/license-appsscript/Code.gs`** (trong repo này).
3. Sửa dòng đầu:
   ```javascript
   const SHARED_SECRET = 'DOI_CHUOI_NGAU_NHIEN_NAY_TRUOC_KHI_DEPLOY';
   ```
   → đổi thành **chuỗi ngẫu nhiên của bạn** (ví dụ tạo bằng cách gõ vài chục ký tự bất kỳ). Nhớ chuỗi này = `SharedSecret`.
4. Bấm **Save** (biểu tượng đĩa).
5. Bấm **Deploy** → **New deployment** → bánh răng ⚙ chọn **Web app**:
   - Description: `v1`.
   - Execute as: **Me**.
   - Who has access: **Anyone**.
   - **Deploy**. Lần đầu sẽ xin quyền → **Authorize access** → chọn tài khoản → **Advanced** → **Go to ... (unsafe)** → **Allow** (đây là script của chính bạn nên an toàn).
6. Copy **Web app URL** kết thúc bằng `/exec`.

➡️ **Ghi lại:** `AppsScriptUrl = https://script.google.com/macros/s/..../exec`
➡️ **Ghi lại:** `SharedSecret = <chuỗi bạn đặt ở bước 3>`

---

## Phần D — Kiểm tra hoạt động

Mở PowerShell trên máy bạn, thay `URL` và `SECRET`:

```powershell
$url = "https://script.google.com/macros/s/..../exec"
$body = @{ email = "khach1@gmail.com"; secret = "SECRET_CUA_BAN" } | ConvertTo-Json
Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json"
```

Kỳ vọng:
- Email **có trong sheet, còn hạn** → `allowed = True`, kèm `expiry`.
- Email **hết hạn** (đổi expiry về quá khứ) → `allowed = False`.
- Email **không có** → `allowed = False`, `error = not_found`.
- Sai secret → `allowed = False`, `error = unauthorized`.

Nếu 4 trường hợp đúng → **Phase 0 xong**.

---

## Tổng kết 3 chuỗi cấu hình (điền ở Phase 1)

| Tên | Giá trị | Lấy từ |
|-----|---------|--------|
| `ClientId` | `....apps.googleusercontent.com` | Phần A bước 4 |
| `AppsScriptUrl` | `https://script.google.com/macros/s/..../exec` | Phần C bước 6 |
| `SharedSecret` | chuỗi ngẫu nhiên bạn đặt | Phần C bước 3 |

## Vận hành sau này (cấp / thu / gia hạn quyền)

- **Cấp quyền cho khách mới**: thêm 1 dòng vào Sheet (email + ngày hết hạn). Có hiệu lực ngay (client re-verify sau tối đa 7 ngày, hoặc khi khách đăng nhập lại).
- **Gia hạn**: sửa cột `expiry`.
- **Thu hồi**: đổi `expiry` về ngày quá khứ, hoặc xóa dòng. (Khách vẫn dùng được tối đa 7 ngày còn cache — nếu cần chặn ngay, giảm `CacheGraceDays` ở Phase 1.)
