# Phase 02 — Dựng tường trong Revit

**Ưu tiên:** cao
**Trạng thái:** hoàn thành — chờ kiểm thử tích hợp trên Revit R25 ở Phase 04

## Liên kết

- `RevitAPP/Services/CadStructure/CadSlabCreationService.cs` — mẫu gần nhất, đọc trước
- `RevitAPP.Core/Services/CadSlabTypeNaming.cs` — đặt tên type, đã có 13 test
- `RevitAPP/Services/CadStructure/CadColumnCreationService.cs` — mẫu dùng hai level

## Việc cần làm

Nhận trục tim + bề dày từ Phase 01, dựng `Wall` trong Revit.

```csharp
Wall.Create(document, curve, wallTypeId, levelId, height, offset, flip, structural)
```

## Wall Type — dò trước, không có mới sinh

Đúng như đã chốt. Cơ chế giống `v1.11.1` vừa làm cho Floor Type, và **lỗi ở đó phải
tránh lặp lại**:

Mọi Floor Type trong Revit đều mang `FamilyName = "Floor"`, nên điều kiện
`FamilyName == seed.FamilyName` khớp bất kỳ type nào cùng chiều dày — chọn bê tông
lấy nhầm metal deck. Wall Type cũng vậy.

Thứ tự dò:

1. Type đang chọn có đúng bề dày? → dùng luôn
2. Có type nào **cấu tạo giống** type đang chọn và đúng bề dày? → dùng
3. Không có → nhân bản type đang chọn, đặt bề dày, tên theo `CadSlabTypeNaming`

"Cấu tạo giống" = cùng số lớp, cùng chức năng lớp, cùng vật liệu, cùng thứ tự.
Đúng như `BuiltLike` đã viết cho Floor Type — nên tách ra dùng chung.

## Chiều cao

Base Level → Top Level, chọn trong bản tùy chọn. Revit nhận qua:

```csharp
wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(topLevelId);
```

Không dùng `WALL_USER_HEIGHT_PARAM` — tường sẽ không theo level khi level đổi cao độ.

## File

**Tạo mới**
- `RevitAPP/Services/CadStructure/CadWallCreationService.cs`
- `RevitAPP/Services/CadStructure/CadWallPreviewFactory.cs`

**Sửa**
- `CadSlabCreationService.cs` — tách `BuiltLike` ra dùng chung

## Các bước

1. Tách `BuiltLike` khỏi `CadSlabCreationService`, chạy lại test Slab
2. `CadWallCreationService` theo khuôn `CadSlabCreationService`:
   - `TransactionGroup` bọc ngoài
   - Chuẩn bị Wall Type trong transaction riêng, trước khi dựng
   - Dựng từng tường, lỗi một cái không làm hỏng cả mẻ
   - Bỏ qua tường trùng với tường đã có
3. Xoay/dời theo điểm móc — dùng lại phép biến đổi của các tab khác

## Xong khi

- Bốn tường khép phòng dựng ra trong Revit, các góc nối liền
- Bề dày CAD chưa có type thì sinh type mới, tên rõ nghĩa
- Chạy hai lần không tạo tường trùng
- Lỗi một tường thì báo riêng tường đó, các tường khác vẫn dựng

## Rủi ro

| Rủi ro | Cách giảm |
|---|---|
| Góc tường hở | Phase 01 đã kéo trục tới giao điểm; kiểm bằng mắt trong Revit |
| Tường lộn mặt trong/ngoài | Revit có `flip`; xem hướng trục có nhất quán không |
| Wall Type sinh trùng tên | `UniqueTypeName` đã có sẵn ở Slab, dùng lại |
