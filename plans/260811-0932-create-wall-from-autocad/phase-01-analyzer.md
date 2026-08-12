# Phase 01 — Phân tích tường từ line CAD

**Ưu tiên:** cao — mọi phase sau phụ thuộc phase này
**Trạng thái:** hoàn thành — fix cuối đạt 542/542 toàn bộ; 80 test Wall/Rail/Beam xanh

## Liên kết

- `RevitAPP.Core/Services/CadBeamAnalyzer.cs` — thuật toán rail, đọc để dùng lại
- `RevitAPP.Core/Services/CadPlanarGraph.cs` — cắt line tại giao điểm
- `RevitAPP.Core/Services/CadArcChords.cs` — cung tròn, đã có 30 test
- `RevitAPP.Core/Models/CadStructure/CadStructureModels.cs` — nơi đặt options

## Việc cần làm

Từ các đối tượng đã quét, tìm ra cái nào là tường rồi cho ra trục tim + bề dày.
Tường trong bản vẽ có hai dạng:

| Dạng vẽ | Cách đọc |
|---|---|
| **Hai line song song** | Bề dày = khoảng cách giữa chúng, trục = đường giữa |
| **Rectangle** (polyline khép kín) | Cạnh ngắn là bề dày, trục = đường nối trung điểm hai cạnh ngắn |

Rectangle đơn giản hơn: bốn cạnh đã khép sẵn, không phải đi tìm cặp. Nhưng phải
phân biệt rectangle tường với rectangle cột — cột gần vuông, tường dài hẳn một chiều.
Tab `Create Column` đã có `IsColumnFootprint` dùng tỷ lệ dài/rộng, dùng lại được.

## Quét lưới trước, như ba tab kia

Bước 1 là `Select Grid Axes`, giống `Create Column`, `Create Beam`, `Create Slab`.
Lưới không dùng để dựng tường, nhưng nó giữ ba việc:

| Việc | Vì sao cần |
|---|---|
| **Điểm móc nguồn** | Người dùng bấm một điểm trong CAD; mọi tọa độ tường quy về điểm đó, rồi đặt vào Revit theo điểm móc đích |
| **Khóa cùng bản vẽ** | `AutoCadModelSelectionService` từ chối nếu bước 2 quét ở bản vẽ khác hoặc khác `INSUNITS` — chặn ghép nhầm hai bản vẽ |
| **Nền cho preview** | Vẽ lưới mờ trong canvas 2D để nhìn ra tường nằm ở trục nào |

Dùng lại nguyên `SelectBeam(gridPackage)` — chỉ cần thêm `SelectWall(gridPackage)`
gọi cùng đường, đổi lời nhắc.

## Lọc layer — bắt buộc với tường

Tường không đọc text, và hình học của nó **giống hệt dầm**: hai line song song cách
nhau 200 mm có thể là tường 200, dầm 200, hay hai đường kích thước. Không có cách nào
từ hình học phân biệt được.

Với sàn thì khác — tôi giải được bằng hình học (cắt line về đoạn giữa hai điểm cắt,
đuôi trục lưới không bao quanh ô nào nên tự loại). Tường không có chỗ dựa đó.

Bản vẽ của người dùng đầy dầm `ĐK1-200x400`. Không lọc thì tab Wall nhận nhầm hết
dầm thành tường.

### Người dùng chọn layer, add-in không đoán

Sau khi quét, hiện danh sách layer tìm được kèm số lượng, người dùng tick:

```
Layer nào là tường?
  ☑ A-WALL              86 line
  ☐ NT2-NET DAM 0.4    142 line
  ☐ S-GRID              14 line
  ☐ 0-DIM               21 polyline
```

- Add-in **tick sẵn** theo phỏng đoán tên (`WALL`, `TUONG`, `VACH`), người dùng chỉ sửa
  chỗ sai — không phải tick từ đầu
- **Nhớ lựa chọn** cho lần sau; cùng bộ bản vẽ thì khỏi tick lại

### Khác gì với bộ lọc đã revert ở `v1.11.0`

Lần đó add-in **tự quyết**: tên layer chứa `GRID` thì bỏ. Trục lưới nằm trên `S-GRID`
nên bước quét lưới không đọc được gì, phải `git revert`.

Lần này phỏng đoán chỉ dùng để **tick sẵn**, không phải luật. Người dùng bỏ tick là
xong — đoán sai không mất dữ liệu.

## Cửa đi và cửa sổ — contract bridge dọc

