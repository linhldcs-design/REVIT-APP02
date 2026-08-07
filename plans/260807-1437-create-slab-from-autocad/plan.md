---
title: "Tạo Sàn Revit từ AutoCAD"
description: "Thêm tab Create Slab vào Model From CAD, dựng đường bao kín từ line CAD, nhận ký hiệu X làm lỗ mở, preview 2D/3D và tạo Floor theo Level cùng offset."
status: in-progress
priority: P1
effort: "5-7 ngày"
branch: "main"
tags: [feature, revit, autocad, floor, slab, wpf]
blockedBy: []
blocks: []
created: 2026-08-07
mode: hard
relatedPlans: [260805-1634-create-beam-from-autocad, 260805-1439-create-grid-column-from-autocad]
---

# Tạo Sàn Revit từ AutoCAD

## Vì sao sàn khác dầm và cột

Đây là điểm quyết định toàn bộ thiết kế, không phải chi tiết kỹ thuật.

| | Dầm | Cột | **Sàn** |
| --- | --- | --- | --- |
| Hình học CAD | 2 biên song song | 1 rectangle | **nhiều line rời tạo vùng** |
| Kết quả | tim → curve | tâm + b/h | **đường bao kín → mặt** |
| Sai số cho phép | tim lệch vài mm vẫn tạo được | tâm lệch vẫn tạo được | **hở 1 mm là `Floor.Create` ném lỗi** |

`Floor.Create` yêu cầu `CurveLoop` khép kín tuyệt đối và không tự giao. Bản vẽ thực tế thì line bị trim, hở, chồng nhau, thừa đuôi. **Dựng loop kín từ line rời là phần khó nhất và sẽ chiếm phần lớn công sức**, không phải phần tạo Floor.

Kinh nghiệm từ tab Create Beam: mọi lỗi mất dầm đều nằm ở khâu đọc và ghép hình học, không nằm ở khâu gọi Revit API. Sàn sẽ lặp lại đúng như vậy nhưng khắt khe hơn.

## Quyết định đã chốt với người dùng

1. **Nhận biên**: tự dựng loop từ line rời do người dùng quét. Không bắt buộc bản vẽ có sẵn polyline kín.
2. **Nguồn biên**: quét hai bước như tab Beam — `1. Select Grid Axes` rồi `2. Select Slab Lines`. Không lấy biên từ dầm đã tạo trong Revit.
3. **Chiều dày**: đọc từ **TEXT/MTEXT nằm trong vùng**, dạng `Hs=100`. Người dùng chỉ chọn family sàn trong Settings; chiều dày lấy theo nhãn của từng vùng. Vùng không có nhãn dùng chiều dày mặc định trong Settings và được đánh dấu để người dùng kiểm.
4. **Cao độ**: đọc từ **TEXT/MTEXT trong vùng**, dạng `+0.000`, `-0.050`, `-0.100` (đơn vị mét). Một `Level` chung chọn trong bảng tùy chọn làm mốc; cao độ đọc được quy ra `Offset` so với Level đó. Vùng không có nhãn dùng `Offset` mặc định trong Settings và được đánh dấu để người dùng kiểm. `Offset` của từng tấm vẫn sửa được trong bảng review.
5. **Lỗ mở**: nhận theo **ký hiệu X** — ô nào có hai đoạn chéo cắt nhau thì không đổ sàn. Nhận theo hình học, **không phụ thuộc layer**, vì quy ước layer khác nhau giữa các bản vẽ.
6. **Vùng HATCH là sàn hạ cao độ**: add-in đọc HATCH để tự đánh dấu vùng nào là sàn âm, thay vì để người dùng dò tay từng ô.

## Luồng người dùng

