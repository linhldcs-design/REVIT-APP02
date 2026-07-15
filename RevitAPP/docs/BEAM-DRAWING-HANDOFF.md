# Beam Drawing (Triển khai bản vẽ dầm) — Hand-off cho AI/người tiếp theo

## Chẩn đoán live 2026-07-02 — DIM 0 chưa được test bằng DLL mới

- MCP kết nối được với Revit; active sheet là `MẶT BẰNG MÓNG` (ViewId `474239`). Hai viewport đang chọn có ID `1921296` và `1921306`, tương ứng cặp view GỐI/NHỊP `(8)` user đang kiểm tra.
- `send_code_to_revit` bị timeout kể cả khi chỉ đọc Dimension tối giản; không gửi thêm truy vấn động để tránh làm Revit lag. Các lệnh MCP dựng sẵn vẫn phản hồi ngay.
- Bằng chứng quyết định từ module đã nạp trong PID Revit `11292` (khởi động 16:51:34): `RevitAPP.dll` đang chạy từ `%APPDATA%\Autodesk\Revit\Addins\2025\RevitAPP\RevitAPP.dll`, SHA-256 `4E0DBDF14A9240A6B273E5148EF50819AE458EE65C6E3AE33B621E858B73B83D`, timestamp 16:49:02.
- DLL có fix loại segment hiển thị `0` là `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP-no-zero-dim-v2.dll`, SHA-256 `CFA132F700C0D9A2176ACF6105F39F1C15524ECF4C1517BF89A9457264AC3057`, timestamp 17:40:46.
- Kết luận: view `(8)` vẫn được tạo bằng assembly cũ đã auto-load khi Revit khởi động. Add-In Manager không thay thế được assembly identity `RevitAPP` đã nạp. Phải đóng Revit hoàn toàn, deploy DLL hash `CFA132...` vào đúng đường dẫn AppData, mở lại Revit rồi tạo cặp view mới trước khi đánh giá thuật toán.
- 2026-07-02 17:51: user đã đóng Revit; xác nhận không còn process `Revit`. Đã sao lưu DLL cũ thành `RevitAPP.dll.backup-20260702-175123`, rồi deploy bản fix vào `%APPDATA%\Autodesk\Revit\Addins\2025\RevitAPP\RevitAPP.dll`. Hash tại đích đã verify đúng `CFA132F700C0D9A2176ACF6105F39F1C15524ECF4C1517BF89A9457264AC3057` (2,712,576 bytes). Bước smoke bắt buộc: mở lại Revit và tạo cặp GỐI/NHỊP mới; không dùng view `(8)` cũ để kết luận.
- 2026-07-03 09:07: smoke bản `CFA132...` xác nhận đã bỏ segment hiển thị `0`, nhưng API cũ `doc.Create.NewDimension` báo `Invalid number of references` sau khi đã tạo dim rộng/một phần dim đứng. Đã chuyển riêng cross dimensions sang API Revit 2025 `LinearDimension.Create(Document, View, Line, IList<Reference>)`, sau đó áp DimensionType bằng `ChangeTypeId`. Mỗi dim `rộng` / `chuỗi đứng` / `tổng chiều cao` được try/catch độc lập và warning ghi rõ nhãn + số reference; một dim lỗi không còn làm cả view trả về 0. Thêm tùy chọn build `RevitAPPAssemblyName` để tạo identity riêng cho Add-In Manager khi `RevitAPP` auto-load đang chạy. Tests **121/121 pass**, Debug.R25 và Release.R25 **0 error**. Bản smoke Add-In Manager: `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.BeamDimFixTest.dll`, assembly identity `RevitAPP.BeamDimFixTest`, 2,714,112 bytes, SHA-256 `E557B245723C78BE462FAB0058B263E9BF593887DE3EE4EBC02E14BB68C0F858`. Chưa deploy AppData; chờ user smoke.
- Theo yêu cầu giữ workflow cũ, bản smoke trên đã được ghi đè thành `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`; file `RevitAPP.BeamDimFixTest.dll` đã xóa. Tên file là cũ nhưng identity bên trong vẫn là `RevitAPP.BeamDimFixTest` để Add-In Manager không dùng nhầm assembly `RevitAPP` đang cache. Hash `E557B245723C78BE462FAB0058B263E9BF593887DE3EE4EBC02E14BB68C0F858`.
- 2026-07-03 09:15: ảnh smoke phóng lớn xác nhận NHỊP vẫn có segment hiển thị `0` ở khe rất nhỏ trên đỉnh; số thực vượt tolerance 10mm nhưng bị Dimension Type của project làm tròn về 0. Fix nguồn: sau khi tạo dim chuỗi bằng `LinearDimension.Create`, gọi `Regenerate`, đọc `DimensionSegment.ValueString`; nếu Revit thực sự hiển thị `0`, xóa dim tạm, bỏ reference nội bộ gây đoạn đó nhưng luôn giữ reference biên dưới/biên trên, rồi tạo lại. Thêm helper/test `ReferenceIndexToRemoveForZeroSegment`; tests **124/124 pass**, Debug/Release R25 **0 error**. Bản Add-In Manager tiếp tục dùng đúng đường dẫn cũ `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`, identity nội bộ mới `RevitAPP.BeamDimFixTest2`, 2,715,136 bytes, SHA-256 `62D15E4483C1EFEA1B4174CBA8E0765E557CF47A8BDADEB0CA384187A53C5744`. Chưa deploy AppData; chờ smoke view mới.
- 2026-07-03 tiếp theo: Test2 vẫn còn dim `0`. Root cause sâu hơn nằm ở mapping reference→cao độ: `GetCrossFaceReferences` đã sort mặt ngang theo `PlanarFace.Origin.Z` nhưng sau đó làm mất cao độ này và gán lại `pair.Geometry.TopZFeet/BottomZFeet` từ bounding box. Khi dầm join sàn, reference mặt thật có thể trùng sàn trong khi cao độ giả khác nhau, nên pre-filter giữ cả hai và Revit đo ra 0. Đã đổi `CrossReferences.Bottom/Top` thành `FaceLevel` mang cùng reference + Z thật của chính PlanarFace; clustering giờ làm việc trên đúng hình học. Cơ chế hậu kiểm `ValueString=0` vẫn giữ làm lớp phòng vệ. Tests **124/124 pass**, Debug/Release R25 **0 error**. Phát hành đúng file cũ `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`, identity `RevitAPP.BeamDimFixTest3`, 2,715,136 bytes, SHA-256 `789349FBBB88CDC630FA918D1152E6770AE9F3028C57D10101FEF0A4405D1C5F`. Chưa deploy AppData; chờ smoke view mới.
- 2026-07-03 Spot Elevation: user xác nhận dim 0 đã hết, nhưng ký hiệu cao độ biến mất. Journal chứng minh SpotElevation được tạo rồi mất reference và Revit tự xóa ở cuối transaction (`SpotElevation element lost reference to Structural Framing ...`; `One or more Spot Dimension references are no longer valid`). Root cause là `SpotElevationPlacer` dùng `new Reference(pair.Beam)` (element reference) và `refPt` dựng từ CropBox, không bảo đảm nằm trên geometry. Đã thay bằng reference của top `PlanarFace` thật tại đúng station; project điểm gần mép trái (lùi 2mm vào face), dùng chính projection làm origin/refPt, leader theo `View.RightDirection`, rồi regenerate để phát hiện lỗi ngay. Tests **124/124 pass**, Debug/Release R25 **0 error**. Phát hành đúng file cũ `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`, identity `RevitAPP.BeamDimFixTest4`, 2,719,744 bytes, SHA-256 `783278B28A5C7B6BE3FB345F6C348C906522E0EC36AF9B3E2259B24504039187`. Chưa deploy AppData; chờ smoke view mới.
- 2026-07-03 Spot Elevation Test4: GỐI tìm được top face nhưng NHỊP cảnh báo không tìm thấy vì khi top dầm trùng top sàn, join đã consume hoàn toàn mặt trên riêng của beam. Đã thêm fallback có điều kiện: chỉ lấy top `PlanarFace` của Floor cắt đúng station nếu cao độ mặt sàn trùng `pair.Geometry.TopZFeet` trong tolerance 10mm; sàn hạ cốt không được dùng fallback nên vẫn giữ nguyên tắc cao độ dầm. Tests **124/124 pass**, Debug/Release R25 **0 error**. Phát hành đúng file cũ `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`, identity `RevitAPP.BeamDimFixTest5`, 2,720,256 bytes, SHA-256 `9777D5AEBD68C197971567F685E99CE007DFEEA3BB46990BD11094720AA0CA70`. Chưa deploy AppData; chờ smoke view mới.
- 2026-07-03 Spot Elevation Test5: GỐI đã ra `+8.050`, NHỊP vẫn không tìm thấy floor face. Root cause trong fallback projection: `PlanarFace.Project(leftTarget)` trả kết quả trên mặt phẳng vô hạn ngay cả khi UV nằm ngoài biên face, nên toán tử `??` không bao giờ thử lại tại tim dầm. Đã đổi thành kiểm tra `IsInside`; nếu điểm gần mép ngoài face thì project lại station center và kiểm tra lần nữa. Tests **124/124 pass**, Debug/Release R25 **0 error**. Phát hành đúng file cũ `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`, identity `RevitAPP.BeamDimFixTest6`, 2,720,256 bytes, SHA-256 `848C6A5C93E1D6DE98BCC79E896D9B5B25EC5B681D76863CEBDDF66DFCC7F3B4`. Chưa deploy AppData; chờ smoke view mới.
- 2026-07-03 Spot Elevation Test6: NHỊP vẫn không tìm thấy face, trong khi DimensionPlacer đã dùng cùng Floor để tạo đúng dim `120/330`. Đã loại bỏ khác biệt giữa hai bộ dò: Spot giờ chấp nhận horizontal PlanarFace theo `abs(normal.Z) >= 0.5` giống DimensionPlacer (không giả định normal top luôn hướng +Z), tính Z mặt phẳng tại station bằng phương trình plane, thử mép trái rồi tim dầm, kiểm tra `IsInside` và sai số projection <=1mm. Tests **124/124 pass**, Debug/Release R25 **0 error**. Phát hành đúng file cũ `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`, identity `RevitAPP.BeamDimFixTest7`, 2,720,256 bytes, SHA-256 `0CE4BBA92EE701834A9D8B021ECC0905109DEF2195A4461B9BD94DAEF6E2FFC5`. Chưa deploy AppData; chờ smoke view mới.
- 2026-07-03 Spot Elevation Test7: NHỊP đã sinh spot nhưng sai ở đáy dầm `+7.600`. Sau khi cho phép normal hai hướng, top face đã bị join mất nên face ngang cao nhất còn lại của beam chính là bottom face; code trả ngay mà không kiểm tra cao độ. Đã bắt buộc mọi beam face ứng viên phải nằm trong 10mm quanh `pair.Geometry.TopZFeet`; mặt đáy bị loại, sau đó mới fallback Floor face trùng top dầm. Tests **124/124 pass**, Debug/Release R25 **0 error**. Phát hành đúng file cũ `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`, identity `RevitAPP.BeamDimFixTest8`, 2,720,256 bytes, SHA-256 `43C626EA0B1B0669AEBC0D4A09D5FB72945EDBB9A808B2009FD7BCB74049D733`. Chưa deploy AppData; chờ smoke view mới.
- 2026-07-03 theo yêu cầu user: **DỪNG FIX, tìm root cause trước.** Test8 loại mặt đáy nhưng cả GỐI/NHỊP đều không tìm được top face. MCP `send_code_to_revit` probe chỉ đọc dầm `1892582` vẫn timeout 49s, nên không dùng tiếp để tránh lag. Đã phát hành bản CHẨN ĐOÁN (không thay thuật toán chọn face): khi search thất bại ghi `expectedTop`, station, beam/floor face Z, normal, reference, IsInside, projection distance và floor IDs vào `%TEMP%\RevitAPP-spot-diagnostic.txt`. File DLL vẫn đúng đường dẫn cũ `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`, identity `RevitAPP.BeamSpotDiag1`, 2,722,304 bytes, SHA-256 `7495FED3A7DDC92BA8738876D73694AE2DB41817E901A128C375F80AD84765BF`. Tests **124/124 pass**, Debug/Release R25 **0 error**. Bước tiếp theo: user load Diag1 và chạy 1 lần; AI đọc file diagnostic rồi mới được sửa code.
- 2026-07-03 kết quả root-cause probe (view `(7)`, beam `1892582`): `pair.Geometry.TopZFeet/EXPECTED_TOP_MM = 8100`, nhưng geometric top face có stable reference thực tế ở **8050mm**. GỐI có beam face `Z=8050`, `REF=True`, `Inside=True`; NHỊP do join mất top beam face nhưng Floor `1894070` có top face `Z=8050`, `REF=True`, `Inside=True`. Tolerance 10mm quanh giá trị sai 8100 đã loại cả hai mặt đúng 8050 (lệch 50mm). Nguồn sai truy ngược tới `BeamGeometryReader.ReadElevations`: đang dùng `beam.get_BoundingBox(null).Max.Z`; bbox của family này lên 8100 vì chứa geometry/control vượt mặt bê tông, không đại diện top vật lý. Dim và spot đúng đều chứng minh top thật là 8050, bottom thật 7600, cao 450. **Chưa fix theo yêu cầu user**; hướng sửa nguồn là ngừng dùng bbox max làm top ưu tiên, lấy `STRUCTURAL_ELEVATION_AT_TOP`/horizontal geometric face thật và chỉ fallback bbox cuối cùng.
- 2026-07-03 sau khi user cho phép sửa: đã fix nguồn `BeamGeometryReader.ReadElevations`. Thêm pure helper `BeamElevationMath.Resolve`: ưu tiên cặp `STRUCTURAL_ELEVATION_AT_TOP/BOTTOM`; nếu chỉ có một đầu thì suy đầu còn lại từ chiều cao section; bbox chỉ fallback khi parameter thiếu; cuối cùng mới fallback axis Z. Thêm 3 regression tests, gồm case thật `topParam=8050`, `bottomParam=7600`, `bboxTop=8100` phải trả 8050/7600. Tổng tests **127/127 pass**, Debug/Release R25 **0 error**. Phát hành Add-In Manager đúng file cũ `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`, identity `RevitAPP.BeamSpotFix9`, 2,723,328 bytes, SHA-256 `400EDB6008AED26CE8543DF5FF79AEAD6B86A16477B6691E83D6BDF5DA614502`. Diagnostic logging chỉ ghi khi search vẫn thất bại để phục vụ smoke; chưa deploy AppData.
- 2026-07-03 Break Line hai phía khác cao độ: ảnh D2-02 cho thấy bên trái có nét cắt, bên phải thiếu. Root cause trong `BreakLinePlacer.TryGetSlabSection`: chỉ giữ một `SlabSection best` theo khoảng Z gần top dầm nhất rồi dùng chung `Solid/BottomZ/TopZ` đó cho cả trái và phải. Khi hai bên là hai Floor/solid khác cao độ, slab thắng ở trái làm mất profile phải. Đã đổi thành `TryGetSlabSides` với `bestLeft` và `bestRight` độc lập; mỗi bên giữ riêng solid, top/bottom và local X. Cùng một sàn hai phía vẫn có thể cấp cả hai side như cũ. Thêm regression test hai one-sided slab cung cấp độc lập left/right. Tổng tests **128/128 pass**, Debug/Release R25 **0 error**. Phát hành đúng file cũ `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`, identity `RevitAPP.BeamBreakSides10`, 2,723,328 bytes, SHA-256 `6426313ACF5B5C38DC3E25D5EBCC1A9FACC2C6254D12F89151DC479ED7171F80`. Chưa deploy AppData; chờ smoke view mới.
- 2026-07-03 09:57: user đã tắt Revit và yêu cầu deploy. Xác nhận không còn process Revit; build Release.R25 chính thức identity `RevitAPP` **0 error**. Đã backup DLL cũ thành `RevitAPP.dll.backup-20260703-095719` và deploy vào `%APPDATA%\Autodesk\Revit\Addins\2025\RevitAPP\RevitAPP.dll`. Verify tại đích: 2,722,304 bytes, SHA-256 `B80161C4E79D7776F9804380B6F33F5586111FC07856CCFB0D95C647C6871579`, identity `RevitAPP`. Bản chính thức gồm fix top elevation parameter, dim 0, spot top dầm và break-line trái/phải độc lập.