Chỗ có cửa chỉ giữ thành **một tường** khi **cả hai line bridge dọc** tiếp tục đúng hai
mặt tường. Hai bridge phải nằm trên chính layer tường người dùng đã chọn; layer không
được chọn hoặc một layer khác dù cũng được chọn không được bridge `A-WALL`.

Không suy luận khoảng mở từ jamb/end-cap ngang. Một bridge là chưa đủ. Hai rail phải
có phần overlap; boundary đơn độc không được sinh tường ma. Short nib được giữ đến
bước lọc cuối để bridge có đủ hình học, và segment của rectangle khép kín chỉ được
consolidate khi có đúng hai bridge dọc này.

Clustering chịu được offset drift nhưng không nắn thẳng line gần song song không đồng
tuyến. Cleanup loại duplicate sinh từ bridge mà vẫn giữ regression hai mặt tường so le.

## Điểm mấu chốt

Tab Beam đã giải đúng bài toán này và đã sửa qua 10 lỗi trên bản vẽ thật:

- Biên lệch góc hoặc lệch offset vài mm vẫn ghép thành một dầm
- Biên bị trim vụn tại mặt cột vẫn dựng đủ chiều dài
- Polyline có một cạnh cong vẫn giữ các cạnh thẳng
- Dầm bị nhánh cắt ngang không mất nhãn

**Nên tách phần chung của `CadBeamAnalyzer` ra dùng lại**, thay vì chép sang file mới.
Chép sẽ phải sửa lại từng lỗi đó một lần nữa.

## Khác biệt tường so với dầm

| | Dầm | Tường |
|---|---|---|
| Tiết diện | `b×h` đọc từ text | `b` đo từ hai line, `h` từ level |
| Nhãn | Bắt buộc — không có thì `MissingText` | **Không đọc text chút nào** |
| Chiều dài | Theo rail | Theo rail |
| Giao nhau | Ít gặp | **Thường xuyên** — góc nhà, chữ T, chữ thập |

**Tường không đọc nhãn.** Mọi thứ cần biết đều lấy được từ hình học và bản tùy chọn:
bề dày đo giữa hai line, chiều cao từ Base/Top Level. Không có `MissingText`, không có
vùng tìm text, không có regex.

Điều đó bỏ hẳn một nhóm lỗi mà tab Slab đã tốn nhiều vòng để sửa: nhãn lan sai vùng,
nhãn của vùng này ăn sang vùng khác, một ô lạc quyết định cả tấm. Tường không có
những lỗi đó.

Chỗ khác biệt thật sự cần nghĩ là **giao tường**. Hai tường gặp nhau ở góc: trục tim
phải kéo dài tới giao điểm, không dừng ở mép. Nếu không, Revit dựng ra hai tường hở góc.

## File

**Tạo mới**
- `RevitAPP.Core/Services/CadWallAnalyzer.cs`
- `RevitAPP.Core/Models/CadStructure/CadWallModels.cs`
- `RevitAPP.Core/Services/CadLayerSuggestion.cs` — đoán layer nào là tường, để tick sẵn
- `tests/RevitAPP.Tests/CadWallAnalyzerTests.cs`
- `tests/RevitAPP.Tests/CadLayerSuggestionTests.cs`

**Sửa**
- `CadBeamAnalyzer.cs` — tách phần dựng rail thành hàm dùng chung
- `AutoCadModelSelectionService.cs` — trả về danh sách layer kèm số lượng
- `CadStructureModels.cs` — `CadStructureSegment` đã có `Layer`, kiểm lại có đầy đủ không

## Các bước

1. Đọc `CadBeamAnalyzer`, xác định phần nào là "ghép rail" thuần hình học
2. Tách phần đó ra — không đổi hành vi, chạy lại test Beam để chắc
3. Cho `AutoCadModelSelectionService` trả về danh sách layer kèm số lượng
4. `CadLayerSuggestion` — đoán layer tường để tick sẵn; **viết test trước**
5. **Viết test tường trước** (xem danh sách dưới)
6. Viết `CadWallAnalyzer` cho đến khi test xanh
7. Xử lý giao tường: kéo dài trục tới giao điểm

## Test cần viết trước

**Hai line song song**

| Ca | Kỳ vọng |
|---|---|
| Cách nhau 200 | Một tường, dày 200, trục ở giữa |
| Lệch góc 1° | Vẫn một tường — như dầm |
| Lệch offset 3 mm | Vẫn một tường |
| Line đơn không có cặp | Bỏ qua |
| Bề dày ngoài dải cho phép | Bỏ qua — hai vật khác nhau, không phải tường |

