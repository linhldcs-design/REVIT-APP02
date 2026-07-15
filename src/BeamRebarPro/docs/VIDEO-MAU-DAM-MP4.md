# Phân tích video mẫu `dam.mp4` — Form "Beam Rebar" của BIMSpeed (form gốc cần làm giống)

> Nguồn: `C:\Users\Admin\OneDrive\Desktop\dam.mp4` (~62s). Frame đã trích sẵn ở
> `C:\Users\Admin\Desktop\dam-frames\frame_001.jpg ... frame_031.jpg` (mỗi 2 giây 1 frame).
> Cách trích: dùng ffmpeg của imageio-ffmpeg:
> `& "C:\Users\Admin\AppData\Local\Programs\Python\Python314\Lib\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe" -i dam.mp4 -vf "fps=1/2" -q:v 3 out\frame_%03d.jpg`

Video quay phần mềm **BIMSpeed** (form mẫu gốc), thao tác dầm 1 nhịp: Span 0 dài 7700mm,
console 480mm mỗi đầu, 2 cột biên (lưới B và 1).

## Cấu trúc form "Beam Rebar" (giống BeamRebarPro đang làm)
- Cột trái Setting: Main Top Bar / Main Bot Bar / Add. Top Bar / Add. Bot Bar / Stirrup / Anti bulge rebar.
- Giữa: Rebar List + Rebar Info (form theo tab).
- Phải: Image minh hoạ (Type 1/Type 2 cho Add Top; Anchor/Span Length cho Add Bot).
- Dưới: mặt đứng dầm (Span 0) có đánh số gối + kích thước nhịp + preview thanh đỏ ĐỘNG theo thông số.

## CHI TIẾT FORM ADD. TOP BAR (frame 004, 008, 016)
Image phải có 2 sơ đồ:
- **TYPE 1: ATTACHED TO COLUMN** — thép vắt qua cột, LEFT LENGTH / RIGHT LENGTH đo từ mép cột,
  đầu ngoài BẺ MÓC XUỐNG (đoạn đỏ dọc).
- **TYPE 2: GO THROUGH THE SPAN** — thép chạy qua nhịp (đoạn đỏ ngang giữa, không móc).

Các field Rebar Info (Add Top):
- `Layer` (T 1 = top layer 1), `Diameter` (D20).
- `Start Point` / `End Point` (0, 1...) = gối bắt đầu/kết thúc.
- `Start Type` / `End Type` (1, 2...) = Type 1/Type 2 ở mỗi đầu.
- `Left Ratio` / `Right Ratio` (vd 0.25) = tỉ lệ chiều dài theo nhịp.
- `Left Length` / `Right Length` (mm, vd 650/1827, 1800/600) = chiều dài tuyệt đối từ mép cột.
- `D Left` / `D Right` (vd Φ30) = chiều dài đoạn MÓC XUỐNG đầu trái/phải.
- `Number` (số thanh), `Position In Section` (vị trí thanh trong tiết diện).
- Rebar List item dạng: `Count-1-D20-S-0-E-0`, `Count-1-D20-S-1-E-1` (Start/End point khác nhau).
- Preview mặt đứng: đoạn đỏ + kích thước (vd 1827, 1800) cập nhật ĐỘNG khi đổi Length/Ratio.

## CHI TIẾT FORM ADD. BOT BAR (frame 024, 028)
Image phải: ANCHOR LEFT — LEFT LENGTH — RIGHT LENGTH — ANCHOR RIGHT, dòng dưới SPAN LENGTH.
Field (Add Bot):
- `LAYER` (B 1 = bottom layer 1), `Diameter` (D20).
- `Start Point` / `End Point`, `Start Type` / `End Type`.
- `Left Ratio` / `Right Ratio` (vd 0.2 / 0.206...).
- `Left Length` / `Right Length` (vd 2189/2189, 2200/2200).
- `Anchor Left` / `Anchor Right` (vd 1461/1461, -1450/-1450 — có thể âm = neo vào trong).
- `Total` (vd 4378, 4400) = tổng chiều dài thanh (auto = anchor + length).
- `Number`, `Position In section`.