> Feature NATIVE trong RevitAPP (KHÔNG add-in độc lập): pick lệnh "Ban Ve Dam" → pick dầm (đã có Rebar sẵn)
> → sinh Sectional Elevation + Cross Section + rebar tag + dimension + spot elevation → đặt lên 1 sheet có title.
> Kế hoạch gốc: `plans/260701-1144-beam-drawing-in-revitapp/` (plan.md + 6 phase file). ĐỌC PLAN TRƯỚC KHI SỬA.

## Trạng thái (cập nhật khi làm — đây là bản khởi tạo lúc plan, code CHƯA bắt đầu)

| Phase | Hạng mục | Trạng thái |
|---|---|---|
| 0 | Gỡ dependency BeamDrawing.* + scaffold namespace RevitAPP.* + wire button | ✅ done (build Debug.R25 0 error) |
| 1 | Domain models thuần + validator + factory + xUnit | ✅ done (RevitAPP.Core; 72/72 test pass) |
| 2 | BeamPicker + BeamGeometryReader + SectionPlaneCalculator | ✅ done (build 0 error; geometry chờ smoke) |
| 3 | WPF MVVM modal (BeamDrawingViewModel + BeamDrawingWindow) | ✅ done (build 0 error) |
| 4 | ProjectResourceProvider + SectionViewBuilder + SheetBuilder + Orchestrator (T1) + wire command | ✅ done (ra view/sheet; chờ smoke) |
| 5 | RebarTagPlacer + Dimension + SpotElevation + T2 + test + deploy | ✅ code done (72/72 test, Debug+Release R25 0 error); ⛔ SMOKE trong Revit CHƯA chạy |
| 6 | Domain v2 đầy đủ form BIMSpeed | ✅ done (73/73 test, Debug.R25 0 error) |
| 7 | Resources v2 + preset store JSON | ✅ done (77/77 test, Debug.R25 0 error) |
| 8 | UI v2 clone form BIMSpeed | ✅ code done (77/77 test, Debug.R25 0 error); chờ UI smoke |
| 9 | Engine v2 áp type/break line/dim | 🟨 code done (85/85 test, Debug+Release 0 error); chỉ còn smoke |

## Quyết định chốt (user-confirmed 2026-07-01) — KHÔNG tự đảo ngược
- Code **native trong `RevitAPP/`** (giống ColumnRebar/SheetAlign). KHÔNG dùng lại project `src/BeamDrawing.Addin`/`BeamDrawing.Core` — Phase 0 GỠ 2 ProjectReference đó.
- **Làm lại từ đầu**; giữ code cũ trong `src/BeamDrawing.*` chỉ làm **tham chiếu geometry** (đọc, không reference).
- Output đủ 4 phần: elevation+tag, cross section, dimension+spot, sheet+title.
- Add-in CHỈ vẽ view/tag/dim cho thép **SẴN CÓ** — KHÔNG rải thép.
- Chỉ **Revit 2025 (R25)**. Modal WPF. Không DI container.
- Phát hành qua **Add-In Manager khi Revit đang mở**.

## Lệnh hay dùng
```bash
# Build check compile (Revit đang mở vẫn chạy được — KHÔNG lock DLL):
cd RevitAPP && dotnet build -c Debug.R25 -p:DeployAddin=false

# Test logic thuần:
cd tests/RevitAPP.Tests && dotnet test

# Deploy (nạp vào Revit): build không có -p:DeployAddin=false sẽ copy DLL vào Addins\2025\.
# Nếu Revit đang giữ DLL → copy sang thư mục test riêng rồi load qua Add-In Manager.
cd RevitAPP && dotnet build -c Debug.R25

# Release trước khi giao:
cd RevitAPP && dotnet build -c Release.R25
```

## Phát hành qua Add-In Manager khi Revit đang mở (user yêu cầu)
1. Build DLL: `cd RevitAPP && dotnet build -c Debug.R25` (nếu Revit lock DLL → build ra thư mục khác / copy tay).
2. Trong Revit: **Add-Ins → Add-In Manager** (SDK/2 công cụ có sẵn) → **Load** file `RevitAPP.dll` mới.
3. Add-In Manager load `IExternalCommand` trực tiếp → chạy `RevitAPP.Commands.BeamDrawingCommand`.
   ⚠️ LƯU Ý: Add-In Manager KHÔNG chạy `Application.OnStartup` → command phải **self-contained**
   (tự setup logger trong Execute nếu cần, không phụ thuộc OnStartup). Ribbon button chỉ hiện khi load qua manifest bình thường.
4. Không cần đóng Revit giữa các lần thử.

## Quy ước QUAN TRỌNG (đọc trước khi sửa)
1. **Đơn vị:** Revit nội bộ = feet; UI = mm. Convert `/304.8`. Không lẫn đơn vị.
2. **Transaction tách:** tạo view/sheet ở T1 → commit → `doc.Regenerate()` → annotate (tag/dim/spot) ở T2.
   Tag/dim cần view ĐÃ commit + regenerate mới có reference hợp lệ. TransactionGroup bọc, rollback nếu lỗi.
3. **Rebar tag section view 2D:** dùng `Rebar.SetUnobscuredInView(view, true)` TRƯỚC khi tag.
   KHÔNG dùng `SetSolidInView` (chỉ View3D). `SetRebarsAsSolidInView` KHÔNG tồn tại — đừng bịa.
4. **Section type / view template / title block là DOCUMENT SETTINGS** — không import từ family.
   Resolve theo tên, thiếu → fallback default + warn tiếng Việt (không chặn).
5. **Geometry rebar/view CHỈ verify trong Revit thật** (Add-In Manager). KHÔNG viết unit test giả `ViewSection.CreateSection`/`Rebar`.
6. **Code thuần vs Revit API:** `Models/BeamDrawing/*` + validator/factory KHÔNG `using Autodesk.Revit` → link vào tests/RevitAPP.Tests. File chạm Revit API thì ĐỪNG link test.

