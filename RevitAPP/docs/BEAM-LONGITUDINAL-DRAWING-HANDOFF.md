# Beam Longitudinal Drawing — Handoff

## Trạng thái

- Build và deploy `Release.R25` thành công ngày 2026-07-22.
- Revit journal xác nhận `RevitAPP.addin` load với `AddInLoadFailureMessage: NoError`.
- Chưa smoke output trên model dầm thực tế; không được coi là nghiệm thu sản xuất trước bước này.
- Add-in chỉ đọc hình học/Rebar có sẵn; không tạo, xóa hoặc thay đổi hình học/cấu hình Rebar. `SetUnobscuredInView` chỉ đặt trạng thái hiển thị cho các view mới.

## Artifact

- DLL: `C:\Users\Admin\AppData\Roaming\Autodesk\Revit\Addins\2025\RevitAPP\RevitAPP.dll`
- SHA-256: `7FAC14D09B25FD4DE79CD211D1CC156DD942EBE45A0B94904CB1B5621106011E`
- Command: `RevitAPP.Commands.BeamLongitudinalDrawingCommand`
- Ribbon: `Mat Cat Doc Dam`

## Verification

- `RevitAPP.Tests`: 196/196 pass.
- `Debug.R25`: build + XAML + ILRepack pass.
- `Release.R25`: build + deploy + ILRepack pass.
- Revit 2025 startup: manifest load `NoError`.

## Smoke bắt buộc

1. Mở model có dầm Structural Framing đã chứa Rebar.
2. Chạy `Mat Cat Doc Dam`, chọn chuỗi dầm, kiểm tra và xác nhận preview.
3. Kiểm tra view dọc, các view station, tag, dimension, spot, detail component và sheet.
4. Thử một nhịp, nhiều nhịp, hai phía gối khác thép và đảo hướng.
5. Nếu lỗi, giữ nguyên TaskDialog/warning và Revit journal để sửa; TransactionGroup phải rollback khi lỗi bắt buộc.

## Rủi ro còn lại

- MCP socket `localhost:8080` chưa chạy nên Codex chưa thể tự điều khiển model để smoke.
- Dimension vùng thép dùng reference Rebar best-effort; family/template cụ thể có thể từ chối reference.
- Viewport cần kiểm tra trực quan với Title Block thực tế và chuỗi dài.
