# BeamRebarPro — Hand-off (cho người/AI tiếp theo)

> Add-in Revit MỚI vẽ thép dầm 3D theo TCVN. Độc lập hoàn toàn với `BeamRebar.*` (v1) cũ —
> KHÔNG sửa, KHÔNG đọc các project cũ. Mọi thứ nằm trong `src/BeamRebarPro/`.

## Trạng thái (cập nhật 2026-06-18)

| Hạng mục | Trạng thái |
|---|---|
| Scaffold Nice3point (Revit 2025, WPF, DI container, Serilog) | ✅ xong |
| Domain model thuần (Models/) | ✅ xong |
| Span model + SpanModelBuilder (gom nhiều nhịp) | ✅ xong |
| Validator + Factory (+ ResolveForSpan per-span) | ✅ xong |
| Geometry reader + picker (Revit API) | ✅ xong |
| Rebar creators: Longitudinal (neo+móc), Additional (cắt %), Stirrup (3 vùng), AntiBulge | ✅ xong |
| Orchestrator (1 transaction, per-span) | ✅ xong |
| WPF UI modeless + per-span detail | ✅ xong |
| `dotnet build -c Debug.R25` | ✅ pass |
| Test xUnit thật (logic thuần) | ✅ **17/17 pass** |
| **F5 / Add-in Manager smoke test trong Revit thật** | ⛔ **CHƯA — blocked bởi phiên Revit đã load DLL cũ / MCP socket không chạy** |
| `Release.R25` build | ⛔ chưa chạy |

## Kiến trúc (data flow)

```
UI (BeamRebarProViewModel, flat fields + per-span Spans)
  → BuildModel() → QuickSettingModel (+ SpanOverrides)
  → QuickSettingValidator (chặn cấu hình sai)
  → ExternalEvent (RebarCreationHandler) ── chạy trên main thread Revit
       → BeamPicker (chọn dầm)
       → BeamRebarOrchestrator.Create(doc, beams, model)  [1 transaction]
            → BeamGeometryReader (đọc b/h, cao độ thật)
            → SpanModelBuilder.Build → BeamRun (nhiều nhịp + cảnh báo)
            → mỗi nhịp: QuickSettingFactory.ResolveForSpan (hợp nhất override)
                 → LongitudinalBarCreator (thép chủ: neo + móc hook)
                 → AdditionalBarCreator   (gia cường cắt theo %)
                 → StirrupCreator         (đai 3 vùng End1/Mid/End2)
                 → AntiBulgeCreator       (thép chống phình dầm cao)
       → RebarCreationResult (count + warnings tiếng Việt) → cập nhật UI
```

## Quy ước QUAN TRỌNG (đọc trước khi sửa)

1. **Code thuần vs Revit API:**
   - File KHÔNG `using Autodesk.Revit` (toàn bộ `Models/*`, `Services/SpanModelBuilder/QuickSettingValidator/QuickSettingFactory`) = test được out-of-process.
   - Project test `tests/BeamRebarPro.Tests` **link trực tiếp** các file thuần này (`<Compile Include>`), KHÔNG copy. Thêm file Core thuần mới trong `Models/` → glob tự nhận; file chạm Revit API thì ĐỪNG link vào test.
2. **Rebar geometry chỉ verify được trong Revit thật.** KHÔNG viết "unit test" giả lập `Rebar.CreateFromCurves` — đó là bịa. Phần này test bằng F5/Add-in Manager.
3. **Đơn vị:** Revit nội bộ = feet; UI = mm. Convert qua `/304.8`. Không hardcode đơn vị lẫn lộn.
4. **Deploy khi Revit đang mở bị LOCK DLL.** Build kiểm tra dùng `-p:DeployAddin=false`. Test trong Revit: build ra DLL rồi load qua **Add-in Manager** (không cần đóng Revit).
5. **Không dùng ILRepack cho R25/.NET 8 hiện tại.** `IsRepackable=false` để tránh conflict assembly trong Revit 2025 (`netstandard 2.0.0.0` vs `2.1.0.0`) thấy trong journal khi bấm command. Dùng `EnableDynamicLoading=true` + deps cạnh DLL.

## Lệnh hay dùng

```powershell
# Build kiểm tra compile (Revit đang mở vẫn chạy được):
cd src/BeamRebarPro ; dotnet build -c Debug.R25 -p:DeployAddin=false

# Chạy test logic thuần:
cd tests/BeamRebarPro.Tests ; dotnet test

# Build deploy (CHỈ khi muốn nạp vào Revit — Revit phải đóng hoặc dùng Add-in Manager):
cd src/BeamRebarPro ; dotnet build -c Debug.R25
```

## VIỆC TIẾP THEO (ưu tiên giảm dần)

1. **F5/Add-in Manager smoke test trong Revit thật.** Mở model có sẵn `RebarBarType` (D6..D25) + `RebarHookType`.
   Ghi chú 2026-06-18:
   - Build kiểm tra đã chạy lại: `dotnet build -c Debug.R25 -p:DeployAddin=false` ✅ pass, còn 4 warning obsolete `Context.UiApplication`.
   - Test logic đã chạy lại: `dotnet test` ✅ 17/17 pass.
   - Journal Revit 2025 (`journal.0673.txt`, khoảng 14:33-14:38) cho thấy add-in đã tạo ribbon/button, nhưng command cũ ghi `API_ERROR`: `Assembly version conflict in some references in BeamRebarPro.dll` / `netstandard 2.0.0.0 conflicts with ... 2.1.0.0`.
   - Đã sửa chuẩn bị smoke test tiếp theo: bỏ `ManifestSettings` khỏi `BeamRebarPro.addin`, đổi `IsRepackable=false`, loại runtime asset của `JetBrains.Annotations`.
   - Deploy build `dotnet build -c Debug.R25` đã cập nhật `%APPDATA%\Autodesk\Revit\Addins\2025\BeamRebarPro\BeamRebarPro.dll` sang bản non-repack (172 KB), nhưng có warning retry vì Revit PID 2080 đang giữ DLL.
   - Smoke test geometry **chưa xác nhận** vì Revit session hiện tại đã load client id/DLL cũ; MCP bridge `localhost:8080` cũng `actively refused`. Cần restart Revit hoặc load DLL mới bằng Add-in Manager trong session sạch rồi test checklist dưới đây.
   Verify trực quan:
   - [ ] Móc neo đúng góc 135° đúng phía (bật checkbox "Uốn móc neo").
   - [ ] Thép gia cường top cắt quanh gối, bottom cắt giữa nhịp theo đúng %.
   - [ ] Dầm nhiều nhịp: đai 2 đầu dày / giữa thưa; thép từng nhịp đúng override.
   - [ ] Thiếu họ thép → báo lỗi tiếng Việt, KHÔNG crash Revit.
   - [ ] Thép nằm gọn trong host (không lòi ra ngoài bê tông / không "rebar outside host").
2. **Nếu geometry sai** → sửa trong `Services/Rebar/` (LongitudinalBarCreator vertical/lateral, StirrupCreator profile, SpanFrame trục). Ghi lại kết quả F5 vào file này.
3. **Thép chủ chạy suốt nhiều nhịp (continuous):** hiện mỗi nhịp host thép riêng (an toàn). Nối liên tục qua nhiều host là rủi ro cao (Revit yêu cầu 1 host/Rebar) — làm sau, có fallback per-span + warning.
4. **Lap splice (nối chồng):** chưa làm — defer.
5. Chạy `Release.R25` build trước khi giao.

## Giả định TCVN cần kỹ sư xác nhận (chưa chốt)

- Chiều dài neo `AnchorLengthMm` mặc định 300mm — phụ thuộc mác bê tông/thép, CHƯA tính theo công thức TCVN.
- Vùng đai dày = L/4 mỗi đầu khi không nhập `EndZoneLengthMm`.
- Cover mặc định 25mm.
- Ngưỡng thép chống phình h > 550mm.

## Kế hoạch gốc

`plans/260618-beamrebar-tcvn-v2-anchor-cutbar-continuous/` (plan + 5 phase file). Plan này ban đầu định "mở
rộng v1", nhưng user đổi hướng sang **addin mới hoàn toàn** → code thực tế nằm ở `src/BeamRebarPro/` (single-project).

## Cap nhat phien hien tai - 2026-06-18

- UI `BeamRebarProView.xaml` da chuyen sang huong Quick Setting giong form mau: so do mat cat dam ben trai, nhom nhap 1-7 cho thep chu/gia cuong/dai, nhom 8-12 cho sheet/support/secondary/multi/more settings.
- Da sua dung khai niem dam nhieu nhip: 1 dam vat ly dai co the chay qua nhieu cot/goi. Nguoi dung chon support/columns o muc 9; `InternalSupportPoints` se cat 1 dam dai thanh nhieu span tinh toan tren cung host.
- `SpanModelBuilder.Build(segments, internalSupportPoints)` da co test core moi: 1 dam dai 0..30 voi goi tai 10 va 18 tao 3 nhip, 4 goi.
- `BeamRebarProViewModel` da expose top/bottom additional layer 2 va dua vao `BuildModel()` (`TopAdditionalLayer2`, `BottomAdditionalLayer2`).
- Build offline: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass, 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.
- User bao Revit da tat; da chay deploy build `dotnet build -c Debug.R25` pass, 0 warning, copy vao Addins 2025.

## Cap nhat fix crash command - 2026-06-18

- User bao loi Revit "Command Failure for External Command" khi bam `BeamRebarPro.Commands.StartupCommand`.
- Journal `journal.0683.txt` cho thay add-in load thanh cong va command duoc goi, nhung khong ghi stack trace chi tiet.
- Da thay `StartupCommand.cs` bang ban co `try/catch` va `TaskDialog.Show("BeamRebarPro error", ex.ToString())` de lan sau neu con crash se hien exception that.
- Da don XAML nut Close: bo `{x:Static SystemCommands.CloseWindowCommand}` va dung `CloseButton_Click` trong code-behind de tranh XAML parse/runtime binding risk.
- Build offline: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass, 0 warning.
- Deploy build: `dotnet build -c Debug.R25` pass, 0 warning, copied to Addins 2025.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.

## Cap nhat fix XAML readonly binding - 2026-06-18

- User chup loi `XamlParseException`: `TextBox.Text` khong bind TwoWay duoc vao readonly property `SupportBeamSummary`.
- Da sua `BeamRebarProView.xaml`: `SupportBeamSummary` va `SecondaryBeamSummary` dung `Text="{Binding ..., Mode=OneWay}"`.
- Revit dang chay PID 22496 nen khong tu dong dong Revit va khong deploy de DLL tranh bi lock.
- Build offline: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass, 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.
- Can deploy lai sau khi dong Revit: `cd src/BeamRebarPro ; dotnet build -c Debug.R25`.
- User da dong Revit; da deploy lai bang `dotnet build -c Debug.R25` pass, 0 warning, copy DLL moi vao Addins 2025.

## Cap nhat thep chu lien tuc - 2026-06-18

- User yeu cau thep chu phai di xuyen suot dam, khong bi tach theo tung nhip/cot.
- Da thay `Services/Rebar/BeamRebarOrchestrator.cs`: main top/main bottom duoc tao mot lan tren toan bo physical beam host (`BeamSegment` day du), truoc khi tao cac thanh theo span.
- Additional bars, stirrups, anti-bulge bars van tao theo calculated span giua cac support/columns.
- Truong hop 1 dam dai qua nhieu cot: `InternalSupportPoints` chi chia nhip cho thep gia cuong/dai; thep chu van chay full length tren cung host dam.
- Build offline: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass, 0 warning.
- Test logic: `dotnet test` pass 18/18.
- Deploy build: `dotnet build -c Debug.R25` pass, 0 warning, copy vao Addins 2025.

## Cap nhat UI + support rebar logic - 2026-06-18

- User yeu cau giao dien giong anh Quick Setting mau hon, dai khong xuyen qua cot, thep tang cuong dung theo goi nhip khi dam qua nhieu cot.
- Da chinh `BeamRebarProView.xaml`: window 1025x525, bo status box lon (collapsed), can lai section diagram/leader, disable combo gia cuong khi checkbox tat de gan voi anh 2 hon.
- Da chinh `BeamRebarOrchestrator.cs`: top additional bars khong tao theo tung span nua; thay vao do tao quanh tung `run.Supports` tren full physical beam frame.
- Khi dam dai qua nhieu cot: cot chon o muc 9 -> `InternalSupportPoints` -> `SpanModelBuilder` tao `run.Supports`; thep tang cuong top bo tri quanh cac support do.
- Da chinh dai: stirrup span frame duoc cat lui khoi non-end supports bang `SupportClearanceFeet` (200mm), dung `run.Supports` da project len truc dam de tranh lech Z cua cot.
- Main top/bottom bars van tao 1 lan full length theo physical beam host.
- Build offline: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass, 0 warning.
- Test logic: `dotnet test` pass 18/18.
- Deploy build: `dotnet build -c Debug.R25` pass, 0 warning, copy vao Addins 2025.

## Cap nhat geometry dai + cover + UI diagram - 2026-06-18 (chieu)

UI:
- Bo tinh nang "Detail Rebar Forms / span overrides" (Expander). Da xoa `SpanDetailViewModel.cs`, `Spans`/`AddSpan`/`RemoveSpan` khoi ViewModel.
- So do mat cat: bo Canvas ve tay, thay bang `<Image>` nhung `Resources/Images/BeamSectionDiagram.png` (user cung cap) -> giong 100%. Da dang ky `<Resource>` trong csproj.
- Diameter combo hien "D16/D20" qua `ItemStringFormat="'D'0"`. Muc 9 text = "Beams work like support are selected!".
- 2 radio A1 (muc 5) loai tru nhau: `GroupName="StirrupMode"` + property `StirrupUniform = !StirrupTwoEnds`.