## Tham chiếu code
- Pattern native RevitAPP: `RevitAPP/Commands/DrawColumnRebarCommand.cs`, `RevitAPP/Services/ColumnRebar/`,
  `RevitAPP/ViewModels/ColumnRebarViewModel.cs`, `RevitAPP/Views/ColumnRebarView.xaml`.
- Geometry/section tham chiếu (ĐỌC, KHÔNG reference): `src/BeamDrawing.Addin/Services/{BeamGeometryReader,SectionPlaneCalculator,SectionViewBuilder,SheetBuilder,BeamDrawingOrchestrator,ProjectResourceProvider}.cs`.
- Ribbon + startup: `RevitAPP/Application.cs`. csproj: `RevitAPP/RevitAPP.csproj`.

## Giả định cần xác nhận (chưa chốt)
- Số cross section = 3 (đầu 0.1 / giữa 0.5 / cuối 0.9 dọc dầm) — có thể chỉnh.
- Scale mặc định 1:25.
- Dầm giả định thẳng (LocationCurve = Line); dầm cong → warn + skip v1.
- Tên view: `MCD-{Mark}` / `MCN-{Mark}-{i}`, fallback ElementId nếu không có Mark.

## VIỆC TIẾP THEO (ưu tiên giảm dần — cập nhật khi tiến hành)
1. Trong Add-In Manager, load `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` và chạy
   `RevitAPP.Commands.BeamDrawingCommand` trên dầm có thép thay đổi spacing theo nhịp.
2. Verify UI 3 vùng/preset CRUD; output từng cross station có tag khác nhau đúng thép thực.
3. Verify viewport/title block/section-template riêng; break line 2 đầu; spot; dim rộng + chuỗi dim lớp.
4. Nếu chain dim fail, dùng RevitLookup kiểm tra reference rebar/subelement rồi sửa `DimensionPlacer`.
5. Ghi kết quả smoke vào đây và `phase-09-engine-v2.md`; khi pass mới đổi Phase 9/plan thành ✅ done.

## v2 — Làm ĐẦY ĐỦ như form BIMSpeed (user-confirmed 2026-07-01)
User chốt: clone chức năng form "Beam Drawing" BIMSpeed. Đặc tả đầy đủ form INPUT + bản vẽ OUTPUT trong
`RevitAPP/docs/BEAM-DRAWING-BIMSPEED-SPEC.md` (ĐỌC TRƯỚC KHI LÀM v2). Frame video: `docs/trien-khai-dam-frames/`.
Phase v2: 6 (domain full) → 7 (resources + preset store) → 8 (UI clone form) → 9 (engine áp type + break line + dim chia lớp).
**Hành vi cốt lõi:** mỗi cross-section tag theo THÉP THỰC TẾ tại vị trí đó (2 mặt cắt mẫu khác nhau: D6 a100 vs a200).

### Lỗi đã gặp khi smoke v1 (đã fix)
- "Modification of the document is forbidden ... no open transaction" khi chạy qua Add-In Manager →
  do `doc.Regenerate()` gọi NGOÀI transaction (giữa T1 và T2). ĐÃ BỎ (T1 commit tự regenerate). Regenerate còn lại
  trong RebarTagPlacer nằm TRONG T2 → OK. Build lại + copy `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`.
- Add-In Manager Manual Mode chặn sửa document nếu không có transaction mở → mọi thao tác phải trong Transaction do command tự mở.

## LEADER GẬP — PHÂN TÍCH CĂN CỨ (2026-07-01 16:30) — ADDIN ĐÃ KHỚP ĐÍCH VỀ CẤU TRÚC
**Log thật (DLL 16:26):** `[LOG GOI(10)] cropY=0..2.14 nRebar=4 thepY=[1.02,0.99,0.91,0.46]`. → nRebar=4 ĐÚNG,
thép TRONG crop. **LỌC STATION ĐÚNG** (giả thuyết 16:26 SAI — số 39.8 lần trước do query quét TOÀN model, không phải view).
**So khớp đích DK2-1 (MCP đo):**
- headY: đích [2.2,1.57,1.05,0.43] vs addin [1.71,1.28,0.85,0.43] — cả 2 RẢI ĐỀU, đều lệch xa thép.
- thepY: đích [1.35,1.31,1.24,0.78] (trải rộng) vs addin [1.02,0.99,0.91,0.46] (CHỤM sát 0.03-0.08).
- Thuộc tính tag: **GIỐNG HỆT** — MRA count=3, 3 MRA-tag + 1 DAI-tag, đều hasLeader=True end=Attached orient=Horizontal.
**KẾT LUẬN (trung thực):** addin đã khớp đích 100% về loại tag/leader/MRA/head-layout. Leader addin trông "xiên" hơn
vì THÉP DẦM ADDIN CHỤM SÁT (0.46-1.02) → 3 leader từ 3 thép gần nhau toả ra 3 head rải đều = hình rẻ quạt.
Đích thép trải rộng nên leader ngắn+thẳng. → Có thể KHÔNG phải bug code, mà do hình học dầm test khác nhau.
**HƯỚNG (chờ user quyết):** (A) chấp nhận — addin đúng kiểu đích, khác biệt do dầm test; (B) nếu muốn leader ngắn hơn:
đặt head Y GẦN Y thép thật (giảm rải đều) — nhưng thép chụm sẽ làm tag chồng, cần cân bằng; (C) đo thêm điểm neo
leader trên thép (leader attach point) để so đích. **XÓA 2 LOG** (`[LOG {view}]` BeamAnnotator + bất kỳ log nào) trước khi finalize.

## (LỊCH SỬ - giả thuyết SAI) LEADER CÒN GẬP KHÚC (2026-07-01 16:26) — LỌC THÉP THEO STATION
**Fix đai-lệch-X (16:21) ĐÃ CHẠY:** MCP đo view(9) → cả 4 tag headX=2.202 đồng nhất (đai hết lệch). ✓
**Nhưng leader VẪN gập khúc.** Căn cứ đo view MCN-DK2-GOI(9):
- cropMaxY=2.136 (dầm 400), tag headY=1.71/1.28/0.85/0.43.
- **Thép localY range = 0.13 .. 39.8 (!!)** — có thép Y=39.8, NGOÀI crop (chỉ tới 2.136) rất xa.
→ **GỐC: `FilterAndSortAtStation` (BeamAnnotator) lọc thép theo station CHƯA CHẶT** → tag dính cả thép của
  đoạn dầm khác dọc trục (Y tới 39.8), head đặt trong crop nhưng thép thật ở xa → Revit kéo leader XIÊN/GẬP.
- So đích DK2-1: headY 2.2/1.57/1.05/0.43 (cao hơn, sát thép trong crop) → leader ngang thẳng.
**VIỆC TIẾP (căn cứ, không đoán):** user chạy DLL **16:26**, đọc dòng `[LOG {view}] cropY=0..X nRebar=N thepY=[...]`
trong TaskDialog. Nếu thepY có giá trị NGOÀI [0, cropMaxY] hoặc nRebar quá nhiều (>4-5) → xác nhận lọc station sai.
FIX: siết `FilterAndSortAtStation` — chỉ lấy thép mà bbox GIAO mặt phẳng cắt tại station (đang dùng tolerance 10mm,
có thể chưa chiếu đúng trục), VÀ chỉ lấy thép nằm TRONG crop Y. Sau khi lọc đúng (chỉ thép của tiết diện đó) →
tag rải đúng + leader thẳng. XÓA 2 LOG (`[LOG {view}]` trong BeamAnnotator) sau khi fix.

## FIX BUG ĐAI-LỆCH-X (2026-07-01 16:21) — ĐÃ TÌM RA GỐC
**Căn cứ log thật:** `[LOG dai] inX=2.202 outX=2.469 delta=0.267`. → Engine truyền head ĐÚNG (2.202) nhưng
`IndependentTag.Create` (có leader) khiến Revit TỰ DỜI head theo bbox text → 2.469. Không phải lỗi slot.
**FIX (DLL 16:21):** sau `IndependentTag.Create`, ép `tag.TagHeadPosition = tagHead` (set lại về đúng cột). Đã XÓA log.
MRA (thép dọc) giữ 2.202 đúng nên không cần sửa. **VIỆC TIẾP: user chạy DLL 16:21, verify tag đai đã thẳng cột
với MRA (cùng X) + leader hết xiên.** Nếu đã khớp DK2-1/DK2-3 → build Release.R25 + finalize (journal/commit).

## (LỊCH SỬ) ĐANG DANG DỞ (2026-07-01 16:13)
**Đích đến CHỐT: 2 view `DK2 - 1` (GỐI) + `DK2 - 3` (NHỊP)** trong model đang mở. Đã đo qua MCP (căn cứ, không đoán):
- Đích: TẤT CẢ tag headX=2.53 ĐỒNG NHẤT (cropMaxX 1.64 + 0.886). headY rải đều: 2.198/1.575/1.055/0.427
  (topGap=botGap=0.427=VerticalMarginFeet, step 0.623). leaderEnd=**Attached**, hasElbow=False. Leader thẳng.
- Công thức đích = `CrossTagLayout.TagHeadLocal` (rải đều trong crop). ĐÃ wire vào BeamAnnotator.

### BUG CÒN LẠI (chưa fix xong): tag ĐAI lệch cột X → leader đai xiên
- **Căn cứ đo (view MCN-DK2-GOI 7, DLL 16:04):** 3 tag MRA (thép dọc) headX=**2.202** ĐÚNG (=cropMaxX 1.316+0.886).
  Tag đai (RebarTagPlacer) headX=**2.469** SAI (lệch +0.267). headY đã đúng (rải đều). leaderEnd=Attached cả 2.
- **Đã LOẠI giả thuyết z:** đo `tf.OfPoint` với z=0 vs z=zMid → deltaX=0. z KHÔNG gây lệch. (Vẫn đổi RebarTagPlacer
  dùng z=0 cho nhất quán MRA — không phải fix.)
- **CHƯA chứng minh** vì sao đai ra 2.469 dù code (RebarTagPlacer dòng ~70) set `slot.X` = 2.202. MRA (MultiRebarAnnotationPlacer,
  z=0, slot.X) giữ đúng 2.202; đai qua IndependentTag.Create ra 2.469. Nghi: IndependentTag.Create tự dời head theo
  tag type/leader — NHƯNG query MCP test-tạo-tag bị "Exception target of invocation" (transaction trong MCP) nên chưa đo được.
- **ĐANG LÀM:** thêm LOG chẩn đoán vào RebarTagPlacer (DLL **16:13**): mỗi tag đai ghi vào warnings
  `[LOG dai] inX=... outX=... delta=...` (inX=slot truyền vào, outX=head Revit trả về). **VIỆC TIẾP: user chạy view mới
  từ DLL 16:13, đọc dòng [LOG dai] trong TaskDialog kết quả** → biết chính xác: nếu inX=2.202 & outX=2.469 thì Revit tự
  dời head sau Create (fix: set lại `tag.TagHeadPosition = tagHead` SAU Create + Regenerate, hoặc bù trừ). Nếu inX≠2.202
  thì BeamAnnotator truyền sai slot (fix ở đó). **XÓA LOG sau khi fix xong.**