```text
Revit: mở Model From CAD -> tab Create Slab
  -> 1. Select Grid Axes + pick source anchor
  -> 2. Select Slab Lines (LINE/polyline/block)
  -> chuẩn hóa unit, snap endpoint, dựng đồ thị phẳng
  -> tìm mọi mặt kín nhỏ nhất (bounded face)
  -> loại mặt có ký hiệu X -> đánh dấu là lỗ mở
  -> đọc HATCH -> đánh dấu ô thuộc sàn hạ cao độ
  -> gộp các ô kề nhau cùng cao độ thành TẤM SÀN LIỀN
  -> preview 2D: tô màu từng tấm, gạch chéo vùng lỗ
  -> người dùng tick chọn tấm cần đổ, sửa Offset từng tấm
  -> chọn Floor Type, Level, Offset
  -> Revit: pick điểm móc đích
  -> preflight: loop kín, không tự giao, diện tích hợp lệ
  -> transaction: Floor.Create cho từng vùng đã chọn
  -> báo Created / Existing / Skipped / Failed
```

## Kiến trúc

### Contract mới

- `CadSlabRegionCandidate`: loop ngoài, danh sách loop trong (lỗ), diện tích, tâm, source segment IDs, trạng thái, cờ `IsOpening`, cờ `IsLowered` và `OffsetMm` sửa được cho từng vùng.
- `CadSlabAnalysis`: danh sách vùng, số line bị bỏ, số hatch không khớp vùng nào, cảnh báo, lỗi.
- `CadHatchRegion`: đường bao hatch đã chuyển sang mm, dùng để phân loại vùng sàn hạ.
- Dùng lại `CadStructureTransferPackage` và `CadBeamAcquisitionSession` (grid scan + slab scan cùng drawing, cùng INSUNITS, cùng source anchor).

### Dựng vùng kín từ line rời

Đây là phần cốt lõi. Thuật toán theo bốn bước:

1. **Chuẩn hóa**: đổi sang mm, bỏ segment ngắn hơn ngưỡng, tách mọi segment tại **giao điểm** với segment khác.
2. **Snap endpoint**: gom các đầu mút cách nhau dưới `Vertex Snap` (đề xuất mặc định `20 mm`) về một nút chung. Đây là bước quyết định — line CAD gần như không bao giờ chạm nhau chính xác.
3. **Dựng đồ thị phẳng**: mỗi nút giữ danh sách cạnh, sắp theo góc.
4. **Duyệt mặt**: đi theo quy tắc "rẽ trái nhất" (next counter-clockwise edge) để lấy **mọi mặt bị bao kín nhỏ nhất**. Mặt vô hạn bên ngoài bị loại theo dấu diện tích.

Cách này cho ra từng ô nhỏ nhất giữa các dầm mà không cần người dùng click, và tự nhiên xử lý được line thừa đuôi (đuôi cụt không thuộc mặt nào). Nhưng ô nhỏ nhất **không phải** kết quả cuối cùng — xem bước gộp dưới đây.

### Gộp các ô thành tấm sàn liền

Sàn được đổ liền khối, không phải mỗi ô giữa bốn dầm một tấm riêng. Tạo sàn rời rạc từng ô sẽ sinh hàng chục element chồng mép, sai thực tế thi công và khó chỉnh sửa về sau.

**Cao độ và chiều dày đọc từ text là khoá gộp.** Mọi ô liên thông cùng cao độ và cùng chiều dày cho ra một tấm; khác một trong hai thì tách.

```text
 +0.000  +0.000  +0.000  -0.050        ┌──────────────┬───────┐
  Hs=100  Hs=100  Hs=100   Hs=100      │              │       │
 +0.000  +0.000  +0.000  -0.050   =>   │  SÀN +0.000  │ -0.050│
  Hs=100  Hs=100  Hs=100   Hs=100      │    Hs=100    │ Hs=100│
                                       └──────────────┴───────┘
                                          1 Floor       1 Floor
```

Sau khi có các ô nhỏ nhất, gộp chúng lại:

1. Hai ô **kề nhau** (dùng chung ít nhất một cạnh) được gộp khi **cùng nhóm cao độ** và **cùng Floor Type**.
2. Cạnh chung giữa hai ô cùng nhóm bị **loại khỏi biên**; biên ngoài của cả cụm trở thành loop của tấm sàn.
3. Ô là lỗ mở (ký hiệu X) không tham gia gộp. Nếu nó nằm lọt trong cụm thì trở thành loop lỗ của tấm đó.
4. Vùng `LoweredSlab` gộp riêng với nhau, tách khỏi sàn cao độ thường — vì khác cao độ thì phải là hai tấm.
5. Kết quả: mỗi cụm liên thông cùng cao độ cho ra **đúng một** `Floor`, có thể lõm, chữ L, chữ U hoặc có lỗ bên trong.

Thuật toán: union-find trên các ô, khoá theo `(cao độ, chiều dày)`; sau đó dựng biên cụm bằng cách giữ lại các cạnh chỉ thuộc **một** ô trong cụm (cạnh biên) và bỏ cạnh thuộc hai ô (cạnh trong). Nối các cạnh biên thành loop khép kín, phân biệt loop ngoài và loop lỗ theo dấu diện tích.

### Nối qua dầm ngăn giữa

Sàn đổ liền qua dầm — dầm nằm trong lòng sàn, không cắt sàn làm đôi. Nhưng dầm được vẽ bằng hai mép, nên nó tạo ra một **dải hẹp** giữa hai ô sàn, và hai ô đó không dùng chung cạnh nào. Union-find theo cạnh chung sẽ tách chúng thành hai tấm — sai.

Quy tắc bổ sung: hai ô cùng cao độ và cùng chiều dày được nối khi giữa chúng chỉ có **một dải hẹp** ngăn cách.

1. Ô nào có bề rộng nhỏ hơn `Bề rộng dầm tối đa` (đề xuất `500 mm`) và dài, nằm kẹp giữa hai ô sàn, được xem là **dải dầm** chứ không phải ô sàn độc lập.
2. Dải dầm không tạo tấm riêng. Nó được **hấp thụ vào tấm** hai bên nếu hai bên cùng cao độ và cùng chiều dày.
3. Khi đã hấp thụ, hai ô hai bên thuộc cùng cụm; cạnh mép dầm trở thành cạnh trong và biến mất khỏi biên. Kết quả là một tấm liền chạy qua dầm.
4. Nếu hai bên **khác cao độ** thì dải dầm là ranh giới thật: mỗi bên giữ biên riêng, và dải được gán cho bên nào thì theo lựa chọn tim/mép ở phần dưới.
5. Dải dầm nằm ở rìa, chỉ có sàn một bên, được hấp thụ vào bên đó.

Quy tắc này áp cho **mọi cao độ**, không riêng sàn âm: hai vùng `-0.050` bị dầm chắn giữa nối thành một tấm `-0.050`, cũng như hai vùng `+0.000` nối thành một tấm `+0.000`.

Bảng review hiển thị số ô đã gộp và số dải dầm đã hấp thụ, để người dùng thấy tấm được dựng từ đâu.

Bảng review vì thế liệt kê **tấm sàn**, không liệt kê ô. Mỗi dòng hiển thị số ô đã gộp để người dùng đối chiếu.

Người dùng vẫn có thể **tách một ô ra khỏi tấm** bằng cách đổi Offset riêng cho ô đó — khi Offset khác đi, ô tự động rời cụm và thành tấm riêng.

### Nhận ký hiệu X làm lỗ mở

- Với mỗi vùng kín, tìm các segment **nằm trong** vùng đó (không thuộc biên).
- Ô được đánh dấu lỗ khi có **đúng hai đoạn chéo cắt nhau gần tâm vùng**, mỗi đoạn nối gần hai góc đối diện, và giao điểm nằm trong khoảng dung sai quanh tâm.
- Không dùng layer làm điều kiện bắt buộc. Nếu người dùng chỉ định layer lỗ mở trong Settings thì dùng làm tín hiệu tăng độ tin cậy.
- Vùng bị đánh dấu lỗ mặc định **không tick**, hiển thị riêng màu trong preview và ghi rõ lý do trong bảng.
- Người dùng vẫn có thể tick tay để đổ sàn ở vùng đó nếu muốn.