Logic thep (fix qua nhieu vong test that trong Revit):
- **Tu dong do cot**: `Services/ColumnDetector.cs` quet OST_StructuralColumns gan truc dam (tol 400mm) lam diem chia nhip. Orchestrator gop auto + cot chon tay (muc 9).
- **Thep tang cuong**: top vat qua moi cot GIUA (bo goi bien `IsEnd`); bottom giua moi nhip.
- **Cao do mat tren/duoi (FIX QUAN TRONG)**: doc tu SOLID THAT (`BeamGeometryReader.TryReadSolidZ` quet edge tessellation lay min/max Z), KHONG dung bbox (loi dai loi len) / param h / location-line Z. Fallback bbox neu khong co solid. -> dai nam dung tiet dien, cover top/bottom dung.
- **Lech ngang**: `BeamSegment.LateralOffsetFeet` bu justification ngang.
- **Moc dai**: `RebarHookOrientation.Left/Left` + hook 135, fallback tao dai khong moc neu Revit tu choi.
- User xac nhan "PHAI VAY CHU" -> dai + cover + moc DA DUNG.

Build offline pass 0 warning; `dotnet test` 18/18 pass.
DLL test: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll` (load qua Add-in Manager).

### Con lai
- Neo thep chu doc (anchor + hook 2 dau) co code nhung chua verify ky trong Revit nhu dai.
- Lap splice chua lam. `Release.R25` build chua chay.

## Cap nhat LON - Detail Forms + Pick-first + TCVN length tu mep cot - 2026-06-19

### 1. Quy trinh moi: Ribbon -> CHON DAM NGAY -> mo dialog
- `Commands/StartupCommand.cs`: bam Ribbon -> `BeamPicker` pick dam NGAY (dang trong API context) ->
  `BeamSpanReader.ReadSpans` doc nhip (tu do cot) -> `viewModel.SetPickedBeams(beams, spans)` -> moi mo Quick Setting.
  Huy (ESC) khi pick -> khong mo dialog.
- `Services/BeamSpanReader.cs` (MOI): doc SpanInfo tu beams da chon (dung chung command + handler).
- `RebarCreationHandler.PreselectedBeams`: dung dam da chon, khong pick lai khi tao thep.
- `RebarCreationRequest.PickBeamInfo` + `OnBeamInfo`: pick dam trong dialog tra ve List<SpanInfo>.

### 2. Man "Beam Rebar" (Detail Rebar Forms) - DA LAM
- `ViewModels/BeamRebarDetailViewModel.cs` + `Views/BeamRebarDetailView.xaml(.cs)`.
- 6 tab Setting: Main Top/Bot, Add Top/Bot, Stirrup (4 sub-tab), Anti-bulge. Form chi tiet tung tab (visibility theo tab).
- Mo tu nut "Go to Detail Rebar Forms" o Quick Setting. KHONG dang ky Host (tao truc tiep trong `GoToDetailRebarForms`).
- **QUAN TRONG - tranh Revit fatal error**: Detail VM TAI DUNG `_handler` + `_externalEvent` cua Quick Setting
  (truyen qua constructor), KHONG tu tao ExternalEvent (tao sai luong -> crash).
- **Quick Setting & Detail LIEN LAC THAT**: Detail VM giu tham chieu `BeamRebarProViewModel parent`.
  `LoadFromParent()` khi mo, `WriteBackToParent()` truoc khi tao thep -> 2 man dong bo.
- Hinh hoa DONG (ve bang Canvas, khong dung anh PNG): vung Image ve tiet dien + 2 hang cham thep,
  so cham = Number nhap, highlight hang theo tab. KHONG con o "% Length" (da bo).
- Bang nhip (DataGrid) duoi: nut "Pick Beam" -> chon dam -> hien L tung nhip + chieu dai thep gia cuong
  auto (TCVN) hoac nhap tay (checkbox Top Auto / Bot Auto). `SpanRowViewModel`, `SpanInfo`.

### 3. Chieu dai thep gia cuong theo TCVN - TINH TU MEP COT (da fix nhieu vong)
- Bo % hoan toan, dung mm tuyet doi: `AdditionalBarConfig.LengthMm` (top = moi ben; bot = doan giua). 0 = auto TCVN.
- **TOP** (`BeamRebarOrchestrator.CreateTopAdditionalAtSupports`): quanh MOI got (CA COT BIEN dau/cuoi, da bo
  `if IsEnd continue`). Moi ben = 0.25L (L nhip ke) TU MEP COT = tim ± nua be rong cot. Got bien chi keo 1 phia (nhip null -> extend 0).
- **BOT** (`AdditionalBarCreator.CreateMidspan`): tinh tren L THONG THUY (khoang ho 2 mep cot) =
  span tim-tim - nua cot trai - nua cot phai. Thep tu 1/8 den 7/8 L thong thuy -> **2 dau cach mep cot BANG NHAU** (user chot doi xung).
- **Be rong cot**: `ColumnDetector.FindAllColumnHits` do TAT CA cot giao dam (ke ca 2 dau mut), tra `ColumnHit(Location, HalfWidthFeet)`.
  `Support.HalfWidthFeet` (MOI). Orchestrator `EnrichSupportsWithColumnWidth` gan width cho MOI got (ke ca bien).
  Cot phai la Structural Column (OST_StructuralColumns) giao dung dam (tol 400mm), neu khong -> width=0 -> tinh tu tim.
- **Layer 2**: Detail BuildModel map Layer=2 -> day vao TopAdditionalLayer2/BottomAdditionalLayer2 (orchestrator lui thep vao ~30mm).

### Trang thai
- Build `Debug.R25 -p:DeployAddin=false` pass 0 warning. `dotnet test` 18/18 pass.
- DLL test moi nhat: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### VIEC TIEP THEO (user dang test, co the con chinh)
1. **Verify thep top tai cot bien** (vua sua) trong Revit - user dang kiem tra. Neu sai, xem `CreateTopAdditionalAtSupports`.
2. **Override per-span tu bang**: hien Detail chi lay mm tu NHIP DAU ap chung (gioi han 1 config). Neu can moi nhip
   chieu dai rieng -> phai truyen SpanRow per-span xuong engine (chua lam).
3. **Stirrup sub-tabs** (Additional/Hanger/Shape) + Anti-bulge form -> dien engine that (hien chi UI).
4. **Ve mat dung dam** (Span 0/1) trong Detail - hien la DataGrid, chua ve hinh elevation.
5. Image minh hoa tung tab Detail (Add Type 1/2, Stirrup section...) - hien chi co so do cham dong cho Main/Add.
6. Lap splice, Release.R25 build.

### LUU Y CHO AI SAU
- Geometry rebar CHI verify duoc trong Revit that (F5/Add-in Manager) - KHONG viet unit test gia lap Revit API.
- Build kiem tra LUON dung `-p:DeployAddin=false` (Revit dang mo se lock DLL). Test trong Revit: load DLL tu
  `C:\Users\Admin\Desktop\BeamRebarPro-test\` qua Add-in Manager (khong can dong Revit).
- User chot: thep bot 2 dau BANG NHAU (doi xung 1/8 moi dau), KHONG phai 1/8-2/8 lech.
- Cao do mat tren/duoi dam doc tu SOLID that (`BeamGeometryReader.TryReadSolidZ`) - DA DUNG, dung sua lai.

## Cap nhat thep chu tren be xuong - 2026-06-19

- User yeu cau thep chu lop tren chay suot dam nhung 2 dau phai be xuong nhu hinh, va nguoi dung chinh duoc chieu dai doan be xuong.
- Da them `MainBarConfig.TopEndBendDownLengthMm` (mm, 0 = de thang/hook cu). Quick Setting co o `Bend down (mm)` cho Main Top, mac dinh 300mm. Detail Form co o `Top bend down (mm)` trong Main bar tab, dong bo nguoc ve Quick Setting.
- `LongitudinalBarCreator` tao MainTop dang polyline 3 doan: dau trai xuong -> ngang tren -> dau phai xuong. Chieu dai be xuong duoc clamp trong chieu cao dam/cover de tranh rebar outside host. MainBottom van tao thang nhu cu.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`. Can verify geometry polyline trong Revit that bang Add-in Manager.

## Cap nhat thep gia cuong top bien be moc - 2026-06-19

- User yeu cau thep gia cuong lop tren tai 2 bien dam cung phai be moc xuong o dau ngoai.
- Da them `AdditionalBarConfig.EdgeHookDownLengthMm` (mm, 0 = khong be). Quick Setting co o `Edge hook (mm)` trong muc Top Additional, mac dinh 300mm. Detail Form co o `Edge hook down (mm)` trong Add Top Bar, dong bo ve Quick Setting.
- `CreateTopAdditionalAtSupports`: neu support la goi bien dau (`support.IsEnd && Index == 0`) thi tao segment top additional co moc xuong o dau start; neu goi bien cuoi thi moc xuong o dau end. Goi giua van tao thanh gia cuong thang nhu cu.
- `LongitudinalBarCreator.CreateSegmentWithEndBends` tao polyline cho thanh gia cuong co moc 1 dau/2 dau, clamp chieu dai moc trong chieu cao dam/cover.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`. Can verify trong Revit that: top additional tai 2 cot bien moc xuong dung dau ngoai, cac goi giua khong bi moc.

## Cap nhat khoang cach dai toi cot - 2026-06-19

- User bao thep dai 2 bien dang lan vao cot, muon dai dau tien cach cot 50mm.
- Da them `StirrupConfig.FirstDistanceFromSupportMm` mac dinh 50mm. Quick Setting co o `First to column`, Detail Form field `Distance of first stirrup to the column (mm)` da duoc map xuong engine.
- `TryCreateStirrupFrame` bay gio cat frame dai o ca dau/cuoi span theo `support.HalfWidthFeet + FirstDistanceFromSupportMm`, tuc la do tu MEP COT neu do duoc width cot, fallback 50mm tu support/end neu khong co width.
- Thay doi nay ap dung cho ca goi bien va goi giua, giu dai khong an vao cot.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`. Can verify trong Revit that dai dau tien cach mep cot 50mm.

### Fix lan 2 cho dai bien van lan cot - 2026-06-19

- User test that bao 2 bien van lan vao cot. Nguyen nhan kha nang cao: support bien khong match duoc `ColumnHit` vi column center cach endpoint > `SupportToleranceFeet` 60mm, nen `HalfWidthFeet=0` va chi lui 50mm tu dau dam.
- Da sua `EnrichSupportsWithColumnWidth`: match support voi cot neu khoang cach <= `max(60mm, HalfWidthFeet + 100mm)` thay vi chi 60mm. Nhu vay support o mep cot/tim cot deu lay duoc nua be rong cot.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`. Can verify lai 2 bien.

### Fix lan 3 cho dai bien van con lan cot - 2026-06-19

- User bao co cai thien nhung 2 bien van con lan cot. Nguyen nhan sau hon: `ColumnDetector.FindAllColumnHits` clamp projection cua column center ve endpoint, lam mat thong tin tim cot cach endpoint bao nhieu. Vi vay support o mep cot van tinh clearance chua qua het cot.
- Da sua `FindAllColumnHits`: van dung projection clamped de check distance, nhung luu `ColumnHit.Location` la projection tim cot that tren truc dam (khong clamp).
- Da sua `EnrichSupportsWithColumnWidth`: `Support.HalfWidthFeet` bay gio luu khoang tu support den mep cot phia trong = `distance(support, columnCenterProjection) + columnHalfWidth`. Nhu vay neu endpoint nam o mep/ngoai cot, dai se lui qua toan bo phan cot + 50mm.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`. Can verify lai 2 bien.

## Cap nhat layout nhom thep doc Fixed Number - 2026-06-19

- User yeu cau layout nhom thep phai dung Fixed Number, va tung nhom rieng: thep chu rieng, thep gia cuong rieng. Vi du Main Top 2D16 -> mot rebar set Fixed Number = 2; Add Top 2D16 -> mot rebar set khac Fixed Number = 2.
- Da sua `LongitudinalBarCreator`: khong loop tao tung thanh rieng nua. Moi lan tao main/additional se tao 1 `Rebar` set tai thanh ngoai cung, sau do `SetLayoutAsFixedNumber(count, usableHalf * 2, true, true, true)`.
- Ap dung cho: main top/bottom, main top co be xuong, additional straight, additional bien co moc xuong. Do orchestrator goi main/add rieng nen cac nhom van tach set rieng.
- `RebarCreationResult.LongitudinalCount` van tra tong so thanh logical (count), khong phai so element set.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`. Can verify trong Revit that moi nhom co Layout Rule = Fixed Number va Quantity dung so thanh nhap.

## Cap nhat dong bo Quick Setting <-> Detail 2 chieu - 2026-06-19

- User yeu cau chon thong so trong Quick Setting khi vao Detail phai nho theo va dong bo 2 chieu.
- Da refactor `BeamRebarDetailViewModel` co state rieng cho tung nhom: Main Top, Main Bot, Add Top L1/L2, Add Bot L1/L2. Khi doi tab/layer se save tab cu va load state tab moi, khong con lay nham Main/Add hoac Top/Bot.
- Detail lang nghe `_parent.PropertyChanged`: neu Quick Setting doi trong luc Detail dang mo, Detail se reload state tu Quick (Quick -> Detail).
- Moi field Detail co partial change handler goi `WriteBackToParent()` ngay, nen sua Detail se cap nhat Quick Setting lap tuc (Detail -> Quick), khong doi bam OK.
- `BuildModel()` cua Detail bay gio dung toan bo state da luu cua cac nhom/layer, khong chi lay tab dang mo.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`. Can verify UI: doi Quick -> mo Detail nho dung; doi Detail -> quay Quick thay doi theo.

### Rebar List dong bo + xoa tung dong - 2026-06-19