### File liên quan bug này
- `RevitAPP/Services/BeamDrawing/BeamAnnotator.cs` (cross branch ~dòng 42-74): tính `slots` bằng `TagHeadLocal`,
  tách longSlots (MRA) + stirrupSlots (đai), truyền xuống 2 placer.
- `RevitAPP/Services/BeamDrawing/RebarTagPlacer.cs` (~dòng 66-95): đặt head tag đai từ slot; CÓ log tạm.
- `RevitAPP/Services/BeamDrawing/MultiRebarAnnotationPlacer.cs`: MRA (thép dọc) — head ĐÚNG, tham chiếu cách làm.
- `RevitAPP.Core/Services/CrossTagLayout.cs`: `TagHeadLocal` (rải đều, dùng cái này) + `SpreadNoOverlap` (ĐÃ BỎ khỏi
  luồng — từng gây head tràn xuống âm -0.01 → leader khúc; đừng dùng lại trừ khi neo theo crop).
- Memory: `[[beam-drawing-cross-tag-layout]]` — toàn bộ số đo đích + bài học.

### Cách đo view qua MCP (đã hoạt động, biến `document` KHÔNG phải `doc`)
```csharp
var v = new FilteredElementCollector(document).OfClass(typeof(ViewSection)).Cast<ViewSection>()
    .FirstOrDefault(x=>x.Name=="DK2 - 1");
var inv = v.CropBox.Transform.Inverse;
foreach (var t in new FilteredElementCollector(document, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
    { var h=inv.OfPoint(t.TagHeadPosition); /* h.X, h.Y local; type=document.GetElement(t.GetTypeId()).Name */ }
```
LƯU Ý: MCP `send_code_to_revit` có Transaction hay ném "Exception target of invocation" — đọc thì OK, GHI/tạo element hay fail.

### Trạng thái build/test hiện tại
- DLL đang test: `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (16:13, có LOG tạm). Load qua Add-In Manager (Remove entry cũ → Load lại).
- `dotnet test tests/RevitAPP.Tests` = 91/91 pass. Debug.R25 build 0 error. Release.R25 CHƯA build lại sau các sửa hôm nay.
- Leader gấp khúc ĐÃ giải quyết bằng LeaderEndCondition.Attached (StraightLeader.cs chỉ set Attached, KHÔNG Free+elbow).
  Vấn đề leader đai còn lại là HỆ QUẢ của lệch X, không phải leader condition.

## Nhật ký thay đổi (append khi làm)
- 2026-07-02: **Fix tiếp dim `0` theo ảnh smoke NHỊP.** Căn cứ: chain vẫn chứa hai reference rất gần nhau;
  dim type làm tròn khoảng nhỏ thành chữ 0, trong khi code chỉ gộp mặt trong 2mm. Tăng ngưỡng mặt trùng lên
  **10mm** (`CrossDimensionLayerMath.CoincidentToleranceMm`), vẫn giữ nguyên drop thật 50mm. Khi gộp cụm trên
  cùng phải giữ Z lớn nhất để dim tổng vẫn 450 (không tụt thành 444). Thêm test khóa gap 6mm bị gộp, drop 50mm
  không bị nuốt và outer envelope không đổi. Tổng **121/121 pass**; Debug.R25 + Release.R25 đều **0 error**.
  Phát hành `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP-no-zero-dim-v2.dll` (2,712,576 bytes), SHA-256
  `CFA132F700C0D9A2176ACF6105F39F1C15524ECF4C1517BF89A9457264AC3057`. Revit đang mở nên không đè Addins.
  Nếu DLL mới vẫn cho kết quả cũ trong cùng phiên thì đó là cache assembly Add-In Manager: restart Revit, load
  file tên mới và bắt buộc tạo view mới để smoke.
- 2026-07-02: **User xác nhận quy luật dim/nét cắt phải xét RIÊNG TỪNG STATION, không xét chung cả cây dầm.**
  Cùng một dầm có thể: GỐI top sàn = top dầm → quy luật cũ 2 đoạn; NHỊP sàn hạ cốt → quy luật mới 3 đoạn,
  hoặc ngược lại. Code hiện đáp ứng vì `GetSlabHorizontalReferences(..., pair)` và `BreakLinePlacer` đều lấy
  `pair.Station`, giao solid/face Floor tại đúng tọa độ cắt rồi mới quyết định cao độ/số lớp. Không cache một kết quả
  sàn cho toàn cây dầm. Đây là yêu cầu bắt buộc, AI sau không được đổi về dò sàn một lần theo beam.
- 2026-07-02: **User chốt ngoại lệ tương thích: Top dầm = Top sàn phải giữ quy luật cũ.** Với dầm 450 và
  sàn dày 100 nằm trong đầu dầm, dim tổng = 450, dim chuỗi chỉ 350 + 100; không sinh đoạn chênh cao 0.
  Logic gộp mặt trùng 2mm hiện đã đáp ứng; thêm regression test riêng để khóa hành vi này, tổng **119/119 pass**.
  Trường hợp Top sàn thấp hơn Top dầm mới dùng quy luật 3 đoạn (ví dụ 50 + 120 + 280).
- 2026-07-02: **Đã sửa dim cao khi sàn hạ cốt theo ảnh target 450 = 50 + 120 + 280.** Quy luật mới không
  hardcode 2 đoạn: lấy 4 surface đáy dầm/đáy sàn/top sàn/top dầm tại đúng station, sort theo Z và gộp mặt trùng
  trong 2mm. Sàn hạ cốt → chuỗi 3 đoạn; top sàn trùng top dầm → tự còn 2 đoạn; dim tổng dùng 2 cao độ ngoài cùng.
  Đồng thời bỏ `SlabThicknessGuessFeet`, đọc Z/reference mặt Floor thật kể cả sàn dốc. Thêm
  `CrossDimensionLayerMath` + 4 regression test; tổng **118/118 pass**, Debug.R25 + Release.R25 đều **0 error**.
  Đã phát hành `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP-slab-drop-dim-3layers.dll`
  (2,712,064 bytes), SHA-256 `74753AB3E9737313F06015034A3FA65CB120FF3D56101D783DE0198CC023AEB1`.
  Smoke: Remove entry cũ, load file tên mới, tạo view mới; kiểm tra sàn hạ cốt ra tổng 450 và chuỗi 50/120/280,
  sàn ngang top dầm vẫn tự gộp còn 2 đoạn.
- 2026-07-02: **Đã sửa nét cắt sàn linh hoạt theo CAO ĐỘ solid thực tại station (ảnh smoke D2-06).** Bản trước dùng
  bbox toàn Floor + offset cứng 40mm, nên GỐI/NHỊP, sàn dốc/lệch hoặc chỉ vươn một phía có thể ra giống nhau.
  Bản mới dò giao tuyến solid tại đúng station và từng mép: bên nào có sàn vươn mới đặt; overhang ngắn thì stub
  tự co, sàn rộng giới hạn 40mm; chiều dài **và toàn bộ cao độ** line lấy BottomZ/TopZ sàn ngay tại X đó. Không neo
  vào TopZ dầm và không giả định top sàn = top dầm; sàn hạ cốt trong chiều cao dầm thì **toàn bộ nét cắt hạ theo**.
  Thêm `SlabBreakLineMath` + 4 regression test; tổng **114/114 pass**, Debug.R25 + Release.R25 đều **0 error**.
  Revit đang mở nên không đè Addins; đã phát hành
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP-slab-elevation-flex.dll` (2,705,408 bytes), SHA-256
  `3A3295B166D3DFE1D1691FDE45679DCCD51E4D30C3308BC9EF963C622DC41BBF`. Smoke: Remove entry cũ,
  load file tên mới, tạo view mới trên dầm có sàn hạ cốt và kiểm tra break-line dịch xuống đúng TopZ/BottomZ sàn.
  Project bộ cài online `install-online/` đã scaffold nhưng tạm dừng chưa verify để ưu tiên fix bản vẽ này.
- 2026-07-02: **Đã deploy chính thức sau khi user tắt Revit + tạo icon riêng chuyên nghiệp cho Beam Drawing.**
  Thêm nguồn vector `Resources/Icons/BeamDrawingIcon.svg` và PNG tối ưu đúng 16/32 px: nền xanh ngọc bo góc,
  trang bản vẽ, tiết diện dầm/đai, 4 chấm thép và đường dim; không có chữ nhỏ nên vẫn đọc rõ trên ribbon dark UI.
  `Application.cs` chỉ đổi nút `BeamDrawingCommand` sang `BeamDrawingIcon16.png/BeamDrawingIcon32.png`; các nút khác
  giữ nguyên. `RevitAPP.csproj` embed hai PNG mới. Release.R25 build **0 error**, tests **110/110 pass**.
  Nice3point đã deploy manifest + DLL tới
  `C:\Users\Admin\AppData\Roaming\Autodesk\Revit\Addins\2025\RevitAPP.addin` và
  `...\RevitAPP\RevitAPP.dll` (2,695,680 bytes), SHA-256
  `4E0DBDF14A9240A6B273E5148EF50819AE458EE65C6E3AE33B621E858B73B83D`; hash nguồn/đích khớp.
  Đồng thời copy bản Add-In Manager chống cache:
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP-WPFCanvas-professional-icon.dll`.
  Lần mở Revit kế tiếp add-in tự load qua manifest; icon mới chỉ hiện khi startup bình thường (Add-In Manager không
  chạy `Application.OnStartup`).
- 2026-07-02: **Bỏ hoàn toàn SkiaSharp, trả preview về WPF Canvas thuần để hết lag/rủi ro native DLL.**
  Trạng thái tiếp quản là một migration dở dang: `CrossSectionDiagramRenderer.cs` vẫn gọi Skia nhưng package đã bị
  gỡ khỏi `RevitAPP.csproj`. Đã xóa renderer này, thêm
  `Views/Controls/CrossSectionDiagramCanvas.cs` (WPF `Canvas` + `DrawingContext`) và thay hai `Image` trong
  `BeamDrawingWindow.xaml` bằng control bind trực tiếp. Preview vẫn đọc rộng×cao dầm thật, vẽ đúng tỷ lệ, đai,
  thép trên/giữa/dưới, leader thẳng, tên type GỐI/NHỊP và dim rộng×cao; đổi combo tự redraw qua DependencyProperty,
  không tạo bitmap/native call. `BeamDrawingWindow.xaml.cs` đã bỏ toàn bộ render thủ công/subscription.
  `rg` xác nhận không còn `SkiaSharp/SKCanvas/SKBitmap`; output Release không có DLL Skia. Test **110/110 pass**;
  Debug.R25 + Release.R25 đều **0 error**. Đã phát hành `RevitAPP.dll` và
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP-WPFCanvas-noSkia.dll` (2,692,608 bytes), SHA-256
  `821456AB77118E2F88568783C513522016C55ABA458EE43C1EABA9CA2264940F`. Smoke: Remove entry cũ trong
  Add-In Manager, load file tên mới, pick 1 dầm và kiểm tra form mở nhanh + đổi combo không lag. **Không đưa
  SkiaSharp trở lại.** Việc engine kế tiếp vẫn là slab-aware dim 2 lớp + Detail Item 1-25; slice này chỉ sửa preview UI.