### Đọc chiều dày từ nhãn trong vùng

Mặt bằng ghi chiều dày ngay trong ô sàn, nên chiều dày phải lấy từ đó thay vì bắt người dùng gán tay cho từng tấm.

- Lần quét Slab Lines nhận cả `TEXT` và `MTEXT`, chuẩn hóa mã điều khiển MTEXT như tab Beam đã làm.
- Parser nhận cả nhãn có tiền tố và **số trần**: `Hs=100`, `Hs = 120`, `HS200`, `h=100`, `S120`, `SÀN DÀY 100`, và `100` / `120` / `200` đứng một mình. Kèm hoặc không kèm `mm`; dấu `,` và `.` đều là dấu thập phân.
- **Số trần chỉ được nhận khi nằm trong dải chiều dày hợp lệ** (đề xuất `50–500 mm`). Mặt bằng đầy số không phải chiều dày — cao độ `-0.050`, khoảng trục `3950`, tên trục `1`–`8`, kích thước `1550` — nên không có dải này thì mọi con số trong ô đều thành chiều dày.
- Số có dấu âm, số thập phân kiểu cao độ (`0.00`, `-0.050`), và số nằm trên cùng dòng với ký tự đơn vị khác (`m`, `%`) không được nhận là chiều dày.
- Nhãn có tiền tố `Hs`/`h`/`S` được ưu tiên hơn số trần khi một vùng có cả hai.
- Nhãn thuộc về vùng **chứa điểm chèn của nó**. Đây là quy tắc chính vì bản vẽ đặt nhãn giữa ô, không đặt ngoài mép như nhãn dầm.
- Nhãn nằm ngoài mọi vùng thì tìm vùng gần nhất trong `Text Search`; nếu vẫn không rõ ràng thì bỏ qua kèm cảnh báo, không đoán.
- Một vùng có **nhiều nhãn khác giá trị** trả trạng thái `AmbiguousThickness`, không tự chọn, mặc định không tick.
- Vùng **không có nhãn** dùng chiều dày mặc định trong Settings, trạng thái `MissingThickness`, vẫn tick được nhưng tô màu riêng để người dùng biết đây là giá trị suy ra chứ không phải đọc được.

**Chiều dày là khoá gộp thứ hai.** Hai ô kề nhau chỉ gộp khi cùng cao độ **và** cùng chiều dày. Ô `Hs=100` và ô `Hs=150` nằm cạnh nhau vẫn là hai tấm sàn riêng, đúng như hai cao độ khác nhau.

### Đọc cao độ từ nhãn trong vùng

Mặt bằng ghi cao độ ngay trong ô, nên cao độ lấy từ đó thay vì để người dùng gán tay từng tấm.

- Parser nhận dạng cao độ: `+0.000`, `0.000`, `-0.050`, `-0.100`, `±0.000`, kèm hoặc không kèm ngoặc. Dấu `,` và `.` đều là dấu thập phân.
- **Đơn vị của cao độ là mét**, khác chiều dày tính bằng mm. Đây là quy ước bản vẽ kết cấu: `-0.050` nghĩa là hạ 50 mm.
- Phân biệt với chiều dày bằng **dạng viết**, không bằng giá trị: cao độ luôn có phần thập phân ba chữ số và/hoặc dấu `+`/`-` đứng trước; chiều dày là số nguyên trong dải hợp lệ. `100` là chiều dày, `-0.100` là cao độ, không nhầm nhau.
- Nhãn thuộc về vùng chứa điểm chèn của nó, cùng quy tắc với nhãn chiều dày.
- Cao độ đọc được quy sang `Offset` so với `Level` đã chọn: `Offset = cao_độ_text × 1000 − cao_độ_Level`. Bảng review hiển thị cả hai để người dùng đối chiếu.
- Một vùng có nhiều nhãn cao độ khác nhau trả `AmbiguousElevation`, không tự chọn, mặc định không tick.
- Vùng không có nhãn cao độ dùng `Offset` mặc định trong Settings, trạng thái `MissingElevation`, tô màu riêng.

