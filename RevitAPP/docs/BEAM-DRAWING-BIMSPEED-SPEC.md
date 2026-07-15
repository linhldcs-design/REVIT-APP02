# Beam Drawing — Đặc tả form + output mẫu BIMSpeed (target v2)

> Nguồn: video `trien khai dam.mp4` (frame trong `docs/trien-khai-dam-frames/`) + 2 ảnh user gửi (form INPUT + bản vẽ OUTPUT).
> Mục tiêu v2: clone chức năng form "Beam Drawing" của BIMSpeed. KHÔNG copy logo/branding.

## A. FORM INPUT (3 vùng)

### Cột trái — Template Setting
- `Setting Name` (textbox) + nút **Add / Update / Delete**.
- **Setting List** (danh sách preset, ví dụ: `BS-A1-25-BEAM-DX`, `BS-A2-25-BEAM-DY`, `BS-A3-25-BEAM-DX-LINH`...).
- Nút **Load / Export** (import/export JSON preset).
- Nút **Up / Down** (sắp thứ tự).

### Giữa — Sơ đồ minh hoạ (Image) + ô số DIM
- SECTIONAL ELEVATION (mặt đứng): tag T1/T2 trên, T1 dưới, item (4) Ø8a100, break line 2 đầu (Lc | L0 | Lc), kích thước L.
- Ô nhập: **DIM SPACING FACTOR** = 6.
- MẶT CẮT NGANG (SE): 2 tiết diện, tag D0–D5, **DISTANCE DIM TO SIDE BEAM (DS1)** = 200, **DISTANCE DIM TO BOT FACE** = 200, ô 550/120/50, checkbox Hs/H/H1.

### Cột phải — 2 cột cấu hình (Sectional | Cross Section)
| Nhóm | Sectional Elevation | Cross Section |
|---|---|---|
| **REBAR TAG** ☑ | T1 (@BSA1-RT_BOT: A1_P_RT_Sl), T2 (@BSA1-RT_BOT: A1_T_RT_Sl), nút (4) @BSA3-RT_MID, ☑ **Rebar Break Symbol** | D0 (@BSA1-RT_BOT), D1 (BS-A1_SL & DK (MCN)-T), D2 (@BSA1-RT_BOT), D3 (@BS-A1_SL & DK (MCN)-P), D4 (@BSA1-RT_BOT), D5 (@BSA1-RT_BOT) |
| **SPOT ELEVATION** ☑ | combo `BS-2-Cao độ mặt đứng` + ô offset (0) | — |
| **DIMENSION** ☑ | SE: `@BS-Dim A1` | CS: `@BS-Dim A1` |
| **VIEW TEMPLATE** | `BS-23-MCD-CT-Dăm PX` | `BS-24-MCN-CT-Dăm PX` |
| **VIEWPORT** | `BS - Tên view MB, MĐ` | `BS - Tên view MB, MĐ` |
| **SECTION TYPE** | `BS-07-MCD-CT-Dăm PX` | `BS-12-MCN-CT-Dăm PX` |
| **SCALE** | 25 | 25 |
| **BREAK LINE** ☑ | `@BS-Break Line _Nhieu ty le` | — |
| **TITLE BLOCK** | | (combo title block) |
| **SHEET NUMBER** | `KC-0011.1.1` | |
| **SHEET NAME** | | `CHI TIẾT THÉP DẦM LẦU 1` |

### Dưới — checkbox
☐ Long Section (+ View Name) · ☑ Cross Section (+ View Name) · ☐ Cross Section for Multi Beam · ☐ Pick the pillow you want to dim · ☐ Create view 3D.

## B. OUTPUT MẪU (mặt cắt ngang — ảnh user gửi)
2 mặt cắt ngang dầm 250×450, mỗi cái:
- Tiết diện chữ nhật + **cover** (khung trong nét mảnh) + thép 4 góc + thép lớp giữa.
- **Spot elevation** đỉnh: `+4.150` (ký hiệu tam giác đặc).
- **Dimension chiều cao (trái)**: chuỗi nhiều đoạn `100 / 350` (tổng 450) — chia theo lớp thép.
- **Dimension chiều rộng (dưới)**: `250`.
- **Rebar tag (phải)**: tag TRÒN số hiệu (43, 4, 46, 2) + text đường kính:
  - `3D16` (thép trên) · `2D16` (thép lớp 2) · `D6 a100` hoặc `D6 a200` (đai) · `3D16` (thép dưới).
- Leader ngang bám thép; số hiệu vòng tròn ĐỎ.

### ⚠️ Hành vi quan trọng
**2 mặt cắt KHÁC NHAU** (D6 a100 vs D6 a200, thứ tự tag khác) → mỗi cross-section station phải tag theo THÉP THỰC TẾ của dầm tại vị trí đó (đọc rebar thật host bởi dầm ở đoạn đó), KHÔNG copy giống nhau giữa các mặt cắt.

## C. Khoảng cách vs bản v1 hiện tại
- v1 dùng 1 rebar tag type mặc định + 1 section type/template chung. v2 cần chọn ĐÍCH DANH tag family theo vị trí (T1/T2/mid, D0–D5), spot type, dim type, viewport type, section type + template RIÊNG sectional/cross, break line, title block.
- v1 chưa có Setting List (preset CRUD). v2 cần.
- v1 tag mọi rebar 1 kiểu; v2 phân biệt theo tag family cấu hình.