- 2026-07-02: **Fix lần 2 chống mặt cắt GỐI phạm cột.** Bản trước chỉ đẩy tâm lát cắt ra mép cột +10 mm,
  nhưng Far Clip 150 mm trải 75 mm về phía cột nên vẫn giao cột. Clearance mới = nửa Far Clip 75 mm +10 mm
  an toàn = **85 mm ngoài mép cột**. Công thức đặt trong `BeamSectionBoxMath` để tự đi theo Far Clip; thêm test
  regression khóa 85 mm. GỐI danh nghĩa vẫn 3.5%, NHỊP 50%. Test **110/110 pass**; Debug.R25 và Release.R25
  đều **0 error**. Đã phát hành
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP-3_5pct-clear85-150mm.dll` (2,671,104 bytes), SHA-256
  `0482CD7B71A9C7B978E3781B3ECEC287AD8F6038A444E199A49A8D5E002D3488`. Smoke: Remove bản cũ, load file
  tên mới, tạo view mới; kiểm tra toàn bộ lát cắt không giao cột.
- 2026-07-02: **GỐI danh nghĩa 3.5% nhưng bắt buộc không phạm cột.** `BeamSupportFinder` trước chỉ dùng tâm cột,
  nên cột rộng có thể bao trùm station 3.5%. Nay chiếu 8 góc bbox cột lên trục dầm, gom cột chồng nhau và nếu
  station danh nghĩa còn trong cột thì đẩy ra mép cột theo hướng nhịp +10 mm. NHỊP vẫn 50%. Thêm 3 test thuần
  cho trường hợp nằm trong cột/đã ngoài cột/nhịp theo hướng giảm station. Test **109/109 pass**; Debug.R25 và
  Release.R25 đều **0 error**. Đã phát hành
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP-3_5pct-150mm.dll` (2,671,104 bytes), SHA-256
  `8681D7ACFACCD0DD5E177A85EF1D002903BDF9EF1762EA12B248697AC12EBC4C`. Smoke: Remove bản cũ, load file
  tên mới, tạo view mới; kiểm tra GỐI không cắt vào cột, NHỊP=50%, Far Clip=150 mm.
- 2026-07-02: **User chốt GỐI = 3.5%, NHỊP = 50%.** Đổi `SupportInsetRatio` + fallback từ 0.03 lên 0.035;
  giữ `EndClamp=0.01`. Cập nhật 5 regression test station. Chờ test/build/Release; phát hành file tên mới chống cache.
- 2026-07-02: **User chốt lại GỐI = 3%, NHỊP = 50%.** Đổi `SupportInsetRatio` + fallback từ 0.02 lên 0.03;
  giữ `EndClamp=0.01`. Test **106/106 pass**; Debug.R25 và Release.R25 đều **0 error**. Đã phát hành
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP-3pct-150mm.dll` (2,665,984 bytes), SHA-256
  `40F38138FB1E1CFE18D92E836B57FC274371D79713A527CBB4DEAEB59F997D8F`. Smoke: Remove bản cũ, load file
  tên mới, tạo view mới; kiểm tra GỐI=3%, NHỊP=50%, Far Clip=150 mm.
- 2026-07-02: **User chốt GỐI = 2%, NHỊP = 50%.** Đổi `SupportInsetRatio` + fallback về 0.02. Đồng thời hạ
  `EndClamp` từ 0.03 xuống 0.01; nếu không, 2% sẽ bị âm thầm clamp thành 3%. Test khóa GỐI 0.02/0.98 hai đầu,
  NHỊP 0.5. Test **106/106 pass**; Debug.R25 và Release.R25 đều **0 error**. Đã phát hành file chuẩn và
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP-2pct-150mm.dll` (2,665,984 bytes), SHA-256
  `C5DA827B54A54DB07E9177CB76BDE5C8527DCC955917F983BC8DD5CE7452A517`. Smoke: Remove entry cũ, load file
  tên mới, tạo view mới; kiểm tra GỐI=2%, NHỊP=50%, Far Clip=150 mm.
- 2026-07-02: **Ép Far Clip Offset luôn = 150 mm SAU View Template.** Ảnh smoke vẫn ra 243.8 mm dù section box
  đã 150 mm. Gốc lỗi: `SectionViewBuilder.ApplyConfig` gán template sau khi tạo box, template có thể ghi đè extents.
  `CreateCrossSection` giờ set + đọc kiểm tra `VIEWER_BOUND_OFFSET_FAR` sau template; nếu template khóa hoặc Set
  không đạt đúng 150 mm thì báo lỗi rõ thay vì âm thầm sinh 243.8. Thêm regression test conversion. Code station
  đã xác nhận vẫn là GỐI 5% / NHỊP 50%. Test **106/106 pass**; Debug.R25 và Release.R25 đều **0 error**.
  Đã phát hành `RevitAPP.dll` và file tên mới chống cache `RevitAPP-5pct-150mm.dll` tại
  `C:\Users\Admin\Desktop\RevitAPP-test\` (2,665,984 bytes), SHA-256
  `A1B43B4F5E982BC5AE585065918D50971301BBCE364E04364734E3EC2DF76D54`. Smoke bắt buộc: Remove entry cũ,
  load file `RevitAPP-5pct-150mm.dll`, tạo view mới, kiểm tra Far Clip=150 mm và GỐI=5%.
- 2026-07-02: **User chốt GỐI = 5% nhịp.** `BeamSectionStationMath` đổi station GỐI + fallback từ 7.5% xuống
  đúng 5% chiều dài nhịp tính từ cột; NHỊP giữ 50%. Test **105/105 pass**; Debug.R25 và Release.R25 đều
  **0 error**. Đã copy `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,665,472 bytes), SHA-256
  `D426EE8AC8FED9D0B5F6C3C92BD6D516DF833A9C5C60F104D17C5DE072C3C019`; chờ smoke view mới.
- 2026-07-02: **Đưa mặt cắt GỐI gần cột hơn một chút.** `BeamSectionStationMath` đổi station GỐI từ 10% xuống
  7.5% chiều dài nhịp tính từ cột; fallback khi không dò được cột cũng 7.5%. Station NHỊP vẫn giữ đúng 50%.
  Cập nhật 5 regression test station. Test **105/105 pass**; Debug.R25 và Release.R25 đều **0 error**.
  Đã copy `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,665,472 bytes), SHA-256
  `88D0177C237F3293C81A43710022C4A2C271C081ED6EFEDE6C2E250E9F2F37B2`; chờ smoke view mới.
- 2026-07-02: **Quyết định cuối theo user: cân đối TOÀN BỘ tag theo chiều cao dầm.** Entry “chỉ sửa mainBot”
  ngay dưới bị supersede. Khôi phục `TagYsFromBeamBounds`: slot đầu = đỉnh +20 mm, slot cuối = đáy −50 mm,
  các slot giữa nội suy đều theo chiều cao thật. Test **105/105 pass**; Debug.R25 và Release.R25 đều **0 error**.
  Đã copy `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,665,472 bytes), SHA-256
  `505246B07DD08CF7327C8C5F59733C1580822FBC56282DCDC609FE60C349FF42`; chờ smoke view mới.
- 2026-07-02: **Thu hẹp fix tag theo user: CHỈ thép chủ dưới thay đổi.** Entry ngay dưới về việc chia đều mọi tag
  theo chiều cao đã bị supersede. Khôi phục `TagYOffsetsFromBeamTop` cho chủ trên/tăng cường/đai; `BeamAnnotator`
  chỉ tìm đúng entity `mainBot` và ghi đè Y = đáy dầm −50 mm. Cập nhật test để khóa các slot khác giữ nguyên.
  Test **105/105 pass**; Debug.R25 và Release.R25 đều **0 error**. Đã copy
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,665,984 bytes), SHA-256
  `BC1DA6F75B8CBDB5AA616351BD0D103E5E67C77E462DDD085B92B6FAD253125A`; chờ smoke view mới.
- 2026-07-02: **Tag thép chủ dưới luôn ra ngoài đáy và thích nghi chiều cao dầm.** Gốc lỗi: layout dùng mảng
  `TagYOffsetsFromBeamTop` cố định theo một mẫu, không đọc đáy dầm; dầm 500 mm chỉ còn ~20 mm dưới đáy và dầm
  cao hơn có thể đưa tag vào trong tiết diện. Đổi sang `TagYsFromBeamBounds`: chủ trên = đỉnh +20 mm, chủ dưới =
  đáy −50 mm, tag giữa chia đều theo chiều cao thật. `BeamAnnotator` đọc cả TopY/BottomY từ bbox dầm. Thêm test
  regression cho dầm cao 400/500/800 mm và kiểm tra slot giữa thay đổi. Test **105/105 pass**; Debug.R25 và
  Release.R25 đều **0 error**. Đã copy `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,665,472 bytes),
  SHA-256 `610A3FEBAEC0815B7BAFBA53E51610DE4A89B2607A54E311449C4C969FF4408F`; chờ smoke view mới.
- 2026-07-02: **Đổi Far Clip Offset mặt cắt ngang về đúng 150 mm.** Smoke cho thấy Properties = 243.8 mm.
  Gốc lỗi: section box dùng `CrossHalfDepthFeet=0.4`, nhưng box chạy từ `-halfDepth` đến `+halfDepth`, nên tổng
  là 0.8 ft = 243.84 mm. Thêm `BeamSectionBoxMath`, đặt tổng Far Clip = 150 mm (mỗi nửa 75 mm) và test
  regression xác nhận chuyển đổi. Test **101/101 pass**; Debug.R25 và Release.R25 đều **0 error**.
  Đã copy `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,665,472 bytes), SHA-256
  `AB40B04A6AA9A9E7751E8EA7AEF099A6B3B77696DDA1920E552A4BB15C7218B3`; chờ smoke view mới xác nhận Properties = 150 mm.