**Cao độ đọc từ text là khoá gộp chính.** Mọi ô có `+0.000` liên thông cho ra một tấm; mọi ô có `-0.050` liên thông cho ra tấm khác. Nhãn HATCH chỉ còn là tín hiệu phụ giúp xác nhận vùng nào bị hạ khi thiếu nhãn cao độ.

### Ánh xạ chiều dày sang FloorType

- Người dùng chọn một family sàn gốc trong Settings.
- Với mỗi chiều dày đọc được, tìm `FloorType` hiện có trong project có đúng chiều dày đó trong dung sai `1 mm`; ưu tiên type cùng family.
- Chưa có thì duplicate type gốc và đặt chiều dày lớp kết cấu theo giá trị đọc được, tên xác định theo chiều dày, ví dụ `Sàn BTCT 100`.
- Nếu type gốc có nhiều lớp, chỉ sửa lớp `Structure` và giữ nguyên các lớp còn lại; type nhiều lớp mà không xác định được lớp kết cấu thì báo lỗi và không tạo, thay vì đoán.
- Bảng review hiển thị chiều dày đọc được và tên type sẽ dùng, để người dùng thấy trước khi tạo.

### Nhận sàn hạ cao độ theo HATCH

Mặt bằng đánh dấu vùng sàn hạ bằng HATCH thay vì ghi chú, nên vùng đó phải được nhận ra trước khi người dùng phải tự dò.

- Đọc `AcDbHatch` trong lần quét Slab Lines: lấy đường bao của từng loop hatch, chuyển sang WCS và mm như mọi hình học khác.
- Một vùng kín được đánh dấu `LoweredSlab` khi tâm của nó nằm trong một hatch, hoặc khi hatch phủ phần lớn diện tích vùng.
- Vùng `LoweredSlab` vẫn là sàn và vẫn được tick mặc định; điểm khác là nó được tô màu riêng trong preview và **ô Offset của nó được làm nổi bật** để người dùng nhập cao độ hạ.
- Giá trị hạ mặc định lấy từ ô `Offset sàn hạ` trong Settings (đề xuất `-50 mm`), người dùng sửa lại từng vùng nếu cần.
- Hatch chỉ dùng để **phân loại vùng**, không dùng làm biên sàn. Biên luôn lấy từ line như phần trên, vì đường bao hatch thường không trùng tim/mép dầm.
- Hatch không nằm trong vùng kín nào thì bỏ qua kèm cảnh báo, không tự tạo sàn từ riêng hatch.

### Cao độ theo từng vùng

- Settings có `Level` áp cho mọi sàn và `Offset` mặc định.
- Bảng review có cột `Offset` sửa được cho **từng vùng**, cho phép số âm.
- Sàn âm, sàn WC, ban công xử lý bằng cách nhập Offset riêng, không cần quét lại lần hai.
- `Reset` trả Offset của một vùng về mặc định theo phân loại của nó (`0` cho sàn thường, giá trị sàn hạ cho vùng `LoweredSlab`).
- Cột `Offset` hiển thị cả cao độ tuyệt đối đã tính để người dùng đối chiếu trước khi tạo.

### Lỗ mở lồng trong sàn lớn

Khi một vùng lỗ nằm hoàn toàn bên trong một vùng sàn được chọn, loop của nó được đưa vào `Floor.Create` như `CurveLoop` phụ, thay vì tạo hai sàn rời. Điều này giữ đúng ý nghĩa kết cấu và tránh sàn chồng mép.

### Người dùng luôn ghi đè được giá trị đọc từ CAD

Giá trị đọc từ bản vẽ là **điểm khởi đầu**, không phải ràng buộc. Bản vẽ có thể ghi thiếu, ghi sai hoặc người dùng muốn làm khác.