- User yeu cau Rebar List trong Detail cung dong bo va co tuy chon delete tung cay/dong, thay vi chi `Delete All`.
- Da doi `RebarList` tu string sang `RebarListItem(Key, DisplayName)` + `SelectedRebarListItem`. XAML `ListBox` bind SelectedItem.
- `RefreshRebarListFromState()` tu dong hien Main Top/Main Bot va cac nhom Add dang enabled (Add Top L1/L2, Add Bot L1/L2). List cap nhat khi Quick -> Detail reload va khi sua field Detail -> Quick.
- Nut `Delete All` doi thanh `Delete Selected`. Xoa selected Add group se set Enabled=false dung group do va dong bo ve Quick. Main Top/Main Bot la cau hinh bat buoc nen khong cho xoa bang Rebar List.
- Nut `Add` bay gio save/cap nhat group hien tai vao state/list thay vi chi add text roi.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Nang cap Main Top/Main Bot UI giong form mau - 2026-06-19

- User yeu cau nang cap Main Top Bar nhu ban ben phai. Da them cac field Detail UI cho Main bar: `Start Point`, `End Point`, `Anchor X Left`, `Anchor X Right`, `Position In Section`, va doi `Top bend down` thanh `Anchor Y`.
- State rieng Main Top/Main Bot da luu them start/end/anchorX/position, doi tab khong mat. Rebar List hien ten dang `Count-3-D16-S-0-E-3` giong form mau.
- `PointOptions` lay theo so nhip da pick (0..spanCount); khi Pick Beam trong Detail se refresh option va set End Point theo so nhip neu dang mac dinh.
- XAML Rebar List them header `Rebar Name`.
- Da noi Start/End/Anchor X vao geometry that: `MainBarConfig` co `StartPointIndex`, `EndPointIndex`, `AnchorXLeftMm`, `AnchorXRightMm`; orchestrator cat MainTop/MainBot theo support range va `LongitudinalBarCreator.CreateRange()` dung Anchor X lam offset ngang that. `Anchor Y` van dieu khien doan be xuong top.
- `Position In Section` hien van la state/UI, chua dung de chon vi tri tung thanh rieng vi layout da la Fixed Number theo bề ngang.
- User muon anh minh hoa PNG thay vi ve tay. Da doi csproj include wildcard `Resources\Images\*.png` va Main bar Image dung `/BeamRebarPro;component/Resources/Images/MainBarDiagram.png`. Can user cung cap file PNG ten dung `MainBarDiagram.png`, sau do build lai.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Anh minh hoa doc lap theo tung Setting - 2026-06-19

- User yeu cau anh trong cac Setting doc lap voi nhau.
- Da them `SelectedDiagramImagePath` trong `BeamRebarDetailViewModel`, switch theo tab:
  `MainTopBarDiagram.png`, `MainBotBarDiagram.png`, `AddTopBarDiagram.png`, `AddBotBarDiagram.png`, `StirrupDiagram.png`, `AntiBulgeDiagram.png`.
- XAML vung Image bay gio chi co mot `<Image Source="{Binding SelectedDiagramImagePath}">`; bo so do dynamic dung chung cho Add.
- Da tao file PNG rieng ban dau bang cach copy anh hien co de tranh trang blank. User co the thay tung file PNG rieng trong `src/BeamRebarPro/Resources/Images/`.
- Da bo sung anh rieng `MainBotBarDiagram.png` theo mau user gui: thep do nam phia duoi, co Lx Left/Right va Ly Left/Right.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Rebar List loc theo Setting dang chon - 2026-06-19

- User yeu cau khi chon Main Top Bar thi chi hien list thep Main Top, tuong tu cac setting khac.
- Da sua `RefreshRebarListFromState()` switch theo `SelectedTab`: MainTop chi hien main-top, MainBot chi hien main-bottom, AddTop chi hien add-top L1/L2, AddBot chi hien add-bot L1/L2, Stirrup/AntiBulge hien nhom tuong ung.
- `OnSelectedTabChanged` goi `RefreshRebarListFromState()` de list doi ngay khi doi tab.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Fix nut Add / Delete Selected tren Rebar List - 2026-06-19

- User bao 2 nut `Add` va `Delete Selected` chua hoat dong.
- Da them `MainBarConfig.Enabled` va validator/engine bo qua Main bar khi disabled.
- Detail VM co `_mainTopEnabledState`, `_mainBottomEnabledState`. Nut `Add` bat lai/cap nhat group dang chon; `Delete Selected` tat dung selected group (main-top/main-bottom/add-top-l1/l2/add-bot-l1/l2) va refresh list.
- Khi main group bi delete, engine khong tao group do. Quick Setting mac dinh van tao main enabled; delete la thao tac trong Detail.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Nang cap Add Top Bar form + anh minh hoa - 2026-06-19

- User yeu cau cap nhat tinh nang va them anh cho Add. Top Bar theo mau Type 1/Type 2.
- Da them field that vao `AdditionalBarConfig`: Start/End Point, Start/End Type, Left/Right Ratio, Left/Right Length, D Left/D Right.
- `CreateTopAdditionalAtSupports` da dung Start/End Point de loc support, dung Left/Right Length uu tien, neu 0 dung Ratio theo nhip, neu 0 dung fallback TCVN/LengthMm; D Left/D Right dieu khien moc xuong bien trai/phai.
- `BeamRebarDetailViewModel` AddBarState da luu cac field moi va dong bo 2 chieu. Form Add Top/Add Bot trong XAML doi theo mau: Layer/Diameter, Start/End Point, Start/End Type, Left/Right Ratio, Left/Right Length, D Left/D Right, Number/Position.
- Da tao `Resources/Images/AddTopBarDiagram.png` minh hoa Type 1 Attached To Column va Type 2 Go Through The Span.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.
- Da copy DLL test moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Xem mau DAM TOP.mp4 + them preview Add Top/Add Bot - 2026-06-19

- User gui `C:\Users\Admin\OneDrive\Desktop\DAM TOP.mp4` va yeu cau xem mau. Da trich frame bang `imageio/imageio-ffmpeg`.
- Mau cho thay form Add Top co preview mat dung ben duoi: cot/goi danh so, kich thuoc nhip, thanh do theo Start/End Point, Left/Right Length/Ratio, va doan be xuong tai bien.
- Da them preview dong trong Detail cho tab Add. Top Bar va Add. Bot Bar:
  - `PreviewLine`, `PreviewText`, `PreviewLines`, `PreviewTexts` trong `BeamRebarDetailViewModel`.
  - Khi chon Add Top/Add Bot, vung duoi hien Canvas mat dung thay cho DataGrid; cac tab khac van hien bang nhip.
  - Preview cap nhat theo AddStartPoint/AddEndPoint, Left/Right Ratio, Left/Right Length, D Left/D Right va so nhip da Pick Beam.
- Day la preview UI de khach hang de hieu; geometry that van theo engine `CreateTopAdditionalAtSupports`.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18.

### Fix Main Top/Main Bottom thong so that - 2026-06-19

- User bao cac thong so Main Top trong khung do chua hoat dong that: Anchor Left/Right, Anchor X Left/Right, Anchor Y, Position In Section.
- Da them vao `MainBarConfig`: `AnchorLeftMm`, `AnchorRightMm`, `PositionInSection` (giu `AnchorLengthMm`/`TopEndBendDownLengthMm` fallback).
- `BeamRebarDetailViewModel.BuildModel()` da truyen that:
  - Anchor Left/Right -> `AnchorLeftMm`/`AnchorRightMm`.
  - Anchor X Left/Right -> `AnchorXLeftMm`/`AnchorXRightMm`.
  - Anchor Y -> `TopEndBendDownLengthMm`.
  - Position In Section -> `PositionInSection`.
- `LongitudinalBarCreator` da dung:
  - Anchor X Left/Right de cat lui diem dau/cuoi theo truc dam.
  - Anchor Left/Right de be xuong trai/phai rieng cho main top; neu 0 thi dung Anchor Y chung.
  - Position In Section de tao thanh tai dung vi tri chi dinh trong mat cat. Truong hop mac dinh dung chuoi day du `0,1,2` thi van giu 1 Rebar set `Fixed Number`; chi khi nhap vi tri khac moi tao theo vi tri rieng.
- XAML hien tai da cho nhap `Position In Section` (khong readonly).
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning. Test logic: `dotnet test` pass 18/18 (co warning NU1900 do sandbox khong truy cap duoc nuget vulnerability index).

## Tham khao video mau dam.mp4 - 2026-06-19

- Da phan tich video mau BIMSpeed (form Beam Rebar goc) -> file `docs/VIDEO-MAU-DAM-MP4.md`.
- So sanh form mau vs BeamRebarPro hien tai + liet ke 6 viec can bo sung (Type 1/2 phan biet engine, Add Bot Anchor+Total, Position In Section, naming, image, preview).
- Frame trich san o `C:\Users\Admin\Desktop\dam-frames\` (frame_001..031.jpg). Cach trich ffmpeg ghi trong file do.
- Da fix loi dam qua 2 cot bi chia nhip ao: ColumnDetector loc cot theo cao do Z cham dam + dung sai ngang 250mm. Dam 2 cot = 1 nhip (xac nhan bang popup chan doan, da go popup sau khi dung). Test 18/18 pass.

### Cap nhat Add Top/Add Bot theo 6 viec trong video mau - 2026-06-19

- Da doi `QuickSettingValidator.ValidateAdditional`: cho phep `StartPointIndex == EndPointIndex` cho Additional bar, dung mau `Count-1-D20-S-0-E-0` / `S-1-E-1`. Main bar van bat buoc End > Start.
- Da noi day du `AddBarState` xuong `AdditionalBarConfig` trong Detail `BuildModel()`:
  - `AnchorLeftMm`, `AnchorRightMm` cho Add Bot.
  - `PositionInSection` cho Add Top/Add Bot.
  - Start/End Type, Left/Right Ratio, Left/Right Length, D Left/D Right giu nguyen va dong bo.
- Add Top engine trong `BeamRebarOrchestrator.CreateTopAdditionalAtSupports` phan biet Type that:
  - Type 1 (`Attached to column`) tinh tu mep cot + Left/Right Length va be moc xuong theo D Left/D Right.
  - Type 2 (`Go through span`) chay ngang qua span/giua hai support, khong moc.
- Add Bot engine trong `AdditionalBarCreator.CreateMidspan` da doi theo form mau: `Anchor Left | Left Length | Right Length | Anchor Right` nam trong L thong thuy. Anchor nhap am hay duong deu lay tri tuyet doi de lui vao trong tu mep cot; thanh do dai `LeftLength + RightLength`.
- Rebar List naming cho Add Top/Add Bot da doi ve dang mau: `Count-{n}-D{diameter}-S-{start}-E-{end}`.
- Preview mat dung:
  - Luon hien lai chieu dai span va `Span i`.
  - Add Bot ve them 4 kich thuoc xanh: Anchor Left, Left Length, Right Length, Anchor Right.
- Image minh hoa:
  - Thay `Resources/Images/AddTopBarDiagram.png` bang PNG sach co Type 1/Type 2.
  - Thay `Resources/Images/AddBotBarDiagram.png` bang PNG sach co Anchor/Length/Span Length.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong vao duoc nuget vulnerability index).

### Fix preview rectangle Stirrup bi lech goc trai tren - 2026-06-20

- User gui anh: vung do Stirrup bi ve sai vi tri, lech len goc trai preview thay vi nam tren span dang chon.
- Nguyen nhan XAML: `Canvas.Left/Top` dat tren `Rectangle` ben trong DataTemplate, nhung ItemsControl dung `ContentPresenter` lam item container nen Canvas khong lay toa do tu Rectangle.
- Da sua `PreviewRects` ItemsControl:
  - Them `ItemContainerStyle` dat `Canvas.Left/Top` theo `PreviewRect.X/Y`.
  - Rectangle chi bind `Width/Height/Fill/Stroke`.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.

### Fix list Add Top van hien group cu S-0-E-3 - 2026-06-19

- User test thay Rebar List van hien `L2 Count-2-D20-S-0-E-3`.
- Nguyen nhan: item chi duoc bung khi bam Add, con state cu theo layer van hien group neu form dang sync tu Quick/legacy.
- Da them `EnsureTopAddItemsFromLegacy()`:
  - Neu Add Top co legacy layer range va chua co item, tu dong tach thanh cac item `S-i-E-i` khi refresh list va khi BuildModel.
  - Khong can bam Add lai moi tach.
- Sua handler `OnAddLayerChanged`, `OnAddStartPointChanged`, `OnAddEndPointChanged` bo qua luc `_isLoadingEditor` de chon item khong bi load/ghi de nguoc ve layer cu.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.

### Them tuy chon thep dai theo tung doan/span - 2026-06-20

- User gui video `C:\Users\Admin\OneDrive\Desktop\THEP DAI.mp4`, da trich frame vao `src/BeamRebarPro/docs/thep-dai-frames/`.
- Mau video: Stirrup Distribution co Uniform / 2 Ends, A1/A2, End 1/End 2, Distance first stirrup, nut `All Span`, `Remaining Span`, `Delete`, preview vung do theo span dang chon.
- Da mo rong `StirrupConfig`:
  - Them `EndZoneStartMm`, `EndZoneEndMm` cho End 1/End 2 rieng.
  - `StirrupCreator` dung End1/End2 rieng, fallback ve `EndZoneLengthMm` cu hoac L/4.
- Detail VM:
  - Them state per span cho stirrup (`StirrupSpanState`).
  - Rebar List tab Stirrup hien tung `Span i: D... A1... A2...`.
  - Chon span trong list load dung thong so cua span do.
  - Them nut `All Span`, `Remaining Span`, `Delete`.
  - `BuildModel()` dua stirrup per-span vao `SpanOverrides.Stirrup`, engine tao that theo tung span.
- Preview:
  - Them `PreviewRect` va XAML Rectangle de to vung span dang chon mau do nhat.
  - Tab Stirrup hien dim chia End1/Mid/End2 va text `D@spacing` theo mode Uniform/2 Ends.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong vao duoc nuget vulnerability index).

### Add Top goi giua D Left/D Right = 0 - 2026-06-19

- User yeu cau o goi giua, doan be xuong cua Add Top phai luon bang 0; chi goi bien moi co moc xuong.
- Da sua `GetDisplayTopAddState()`:
  - Support 0 giu `DLeftMm`, cac support khac `DLeftMm = 0`.
  - Support cuoi (`DefaultEndPoint`) giu `DRightMm`, cac support khac `DRightMm = 0`.
- Da sua luc tach item Add Top tu range:
  - Item giua duoc luu san `DLeftMm=0`, `DRightMm=0`.
  - Item bien trai/biên phai moi giu D tu form.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.

### Add Top chon item cap nhat thong so that - 2026-06-19

- User bao khi chon tung cay Add Top trong Rebar List, cac thong so Left/Right Length van hien 0, chua cap nhat theo item.
- Da them `GetDisplayTopAddState()`:
  - Support item `S-i-E-i` lay Left Length theo span ben trai * Left Ratio.
  - Lay Right Length theo span ben phai * Right Ratio.
  - Bien trai/phai khong co span thi length = 0.
- `OnSelectedRebarListItemChanged` cua `add-top-item-*` load state hien thi da tinh, nen form hien dung 1000/1050... theo tung support giong preview.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.

### Can giua so dim Add Bot nhu mau - 2026-06-19

- User bao so dim Add Bot bi dinh nhau, muon so nam ngay giua tung doan dim dep nhu hinh mau.
- Da sua `AddPreviewDimension()`:
  - Uoc luong width text va dat X = center - width/2, khong tru co dinh `18` nua.
  - Giam font size ve 9 khi doan dim ngan.
- Da sua preview Add Bot de chuoi dim 4 doan `Anchor Left | Left Length | Right Length | Anchor Right` chia theo ty le trong khoang clear span, khong ve vuot ra ngoai nhịp khi tong length lon hon span.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.

### Add Bot preview dung mau BIMSpeed hinh 2 - 2026-06-19

- User chi ro Add Bot phai hien thi nhu hinh 2: chi hien item dang chon, co chuoi kich thuoc Anchor Left / Left Length / Right Length / Anchor Right.
- Da sua preview Add Bot:
  - Khong ve tat ca item do/xanh nua.
  - Chi ve item dang chon trong Rebar List (neu chua chon thi ve item dau tien).
  - Thanh thep hien mau do, khong dung xanh highlight.
  - Luon hien 4 doan kich thuoc xanh phia tren theo item dang chon.
- Khi chon item Add Bot co Left/Right Length = 0, form se hien gia tri tinh tu ratio theo dung span cua item:
  - Left/Right Length tinh theo ratio * clear length.
  - Anchor Left/Right fallback = clearLength/2 - length, de Total/Anchor hien gia tri that nhu mau.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.

### Add Bot preview dong theo tung item nhu Add Top - 2026-06-19

- User yeu cau bo sung thep dong theo tua Add Top cho Add Bot.
- Da sua `RefreshElevationPreview()` cho tab Add Bot:
  - Neu co `_bottomAddItems`, preview loop qua tat ca item va ve tung thanh theo `StartPoint/EndPoint`, `Left/Right Length`, `Anchor Left/Right`.
  - Item dang chon trong Rebar List duoc highlight mau xanh dam hon va hien kich thuoc Anchor/Left/Right/Anchor.
  - Neu item length = 0 thi fallback tinh length theo ratio tren dung khoang span cua item; neu ca ratio/length deu 0 thi fallback 0.5L + 0.5L.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.

### Add Bot chinh tung cay/item rieng trong Rebar List - 2026-06-19

- User muon Add Bot co chuc nang giong Add Top: list co tung cay de chon va sua rieng.
- Da them `QuickSettingModel.BottomAdditionalItems`.
- Detail `Add Bot`:
  - Range `S=0,E=3` tu tach theo tung nhip: `S-0-E-1`, `S-1-E-2`, `S-2-E-3`.
  - Chon item trong Rebar List se load thong so item do len form.
  - Sua thong so se ghi vao dung item dang chon.
  - `Delete Selected` xoa dung item Add Bot dang chon.
- Orchestrator uu tien `BottomAdditionalItems` neu co item; khi do khong tao group legacy `BottomAdditional`/`BottomAdditionalLayer2` de tranh trung.
- Validator/RebarFamilyValidator da quet them `BottomAdditionalItems`.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.

### Add Top chinh tung cay/item rieng trong Rebar List - 2026-06-19

- User muon chinh linh hoat tung cay/thep tang cuong trong list, khong chi 1 group `S-0-E-3`.
- Da them `QuickSettingModel.TopAdditionalItems` de Detail co the dua nhieu cau hinh Add Top rieng xuong engine.
- Detail `Add Top`:
  - Bam `Add` voi range `S=0,E=3` se bung thanh cac item rieng: `S-0-E-0`, `S-1-E-1`, `S-2-E-2`, `S-3-E-3`.
  - Rebar List hien tung item `L{layer} Count-{n}-D{diam}-S-{s}-E-{e}`.
  - Chon tung item trong list se load thong so cua item do len form; sua field nao thi ghi lai dung item dang chon.
  - `Delete Selected` xoa dung item dang chon.
- Orchestrator uu tien `TopAdditionalItems` neu co item; luc do bo qua 2 config legacy `TopAdditional`/`TopAdditionalLayer2` de tranh tao trung.
- Validator/RebarFamilyValidator da quet them `TopAdditionalItems`.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong vao duoc nuget vulnerability index).
- Da copy DLL moi sang `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.
- Can verify geometry that trong Revit: Add Top Type 1/2, Add Bot anchor am/duong, va Position In Section cho Add bars.