- 2026-07-02: **Fix GỐI/NHỊP cắt trùng vị trí.** Gốc lỗi: `BeamSupportFinder` lấy thẳng hai instance cột đầu
  mà không gom station trùng; cột chồng nhau cho `colStations[0] == colStations[1]`, khiến NHỊP bằng GỐI.
  Tách logic thuần `BeamSectionStationMath`: gom cột trùng trong 100 mm, GỐI cắt tại 10% từ cột vào bụng dầm,
  NHỊP cắt tại trung điểm hai gối; nếu chỉ thấy một cột thì chọn phía bụng dầm dài hơn; không thấy cột fallback
  0.1/0.5. Thêm 5 test regression trong `BeamSectionStationMathTests`. Test **98/98 pass**; Debug.R25 và
  Release.R25 đều **0 error**. Release DLL 2,664,960 bytes, SHA-256
  `F8CBE4874D7D1B906CC2FFAFF6E131506E73B46DE12FA6DF34AD4631B9EC35C5`. Đã copy đúng SHA tới
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` cho Add-In Manager. Đã sync `plan.md` +
  `phase-09-engine-v2.md`; chờ smoke Revit xác nhận vị trí thực.
- 2026-07-02 10:24: **Hoàn thiện UI + fix Viewport combo.** (1) Layout nút Setting 3 hàng (Add/Update/Delete |
  Load/Export | Lên/Xuống) — hết chồng chữ; cột trái nới 270px. (2) ComboBox.Standard thêm ControlTemplate nền
  tối theo theme (trước nền trắng mất chữ) + mũi tên ▼ rõ. (3) **Viewport type FIX: lọc `ElementType.FamilyName=="Viewport"`**
  (category OST_Viewports trả 0) + fallback `Viewport.GetValidTypes()` → nạp 10 type thật. (4) Spot elevation đọc
  param `STRUCTURAL_ELEVATION_AT_TOP` (-0.050), lệch trái leader ngắn. (5) Crop margin cân đối 0.5ft ôm gọn tiết diện.
  (6) Dim side/bot dùng đúng giá trị user nhập (bỏ Math.Max). Debug+Release R25 0 error, test 93/93. DLL 10:24.
  Combo data thật: Viewport=10, Section=18, ViewTemplate=79, MRA=12, Dim=58, TitleBlock=9.
- 2026-07-02 09:26: **✅ TAG MẶT CẮT NGANG ĐẠT ĐÍCH DK2-1/DK2-3 (user xác nhận "tag OK").** Chốt các fix:
  (1) tag đai ép TagHeadPosition sau Create (thẳng cột). (2) columnX = mép phải dầm + 1.378ft (không dùng mép crop).
  (3) mỗi lớp Y 1 element đại diện (band 0.08ft), MRA dim 1 element → Revit tự nref. (4) dim line Y = head Y → leader
  ngang thẳng vuông góc, không bám xiên thanh thép. (5) tag chủ lớp trên móc lên đỉnh dầm (+0.066). (6) THỨ TỰ TAG
  theo vùng: GỐI = chủ-trên/tăng-cường/đai/chủ-dưới; NHỊP = chủ-trên/đai/tăng-cường/chủ-dưới. Dọn log tạm + dead-code
  DiameterKey. Debug+Release R25 0 error, test 93/93. DLL 09:26. Memory [[beam-drawing-cross-tag-layout]] đầy đủ căn cứ.
  CÒN LẠI (optional): finalize commit; smoke sectional elevation (nếu bật); dim/spot cross tinh chỉnh nếu cần.
- 2026-07-02 08:51: **FIX headX: đặt theo MÉP PHẢI DẦM, không phải mép crop.** Căn cứ: đo 4 tag user chọn ở DK2-1
  → headX=2.526, mép phải dầm=1.148 → cách mép dầm 1.378ft. Bug cũ: dùng cropMax.X+0.886 (crop rộng hơn dầm nhiều
  → head sai chỗ "sai bét"). FIX: `BeamAnnotator.BeamRightEdgeLocalX` đọc mép phải dầm bbox trong view; columnX =
  mép dầm + `CrossTagLayout.TagColumnOffsetFromBeamFeet(1.378)`. Fallback cropMax nếu không đọc bbox. Cũng đã gom
  nhóm cùng lớp (nref 3/2/0/3 khớp đích). Build 0 error, test 93/93. DLL 08:51.
  Thông số đích đầy đủ trong memory [[beam-drawing-cross-tag-layout]] + BEAM-DRAWING-LAYOUT-COMPARE.md.
- 2026-07-02 08:46: **FIX GỐC leader MRA gập — GOM thép cùng lớp vào 1 MRA.** Căn cứ: user chọn tag "3D16"(29) ở
  đích, MCP đọc = 1 MRA gom **nRef=3** (3 thanh cùng lớp trên) → leader ngang thẳng. Addin cũ: mỗi thanh 1 MRA
  (nRef=1) → thép chụm → leader xiên. FIX: `BeamAnnotator.GroupLongitudinalByLayer` gom thép dọc cùng đường kính
  (RebarBarType) + cùng Y band (0.16ft ~50mm) thành nhóm; `MultiRebarAnnotationPlacer.Place` đổi nhận
  `IReadOnlyList<IReadOnlyList<Rebar>>` (nhóm), mỗi nhóm 1 MRA với `SetElementsToDimension(nhiều id)`. Entity list
  (nhóm dọc + từng đai) sắp theo Y → SpreadClampedToCrop rải head. Build 0 error, test 93/93. DLL 08:46.
  VIỆC TIẾP: user smoke — tag "3D16" giờ gom 3 thanh, leader ngang thẳng như đích chưa. Memory [[beam-drawing-cross-tag-layout]].
- 2026-07-02 08:28: **Fix leader xiên bằng: head bám Y thép + nới crop cao.** Căn cứ đã đo: crop view addin cao
  2.14ft QUÁ HẸP cho 4 tag gap 0.5 (cần ≥1.5ft khả dụng, chỉ có 1.29) → tag bị dồn/rải-đều xa thép → leader xiên.
  Đích DK2-1 crop cao 2.625. FIX: (1) `SectionPlaneCalculator.CreateCrossSectionBox` dùng `CrossHeightMarginFeet=0.8`
  (thay CrossMarginFeet 0.33) → crop cross cao hơn, đủ chỗ tag. (2) `CrossTagLayout.SpreadClampedToCrop`: head Y BÁM
  Y thép thật (leader gọn) + đẩy min-gap 0.5 chống chồng + clamp trong [cropMin+0.427, cropMax−0.427] (không tụt âm
  như SpreadNoOverlap cũ). (3) BeamAnnotator dùng SpreadClampedToCrop thay TagHeadLocal. Test 93/93, build 0 error.
  DLL 08:28. VIỆC TIẾP: user smoke — leader đã gọn chưa (head gần thép hơn + crop rộng đủ). Nếu ok → finalize.
- 2026-07-01 16:36: **Dọn log tạm + build sạch.** Xóa hết `[LOG ...]` (BeamAnnotator + RebarTagPlacer). Giữ fix
  ép `tag.TagHeadPosition=tagHead` sau Create (đai thẳng cột). Debug.R25 + **Release.R25 đều 0 error**, test 91/91 pass.
  DLL sạch deploy `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (16:36). Sẵn sàng finalize (journal/commit) khi user
  chốt leader (A chấp nhận / B head gần thép / C đo điểm neo). Addin đã khớp đích DK2-1 100% về cấu trúc tag/leader/MRA.
- 2026-07-01 16:00: **Chống chồng tag (đích = DK2-1/DK2-3).** Leader đã hết gấp khúc (Attached). Vấn đề còn: tag
  chồng khi 2 thép sát nhau. Đọc MCP đích DK2-1/3: headX=2.53 đồng nhất, Y=2.2/1.57/1.05/0.43 (≈Y thép nhưng
  giãn gap ~0.5-0.6), Attached. FIX: `CrossTagLayout.SpreadNoOverlap(rebarYsDesc, minGap=0.5)` — lấy Y thép thật
  (sắp giảm dần theo Z = đúng thứ tự trên→dưới), đẩy cặp liền kề cách ≥0.5ft, giữ tâm khối. `BeamAnnotator` cross:
  slot = (columnX chung, spreadY). 2 placer (MRA+RebarTag) dùng lại slot.Y (bỏ tự-tính Y). Test 91/91, build 0 error.
  DLL 16:00. Memory [[beam-drawing-cross-tag-layout]] cập nhật đích DK2-1/3.
- 2026-07-01 15:41: **Leader thẳng hàng vuông góc + bỏ ép-đai-giữa.** User yêu cầu: tag không chồng, thẳng hàng,
  leader KHÔNG gấp khúc (thẳng+vuông góc như ảnh). (1) `BeamAnnotator`: bỏ InsertRange ép đai giữa → xếp TẤT CẢ
  tag theo Z thật giảm dần (đai tự đúng vị trí từng mặt cắt: gối slot dưới, nhịp slot trên — khớp layout user tự làm).
  (2) Thêm `StraightLeader.cs`: `LeaderEndCondition.Free` + `SetLeaderElbow` tại (X trên điểm thép, Y=cao độ tag)
  → shoulder NGANG ở cao độ tag rồi gập VUÔNG xuống thép; thay `Attached` (auto dog-leg xiên). Áp cho cả
  RebarTagPlacer (đai) + MultiRebarAnnotationPlacer (dọc, qua GetTaggedReferences). Build 0 error, test 88/88.
  DLL 15:41. ⛔ GetLeaderEnd/SetLeaderElbow chỉ verify được khi smoke Revit — đã bọc try/catch tránh crash.
- 2026-07-01 15:30: **Đọc 2 view addin thật qua MCP (ghi nhớ):** `MCN-DK2-GOI` (id 1912735) + `MCN-DK2-NHIP` (1912745).
  ✅ Đai ĐÃ ra GIỮA (Y=0.85, giữa 3 MRA dọc Y=1.84/1.28/0.43) — fix InsertRange OK.
  ⚠️ BUG tag đai: addin gán type `A1_P_RT_DK&KC_MID` → text thô `32D6.0a100.0`/`32D6.0a200.0`. Mẫu DK1 dùng
  `...KC_BOT` → gọn `D6 a100`. **FIX cần: đổi stirrup tag type sang bản `_BOT` cho cả GỐI+NHỊP** (nguồn: default
  trong command/CrossAnnotationConfig đang trỏ `_MID`). MRA tag text ra "D" — MRA tự sinh, cần verify riêng.
  Dim đúng: `@BS-Dim A1` cao 400/rộng 200, spot `BS-2-Cao độ mặt đứng` OK. Memory: [[beam-drawing-cross-tag-layout]].
- 2026-07-01 15:28: **FIX đai ra GIỮA cột tag.** So sánh KẾT QUẢ vs đích DK1-15/17: tag đã thẳng hàng (fix 15:21),
  nhưng đai (D6) ra hàng 2 thay vì GIỮA. `BeamAnnotator` cross branch: tách thép dọc (sort Z giảm dần) rồi
  `InsertRange` đai vào index giữa `(count+1)/2` → đúng thứ tự mẫu: thép trên / lớp2 / ĐAI GIỮA / lớp2 / thép dưới.
  Build 0 error, DLL 15:28. CHỜ SMOKE.
  **CÒN LẠI — chưa fix:** NHỊP tag đai hiện "10D6" (số lượng) thay vì "D6 a200" (Ø+spacing) như mẫu/gối. Nguyên nhân
  nghi: mid-stirrup tag type (NHỊP) khác end-stirrup (GỐI), hoặc trỏ nhầm rebar. Mẫu DK1-15/17 dùng CHUNG 1 type
  `A2_P_RT_DK&KC_BOT` cho cả 2. Cần đọc view addin-gen qua MCP xem tag type NHỊP thật rồi ép cùng type gối.
  (CrossAnnotationConfig có End/Mid stirrup tag riêng — cân nhắc default cả 2 về cùng type.)