**Trong bảng tùy chọn (Settings)** — áp cho toàn bộ:
- `Level` mốc, `Chiều dày mặc định`, `Offset mặc định`, family sàn gốc.
- `Ghi đè chiều dày` và `Ghi đè cao độ`: khi bật, mọi tấm dùng giá trị trong Settings và **bỏ qua nhãn CAD**. Dùng khi bản vẽ ghi không đáng tin hoặc muốn đổ đồng loạt một cao độ.

**Trong bảng review** — sửa từng tấm:
- `h Tạo` và `Offset` sửa trực tiếp; giá trị CAD vẫn hiển thị read-only bên cạnh để đối chiếu.
- Sửa `Offset` một tấm khiến nó rời cụm cũ và thành tấm riêng, vì cao độ là khoá gộp.
- Tấm đã sửa tay mang trạng thái `ManualOverride` và tô màu riêng, để không lẫn với giá trị đọc được.
- `Reset CAD/Text` trả tấm về đúng giá trị trong bản vẽ.

Thứ tự ưu tiên: **sửa tay từng tấm** > **ghi đè trong Settings** > **nhãn đọc từ CAD** > **mặc định trong Settings**.

### Bước Review trước khi tạo

Giống hệt tab Create Beam, và bắt buộc trước khi bấm Tạo Sàn.

**Preview**
- Chuyển `2D / 3D`; 3D dựng khối sàn theo chiều dày và đúng cao độ từng tấm, thấy rõ tấm nào bị hạ.
- Zoom bằng con lăn, pan, orbit khi ở 3D, `Vừa màn hình`.
- Overlay lưới CAD làm nền; bật/tắt `Lưới` và `Nhãn`.
- Màu riêng cho: sàn thường, sàn hạ cao độ, lỗ mở, vùng cảnh báo, vùng bỏ chọn.
- Click một tấm trên canvas chọn đúng dòng trong bảng và ngược lại.

**Bảng review** — mỗi dòng là **một tấm sàn**, không phải một ô:

| Cột | Ý nghĩa |
| --- | --- |
| `Tạo` | tick chọn tạo hay không |
| `Số ô` | số ô nhỏ đã gộp thành tấm này, để đối chiếu |
| `Diện tích` | m², tính từ loop ngoài trừ các lỗ |
| `h CAD` | chiều dày đọc được từ text, read-only |
| `h Tạo` | chiều dày sẽ dùng, sửa được |
| `Cao độ CAD` | cao độ đọc được từ text (`+0.000`, `-0.050`), read-only |
| `Offset` | Offset so với Level, quy từ cao độ CAD, sửa được |
| `Type` | tên FloorType sẽ dùng hoặc sẽ duplicate |
| `Lỗ` | số lỗ mở bên trong tấm |
| `Trạng thái` | Ready / MissingThickness / AmbiguousThickness / OpenLoop / CurvedEdge |
| `Text` | nội dung nhãn đã đọc |

**Thao tác**: `Chọn tất cả`, `Bỏ chọn`, `Reset CAD/Text` trả `h Tạo` và `Offset` về giá trị đọc từ bản vẽ.

**Cảnh báo dưới bảng**: số line bị bỏ, số nút chưa khép thành vùng, số hatch không khớp vùng nào, kèm tên thông số đang chặn — như dòng cảnh báo đã thêm cho tab Beam.

Review chỉ là hình học WPF, không tạo element Revit nào cho tới khi bấm Tạo Sàn.

### Tạo Floor trong Revit

- Lọc `FloorType` thuộc `OST_Floors`; hiển thị chiều dày để người dùng đối chiếu.
- `Floor.Create(doc, IList<CurveLoop>, floorTypeId, levelId)` — loop đầu là biên ngoài, các loop sau là lỗ.
- Gán `FLOOR_HEIGHTABOVELEVEL_PARAM` từ ô Offset.
- Preflight bắt buộc trước transaction: loop kín trong dung sai Revit, không tự giao, diện tích lớn hơn ngưỡng tối thiểu, mọi curve hợp lệ.
- Duplicate key: tập đỉnh loop ngoài không phân biệt thứ tự + level + offset + type, dung sai `1 mm`.
- Chạy lại cùng dữ liệu không tạo sàn trùng.