## SO SÁNH với BeamRebarPro hiện tại (theo HANDOFF + code)

| Field/Tính năng form mẫu | BeamRebarPro hiện có? | Ghi chú |
|---|---|---|
| Add Top: Start/End Point | ✅ (`AdditionalBarConfig.StartPointIndex/EndPointIndex`) | đã nối engine `CreateTopAdditionalAtSupports` |
| Add Top: Start/End Type (Type 1/2) | ⚠️ field có nhưng engine CHƯA phân biệt Type 1 (attached) vs Type 2 (through span) | cần: Type 1 móc cột + bẻ xuống; Type 2 chạy suốt nhịp |
| Add Top: Left/Right Ratio | ✅ (`LeftRatio/RightRatio`) | engine `ResolveAdditionalExtendFeet` ưu tiên Length>Ratio>TCVN |
| Add Top: Left/Right Length (mm) | ✅ (`LeftLengthMm/RightLengthMm`) | |
| Add Top: D Left/D Right (móc xuống) | ✅ (`DLeftMm/DRightMm` → móc biên) | hiện chỉ móc ở GỐI BIÊN; mẫu móc theo Type/đầu |
| Add Top: Number, Position In Section | ⚠️ Position In Section CHƯA dùng (layout Fixed Number theo bề ngang) | |
| Add Bot: Anchor Left/Right | ⚠️ `AnchorXLeftMm/Right` có cho Main, CHƯA chắc Add Bot dùng | cần kiểm: Add Bot có Anchor riêng + cho phép ÂM (neo vào) |
| Add Bot: Total auto | ❌ chưa có field Total = anchor + length | |
| Preview mặt đứng động (đoạn đỏ di chuyển) | ✅ có `PreviewLines/PreviewTexts` cho Add Top/Bot | cần đối chiếu khớp số liệu mẫu |
| Image Type 1/Type 2, Anchor/Span Length | ✅ có `AddTopBarDiagram.png` | nên cập nhật ảnh khớp mẫu hơn |

## VIỆC CẦN BỔ SUNG (cho AI sau, ưu tiên giảm dần)
1. **Start Type / End Type (Type 1 vs Type 2) phân biệt thật trong engine:**
   - Type 1 = Attached to column: thép từ gối, dài Left/Right Length từ MÉP CỘT, đầu ngoài bẻ móc xuống (D Left/D Right).
   - Type 2 = Go through span: thép chạy suốt qua nhịp (không cắt tại gối, không móc).
   - Hiện engine chỉ tạo đoạn quanh gối + móc ở biên; chưa rẽ nhánh theo Type ở từng đầu.
2. **Add Bot: Anchor Left/Right (cho phép ÂM) + Total auto** = Anchor + Left Length + Right Length. Neo âm = thanh neo ngược vào trong nhịp.
3. **Position In Section**: cho phép chọn vị trí thanh gia cường trong tiết diện (góc/giữa) thay vì luôn Fixed Number trải đều.
4. **Rebar List naming** giống mẫu: `Count-1-D20-S-0-E-0` (đã gần đúng theo HANDOFF, kiểm lại Add bar).
5. **Image**: cập nhật `AddTopBarDiagram.png` / `AddBotBarDiagram.png` khớp sơ đồ mẫu (Type 1/2, Anchor/Span Length) — frame 004 (Add Top) và 024 (Add Bot) là mẫu chuẩn.
6. **Preview**: đối chiếu số liệu preview với mẫu (1827, 1800, 2189, 4400...) để công thức Total/Ratio khớp.

## LƯU Ý
- Đây là tài liệu THAM KHẢO để làm giống form mẫu. Không bắt buộc giống 100% mọi field — ưu tiên cái ảnh hưởng geometry thật (Type 1/2, Anchor, Length, móc D).
- Geometry rebar CHỈ verify trong Revit thật (Add-in Manager). Không test giả Revit API.
- Frame tham khảo: 004 (Add Top trống + Image Type1/2), 008/016 (Add Top có số liệu), 024/028 (Add Bot + Image Anchor/Span).