- 2026-07-01 15:21: **FIX tag chồng chéo → gọn gàng, thẳng hàng.** Đọc view mẫu `DK1 - 15` qua MCP
  (`get_current_view_elements` + `send_code_to_revit` biến `document`): tag head mẫu CÙNG localX = cropMax.X+0.886ft,
  Y RẢI ĐỀU (margin trên/dưới 0.427ft), thứ tự trên→dưới — KHÔNG bám tâm rebar. Bug cũ: MRA/RebarTagPlacer đặt tag
  head tại localCenter.Y → chồng. FIX: thêm `RevitAPP.Core/Services/CrossTagLayout.cs` (công thức thuần, có xUnit);
  `BeamAnnotator` cross branch sắp thép trên→dưới, tính slot đều cho TẤT CẢ (dọc+đai), truyền `tagHeadLocals` vào
  `MultiRebarAnnotationPlacer.Place(...)` + `RebarTagPlacer.TagRebars(...)`. Tests **88/88 pass**; Debug.R25 0 error.
  DLL `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,682,880 bytes, 15:21). Memory: [[beam-drawing-cross-tag-layout]].
  ⛔ Chờ smoke xác nhận tag thẳng hàng đúng như DK1-15/DK1-17.

- 2026-07-01 14:39: **Dùng Revit MCP đọc trực tiếp target view `DS1-01 - 4` (Detail, 1:25).**
  View thật có 23 elements: dầm `250x450` Mark `DS1-01`; 3 nhóm D16 + 1 nhóm D6; 3
  `MultiReferenceAnnotation`, 3 Structural Rebar Tags đi kèm MRA, 1 Rebar Tag đai, 6 Dimensions,
  1 Spot Elevation và 1 Detail Item. Type thực tế: MRA `BS-A2_SL & DK (MCN)-P`; tag MRA
  `@BSA2-MRA_SL&DK_BOT: A2_P_MRA_SL&DK_BOT`; tag đai
  `@BSA2-RT_BOT: A2_P_RT_DK&KC_BOT`; dim hình học `@BS-Dim A2`; dim đi cùng MRA
  `BS - Dim thép MCN Cột, Vách, Dầm`; spot `BS-2-Cao độ mặt đứng`.
  Detail Item type ID 435327 = `@BS-Break Line _Nhieu ty le: 1-10`; project còn 1-5/20/25/50/100/200.
  Từ phát hiện này đã bỏ input D0–D5 mang tính suy đoán, thay bằng 4 combo đúng bản chất:
  MRA dọc GỐI, tag đai GỐI, MRA dọc NHỊP, tag đai NHỊP; thêm Detail Item tham chiếu.
  `ProjectResources` nạp `MultiReferenceAnnotationType`; domain thêm `CrossAnnotationConfig`.
  Engine thêm `MultiRebarAnnotationPlacer`: mỗi nhóm thép dọc tạo 1 MRA (tự sinh dimension+tag),
  thép đai vẫn dùng IndependentTag. Khi chưa có preset, form tự chọn các type MCP học được ở trên.
  Tests **85/85 pass**, Debug/Release R25 **0 error**. Phát hành lại
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,648,064 bytes), SHA-256
  `AB654CB986B262AF692FE552FFC2B59CE3132D079404340E193AFCDEBE4DF181`.
  `send_code_to_revit` custom query bị timeout, nhưng các MCP command built-in
  `get_current_view_info/get_current_view_elements/get_available_family_types` hoạt động và cung cấp dữ liệu trên.
- 2026-07-01 14:29: **Sheet đích đổi sang chọn từ các Sheet có sẵn.** `ProjectResources` thêm
  `ExistingSheets` (`ProjectSheetOption`: Number/Name/Display); `ProjectResourceProvider` quét toàn bộ
  `ViewSheet` không phải placeholder và sắp theo Sheet Number. UI thay Title Block + ô nhập sheet bằng
  combobox editable `Sheet Number — Sheet Name`, hỗ trợ gõ tìm; number/name bên dưới readonly.
  ViewModel bắt buộc `SelectedExistingSheet != null`, preset tự dò lại sheet theo number. `SheetBuilder`
  không còn tạo sheet mới: chỉ resolve sheet hiện hữu; nếu sheet bị xóa sẽ throw thông báo yêu cầu chọn lại.
  Lần build đầu phát hiện/fix `ProjectResources.Empty` dùng sai `string[]` cho sheet option. Sau fix:
  tests **85/85 pass**, Debug/Release R25 **0 error**. Phát hành lại
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,638,336 bytes), SHA-256
  `A1B1AFF0A1A2E4274396D1DE52A14CFC9F83E806A535C0105FF677EE70817038`.
- 2026-07-01 14:22: **User thu hẹp scope: chỉ phát triển MẶT CẮT NGANG DẦM.** UI đã bỏ hoàn toàn
  Sectional Elevation, T1/T2/MID, break line, Long Section và các flags không liên quan. Form mới còn 3 vùng:
  preset; sơ đồ `GỐI/NHỊP` + DIM offsets; cấu hình cross D0–D5/spot/dim/template/viewport/section/scale/sheet.
  `BeamDrawingViewModel.BuildSetting` ép `LongSection=false`, `CrossSection=true`, `BreakLine=false`, xóa tag
  T1/T2/MID. Engine chỉ tạo 2 cross views tại station 0.1 (`GỐI`) và 0.5 (`NHỊP`), đặt ngang cạnh nhau trên
  sheet. View Name giữ Mark để unique; `VIEW_DESCRIPTION`/Title on Sheet đặt `GỐI` và `NHỊP`.
  `DimensionPlacer` tạo dim rộng, dim tổng chiều cao và dim chuỗi lớp thép song song. Target output user gửi:
  spot +4.150; dim 450 và 100/350; rộng 250; tag 3D16/2D16; đai GỐI a100 và NHỊP a200;
  title `GỐI/NHỊP` + `TL 1:25`. Tests **85/85 pass**, Debug/Release R25 **0 error**. Phát hành lại
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,634,240 bytes), SHA-256
  `39140B81F66000668C9BEFF60F37449B6C2DFC2FE181CA7DC38BC7C804834AF0`.
  **Smoke bắt buộc tiếp theo:** xác nhận reference tạo được cả dim tổng + dim chuỗi, tag family đọc đúng spacing
  thực tế, Title on Sheet hiển thị qua viewport type đã chọn.
- 2026-07-01 14:15: **Đã nhận INPUT mẫu gốc và sửa lại mapping chính xác theo sơ đồ — entry 14:10
  bên dưới bị supersede.** UI đổi tỷ lệ cột thành 220/570/* giống mẫu: Setting List trái, sơ đồ lớn giữa,
  ma trận setting hai cột bên phải. Sơ đồ WPF code-native hiển thị mặt đứng T1/T2/MID và hai mặt cắt:
  vùng đầu dầm dùng D3/D4/D5, giữa nhịp dùng D0/D1/D2. Engine `BeamAnnotator` map tương ứng:
  giữa nhịp D0=nhóm dọc trên, D1=các nhóm dọc còn lại, D2=đai; vùng đầu D5=nhóm dọc trên,
  D3=các nhóm dọc còn lại, D4=đai. Ngưỡng vùng đầu hiện là station `<0.25` hoặc `>0.75`;
  cần smoke xác nhận với model thật. Tests **85/85 pass**, Debug/Release R25 **0 error**. Phát hành lại
  `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,636,288 bytes), SHA-256
  `6FD8178C57964CC5359078E3195C1B8A5FAB7E0D4FED3179260725B903FAA422`.