## Phạm vi

### Trong scope V1

- Sàn phẳng, biên thẳng, nằm ngang.
- Lỗ mở nhận theo ký hiệu X và lỗ lồng trong sàn.
- Sàn hạ cao độ nhận theo HATCH; Offset sửa được cho từng vùng.
- Preview 2D tô vùng; preview 3D dựng khối sàn theo chiều dày và đúng cao độ từng vùng.
- Floor Type và Level chọn chung; Offset chung có thể ghi đè theo từng vùng.
- Duplicate detection, transaction rollback, một lần Undo cho cả batch.
- Revit 2022–2027, smoke chính trên R25.

### Ngoài scope V1

- Biên cong (arc, spline, bo góc) — sẽ báo cảnh báo và bỏ vùng đó.
- Sàn dốc, sàn nhiều cao độ trong một vùng, sub-element point.
- Đọc chiều dày từ TEXT trong CAD.
- Nhận vùng theo HATCH (xem Câu hỏi cần chốt).
- Span direction, shape editing, structural deck.

## Các phase

| Phase | Tên | Effort | Depends On |
| --- | --- | --- | --- |
| 00 | Contract sàn và baseline test | 0.5 ngày | [] |
| 01 | Quét Slab Lines và chuẩn hóa hình học | 1 ngày | [00] |
| 02 | Dựng đồ thị phẳng và duyệt vùng kín | 1.5-2 ngày | [01] |
| 03 | Nhận ký hiệu X, HATCH sàn hạ và lỗ lồng | 1 ngày | [02] |
| 04 | Gộp ô thành tấm sàn liền và dựng biên cụm | 1-1.5 ngày | [02, 03] |
| 05 | Tab Create Slab, preview 2D/3D, Offset từng tấm | 1-1.5 ngày | [04] |
| 06 | Tạo Floor, duplicate, rollback | 1 ngày | [04, 05] |
| 07 | Test, build đa phiên bản, smoke | 1 ngày | [06] |

## Touchpoints dự kiến

| Khu vực | File |
| --- | --- |
| Contract/core | `RevitAPP.Core/Models/CadStructure/CadSlabModels.cs`, `RevitAPP.Core/Services/CadSlabAnalyzer.cs` |
| Đồ thị phẳng | `RevitAPP.Core/Services/CadPlanarGraph.cs` |
| AutoCAD | `RevitAPP/Services/CadStructure/AutoCadModelSelectionService.cs` |
| Revit | `RevitAPP/Services/CadStructure/CadSlabCreationService.cs` |
| ViewModel | `RevitAPP/ViewModels/ModelFromCadViewModel.cs`, `CadSlabRowViewModel.cs` |
| View | `RevitAPP/Views/ModelFromCadWindow.xaml(.cs)` |
| Tests | `tests/RevitAPP.Tests/CadSlabAnalyzerTests.cs` |

## Rủi ro và giảm thiểu