### Sua lai hieu nham Add Top preview khong duoc chay xuyen nhip - 2026-06-19

- User nhac ro: khong tu y sua thep tang cuong thanh cay chay xuyen suot.
- Xac nhan engine `CreateTopAdditionalAtSupports` dang tao Add Top theo tung support rieng trong khoang Start/End:
  - `S=0,E=1` tao 2 doan cuc bo quanh support 0 va support 1, khong tao 1 thanh lien tuc noi qua ca nhip.
  - `S=E` tao 1 doan cuc bo quanh support do.
  - Type 1 chi dieu khien moc xuong tai dau bien duoc chon; D Left/D Right la chieu dai moc xuong.
- Da sua `BeamRebarDetailViewModel.RefreshElevationPreview()` de preview Add Top cung ve tung doan rieng theo tung support, tranh hien thi nham thanh 1 duong do lien tuc.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong vao duoc nuget vulnerability index).
## Cap nhat moi - 2026-06-20

### Main Top/Bot full run khi Muc 9 them goi

- User bao khi chon Muc 9 them goi, Main Top/Main Bot van `S-0-E-1`, chi chay span dau, khong chay het dam.
- Nguyen nhan: Detail load config da luu theo ID dam voi `EndPoint=1`, sau khi them goi so support thanh `0-1-2` nhung saved config van de len end point cu.
- Da them `EnsureMainBarsFullRun()` trong `BeamRebarDetailViewModel`:
  - Main Top/Bot luon `StartPoint=0`, `EndPoint=DefaultEndPoint`.
  - Goi sau `ApplySavedDetailConfig`, sau `LoadPickedSpans`, sau `OnBeamInfo`, va trong `BuildModel()` de geometry that cung full run.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong doc duoc NuGet vulnerability index).

### Fix tie 180 bi tach thanh doan roi

- User test thay moc 180 tu ve polyline bi hien thanh cac doan ngan tach roi, khong dung thanh tie lien tuc.
- Da bo cach tao tie 5 doan thu cong.
- `AntiBulgeCreator` hien tao tie la 1 line thang lien tuc va gan `RebarHookType` 180 o ca start/end:
  - `var tieHook = _families.GetHookType(HookAngle.Deg180);`
  - `Rebar.CreateFromCurves(..., hookType, hookType, ..., [Line.CreateBound(leftPoint, rightPoint)], ...)`
- Muc tieu: section hien thanh ngang lien tuc nhu user khoanh, hook 180 do do Revit hook family xu ly.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong doc duoc NuGet vulnerability index).

### Anti-bulge tie doi moc 90 sang moc 180

- User muon Tie D cua Anti-bulge khong be 90 do nua ma be moc 180 do.
- Da sua `TryCreateTieWithEndHooks`:
  - Shape tie tu 3 doan thanh 5 doan: duoi moc trai + canh dung + doan ngang + canh dung phai + duoi moc phai.
  - Duoi moc quay vao trong tiet dien: trai theo `frame.Across`, phai theo `-frame.Across`.
  - Chieu dai duoi moc mac dinh `max(75mm, 10d)`, clamp khong vuot qua nua be rong tie.
  - Neu khong du khong gian duoi moc thi fallback ve moc 90 thay vi fail.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong doc duoc NuGet vulnerability index).

### Fix Anti-bulge tie van thang do clamp sai dau

- User test thay tie chong phinh van y cu, khong co moc.
- Nguyen nhan: trong `TryCreateTieWithEndHooks`, khoang trong len/xuong tinh sai dau:
  - Cu: `vertical - topLimit`, `bottomLimit - vertical` => thuong ve 0, nen fallback thanh straight.
  - Moi: `topLimit - vertical`, `vertical - bottomLimit`.
- Tang moc mac dinh len `max(100mm, 12d)` de nhin ro hon trong section.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong doc duoc NuGet vulnerability index).

### Anti-bulge Tie D co moc hai dau nhu hinh 2

- User muon tie chong phinh khong phai thanh ngang cut thang, ma co dau moc/bẻ 90 do o hai bien nhu hinh 2.
- Da sua `AntiBulgeCreator`:
  - Tie D tao polyline 3 doan: moc trai + doan ngang + moc phai.
  - Chieu dai moc mac dinh `max(75mm, 10d)` va clamp trong cover top/bottom de khong chọc ra ngoai dam.
  - Tie van tao theo tung span/host, khong xuyen qua cot.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong doc duoc NuGet vulnerability index).

### Anti-bulge Number / Tie D / @ thanh thong so that

- User bao `Tie D` va `@` trong Anti-bulge Rebar chua that.
- Da mo rong `AntiBulgeConfig`:
  - `Count`: so hang thanh chong phinh moi mat ben.
  - `TieDiameter`: duong kinh thanh giang ngang.
  - `SpacingMm`: buoc tie theo phuong doc dam.
- `AntiBulgeCreator` da viet lai:
  - Tao thanh doc chong phinh theo `Number`, 2 mat ben.
  - Tao tie D theo `TieDiameter` va rai theo `SpacingMm`.
  - Tie chi tao ben trong tung span/host dam, bat dau/ket thuc lui cover nen khong xuyen qua cot.
- `RebarFamilyValidator` quet them `TieDiameter`.
- Detail BuildModel dua `AntiBulgeNumber`, `AntiBulgeTieDiameterMm`, `AntiBulgeSpacingMm` vao model that.
- Rebar List hien `AntiBulge: {n}xD... Tie D...@...`.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong doc duoc NuGet vulnerability index).

### Sua Add Bot preview length label

- User chi ro voi Add Bot `Left Length = 3000` / `Right Length = 3000`, nhan kich thuoc tren preview phai hien `1500` moi dung.
- Da sua preview Add Bot: nhan kich thuoc cua Left/Right Length hien `length / 2`.
- Da sua cong thuc dong bo Add Bot: `Anchor = L/2 - Length`, va khi nhap Anchor thi `Length = L/2 - Anchor` de gia tri am nhu `-1000` ra dung `3000`.
- Da sua helper tinh L thong thuy cho Add Bot: range `S-0-E-1` chi lay span 0, khong cong nham span tiep theo.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong doc duoc NuGet vulnerability index).

### Main Bot Bar lam tuong tu Main Top

- Main Bot Bar da dung `Anchor Left` / `Anchor Right` that trong `LongitudinalBarCreator`:
  - Top Bar: `Anchor Left/Right` hoac `Anchor Y` tao doan be xuong.
  - Bot Bar: `Anchor Left/Right` tao doan be len o hai dau.
- Preview Main Bot Bar ve thanh do theo `Start Point`/`End Point`, `Anchor X Left/Right` va doan be len Ly tu `Anchor Left/Right`.
- Chon dong `main-bottom` trong Rebar List nap lai dung state len form va refresh preview.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong doc duoc NuGet vulnerability index).

### Main Top Bar theo video `DAM MOI 1.mp4`

- Da trich frame video vao `src/BeamRebarPro/docs/dam-moi-1-frames/` de doi chieu mau BIMSpeed.
- Da bo sung preview mat dung cho Main Top/Main Bottom thay vi chi ve khung dam:
  - Main Top ve thanh do theo `Start Point`/`End Point`.
  - `Anchor X Left`/`Anchor X Right` ve kich thuoc Lx tu mep cot vao thanh.
  - `Anchor Left`/`Anchor Right`/`Anchor Y` ve doan be xuong Ly hai dau cho Main Top.
- Chon dong `main-top` / `main-bottom` trong Rebar List se nap lai dung state hien tai len form va refresh preview.
- Geometry that da co san trong `LongitudinalBarCreator`: `AnchorXLeftMm/RightMm` lam inset ngang; `AnchorLeftMm/RightMm` hoac `TopEndBendDownLengthMm` lam doan be xuong cho thep chu tren.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18 (co warning NU1900 do sandbox khong doc duoc NuGet vulnerability index).

## Cap nhat geometry thep - 2026-06-20 (chieu, fix theo data that tu Revit)

### Thep chu Fixed Number (FIX GOC RE nho popup chan doan)
- User bao thep chu Top/Bot bi Single. Da them popup `[FixedNumber]` chan doan tam thoi -> data that:
  `count=2 positions=2, usableHalf=92mm, bar offset xa nhat=92mm, WidthFeet=250mm` -> layout Fixed Number CHAY DUNG.
- Goc that: thep chu co BE XUONG dau (top) di vao nhanh `EvenLaterals` tao TUNG cay single, KHONG gop set.
- Da fix `LongitudinalBarCreator.CreateBars`: nhanh be xuong cung tao 1 rebar set + `SetFixedNumberLayout(count, usableHalf*2)`
  giong nhanh thang. Gio thep chu Top+Bot = 1 rebar set, Layout Rule = Fixed Number, Quantity = count.