- 2026-07-01 14:10: **Sửa UX theo feedback smoke: T1/T2/D0–D5 khó hiểu.** Bỏ ảnh screenshot thu nhỏ
  ở cột giữa, thay bằng bảng quy ước chữ lớn: T1=thép dọc dưới, T2=thép dọc trên, MID=thép giữa/đai;
  D0–D4=các nhóm thép dọc thực tế theo cao độ trên→dưới (cùng cao độ trái→phải), D5=thép đai.
  Nhãn từng combobox đã đổi sang mô tả tiếng Việt và thêm tooltip. Engine `BeamAnnotator` cũng được sửa
  khớp UI ở bản trung gian (mapping này đã được thay lại lúc 14:15 theo INPUT mẫu). Xóa frame screenshot
  khỏi resource assembly. Tests **85/85 pass**, Debug/Release R25 **0 error**.
  Đã phát hành lại `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,636,288 bytes), SHA-256
  `41F1D620836DD9EECCA41FFC724EA5689AAF0922ED087AD1D1AD8165A43F7B23`.
- 2026-07-01 14:04: **Đã phát hành bản Release.R25 cho Add-In Manager.** Build Release thành công
  **0 error** và copy đè vào `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll` (2,975,744 bytes).
  SHA-256 nguồn/đích trùng nhau:
  `9C0E5EA5250CE3FC8465DABB937E2CE3644E9A2D43AD415714043A32D7E7A998`.
  Trong Revit chọn Add-Ins → Add-In Manager → Load file trên → chạy
  `RevitAPP.Commands.BeamDrawingCommand`. Đây là bản Release để smoke; Phase 9 vẫn chờ kết quả geometry thực tế.
- 2026-07-01: **Phase 9 code complete, smoke pending.** Thêm test thuần `BeamAnnotationMathTests`
  cho station range/tolerance và D0–D5 slot clamp; tổng **85/85 tests pass**. Build Debug.R25 và
  Release.R25 đều **0 error**. Review caller phát hiện/fix edge case: bật Break Line nhưng tắt tag/dim/spot
  vẫn phải mở T2. DLL smoke đã copy tới `C:\Users\Admin\Desktop\RevitAPP-test\RevitAPP.dll`
  (3,009,536 bytes, 2026-07-01 14:01 local). Phase 9 chưa đánh dấu done vì Revit API geometry/reference
  chỉ xác nhận được trong model thật.
- 2026-07-01: **Phase 9b (cross annotations) đã code + build xanh.** `DimensionPlacer` thêm
  `PlaceCrossDimensions`: dim rộng từ 2 mặt bên, dim cao dạng chuỗi từ mặt đáy → các lớp rebar theo Z →
  mặt đỉnh; áp Cross Dimension Type, DS1, distance-to-bot và spacing factor (paper spacing × view scale).
  `RebarTagPlacer` cũng dùng spacing factor để đẩy tag head ra ngoài crop. Cross view giờ đặt Spot Elevation
  tại đúng station (không cố định giữa nhịp), áp type/offset và leader theo hướng view. Tất cả là best-effort:
  reference rebar/face nào Revit không chấp nhận sẽ warn, không crash/rollback toàn feature. Build Debug.R25
  **0 error**. **Chưa smoke:** cần xác nhận `new Reference(rebar)` tạo được chuỗi dim lớp trong model thật;
  nếu không, thay bằng subelement/geometric references sau khi kiểm tra RevitLookup.
- 2026-07-01: **Phase 9a (engine type wiring) đã code + build xanh, Phase 9 CHƯA hoàn tất.**
  `BeamDrawingOrchestrator/SheetBuilder` áp Section Type + View Template riêng, Title Block, Viewport Type,
  Sheet Number/Name và View Name từ setting. `ViewBeamPair` lưu `Station`; `BeamAnnotator` lọc rebar theo
  bounding projection tại từng station 0.1/0.5/0.9, vì vậy các cross section không còn dùng mù cùng một
  danh sách rebar. Tag sectional phân loại top/bottom/mid; đai được nhận diện đúng API R25 qua
  `RebarShape.RebarStyle` (không dùng `Rebar.Style`, đã gây compile error lần đầu). D0–D5 được áp theo
  thứ tự rebar thực tế đã lọc/sort trong từng cross view. `SpotElevationPlacer` áp type + offset mm và leader
  theo `View.UpDirection/RightDirection`; `DimensionPlacer` áp dim type + bot-face offset; thêm
  `BreakLinePlacer` đặt 2 detail components ở hai đầu crop sectional. Build Debug.R25 **0 error**.
  **Còn lại Phase 9:** dimension chiều rộng/cao + chia lớp ở cross, dùng spacing factor/DS1 đầy đủ, test logic
  thuần nếu tách được, chạy 77 tests, build Release.R25 và smoke thật để xác nhận tag station/break-line API.
- 2026-07-01: **Phase 8 hoàn tất ở mức code/build.** Thay `BeamDrawingViewModel` bằng bản v2 với toàn bộ
  field trong SPEC mục A; wire `BeamDrawingPresetStore` cho Add/Update/Delete/Load/Export/Up/Down và tự lưu.
  Thay `BeamDrawingWindow.xaml` bằng layout 3 vùng 1320×800: Setting List, ảnh/số DIM, hai cột cấu hình
  Sectional/Cross. XAML chỉ dùng `DynamicResource`, không hardcode màu/`StaticResource`; code-behind vẫn chỉ
  InitializeComponent + DataContext + CloseRequested. Dùng resource ảnh
  `docs/trien-khai-dam-frames/frame_005.jpg`. Bổ sung hai field tùy chọn
  `DrawingFlags.LongSectionViewName/CrossSectionViewName` để preset roundtrip không mất View Name.
  Build Debug.R25 `-p:DeployAddin=false` **0 error**, tests **77/77 pass**.
  **Chưa verify trực quan trong Revit:** cần smoke kích thước cửa sổ, scroll, combo item và dialog import/export.
  **Điểm tiếp quản:** Phase 9 — resolve/truyền type IDs, viewport type, tag mapping, break line, dim/spot.
- 2026-07-01: **Phase 7 hoàn tất.** `ProjectResources` có đủ nguồn tên cho Rebar Tag, Spot Elevation,
  Dimension, Section Type, View Template, Viewport, Break Line và Title Block. `ProjectResourceProvider`
  nạp theo đúng category/class, hiển thị `Family: Type` cho FamilySymbol, đồng thời có các resolver
  `ResolveRebarTagType/ResolveSpotType/ResolveDimType/ResolveViewportType/ResolveBreakLineSymbol/ResolveTitleBlock`
  với fallback + cảnh báo tiếng Việt. Thêm `RevitAPP.Core/Services/BeamDrawingPresetStore.cs`: JSON schema
  `{version:1,presets:[...]}`, mặc định `%APPDATA%\RevitAPP\beam-drawing-presets.json`, hỗ trợ
  Load/Save/Import/Export và đọc lỗi an toàn. Test mới ở `BeamDrawingPresetStoreTests.cs`;
  `dotnet test` **77/77 pass**, build Debug.R25 `-p:DeployAddin=false` **0 error**. Warning build còn lại
  là warning cũ ở BeamRebarPro/ElementIdHelper/TogglePointCloudPanel/GeminiTranslationService/ILRepack.
  **Điểm tiếp quản:** bắt đầu Phase 8, thay ViewModel/XAML v1 bằng UI 3 vùng và wire preset store.
- 2026-07-01: Phase 6 (v2 domain) xong. Refactor `BeamDrawingSetting` sang v2 đầy đủ form BIMSpeed. THÊM record: `RebarTagSet` (T1/T2/MidItem/D0–D5 + RebarBreakSymbol), `SpotElevationConfig` (Enabled/TypeName/OffsetMm), `DimensionConfig` (Enabled/SE+CS dim type/SpacingFactor/DistanceToSideBeamMm/DistanceToBotFaceMm), `PerViewConfig` (Scale/SectionTypeName/ViewTemplateName/ViewportTypeName), `DrawingFlags` (LongSection/CrossSection/CrossSectionForMultiBeam/PickPillowToDim/CreateView3D). XOÁ `ViewConfig.cs` + `AnnotationFlags.cs` (thay bằng PerViewConfig + sub-config). Factory/Validator cập nhật (default scale 25, spacingFactor 6, DS1/botFace 200). ViewModel v1 + engine (Orchestrator/Annotator/RequiredFamilyValidator) cập nhật để compile: annotation gate giờ đọc `setting.Dim.Enabled`/`setting.Spot.Enabled` + tag mặc định document. `dotnet test` 73/73 pass; build Debug.R25 0 error. UI v1 vẫn chạy (chưa clone form — Phase 8). **DỪNG Ở ĐÂY — bàn giao cho AI khác viết Phase 7–9** (xem `plans/260701-1144-beam-drawing-in-revitapp/NEXT-AI-PROMPT.md`).
- 2026-07-01: Code review (code-reviewer) — 5/5 acceptance PASS, không regression. Đã FIX: **H1** Scale bind sang `ScaleText` (string) + parse trong Ok() (tránh binding im lặng khi nhập chữ); **H2** `DimensionPlacer.GetEndReferences` giờ chọn 2 mặt có normal song song trục dầm (2 mặt đầu), sort theo chiếu lên trục lấy 2 đầu xa nhất — thay vì 2 face đầu tiên tuỳ ý; **M2** `BeamAnnotator` gom Rebar theo host 1 lần (`GroupRebarsByHost`) thay vì scan toàn document mỗi dầm×view → bỏ `BeamRebarLocator.cs` (không còn dùng). Build Debug.R25 0 error, test 72/72 pass.
  CÒN LẠI (verify khi smoke, KHÔNG chặn ship): **M1** `SpotElevationPlacer` bend/end offset dùng trục world (0,0,1)/(1,0,0) — leader có thể lệch hướng nếu dầm không song song trục X; chỉnh sang hướng view khi thấy sai. **M3** cross-section dùng CHUNG scale với sectional (UI 1 ô scale) — hiện là chủ ý v1.
- 2026-07-01: Tạo plan + HANDOFF. Code chưa bắt đầu.
- 2026-07-01: Phase 5 code xong. `RevitAPP/Services/BeamDrawing/{RebarTagPlacer,DimensionPlacer,SpotElevationPlacer,RequiredFamilyValidator,BeamAnnotator}.cs` (BeamRebarLocator đã gộp vào BeamAnnotator ở bước review-fix); command set `Annotator = new BeamAnnotator()` + prepend family warnings. RebarTagPlacer tái dùng logic đã verify (SetUnobscuredInView → regenerate → IndependentTag.Create với subelement reference, LeaderEndCondition.Attached). Dimension + spot elevation là BEST-EFFORT (lỗi→warn, không chặn) — CHƯA verify trong Revit, dễ cần chỉnh reference/vị trí khi smoke. Debug.R25 + Release.R25 0 error; `dotnet test` 72/72 pass.
  ⛔ **SMOKE CHƯA CHẠY** — cần load qua Add-In Manager + verify checklist (phase-05). Việc còn lại: (1) verify section view không rỗng/nhầm hướng, (2) tag hiện đúng thép, (3) dimension/spot có ra không (dễ fail reference — chỉnh DimensionPlacer.GetEndReferences / SpotElevationPlacer nếu cần), (4) viewport layout không chồng.
- 2026-07-01: Phase 4 xong. `RevitAPP/Services/BeamDrawing/{ProjectResourceProvider,SectionViewBuilder,SheetBuilder,BeamDrawingOrchestrator,ViewBeamPair}.cs`; `RevitAPP/Commands/BeamDrawingCommand.cs` wire thật (pick→dialog owner=MainWindowHandle→Generate). Orchestrator: TransactionGroup + T1 (view+sheet+viewport) commit; T2 annotation qua hook `IBeamAnnotator Annotator` (null lúc này → Phase 5 gán, chạy sau doc.Regenerate()). Tên view `MCD-{Mark}`/`MCN-{Mark}-{i}`, fallback `Dam-{id}`. Cross section 3 vị trí 0.1/0.5/0.9. BeamDrawingResult map ElementId.Value→long. Build Debug.R25 0 error. CHỜ SMOKE: view có rỗng/nhầm hướng không, viewport layout có chồng không.
- 2026-07-01: Phase 3 xong. `RevitAPP/ViewModels/BeamDrawingViewModel.cs` + `RevitAPP/Views/BeamDrawingWindow.xaml(.cs)`. Modal, MVVM CommunityToolkit ([ObservableProperty]/[RelayCommand]), Theme.xaml DynamicResource (đã verify 11 resource key tồn tại). Combo section-type/template/title-block có mục "(Mặc định)" = null. OkCommand validate qua BeamDrawingSettingValidator, sai → MessageBox không đóng. Pattern CloseRequested→DialogResult giống ColumnRebarView. `ProjectResources` record (Core) làm nguồn combo — Phase 4 provider sẽ nạp. Build Debug.R25 0 error, XAML compile OK. (Owner=MainWindowHandle set ở command Phase 4.)
- 2026-07-01: Phase 2 xong. `RevitAPP/Services/BeamDrawing/{BeamSelectionFilter,BeamPicker,BeamGeometryReader,SectionPlaneCalculator}.cs`. BeamPicker ưu tiên dầm đã select trước, else pick tương tác. GeometryReader đọc b/h từ param structural-section → fallback bbox; Top/Bottom Z từ bbox solid → fallback Z location line. SectionPlaneCalculator tái dùng box math đã verify từ src/BeamDrawing.Addin (map sang Core BeamGeometry/Point3). Build Debug.R25 0 error. Geometry/transform CHỈ verify được khi smoke Revit thật (Phase 5).
- 2026-07-01: Phase 1 xong. Domain models thuần đặt trong **RevitAPP.Core** (KHÔNG dùng `<Compile Include>` link như plan gợi ý — theo convention: RevitAPP.Tests đã reference RevitAPP.Core). File: `RevitAPP.Core/Models/BeamDrawing/{ViewConfig,SheetConfig,AnnotationFlags,BeamDrawingSetting,BeamGeometry,BeamDrawingResult}.cs` + `RevitAPP.Core/Services/{BeamDrawingSettingFactory,BeamDrawingSettingValidator}.cs`. Test: `tests/RevitAPP.Tests/BeamDrawingSettingTests.cs`. `dotnet test` 72/72 pass; addin build Debug.R25 0 error. LƯU Ý cho Phase 4: BeamDrawingResult dùng `long` id → lớp Revit map từ `ElementId.Value`.
- 2026-07-01: Phase 0 xong. Gỡ 2 ProjectReference BeamDrawing.Addin/Core khỏi RevitAPP.csproj; xoá `using BeamDrawing.Addin.Commands` trong Application.cs; tạo `RevitAPP/Commands/BeamDrawingCommand.cs` (stub native); button "Ban Ve Dam" trỏ `RevitAPP.Commands.BeamDrawingCommand`. Build Debug.R25 -p:DeployAddin=false 0 error. Không còn symbol BeamDrawing.Addin/Core trong .cs.