- **Critical — line hở vài mm không tạo được vùng**: snap endpoint trước khi dựng đồ thị; `Vertex Snap` hiển thị trong Settings để chỉnh; báo rõ số nút chưa khép.
- **Critical — `Floor.Create` ném lỗi vì loop không kín hoặc tự giao**: preflight từng loop trước transaction; vùng không đạt bị đánh dấu không tạo được kèm lý do, không làm hỏng cả batch.
- **High — nhận nhầm ô lưới/vùng kiến trúc thành sàn**: giới hạn diện tích tối thiểu/tối đa cấu hình được; preview bắt buộc; mặc định chỉ tick vùng đạt mọi gate.
- **High — sàn bị dầm cắt thành nhiều tấm rời**: dải hẹp giữa hai ô cùng cao độ được hấp thụ vào tấm thay vì thành tấm riêng; `Bề rộng dầm tối đa` cấu hình được vì dầm rộng hơn ngưỡng sẽ bị hiểu thành ô sàn. Test bắt buộc: hai vùng sàn âm hai bên một dầm phải ra đúng một tấm.
- **Medium — hấp thụ nhầm hành lang hẹp thành dầm**: dải chỉ được hấp thụ khi hẹp **và** dài **và** kẹp giữa hai ô cùng cao độ; hành lang thật thường rộng hơn `500 mm`. Vùng nghi ngờ hiển thị riêng trong preview để người dùng kiểm.
- **High — bỏ sót lỗ mở dẫn tới sàn đè lên ô thang**: ký hiệu X mặc định không tick và tô màu riêng; hiển thị số vùng bị nhận là lỗ để người dùng đối chiếu.
- **High — hiệu năng khi vùng quét lớn**: tách giao điểm bằng spatial index thay vì so từng cặp; giới hạn số segment; chỉ render vùng đang hiển thị.
- **Medium — vùng lồng nhau nhiều cấp**: xác định quan hệ chứa bằng point-in-polygon trên tâm; chỉ hỗ trợ một cấp lỗ trong V1, cấp sâu hơn báo cảnh báo.
- **Medium — biên cong**: phát hiện bulge/arc và loại vùng đó kèm cảnh báo, không xấp xỉ thành đoạn thẳng để tránh sai diện tích.
- **Đã biết từ tab Beam — đọc COM bỏ sót entity**: giữ nguyên cách đã sửa (chỉ release COM sau khi đọc xong toàn bộ selection). Không quay lại release trong vòng lặp.

## Verification

- Pure test cho: snap endpoint, tách giao điểm, duyệt mặt kín, nhận X, quan hệ lồng nhau, duplicate key.
- Test hình học thật: ô chữ nhật đơn, dãy ô liền kề dùng chung biên, ô có đuôi thừa, ô hở 5/10/50 mm, ô có X, ô lồng trong ô.
- `dotnet test tests/RevitAPP.Tests/RevitAPP.Tests.csproj -c Release` đạt, không regression Grid/Column/Beam.
- Build `Release.R22` đến `Release.R27` với deploy/launch tắt.
- R25 smoke: quét trục, quét line sàn, kiểm vùng nhận được so với bản vẽ, tạo sàn, kiểm Level/Offset/Type trong Properties, chạy lại chống trùng, Undo, Cancel.

## Câu hỏi cần người dùng chốt

1. **Ở rìa ngoài công trình và ở ranh giới hai cao độ, sàn dừng ở đâu?** Dầm giữa lòng sàn đã được xử lý bằng quy tắc hấp thụ dải dầm ở trên, nên chỉ còn hai chỗ cần quyết:
   - **Rìa ngoài**: sàn dừng ở mép ngoài dầm biên, mép trong, hay tim dầm biên? Đề xuất **mép ngoài**, vì sàn thường phủ hết dầm biên.
   - **Ranh giới hai cao độ**: dải dầm giữa vùng `+0.000` và vùng `-0.050` thuộc về bên nào? Đề xuất chia tại **tim dầm**, mỗi bên nhận một nửa.
2. **Dải chiều dày hợp lệ cho số trần?** Đề xuất `50–500 mm`. Cần dải này để `100`, `120`, `200` được nhận là chiều dày còn `3950` (khoảng trục) hay `1550` (kích thước) thì không. Nếu dự án có sàn ngoài dải này, cho biết để nới.
3. **`Vertex Snap` mặc định bao nhiêu?** Đề xuất `20 mm`. Quá nhỏ thì line hở không khép thành vùng; quá lớn thì hai ô sát nhau bị dính làm một.
4. **Diện tích tối thiểu để coi là một ô sàn?** Đề xuất `1 m²`; nhỏ hơn xem là ô kỹ thuật và mặc định không tick.
5. **Giá trị hạ mặc định cho vùng HATCH?** Đề xuất `-50 mm`, sửa được cho từng tấm trong bảng.

## Cổng tiếp theo

Người dùng trả lời bốn câu trên, đặc biệt câu 1 về tim/mép dầm vì nó quyết định hình học đầu ra. Sau khi chốt mới viết các file phase và chuyển sang implementation.