- `SetFixedNumberLayout`: set `barsOnNormalSide:true`, regenerate, neu `MaxBarOffsetFromCenter > usableHalf` (rai ra ngoai dam)
  thi dao chieu `barsOnNormalSide:false` + regenerate. Verify bang khoang cach toi TAM dam theo phuong Across (khong dung host bbox vi dam nghieng bbox sai).
- DA GO popup chan doan sau khi dung. User xac nhan "OK DA DUNG".

### Thep chu nam TRONG dai (khong de len duong dai)
- User: thep chu phai nam ben trong dai, khong de len duong dai.
- `LongitudinalBarCreator` them param `stirrupDiameterMm` (truyen tu `model.Stirrup.Diameter` trong orchestrator).
  `Vertical()` cong them `_stirrupClearFeet` (1 duong kinh dai) vao coverTop/Bottom/Side -> thep chu lui vao trong dai.

### Thep gia cuong phan bo deu Fixed Number (khong de len thanh chu)
- User: 2 cay gia cuong khong duoc trung vi tri thanh chu.
- `LongitudinalBarCreator.CreateSegment`/`CreateSegmentWithEndBends`: mac dinh phan bo deu Fixed Number theo so cay
  gia cuong (`count`), KHONG dung `mainCount`. Chi khi user nhap Position In Section TUONG MINH (khac mac dinh) moi dat theo KHE giua thanh chu (`GetGapOffsets`).
- BO `PositionInSection` cho THEP CHU (CreateBars luon `EvenLaterals`/Fixed Number theo count that). Truoc do position "0,1,2"
  voi count=2 lam idx vuot denom -> thep loi ra ngoai +3*usableHalf.

### Anti-bulge Tie D: huong moc + vi tri + max spacing + khong qua cot
- Tie D moc 180 quap XUONG (`RebarHookOrientation.Left/Left`).
- Tie rai theo MAXIMUM SPACING: tao 1 tie row roi `SetLayoutAsMaximumSpacing(spacingFeet, rowLength, ...)` (buoc <= SpacingMm).
- Tie vi tri: `tieDropFeet = -(bar+tieBar)/2` (DOI LEN, thanh ngang o mep tren 2 thanh doc, moc quap xuong om thanh).
- Thep doc chong phinh + tie KHONG BANG QUA COT: `AntiBulgeCreator.Create` nhan `leftHalfFeet`/`rightHalfFeet`,
  thanh doc + tie lui khoi MEP COT = (nua be rong cot + cover) moi dau, giong dai.

### Trang thai
- Build `Debug.R25 -p:DeployAddin=false` pass 0 warning. `dotnet test` 18/18 pass.
- DLL test: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### LUU Y CHO AI SAU
- Geometry rebar CHI verify duoc trong Revit that. Khi user bao loi geometry kho hieu, THEM popup `TaskDialog` chan doan
  (NumberOfBarPositions, usableHalf, offset...) de lay DATA THAT thay vi doan. Go popup sau khi fix xong.
- Thep chu/gia cuong dung `SetFixedNumberLayout` -> can verify chieu rai bang `MaxBarOffsetFromCenter` so TAM dam (Across), khong dung host bbox.
- User chot nhieu lan: phai SUA THAT, khong doan. Luon test (dotnet test) truoc khi copy DLL.

### Luu/nho cau hinh theo ID dam - 2026-06-20
- User muon luu thong so de lan sau pick dam khoi nhap lai (nho theo ID dam).
- `Services/BeamConfigStore.cs` (MOI): luu map beamId(long) -> QuickSettingModel ra JSON tai
  `%AppData%\BeamRebarPro\beam-configs.json` (System.Text.Json). Bo InternalSupportPoints/SpanOverrides khi luu.
- Save: trong `RebarCreationHandler` sau khi tao thep OK (`result.Succeeded`) -> `BeamConfigStore.Save(beamIds, Model)`.
- Load: trong `StartupCommand` sau khi pick dam -> `BeamConfigStore.Load(beamIds)` -> `viewModel.LoadSavedConfig(saved)`.
- `BeamRebarProViewModel.LoadSavedConfig(model)` do nguoc model vao flat fields (Main/Add/Stirrup/AntiBulge/Cover).
- Build pass 0 warning, test 18/18 pass. DLL: C:\Users\Admin\Desktop\BeamRebarPro-test\.

### Deploy chinh thuc + ribbon "Ve Dam" + logo - 2026-06-20
- `Application.cs`: doi panel/tab "Ve Dam", nut "Ve Dam".
- Logo: tu tao bang PIL (script o %TEMP%\make_icon.py) -> `Resources/Icons/RibbonIcon16.png` + `RibbonIcon32.png`:
  tiet dien dam (khung xam) + khung dai do + 4 cham thep xanh o goc.
