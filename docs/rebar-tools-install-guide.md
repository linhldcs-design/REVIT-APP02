# Hướng dẫn cài Bộ công cụ vẽ thép (RevitAPP Rebar Tools)

Bộ cài: **`RevitAPP-RebarTools-1.0.0.msi`** — gồm add-in RevitAPP + 4 công cụ vẽ thép (cột, dầm, móng đơn, tường) chạy qua AI (MCP), có license đăng nhập Google.

---

## Yêu cầu máy khách

| Thành phần | Bắt buộc |
|-----------|----------|
| **Revit 2025** | Có |
| **Node.js** (cho `npx revit-mcp` phía Claude) | Có — tải tại https://nodejs.org (bản LTS) |
| **Claude Desktop / Claude Code** | Có (để gọi 4 tool AI) |
| Quyền Admin khi cài | Có (MSI cài per-machine) |
| Tài khoản Google được cấp quyền | Có (nằm trong Google Sheet license) |

---

## Bước 1 — Cài .msi

1. Đóng Revit (nếu đang mở).
2. Chạy **`RevitAPP-RebarTools-1.0.0.msi`** → chấp nhận LICENSE → Next → Install (bấm Yes khi Windows xin quyền admin).
3. Bộ cài chép vào `C:\ProgramData\Autodesk\Revit\Addins\2025\` — gồm addin RevitAPP + plugin MCP + 4 tool.

## Bước 2 — Cấu hình Claude client (phía máy chạy Claude)

Thêm vào file cấu hình MCP của Claude:

**Claude Desktop** — `%AppData%\Claude\claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "revit-mcp": {
      "command": "npx",
      "args": ["-y", "revit-mcp@2.3.0"]
    }
  }
}
```
Khởi động lại Claude sau khi sửa. (Yêu cầu đã cài Node.js.)

## Bước 3 — Mở Revit & bật MCP

1. Mở **Revit 2025**.
2. Tab **Add-Ins** → panel **RevitAPP** → thấy các nút: License, Vẽ Thép Cột, Ban Vẽ Dầm, Vẽ Thép Dầm, Vẽ Móng Đơn, Vẽ Thép Tường...
3. Bật MCP server (nút MCP của plugin revit-mcp trên ribbon, nếu có) hoặc theo hướng dẫn plugin.

## Bước 4 — Kích hoạt License (đăng nhập Google)

1. Trên ribbon **RevitAPP** → bấm nút **License**.
2. Bấm **Đăng nhập Google** → trình duyệt mở → chọn tài khoản Google **đã được cấp quyền** → cho phép.
3. Dialog hiện **"Đã kích hoạt • email • Hết hạn ..."** = xong.

> Nếu báo "Chưa được cấp quyền": email đó chưa có trong danh sách license. Liên hệ nhà cung cấp để thêm.

## Bước 5 — Dùng thử

Trong Claude, gọi thử (ví dụ):
- `draw_column_rebar` với 1 columnId
- `draw_wall_rebar` với 1 wallId

Nếu **chưa đăng nhập** → tool trả: *"[License] Không thể vẽ thép... mở ribbon RevitAPP → License → Đăng nhập"*. Đăng nhập xong (Bước 4) thì tool vẽ bình thường.

---

## Vận hành — cấp / gia hạn / thu hồi quyền (dành cho nhà cung cấp)

Danh sách khách hàng nằm trong **Google Sheet** (`RevitAPP Licenses`, tab `Licenses`):

| email | expiry | note |
|-------|--------|------|
| khach@gmail.com | '2026-12-31 | goi 1 nam |

- **Cấp quyền mới**: thêm 1 dòng (email + ngày hết hạn). Cột `expiry` gõ dạng `'2026-12-31` (có dấu nháy đơn ở đầu để lưu là chữ, tránh lỗi ngày).
- **Gia hạn**: sửa cột `expiry`.
- **Thu hồi**: đổi `expiry` về quá khứ hoặc xóa dòng.
- Hiệu lực: khách re-verify sau tối đa **7 ngày** (cache offline), hoặc ngay khi họ đăng nhập lại / đăng xuất rồi đăng nhập.

Chi tiết setup Google (Client ID, Sheet, Apps Script): xem [license-google-setup.md](license-google-setup.md).

---

## Xử lý sự cố

| Triệu chứng | Xử lý |
|-------------|-------|
| Cài .msi báo "Revit đang chạy" | Đóng hết cửa sổ Revit rồi cài lại |
| Không thấy panel RevitAPP | Kiểm tra `C:\ProgramData\Autodesk\Revit\Addins\2025\RevitAPP.addin` tồn tại; mở lại Revit |
| Tool báo lỗi License dù đã đăng nhập | Mở ribbon License xem trạng thái; nếu hết hạn → liên hệ gia hạn |
| Đăng nhập Google không mở browser | Kiểm tra máy có trình duyệt mặc định; thử lại |
| Tool "Method not found" | Khởi động lại Revit để nạp DLL mới |
| Không có mạng | Vẫn dùng được nếu còn trong 7 ngày kể từ lần verify cuối |