**Rectangle**

| Ca | Kỳ vọng |
|---|---|
| Rectangle 200×4000 | Một tường dày 200, dài 4000, trục nối trung điểm hai cạnh ngắn |
| Rectangle 400×400 (cột) | Bỏ qua — vuông, không phải tường |
| Rectangle 300×900 | Bỏ qua hay nhận? **Cần chốt ngưỡng** — xem Câu hỏi mở |
| Rectangle xoay 30° | Trục xoay theo, bề dày vẫn đo cạnh ngắn |
| Polyline 6 cạnh hình L | Bỏ qua — không phải rectangle |

**Chung**

| Ca | Kỳ vọng |
|---|---|
| Bốn tường khép thành phòng | Bốn tường, các góc gặp nhau đúng |
| Tường chữ T | Ba tường, trục nhánh chạm trục thân |
| Tường cong (cung tròn) | Trục cong, bề dày giữ đều |
| Lẫn cả hai dạng vẽ trong một bản | Nhận đủ, không trùng lặp |

**Chỗ có cửa**

| Ca | Kỳ vọng |
|---|---|
| Tường 6000, cả hai bridge dọc cùng layer tường được chọn | Một tường 6000 |
| Chỉ có một bridge dọc | Giữ các đoạn riêng, không nối qua cửa |
| Bridge ở layer không chọn hoặc layer khác cũng được chọn | Không bridge `A-WALL` |
| Chỉ có jamb/end-cap ngang | Không suy luận bridge, không sinh tường ma |
| Boundary không có rail đối diện | Không sinh candidate |
| Hai line gần song song nhưng không đồng tuyến | Không nắn thẳng để nối |
| Hai tường độc lập có cap, dày 200 và dài 10 m | Vẫn là hai tường riêng |
| Rectangle khép kín bị chia tại cửa | Chỉ consolidate khi đủ hai bridge dọc đúng layer |
| Hai tường thật cách nhau 2000 | **Hai** tường riêng, không nối |

**Chọn layer**

| Ca | Kỳ vọng |
|---|---|
| Chỉ tick layer tường | Dầm cùng bề dày không thành tường |
| Không tick layer nào | Báo rõ "chưa chọn layer", không dựng gì |
| Layer tên `A-WALL` | Tick sẵn |
| Layer tên `TUONG-100` | Tick sẵn |
| Layer tên `NT2-NET DAM 0.4` | Không tick sẵn |
| Layer tên lạ không đoán được | Không tick sẵn, người dùng tự chọn |

## Tùy chọn

**Người dùng nhập trước khi quét** — hai ô này quyết định cái gì được nhận là tường,
nên phải đặt được trước, không chờ quét xong mới chỉnh:

```
Bề dày min:  100 mm    // mỏng hơn không phải tường
Bề dày max:  400 mm    // dày hơn là hai vật riêng, không phải một tường
```

Đặt ở đầu bản tùy chọn, cạnh nút quét, để thấy ngay. Người dùng biết bản vẽ của mình
dùng tường 100/200/300 nên chỉnh một lần rồi quét là đủ.

Các tùy chọn còn lại, mặc định dùng được ngay:

```
MinimumLengthMm = 300
MinimumLengthRatio = 3.0         // dài / dày; thấp hơn là cột, không phải tường
RailOffsetToleranceMm = 10       // như Beam
GapJoinToleranceMm = 300         // như Beam
JoinDistanceMm = 200             // kéo trục tới giao điểm trong khoảng này
```

## Câu hỏi mở

**Ngưỡng phân biệt tường với cột.** Rectangle 300×900 — tỷ lệ 3:1 — là vách ngắn hay
cột chữ nhật? Hai thứ trông giống nhau trong bản vẽ.

Đề nghị: mặc định 3.0, cho chỉnh trong bản tùy chọn. Cần hỏi lại khi bắt đầu phase này.

## Xong khi

- Test xanh hết, kể cả test Beam cũ
- Bốn tường khép phòng cho ra bốn trục gặp nhau ở góc
- Không đụng gì tới hành vi tab Beam

## Rủi ro

| Rủi ro | Cách giảm |
|---|---|
| Tách chung làm hỏng tab Beam | Chạy test Beam sau mỗi bước tách |
| Giao tường phức tạp hơn dự tính | Làm hai tường trước, chữ T sau, chữ thập sau nữa |
| Nhận nhầm hai dầm song song thành tường | Giới hạn bề dày; và tường thường khép thành phòng |
