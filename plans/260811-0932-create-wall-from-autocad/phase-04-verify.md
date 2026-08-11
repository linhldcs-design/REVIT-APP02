# Phase 04 — Kiểm chứng và phát hành

**Ưu tiên:** cao — chạy suốt, không để cuối
**Trạng thái:** chưa bắt đầu

## Vì sao phase này quan trọng

Tab Slab tốn nhiều vòng thử vì tôi sửa rồi mới nhờ bạn kiểm, mỗi vòng phải đóng/mở
Revit. Lần này khác: tự kiểm hết bằng test và probe, xong xuôi mới đưa bạn thử.

## Hai bài học phải theo

**1. Test trước, sửa sau.** Công thức cung tròn ở `v1.11.0` tôi dò dấu bằng script rời,
tám vòng không hội tụ. Viết 30 test trước rồi sửa — ba lần là xanh.

**2. Không lọc theo tên layer.** Ở `v1.11.0` tôi thêm bộ lọc `GRID`/`DIM`/`TEXT`, làm
hỏng bước quét lưới vì trục nằm trên `S-GRID`, phải `git revert`. Tên layer không nói
lên công dụng của line — hình học thì có.

## Thứ tự bắt buộc: deploy R25 → người dùng kiểm → mới phát hành

Không phát hành trước rồi mới nhờ kiểm. Thứ tự là:

1. Test xanh hết, kể cả test Beam và Slab cũ
2. Probe dựng lại đủ các ca trong Phase 01
3. Build R22–R27 — bắt lỗi API cũ như `double.IsFinite` không có trên net48
4. **Deploy `Debug R25`** — chờ người dùng đóng Revit, không tự đóng
5. **Người dùng thử trên bản vẽ thật** và xác nhận đạt
6. Chỉ khi đó mới sang bước phát hành

Lệnh deploy:

```
dotnet build RevitAPP/RevitAPP.csproj -c "Debug R25"
```

Kiểm tra `RevitAPP.dll` trong thư mục Addins của Revit 2025 có dấu thời gian mới,
rồi báo người dùng mở Revit.

## Ca kiểm trên bản vẽ thật

| Ca | Xem gì |
|---|---|
| Tường bao khép kín | Các góc nối liền, không hở |
| Tường trong chia phòng | Chạm tường bao, không thừa/thiếu |
| Nhiều bề dày (100/200/300) | Mỗi loại một Wall Type đúng |
| Tường cong | Trục cong theo CAD |
| Lẫn rectangle và hai line | Nhận đủ, không trùng |
| Chạy hai lần | Không tạo tường trùng |

## Phát hành — chỉ sau khi người dùng xác nhận R25 đạt

Theo `HANDOFF-REVITAPP.md` mục "PHÁT HÀNH BẢN MỚI" — 10 bước, không bỏ bước nào:

1. `git status -sb`, không stage file lạ
3. `dotnet test tests/RevitAPP.Tests/RevitAPP.Tests.csproj -c Release`
4. Build 6 bản với `DeployAddin=false -p:LaunchRevit=false`
5. Tăng version — tab mới là `v1.12.0`
6-7. Commit, push, tag
8. Chờ 8 job GitHub Actions
9. Kiểm đủ 8 asset
10. `latest.json` trả HTTP 200

Cập nhật handoff: version, số test, mô tả `v1.12.0`, lỗi đã sửa, bài học nếu có.

## Xong khi

- Người dùng đã thử bản R25 trên bản vẽ thật và xác nhận đạt
- `v1.12.0` phát hành, 8 asset đủ, `latest.json` 200
- Handoff ghi đủ để phiên sau tiếp được