- Da deploy `dotnet build -c Debug.R25` (Revit tat) -> `%AppData%\Autodesk\Revit\Addins\2025\BeamRebarPro\`.
- Lan sau sua code: dong Revit roi build lai de deploy (Revit mo se lock DLL).

### Nho them config DETAIL (anchor X/Y, position, start/end point) - 2026-06-20
- User: nho luon thong so trong man Beam Rebar (Detail), khong chi Quick Setting.
- Detail `ApplyRebar` build model DAY DU -> `_handler.Model` -> `BeamConfigStore.Save` luu het.
- `BeamRebarProViewModel.SavedConfig` (MOI): giu model day du sau `LoadSavedConfig`.
- Detail `LoadFromParent` goi `ApplySavedDetailConfig(_parent.SavedConfig)` -> override Anchor Left/Right,
  Anchor X Left/Right, Anchor Y, Start/End Point, Position In Section cho Main Top + Main Bottom tu config da luu.
- Build pass, test 18/18. DLL test: C:\Users\Admin\Desktop\BeamRebarPro-test\.
- LUU Y: ApplySavedDetailConfig moi lam Main Top/Bottom; Add bar items/Stirrup per-span/AntiBulge chua override tu SavedConfig (TODO neu can nho het).

### Muc 9 + Muc 10 (goi bo sung + dam phu) - 2026-06-20
- MUC 9 (Beams work like support): SupportPicker da cho pick OST_StructuralFraming (dam giao) + cot.
  Pick -> diem -> SpanModelBuilder chieu len truc dam chinh -> them goi -> chia nhip. DA HOAT DONG (bo sung auto-do cot).
- MUC 10 (secondary beams): TRUOC chi hien text. GIO lam that:
  - `RebarCreationRequest.SelectSecondary` + `OnSecondarySelected` (handler) -> pick dam phu (dung SupportPicker).
  - VM `_secondaryPoints` + `SecondaryBeamCount` -> `QuickSettingModel.SecondaryBeamPoints`.
  - Orchestrator `ProjectSecondaryStations`: chieu diem dam phu len truc nhip -> station feet.
  - `StirrupCreator.Create(..., secondaryStationsFeet)`: tai moi station tao CUM DAI TANG CUONG
    buoc = max(100mm, A1/2), trai +-HeightFeet quanh dam phu (dai treo TCVN). Refactor TwoEnds -> CreateTwoEnds().
- BeamConfigStore.Save clean ca SecondaryBeamPoints (phu thuoc phien chon).
- Build pass, test 18/18. DLL: C:\Users\Admin\Desktop\BeamRebarPro-test\ (12:08).
- TODO neu can: "dai khong bang qua dam phu" hien dien giai = them dai day quanh do (dai van la khung kin mat cat,
  khong cat theo phuong doc). Neu user muon CHEN them dai treo dang chu U/mooc rieng thi lam them creator.

### Dam JOIN voi san - thep di du chieu cao (khong tru phan san) - 2026-06-20
- LOI: dam join san -> Revit cat solid dam vung chong san -> solidTop (dinh solid) bi ha xuong day san
  -> chieu cao dam thieu phan san -> dai + thep chu top bi "tru phan san".
- FIX trong `BeamGeometryReader`: mat tren that = `Math.Max(solidTop, solidBottom + heightFeet)`
  (solidBottom KHONG bi cat vi san nam tren; heightFeet = section h family). Dam khong join -> solidTop dung -> Max giu nguyen.
- LUU Y: user truoc chot "cao do from SOLID dung, dung sua". Day chi sua MAT TREN khi bi san cat;
  DAY van tu solidBottom. Khong pha logic dam thuong. Neu dam thuong sai cao do -> revisit.
- Build pass, test 18/18. DLL: C:\Users\Admin\Desktop\BeamRebarPro-test\ (13:28).

### Muc 9 them goi -> Detail chia lai Add/Stirrup theo span that - 2026-06-20
- User bao khi them goi bang "Beams work like support", trong Detail:
  - Stirrup chon A1/A2 hien sai (End1/End2 cu lon hon nhip moi, mat vung A2).
  - Thep tang cuong va dai chua chia lai that theo nhip moi.
- Da sua `BeamRebarDetailViewModel`:
  - Detail lang nghe `PickedSpans` cua Quick Setting; khi Muc 9 recompute span thi clear/load lai `SpanRows`.
  - Sau khi span thay doi, rebuild item phu thuoc nhip: `_topAddItems`, `_bottomAddItems`, `_stirrupSpanItems` theo `DefaultEndPoint` moi.
  - `StirrupSpanState` duoc normalize theo chieu dai span that: neu `End1 + End2 >= L` thi tu dong ve `L/4` moi dau de con vung giua A2.
  - Normalize ap dung ca preview, form editor, va `BuildSpanOverrides()` gui xuong engine, nen khong chi sua hinh ve.
- Build kiem tra: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test logic: `dotnet test` pass 18/18 (co warning NU1900 do sandbox khong doc duoc NuGet vulnerability index).
- DLL test da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Fix geometry that tu Detail mat goi Muc 9 - 2026-06-20
- User test tiep: "thep tang cuong va thep dai chua chia that theo them goi".
- Nguyen nhan that: `BeamRebarDetailViewModel.BuildModel()` khong copy `_supportPoints` cua Quick Setting vao
  `QuickSettingModel.InternalSupportPoints`; preview Detail da co span moi nhung engine `BeamRebarOrchestrator` van nhan model khong co goi pick.
- Da them getter `BeamRebarProViewModel.SupportPoints` va `SecondaryBeamPoints`.
- Detail `BuildModel()` nay set:
  - `InternalSupportPoints = _parent.SupportPoints`
  - `SecondaryBeamPoints = _parent.SecondaryBeamPoints`
  - `Cover = new CoverSettings { TopMm = _parent.CoverMm, BottomMm = _parent.CoverMm, SideMm = _parent.CoverMm }`
- Ket qua mong doi: Add Top/Bot, Stirrup, Secondary stirrup tu Detail deu chia theo goi/dam phu da pick nhu Quick Setting.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Dam phu -> dai gia cuong 4 thanh @50 moi ben - 2026-06-20
- User yeu cau khi chon dam phu (Muc 10) thi tai dam phu phai co 4 dai gia cuong @50 hai ben.
- Truoc do `StirrupCreator` tao vung dai day trai +- chieu cao dam, buoc `max(100, A1/2)` -> khong dung mau.
- Da sua `Services/Rebar/StirrupCreator.cs`:
  - Moi station dam phu tao 2 cum rieng.
  - Ben trai: 4 dai @50 tai `station - 200/-150/-100/-50mm`.
  - Ben phai: 4 dai @50 tai `station + 50/+100/+150/+200mm`.
  - Dung `CreateFixedCountZone(... count=4, spacing=50)` va `SetLayoutAsNumberWithSpacing`.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Dam phu -> tinh cum dai tu MEP dam phu, khong tu TIM - 2026-06-20
- User chi ro hinh dung: 2 cum dai phai nam ngoai 2 mep dam phu, giua 2 cum chua dung be rong dam phu.
- Nguyen nhan: code truoc dat 4 dai @50 theo tim dam phu (`station +/- 50..200mm`) nen cum bi sat/lan vao vung dam phu.
- Da them:
  - `Models/SecondaryBeamInfo(Location, HalfWidthFeet)`.
  - `Models/SecondaryStirrupStation(StationFeet, HalfWidthFeet)`.
  - `SupportPicker.PickSecondaryBeams(...)` do half-width cua dam phu theo truc dam chinh bang bbox projection.
  - `QuickSettingModel.SecondaryBeams` va VM truyen du lieu nay tu Muc 10 xuong Detail/engine.
- `StirrupCreator` dat:
  - Ben trai: tu `leftFace - 200mm` den `leftFace - 50mm` (4 dai @50).
  - Ben phai: tu `rightFace + 50mm` den `rightFace + 200mm` (4 dai @50).
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Chinh chieu rai dai dam phu: tinh tu thanh gan mep 50mm - 2026-06-22
- User ve lai: vi tri dai gan nhat phai cach mep dam phu 50mm, sau do moi rai them 3 dai @50 ra ngoai.
- Da sua `StirrupCreator` de layout bat dau tai thanh GAN MEP:
  - Ben trai: first bar = `leftFace - 50mm`, `barsOnNormalSide=false` -> rai ra ngoai trai thanh `-50/-100/-150/-200`.
  - Ben phai: first bar = `rightFace + 50mm`, `barsOnNormalSide=true` -> rai ra ngoai phai thanh `+50/+100/+150/+200`.
- `CreateFixedCountZone` nay nhan `barsOnNormalSide` va check min/max cua cum, tranh cat qua dau nhip.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Fix station dam phu dung giao diem truc dam chinh/phu, khong lay midpoint - 2026-06-22
- User bao thuc te van chua dat va nhac "khong gia dinh/khong doan".
- Root cause hop ly da sua: `SupportPicker.PickSecondaryBeams` truoc do lay midpoint `LocationCurve` cua dam phu.
  Neu dam phu dai va giao voi dam chinh khong dung giua dam phu, station bi lech -> cum dai co the lot vao vung dam phu.
- Da sua `SupportPicker`:
  - Lay `mainLine` cua dam chinh da preselect.
  - Voi moi dam phu co `LocationCurve` line, tinh giao diem 2D giua `mainLine` va `secondaryLine`.
  - `SecondaryBeamInfo.Location` = giao diem truc dam chinh/phu; fallback midpoint chi khi khong co line/parallel.
  - `HalfWidthFeet` van do bang bbox projection theo truc dam chinh.
- Dong thoi bo layout set cho cum dam phu trong `StirrupCreator`: tao 8 dai don tai dung toa do tuyet doi
  `leftFace - 50/100/150/200` va `rightFace + 50/100/150/200` de khong phu thuoc `barsOnNormalSide` cua Revit.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Dam phu: dung Max Spacing @50, khong tao single + hien DATA that - 2026-06-22
- User bao thuc te van y cu va yeu cau khong doan, khong tao single; lam lai la Max Spacing.
- Da sua `StirrupCreator`:
  - Khong tao 8 single nua.
  - Moi ben dam phu tao 1 rebar set bang `SetLayoutAsMaximumSpacing(50mm, 150mm, includeFirst=true, includeLast=true)`.
  - Vung trai: `leftFace - 200mm .. leftFace - 50mm`.
  - Vung phai: `rightFace + 50mm .. rightFace + 200mm`.
- Da them warning data that moi lan tao:
  `DATA dam phu: station=... halfWidth=... leftFace=... rightFace=... dai trai=.....@50 dai phai=.....@50`.
- Da sua `BeamRebarDetailViewModel.OnRebarCompleted`: neu tao thanh cong ma co warnings thi hien warnings/data trong StatusMessage.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Dam phu: tinh mep theo be rong tiet dien that, khong dung bbox - 2026-06-22
- User yeu cau "tinh toan va lam that de duoc ket qua nhu hinh".
- Nguyen nhan con lai: `HalfWidthFeet` cua dam phu dang fallback tu bbox projection, co the sai khi dam phu xoay/skew hoac bbox phinh.
- Da sua `SupportPicker.ToSecondaryBeamInfo`:
  - Van lay station = giao diem 2D truc dam chinh va truc dam phu.
  - `halfWidthOnMain = (b / 2) / abs(dot(mainAxis, secondaryAcross))`.
  - `b` doc tu instance/symbol parameters: `b`, `B`, `Width`, `width`, `WIDTH`, `Chieu rong`, `Chiều rộng`.
  - Neu khong doc duoc width hoac gan song song moi fallback bbox.
- Ket qua mong doi:
  - `leftFace = station - halfWidthOnMain`.
  - `rightFace = station + halfWidthOnMain`.
  - Max spacing set trai: `leftFace - 200 .. leftFace - 50`.
  - Max spacing set phai: `rightFace + 50 .. rightFace + 200`.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Dam phu: phuong phap triệt de hon - best intersection + TaskDialog DATA - 2026-06-22
- User yeu cau lam ky hon, co phuong phap triet de hon.
- Da sua `SupportPicker.PickSecondaryBeams`:
  - Khong lay truc dam chinh dau tien nua.
  - Lay tat ca `PreselectedBeams` lam `mainLines`.
  - Voi moi dam phu, tinh giao diem 2D voi tung mainLine, cham diem bang khoang cach den segment dam chinh + segment dam phu.
  - Chon giao diem co score nho nhat -> dung voi dam chinh/host that dang giao, tranh sai khi co nhieu beam host.
- Da them `TaskDialog.Show("BeamRebarPro - DATA dam phu", ...)` sau khi tao thep neu co dong DATA, de user thay ngay:
  station, halfWidth, leftFace/rightFace, vung dai trai/phai.
- Van tao cum dai dam phu bang Max Spacing @50:
  - Trai: `leftFace - 200 .. leftFace - 50`
  - Phai: `rightFace + 50 .. rightFace + 200`
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Dam phu: MaxSpacing neo tu thanh gan mep, rai ra ngoai - 2026-06-22
- User gui DATA that: station=1950, halfWidth=100, leftFace=1850, rightFace=2050, vung mong muon
  trai 1650..1800, phai 2100..2250. DATA tinh toan da dung, loi con lai la huong rải Revit.
- Da sua `StirrupCreator.CreateMaxSpacingZone`:
  - Khong dat profile o dau xa cua vung nua.
  - Ben trai dat profile tai thanh gan mep `leftEnd = leftFace - 50`, `barsOnNormalSide=false`, array length 150 -> rải ra ngoai ve 1650.
  - Ben phai dat profile tai thanh gan mep `rightStart = rightFace + 50`, `barsOnNormalSide=true`, array length 150 -> rải ra ngoai ve 2250.
  - Van la Max Spacing @50, include first/last.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Dam phu: cat bo DAI THUONG trong vung dam phu + cum gia cuong - 2026-06-22
- User test DATA dung nhung van thay cay sat mep dam phu; ly do con lai: dai thuong cua nhip van rai xuyen qua vung dam phu,
  nen nhin nhu cum gia cuong khong cach mep 50.
- Da sua `StirrupCreator`:
  - Tao `SecondaryStirrupRange` cho moi dam phu.
  - Khi tao dai thuong Uniform/TwoEnds, `CreateZone` se split va BO QUA toan bo vung `LeftStartFeet..RightEndFeet`.
  - Voi DATA mau: dai thuong se khong tao trong `1650..2250`; trong vung nay chi con 2 cum MaxSpacing @50:
    trai `1650..1800`, phai `2100..2250`.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Dam phu: tinh tu MEP DAM PHU ra ngoai 50mm - 2026-06-22
- User nhac ro: "tinh tu mep dam ra" -> nghia la lay `leftFace/rightFace` cua dam phu, roi dat dai ra ngoai.
- Cong thuc hien tai:
  - `leftFace = station - halfWidthOnMain`, `rightFace = station + halfWidthOnMain`.
  - Khoang cach thong thuy tu mep dam phu den mat ngoai thanh dai = 50mm.
  - Tim dai gan mep = `face +/- (50mm + D_dai/2)`.
  - Cac dai tiep theo rai ra ngoai @50.
- DATA popup hien `clear=50mm, D=...` va toa do tim dai sau khi cong `D/2`.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Dam phu: MCP + popup ACTUAL de bat dung vi tri Revit tao - 2026-06-22
- User da bat MCP va chon dung 1 dam phu `200x300` + 2 rebar dai gia cuong `D6 : Shape BS_M_T1`; MCP xac nhan selection co cac ID do.
- Da sua `RebarCreationHandler`: popup `BeamRebarPro - DATA dam phu` nay hien ca:
  - `DATA dam phu`: station/halfWidth/leftFace/rightFace/clear/D/vung tinh toan.
  - `ACTUAL dam phu trai/phai`: toa do expected va actual doc tu `GetCenterlineCurves` cua rebar set sau `SetLayoutAsMaximumSpacing`.
- Muc dich: lan test tiep theo phan biet ro loi nam o dau:
  - Neu `DATA` dung, `ACTUAL` dung ma hinh van co cay trong vung dam phu -> do rebar cu/regular stirrup con ton tai trong model.
  - Neu `ACTUAL` lech expected -> loi Revit layout/normal side cua `SetLayoutAsMaximumSpacing`.
  - Neu `DATA` sai -> loi doc station/halfWidth cua dam phu.
- Build: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test: `dotnet test` pass 18/18.
- DLL test da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Dam phu: fix doc ACTUAL cua rebar set - 2026-06-22
- User gui popup:
  - `DATA` dung: trai `1647..1797@50`, phai `2103..2253@50`.
  - `ACTUAL` cu sai: trai lap 4 lan `1797`, phai lap 4 lan `2103`.
- Nguyen nhan: code chan doan doc `GetCenterlineCurves(..., barPositionIndex)` truc tiep, voi shape-driven rebar set Revit tra base curve trung nhau.
- Da sua `StirrupCreator.AddActualLayoutData`:
  - Lay base curve o position 0.
  - Lay `rebar.GetShapeDrivenAccessor().GetBarPositionTransform(i)`.
  - Transform base point sang tung bar position roi moi tinh station.
- Geometry tao thep khong doi; day la fix de popup phan anh dung vi tri tung thanh trong rebar set.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Quy tac lam viec tu 2026-06-22: khong doan / phai co can cu
- User yeu cau ro: tu day ve sau KHONG tuong tuong/khong doan; moi fix geometry phai co co so.
- Bat buoc truoc khi sua tiep geometry:
  - Lay DATA that bang MCP/popup/log Revit neu co the.
  - Neu nghi loi do toa do, phai in/doi chieu station, face, expected, actual.
  - Neu nghi loi do model cu/chong rebar, phai kiem tra ID/selection/element trong Revit truoc.
  - Final phai neu ro build/test da chay hay chua.

### Dam phu: fix nguon lech do LocationCurve/justification cua dam phu - 2026-06-22
- Bang chung MCP:
  - Beam phu/dam lien quan co `LocationCurve` nam o canh/duong justification, khong phai tim hinh hoc.
  - Vi du element `2759918` bbox X `1836..2036` nhung line location o X `2036`; neu lay giao diem line se lech mep/tim.
- Da sua `SupportPicker.ToSecondaryBeamInfo`:
  - Uu tien doc geometry solid/edge cua dam phu.
  - Chieu tat ca diem geometry len truc dam chinh.
  - Lay min/max projection lam face that theo phuong dam chinh.
  - Doi point ve midpoint projection va halfWidth = `(max-min)/2`.
  - Chi fallback ve width parameter/bbox neu khong doc duoc geometry.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Dam phu: go popup/debug DATA-ACTUAL sau khi user xac nhan dung - 2026-06-22
- User xac nhan hinh hoc dai gia cuong quanh dam phu da dung.
- Da go `TaskDialog.Show("BeamRebarPro - DATA dam phu", ...)` trong `RebarCreationHandler`.
- Da go cac warning debug `DATA dam phu` va `ACTUAL dam phu` trong `StirrupCreator`; logic tao dai khong doi.
- Neu sau nay can chan doan lai, co the them tam thoi popup/log nhung phai go sau khi fix xong.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Thep gia cuong Quick/Detail: xen ke voi thep chu - 2026-06-22
- User yeu cau thep gia cuong muc 1/7 trong Quick Setting phai xen ke voi thep chu, khong trung vi tri thanh chu.
- Can cu code cu: `LongitudinalBarCreator.GetGapOffsets` coi default `PositionInSection = "0,1"` la khong override,
  nen add bar quay ve Fixed Number rieng; voi main 3 cay + add 2 cay co the trung 2 thanh bien.
- Da sua:
  - Default `"0,1"` nay duoc hieu la khe 0 va khe 1 giua cac thanh chu.
  - Vi du main `3D16`, add `2D16` -> add nam tai 2 khe giua 3 thanh chu.
  - Neu so add bar lon hon so khe, fallback chia deu trong vung long dam, tranh 2 bien.
- Ap dung cho `CreateSegment` va `CreateSegmentWithEndBends`, nen Add Top/Add Bot va cac item detail cung theo rule nay.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Thep gia cuong: ngoai le khi thep chu chi co 2 cay - 2026-06-22
- User chot quy tac:
  - Thep chu tu 3 cay tro len -> thep gia cuong xen ke theo khe giua thep chu.
  - Thep chu chi 2 cay -> thep gia cuong nam o giua tiet dien, khong bam vao 2 bien.
- Da sua `LongitudinalBarCreator.GetGapOffsets`: neu `mainCount <= 2` thi dung `EvenInteriorOffsets(addCount, usableHalf)`.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Thep gia cuong Layer 2: nam sat dai - 2026-06-22
- User yeu cau thep gia cuong layer/lop 2 nam sat dai.
- Can cu code cu: layer 2 chi co `Layer2OffsetFeet = 30mm` theo phuong dung, nhung vi tri ngang van dung rule xen ke nhu layer 1.
- Da sua:
  - `LongitudinalBarCreator.CreateSegment/CreateSegmentWithEndBends` them `forceFixedNumberAcrossWidth`.
  - `AdditionalBarCreator` va `BeamRebarOrchestrator.CreateTopAdditionalAtSupports` truyen `forceFixedNumberAcrossWidth: config.Layer >= 2`.
  - Ket qua: Layer 1 van xen ke voi thep chu; Layer 2 rai Fixed Number theo be rong tiet dien, cac thanh bien sat dai.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Muc 9 chon dam lam goi: dai khong xuyen vao goi - 2026-06-22
- User yeu cau khi chon them dam lam goi thi thep dai cua dam chinh khong duoc xuyen vao vung goi.
- Can cu code cu:
  - Muc 9 chi luu `Point3` (`InternalSupportPoints`), khong luu nua be rong goi.
  - `TryCreateStirrupFrame` da lui dai theo `support.HalfWidthFeet + FirstDistanceFromSupport`, nhung support pick bang dam co `HalfWidthFeet=0`.
- Da them `SupportInfo(Point3 Location, double HalfWidthFeet)`.
- `SupportPicker.PickSupportInfos` nay tinh half-width cua cot/dam chon bang geometry solid/bbox chieu len truc dam chinh.
- `QuickSettingModel.InternalSupports`, Quick VM va Detail VM truyen du lieu nay xuong engine.
- `BeamRebarOrchestrator` gop `InternalSupports` vao support points va enrich `run.Supports` bang half-width do, nen dai se lui khoi mep dam goi + khoang FirstDistance.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Tu dong tranh tat ca dam giao vao thep dai - 2026-06-22
- User yeu cau them dieu kien: cac dam giao vao dam chinh thi thep dai khong duoc xuyen qua.
- Da sua `BeamRebarOrchestrator`:
  - Khi tao stirrup cho tung span, quet tat ca `OST_StructuralFraming` khac cac dam chinh da pick.
  - Loc dam co cao do bbox cham vung cao do dam chinh.
  - Tinh giao diem 2D giua truc span va LocationCurve dam giao.
  - Doc geometry solid/bbox cua dam giao, chieu len truc dam chinh de lay station giua + halfWidth theo mep thuc.
  - Gop vao danh sach `SecondaryStirrupStation`, dedupe theo station; `StirrupCreator` se cat bo dai thuong trong vung do va dat cum dai gia cuong hai ben.
- Muc dich: ngay ca khi user khong chon muc 10, dam giao van tao vung tranh dai, khong cho dai xuyen vao beam giao.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### FIX: them goi muc 9 chua tac dung o Detail + ve thuc te - 2026-06-20
- LOI: pick goi muc 9 chi luu _supportPoints. Detail doc PickedSpans (tinh luc pick Ribbon, chi cot tu do)
  -> khong thay goi them. BeamSpanReader.ReadSpans khong nhan goi thu cong.
- FIX:
  - BeamSpanReader.ReadSpans(doc, beams, extraSupportPoints=null): chia nhip = cot tu do + goi thu cong.
  - RebarCreationHandler SelectSupports: sau pick (con trong API context) tinh lai ReadSpans(doc, PreselectedBeams, supports) -> OnBeamInfo.
  - VM SelectSupportBeams gan _handler.OnBeamInfo = OnSpansRecomputed -> cap nhat PickedSpans -> Detail+preview thay nhip moi.
- Engine tao thep da dung InternalSupportPoints (BeamRebarOrchestrator dong 46) -> ve thuc te dung; van de chi la Detail/preview chua refresh.
- Build pass, test 18/18. DLL: C:\Users\Admin\Desktop\BeamRebarPro-test\ (13:44).

### FIX: dai khong xuyen vao goi co dam giao (dau/cuoi nhip) - 2026-06-22
- LOI: dai da lui khoi COT (HalfWidthFeet tu ColumnDetector) nhung goi la DAM GIAO them thu cong (muc 9)
  co HalfWidthFeet=0 -> dai xuyen vao vung dam giao tai dau/cuoi nhip.
- FIX: `EnrichSupportsWithColumnWidth(run, hits, defaultInternalHalfWidthFeet=0)`:
  goi GIUA (khong IsEnd) khong match cot -> gan defaultInternalHalfWidthFeet = nua be rong dam chinh.
  Bo dieu kien `if (hits.Count==0) return` de xu ly ca khi khong co cot.
- Orchestrator + BeamSpanReader truyen `segments[0].Section.WidthMm/304.8/2` lam default.
- TryCreateStirrupFrame da lui dai theo HalfWidthFeet+firstDistance -> gio dai lui khoi mep dam giao.
- Build pass, test 18/18. DLL: C:\Users\Admin\Desktop\BeamRebarPro-test\ (10:09).

### FIX: dai lui khoi DAM GIAO dung 50mm (do bbox that, khong doan) - 2026-06-22
- VAN DE: dai xuyen vao dam giao tai goi bien/cuoi. Dam giao gac LECH vao trong nhip (khong doi xung qua giao diem).
- CO SO (xac minh bang popup chan doan + user do thuc te = 50mm):
  - ColumnDetector collect _crossBeams (OST_StructuralFraming + width param b).
  - FindCrossBeamHits: TryIntersectXy (giao 2 truc XY) tim diem cat; ProjectBboxOntoAxis chieu 8 goc bbox dam giao
    len truc dam chinh -> vung [tMin,tMax] THAT dam giao chiem. center=(tMin+tMax)/2, half=(tMax-tMin)/2.
    -> dai lui toi MEP THAT du dam giao lech (khong gia dinh doi xung qua giao diem).
  - Goi (bien+giua) match cot HOAC dam giao -> HalfWidthFeet that. TryCreateStirrupFrame lui dai = half+firstDistance.
- Ket qua: dai cach mep trong dam giao = firstDistance (50mm) ca 2 dau. User xac nhan "50MM".
- DA GO het popup/dong chan doan [Goi].
- Build pass, test 18/18. DLL: C:\Users\Admin\Desktop\BeamRebarPro-test\ (10:34).
- LUU Y AI SAU: dam giao phai la Structural Framing + co param b (Width). Cot van uu tien neu match gan hon.

### Dai lui khoi dam giao = 50mm - DA VERIFY bang so lieu Revit - 2026-06-22
- Engine dat dai cach mep TRONG dam giao dung 50mm (firstDistance). So lieu tu chan doan:
  dam giao dau bbox[0,200] -> mep trong @200; DAI DAU @250 -> cach 50mm. User xac nhan muon "mep trong @200 -> dai @250".
- Dam giao do bang BBOX chieu len truc (ProjectBboxOntoAxis) vi truc dam chinh thuong trung CANH dam giao
  -> giao diem 2 truc roi vao MEP (khong phai tam). Tam=(tMin+tMax)/2, half=(tMax-tMin)/2.
- LUU Y: vung vang NHAT tren man Revit (join/overlap) rong hon mep ket cau that -> nhin "xa hon 50mm" la ao giac thi giac.
  Mep ket cau that (duong dam) @200, dai @250 = 50mm.
- DA GO het popup/chan doan [Goi]. Build pass, test 18/18. DLL (10:56).
- KHONG sua them tru khi thuoc Measure that cho so khac 50.

### FIX THAT: ho dai o goi do XUNG DOT 2 co che (goi vs dam phu) - 2026-06-22
- GOC RE (dung canh bao user "khong sua dong thoi 2 loi"): dam giao o GOI BIEN bi xu ly boi CA 2:
  1. Co che goi: stirrupFrame lui khoi no (50mm) - dung.
  2. Co che DAM PHU (FindIntersectingBeamStations): coi no la dam phu -> tao BLOCKED RANGE + dai tang cuong
     quanh do -> XOA dai trong vung -> ho o mep goi -> khoang > 50mm.
- FIX: dam giao sat 2 dau nhip (trong khoang = CHIEU CAO DAM) KHONG coi la dam phu nua (da la goi).
  Doi bien loai station tu SupportToleranceFeet (60mm) -> edgeMarginFeet = chieu cao dam.
- Engine truoc da dat dai @250 cach mep @200 dung 50mm; loi la dai bi blocked-range xoa lam HO them.
- Build pass, test 18/18. DLL (10:59).

### GOC RE THAT: dai lui o goi DAU bi du (303mm) con goi CUOI dung (50mm) - 2026-06-22
- DI VONG NHIEU vi cu sua "cach do be rong dam giao" (bbox/family/solid) - DO KHONG PHAI GOC RE.
- GOC RE = 1 dong trong EnrichSupportsWithColumnWidth:
    HalfWidthFeet = distanceToColumnCenter + hit.HalfWidthFeet   // dist la VO HUONG (luon duong)
  -> goi cuoi: tam dam giao ~ diem goi -> dist~0 -> dung 50mm.
  -> goi dau: dam giao lech ve phia nhip -> tam cach diem goi xa -> dist lon -> dist+half CONG DON DU -> dai lui 303mm.
  Vo huong nen khong phan biet lech trai/phai -> BAT DOI XUNG (1 ben dung 1 ben sai).
- FIX: tinh khoang toi MEP TRONG theo HUONG VAO NHIP (co dau, chieu len vector toi goi ke), khong dung dist vo huong.
- BAI HOC: loi "1 ben dung 1 ben sai" -> tim cho dung .DistanceTo (vo huong) o noi can CO HUONG, dung sua tang tren.
- Build pass, test 18/18. DLL (11:19).

### GOC RE THAT (chung minh bang MCP doc Revit) - 2026-06-22
- Dung MCP send_code_to_revit doc truc tiep view dang mo: dam chinh B2759768 start X=4236->end X=36,
  dam giao trai B2353547 truc X=36 b=400 bbox X=[36,436] -> mep trong @X=436. Dai cu @X=130 -> XUYEN.
- GOC RE: trong EnrichSupportsWithColumnWidth, dam giao nam TRON 1 phia nhip nhung XY TAM trung X=36 voi diem goi
  -> cong thuc cu proj(tam-goi)*huong = 0 -> leftHalf chi = half(200), khong thay dam giao trai toi X=436.
- FIX: khi hit la dam giao (co EdgeMin/Max), chieu 2 MEP (tam +- half theo truc) len huong vao nhip,
  lay mep XA nhat = mep trong that. -> leftHalf=400 -> dai @X=486 cach mep X=436 dung 50mm.
- ColumnHit them EdgeMinFeet/EdgeMaxFeet (station tu segment.Start).
- BAI HOC: do khoang theo TRUC DAM (station), KHONG dung distXY - vi tam dam giao co the trung XY voi goi
  nhung van trai doc truc. Da go het DBG. Build pass, test 18/18. DLL (11:38).
- Verify bang MCP doc lai vi tri Rebar sau khi tao.

### HOAN TAT: dai cach mep dam giao 50mm (3 goc re, verify MCP) - 2026-06-22
User xac nhan "OK DA DUNG". 3 goc re tim bang MCP doc Revit truc tiep (bien `document`):
1. HALF goi dam giao = 0: dung solid-vertex chieu len truc + loc perp<be_rong_dam_chinh -> chi giu 2 dinh cung
   station -> vung rong. FIX: dung ProjectBboxOntoAxis (bbox chieu len truc, MCP xac nhan b=400 -> vung=400 dung).
2. MEP TRONG tinh sai khi dam giao lech 1 phia: distXY vo huong = 0 (XY tam trung goi). FIX: ColumnHit them
   EdgeMin/Max; Enrich chieu 2 mep theo HUONG VAO NHIP (goi->goi ke), lay mep xa = mep trong that.
3. LAYOUT dai khong phu het vung: SetLayoutAsNumberWithSpacing buoc CO DINH -> cum (n-1)*spacing ngan hon vung
   -> don ve `from`, ho o `to`=mep goi -> dai cach mep du ~94mm. FIX: SetLayoutAsMaximumSpacing(spacing, zoneLen)
   -> Revit chia deu phu het [from,to], dai cuoi tai mep goi.
- DA GO het DBG. Build pass, test 18/18. DLL (12:02).
- CACH DEBUG GEOMETRY: dung MCP send_code_to_revit doc toa do that thay vi popup + bat user do (xem memory).

### Detail Delete Selected: khong duoc tu sinh lai toan bo Add Bar - 2026-06-22
- User bao o Add Bot: bam `Delete Selected` xoa dong dang chon nhung Rebar List/preview lai hien lai tat ca thanh.
- Goc re code:
  - `DeleteSelectedRebar` remove item khoi `_bottomAddItems`.
  - Neu item cuoi bi xoa -> `_bottomAddItems.Count == 0`.
  - `RefreshRebarListFromState` goi `EnsureBottomAddItemsFromLegacy()`.
  - Ham ensure thay list rong nen dung `_bottomAddLayer1/_bottomAddLayer2` legacy de tao lai tat ca item.
- Fix:
  - Them `_topAddItemsMaterialized`, `_bottomAddItemsMaterialized`.
  - `EnsureTop/BottomAddItemsFromLegacy` chi dung legacy khi list chua tung materialize.
  - Reset span / AddOrUpdate danh dau materialized = true.
  - Delete item cuoi khong con sinh lai legacy; preview refresh lai sau delete.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Detail Delete Selected: xoa thanh cuoi roi doi tab quay lai van hien 1 thanh - 2026-06-22
- User bao sau fix tren: xoa thanh cuoi, qua tab khac quay lai van con 1 thanh.
- Goc re bo sung:
  - Materialized flag da chan `Ensure...FromLegacy`, nhung `RefreshRebarListFromState` van co nhanh `else AddIfEnabled(legacy)`
    khi `_bottomAddItems.Count == 0`.
  - `BuildModel` cung dung `_bottomAddItems.Count > 0` de quyet dinh fallback legacy, nen neu list da xoa het van co the tao lai legacy luc OK/Create.
  - `SaveAddState` khi doi tab co the ghi editor vao legacy neu khong co selected item.
- Fix:
  - Trong Refresh: neu `_top/_bottomAddItemsMaterialized == true`, chi hien item-list, ke ca list rong; khong fallback legacy.
  - Trong BuildModel: neu materialized true thi legacy Additional disabled va items la nguon that.
  - Trong SaveAddState: neu materialized true va khong selected item thi khong ghi nguoc vao legacy.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Detail Add/Delete Add Top/Add Bot: preview dung theo list that - 2026-06-22
- User bao Add Top: Rebar List rong nhung preview duoi van con nhieu thanh do; bam Add lai tao hang loat thanh theo Start 0 -> End 3.
- Goc re code trong `BeamRebarDetailViewModel`:
  - `RefreshElevationPreview()` cua Add Top van ve truc tiep tu editor `AddStartPoint/AddEndPoint`, khong doc `_topAddItems`.
  - Add Bot preview chi ve 1 item dang chon/first item, khong phai toan bo list that.
  - `AddOrUpdateTopAdditionalItems` va `AddOrUpdateBottomAdditionalItems` loop qua ca range Start/End nen bam Add 1 lan sinh nhieu item.
  - `WriteBackToParent()` luon `SaveEditorToSelectedTab()`, co the ghi de item dang chon truoc khi Add.
- Fix:
  - Add Top/Add Bot preview nay chi ve tu `_topAddItems/_bottomAddItems`; list rong thi preview rong, delete dong nao hinh duoi mat dong do.
  - Add Top bam Add chi them/cap nhat 1 support tai `StartPoint` (EndPoint = StartPoint).
  - Add Bot bam Add chi them/cap nhat 1 span tai `StartPoint -> StartPoint+1`.
  - Add khong con save-de item dang chon qua `WriteBackToParent(saveEditor:false)`; van nho layer dang chon.
- Build: `dotnet build -c Debug.R25 -p:DeployAddin=false` pass 0 warning.
- Test: `dotnet test` trong `tests/BeamRebarPro.Tests` pass 18/18.
- DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Detail Add: bam nhieu lan phai them tung cay tiep theo - 2026-06-22
- User bao sau fix tren chi Add duoc 1 cay. Goc re:
  - Add da chuyen sang 1 item/lần, nhung neu `StartPoint` khong doi thi lan bam tiep theo match cung `Layer+Start+End` va update item cu.
- Fix:
  - `AddOrUpdateTopAdditionalItems`: neu support hien tai da co item cung layer thi tim support trong ke tiep; sau khi add tu dong chuyen editor sang support trong ke tiep.
  - `AddOrUpdateBottomAdditionalItems`: tuong tu, tim span trong ke tiep va set editor sang `StartPoint=span tiep`, `EndPoint=span+1`.
  - Khi tat ca support/span da co item, bam Add se update item tai vi tri hien tai (khong nhan ban trung).
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Detail Add lai: chieu dai phai tinh theo quy luat cua vi tri moi - 2026-06-22
- User yeu cau khi Add lai tung cay thi chieu dai tung cay moi phai tinh lai theo quy luat ban dau, khong dung lai so cua cay truoc.
- Goc re:
  - Khi chon 1 item cu, form load `LeftLength/RightLength` da tinh san cua item do.
  - Lan Add tiep theo dung lai so mm tuyet doi nay cho support/span moi, nen chieu dai sai voi nhip moi.
- Fix:
  - Them normalize truoc khi luu item:
    - Add Top: tinh `LeftLength = LeftRatio * leftSpan`, `RightLength = RightRatio * rightSpan` theo support target.
    - Add Bot: tinh clear span theo `StartPoint -> EndPoint`, roi tinh left/right/anchor theo ratio cua target.
  - Sau khi Add va tu nhay sang vi tri trong ke tiep, editor cung load state da tinh cho vi tri moi.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Detail Add: sua Left/Right Length phai dong bo vao preview/item dang chon - 2026-06-22
- User bao khi sua `Left Length`/`Right Length` tren Add Top, so tren form doi nhung preview duoi khong doi theo.
- Goc re:
  - De tranh loi bam Add ghi de item dang chon, truoc do `SyncEditorToParent()` dung `WriteBackToParent(saveEditor:false)` tren tab Add.
  - Tac dung phu: cac thay doi tu editor khong goi `SaveAddState`, nen `_topAddItems/_bottomAddItems` khong cap nhat; preview ve theo item cu.
- Fix:
  - `SyncAddEditor()` nay save truc tiep vao selected Add Top/Add Bot item bang `SaveAddState(...)`, roi moi `WriteBackToParent(saveEditor:false)`.
  - Neu khong chon item va list da materialize thi `SaveAddState` van khong tu tao legacy lai, giu dung fix delete cu.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### FIX: Add bar layer 1 count>=2 phai la Fixed Number + tao xong dong Quick Setting - 2026-06-22
- User yeu cau 2 viec:
  1. Thep tang cuong layer/lop 1 count >= 2 khong duoc tao tung set Single nua, phai la 1 rebar set Fixed Number.
  2. Tao thep xong thanh cong thi dong luon cua so Quick Setting.
- Goc re layer 1:
  - `LongitudinalBarCreator.CreateSegment` va `CreateSegmentWithEndBends` khi co `gapOffsets` dang foreach tung vi tri va goi tao `count=1`, nen Revit hien Single.
- Fix layer 1:
  - Neu `gapOffsets.Count > 0` va `count >= 2`, tao 1 rebar set tai lateral dau tien va goi `SetLayoutAsFixedNumber(count, layoutDistance)`.
  - `layoutDistance = lateral cuoi - lateral dau`, nen van giu dung vi tri xen ke/interior da tinh, nhung Revit layout la Fixed Number.
  - Count=1 van tao Single nhu dung ban chat 1 cay.
- Fix dong Quick:
  - Them `BeamRebarProViewModel.RequestClose`.
  - `BeamRebarProView` subscribe event va `Close()`.
  - `OnRebarCompleted` chi invoke close khi `result.Succeeded == true`; neu loi thi giu form de user sua.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Default Main Bar anchor fields = 0 khi mo add-in lan dau - 2026-06-22
- User yeu cau cac field trong Detail Main Top/Main Bottom ban dau phai la 0, user muon thi tu nhap:
  - Anchor Left / Anchor Right
  - Anchor X Left / Anchor X Right
  - Anchor Y (Top bend down)
- Goc re:
  - Quick VM default `MainAnchorLengthMm=300`, `MainTopBendDownLengthMm=300`.
  - Detail `LoadFromParent()` tu sinh `AnchorX = MainAnchorLength * 0.53` -> 300 tao ra 159.
  - Detail observable default cung dang co 300/167.
- Fix:
  - `MainBarConfig.AnchorLengthMm` default = 0.
  - `BeamRebarProViewModel.MainAnchorLengthMm` va `MainTopBendDownLengthMm` default = 0.
  - `BeamRebarDetailViewModel.LoadFromParent()` khong con tinh `AnchorX = 0.53 * AnchorLength`; Anchor X mac dinh 0.
  - Detail observable default Anchor Left/Right/X/Y deu = 0.
  - Saved config co gia tri khac 0 van duoc load lai; khong pha luong nguoi dung da luu cau hinh.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Anti-bulge: tuy chon thep doc dam vao cot - 2026-06-22
- User yeu cau thep DOC chong phinh co tuy chon dam vao cot/goi mot doan, vi du 100mm.
- Da them `AntiBulgeConfig.ColumnEmbedMm`.
- Detail tab Anti-bulge them field `Column embed (mm)` bind `AntiBulgeColumnEmbedMm`.
- `BeamRebarProViewModel` va `BeamRebarDetailViewModel` dua field nay vao `BuildModel()` va load saved config.
- Geometry:
  - `AntiBulgeCreator` chi ap dung embed cho thanh DOC chong phinh.
  - Neu `ColumnEmbedMm = 0`: giu logic cu, thanh doc bat dau sau mep cot + cover.
  - Neu `ColumnEmbedMm = 100`: dau thanh doc lui vao trong cot 100mm tinh tu mep cot.
  - Tie ngang van giu logic cu, khong xuyen qua cot, de khong pha yeu cau truoc do.
- Build pass 0 warning, test 18/18 pass. DLL da copy: `C:\Users\Admin\Desktop\BeamRebarPro-test\BeamRebarPro.dll`.

### Tinh nang DAI PHU (Additional Stirrup) tu video THEP NHANH.mp4 - 2026-06-22
- Video phan tich: tab Stirrup co 4 tab con: Stirrup Distribution | Additional Stirrup | Hanger bar For 2nd Beam | Stirrup Shape.
- DAI PHU = dai chu nhat CON long trong dai chinh, om dai thanh chu GIUA (tru 2 thanh goc) theo phuong ngang
  -> giu thanh chu giua khoi phinh (TCVN >3 thanh/lop). Mat cat: dai chinh (do) om 4 thanh, dai phu (tim) om thanh 2-3.
- DA LAM:
  - Model: `AdditionalStirrupConfig` (Diameter, FromBarIndex, ToBarIndex) trong StirrupConfig.AdditionalStirrups.
  - Engine StirrupCreator: CreateAdditionalStirrup + StirrupProfileNarrow (canh trai/phai tai vi tri thanh chu
    giua, cao bang dai chinh) + CreateNarrowZone (rai cung vung TwoEnds/Uniform, tranh blocked range, MaxSpacing).
    Bo qua neu mainBarCount<3. Orchestrator truyen resolved.MainTop.Count.
  - VM: AdditionalStirrupItem record + ObservableCollection + Add/Modify/Remove commands + fields (Diameter, FromBar, ToBar 1-based).
  - XAML: tab con "Additional Stirrup" co Diameter, om thanh tu..den, list + Add/Modify/Remove.
- CHUA LAM (placeholder): "Hanger bar For 2nd Beam" (thep treo dam phu), "Stirrup Shape" (C-stirrup 180/135/135x90).
- Build pass, test 18/18. DLL (15:15). Frames video: C:\Users\Admin\Desktop\thep-nhanh-frames\.

### Deploy chinh thuc + Dai C giu thep gia cuong lop 2 - 2026-06-23
- Deploy `dotnet build -c Debug.R25` (Revit tat) -> Addins\2025\BeamRebarPro\ (8:40).
- TINH NANG MOI: Dai C giu thep gia cuong LOP 2 (>=3 cay):
  - `AdditionalTieCreator` (MOI): thanh thang + 2 hook 180 (RebarStyle.Standard) om 1 CAP thanh gia cuong ke nhau,
    rai doc nhip MaxSpacing, LUI khoi mep cot (leftHalf/rightHalf) -> khong xuyen ngang cot. Ap Top+Bot Layer2.
  - Model: AdditionalBarConfig them TieCDiameterMm + TieCSpacingMm (0=khong tao).
  - VM: AddTieCDiameterMm/AddTieCSpacingMm + ShowAddTieC (Layer==2 && Number>=3). MakeAdd map khi layer==2.
  - Orchestrator: goi additionalTieCreator.Create cho model.TopAdditionalLayer2 + BottomAdditionalLayer2 sau vong span.
  - XAML: hang "Dai C giu: [D] @ [mm]" hien khi ShowAddTieC.
- Section panel: dai phu (moc C = 2 net + 2 vong moc Ellipse | long kin = Rectangle kin). Ve SAU chams (noi len tren).
- Build pass, test 18/18. CHUA verify dai C trong Revit (test sau bang MCP).

### Dai C giu thep gia cuong lop 2 - HOAN THIEN (verify MCP) - 2026-06-23
- AdditionalTieCreator: dai C = thanh ngang + 2 hook 180 (RebarStyle.Standard), om tu CAY BIEN den CAY BIEN
  (LatOf(0)-grow .. LatOf(n-1)+grow, grow=nua D thep GC + nua D tie -> bao tron thanh).
- Chi rai trong VUNG thep gia cuong (AdditionalBarRanges): top=2 doan goi (0.25L moi ben tu mep cot),
  bottom=1 doan giua nhip (1/8..7/8 L thong thuy). KHONG rai suot nhip.
- Lui khoi cot (leftHalf/rightHalf) -> khong xuyen ngang cot.
- Vi tri dung (verify MCP): tieV top = vertical - offsetToInner - barFeet/4 (XUONG); bottom = vertical - barFeet/4 (LEN xiu).
  Ket qua: dai C top Z=[18260,18295] om GC top [18258,18298]; bottom Z=[17956,17988] om GC [17950,17990]. KHIT.
- BAI HOC QUAN TRONG: he toa do Z AM (vertical am). TRU = xuong (thap), CONG = len (cao). De nham chieu khi move.
- UI: AddTieCDiameterMm/SpacingMm, ShowAddTieC khi Layer==2 && Number>=3. Hang "Dai C giu: [D] @ [mm]".
- User xac nhan OK. Build pass, test 18/18.

### Deploy ban hoan chinh (dai C giu thep GC lop 2) - 2026-06-23 9:42
- dotnet build -c Debug.R25 (Revit tat) -> Addins\2025\ (557KB). Gom het tinh nang + fix dai C verify MCP.

### Deploy + dai C linh hoat theo chieu dai thep GC + mac dinh D6@500 - 2026-06-23 11:01
- Deploy Debug.R25 (Revit tat) -> Addins\2025\ (558KB).
- Dai C giu thep gia cuong lop 2:
  - Mac dinh D6@500 (EnsureDefaultTieC khi layer2 & >=3 cay).
  - Vung rai LINH HOAT theo Length/Anchor nguoi dung sua (AdditionalBarRanges):
    BOT khop CreateMidspan (start=clearStart+AnchorLeft, end=clearEnd-AnchorRight);
    TOP khop CreateTopAdditionalAtSupports (quanh goi: [0, leftHalf+leftExtend] + [len-rightHalf-rightExtend, len]).
- Thep gia cuong BOT lop2 mac dinh ratio 0.375 -> 1/8-7/8 (ngan giua nhip, khong het dam).
- FIX cong thuc "sua so ve khong dung": UI rang buoc Anchor=clear/2-Length; engine cu start=mid-Length-Anchor
  -> TRIET TIEU Length -> luon mep cot. Sua: start=clearStart+AnchorLeft, end=clearEnd-AnchorRight.
- So goi y lam tron 50 (Round50), tu dien san vao o khi vao tab (LoadAddStateIntoEditor).

### MCP tool rieng cho BeamRebarPro: draw_beam_rebar - 2026-06-23
- Theo pattern bai giang mcp-revit (Convert Command -> Service + MCP tool 2 phia):
  - src/BeamRebarPro/Services/BeamRebarApi.cs: DrawForBeams(doc, beamIds, options) headless. BeamRebarApiOptions
    (MainTop/Bot Count+Diameter, Stirrup D+spacing, Cover). Goi QuickSettingFactory.CreateDefault() + override -> Orchestrator.
    LUU Y: BeamRebarOrchestrator TU mo transaction -> handler KHONG mo (tranh nested).
  - RevitMcpCommands/DrawBeamRebarCommand.cs: tool "draw_beam_rebar", parse JSON beamIds + params.
  - RevitMcpCommands/DrawBeamRebarEventHandler.cs: chay UI thread, KHONG mo transaction.
  - RevitMcpCommands.csproj them ProjectReference ..\src\BeamRebarPro\BeamRebarPro.csproj.
- Build: dung -p:DeployAddin=false khi Revit dang chay (tranh lock DLL). Code compile OK.
- DE CHAY: dong Revit -> build RevitMcpCommands deploy vao revit_mcp_plugin dir -> dang ky command ->
  bat nut MCP Server (8080) -> mo TAB Claude Code MOI -> /mcp -> thay draw_beam_rebar.
- Options hien toi gian (chu+dai+cover). Gia cuong/dai phu/dai C dung mac dinh TCVN. Mo rong BeamRebarApiOptions neu can.

### Deploy MCP tool draw_beam_rebar vao plugin - 2026-06-23 11:38
- Copy RevitMcpCommands.dll (2 command) + BeamRebarPro.dll + deps -> revit_mcp_plugin\Commands\ColumnRebar\.
- Them entry draw_beam_rebar vao commandRegistry.json (tro cung DLL, enabled, v2025).
- DE THAY TOOL: bat nut MCP Server (8080) trong Revit + MO TAB Claude Code MOI -> /mcp.
