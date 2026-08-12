# HANDOFF — tab Create Wall

## Câu lệnh bắt đầu

> Đọc `plans/260811-0932-create-wall-from-autocad/HANDOFF.md` và `plan.md`, kiểm tra
> `git status -sb`, rồi làm tiếp từ mục "Việc còn lại". Không đóng hoặc mở Revit nếu
> người dùng chưa cho phép.

## Đang ở đâu

| Phase | Trạng thái |
|---|---|
| 01 — Phân tích | **Xong** — fix bridge cửa đạt 542/542 toàn bộ; 80 Wall/Rail/Beam |
| 02 — Dựng trong Revit | **Xong**, chưa có test tích hợp |
| 03 — Giao diện | **Xong về code** — tab Wall, review 2D/3D và command đã nối |
| 04 — Kiểm chứng, phát hành | **Đang phát hành v1.12.0** — test/build final đã qua; người dùng yêu cầu phát hành trước khi smoke Wall riêng hoàn tất |

Commit nền gần nhất: `268338f`; fix analyzer cuối đang ở working tree. Test 542/542 pass.
`Debug.R25` và `Release.R22`–`Release.R27` build final đều exit 0 với deploy tắt.

Ngày 12/08/2026, người dùng đã ra lệnh phát hành `v1.12.0` sau khi kiểm tra thực tế các
thay đổi Vẽ Thép Cột. Runtime smoke riêng cho tab Wall chưa được xác nhận đầy đủ; đây là
rủi ro phát hành đã được chấp nhận bằng lệnh phát hành trực tiếp.

## Người dùng đã chốt

| Điểm | Chốt |
|---|---|
| Bước 1 | Quét lưới tham chiếu, bắt buộc trước |
| Nhận tường | Hai line song song **và** rectangle hẹp |
| Bề dày | Đo hình học, **không đọc text** |
| Chiều cao | Base Level → Top Level |
| Wall Type | Dò type có sẵn trước, không có mới sinh |
| Bề dày min/max | Ô nhập **trước khi quét** |
| Lọc layer | **Bắt buộc** — người dùng tick, add-in tick sẵn theo tên |
| Ghép cặp | Hai line phải **cùng layer** mới thành tường |
| Cửa đi/cửa sổ | Chỉ nối khi cả hai bridge dọc tiếp tục đúng hai mặt tường trên chính layer tường người dùng chọn; không suy luận jamb/end-cap ngang |
| Review | 2D/3D như tab Beam, sửa được bề dày |
| Trước phát hành | Deploy R25 → người dùng kiểm → mới phát hành |

## Đã viết

**`RevitAPP.Core/Services/CadRailBuilder.cs`** — tách từ `CadBeamAnalyzer`, cả hai tab
dùng chung. Gom line rời thành boundary, giữ nguyên dung sai đã kiểm chứng qua 10 lỗi
thực tế ở tab Beam. Rail mang theo `Layer`, và gom theo layer nên hai layer không trộn.
16 test.

**`RevitAPP.Core/Services/CadWallAnalyzer.cs`** — nhận tường từ hai line song song và
rectangle hẹp; đo bề dày; kéo trục tới giao điểm ở góc. Fix cuối chỉ consolidate đoạn
qua cửa khi cả hai bridge dọc tiếp tục hai mặt tường trên chính layer được chọn. Pairing
đòi cả hai rail overlap; short nib được giữ đến final filter; rectangle không được nối
bằng jamb/end-cap ngang.

**`RevitAPP.Core/Models/CadStructure/CadWallModels.cs`** — `CadWallCandidate`,
`CadWallAnalysisOptions`, `CadLayerTally`.

**`RevitAPP/Services/CadStructure/CadWallCreationService.cs`** — `Wall.Create`, dò/sinh
Wall Type theo cấu tạo lớp, bỏ tường trùng, `TransactionGroup` bọc ngoài.

**`RevitAPP/Services/CadStructure/CadWallPreviewFactory.cs`** — nối quét CAD với analyzer.

**`RevitAPP/ViewModels/CadWallRowViewModel.cs`** — dòng tường + dòng layer.

**`ModelFromCadViewModel.cs`** — đã thêm: `ModelFromCadMode.Wall`, `WallData`, `Walls`,
`WallLayers`, `WallTypes`, các `[ObservableProperty]` cho tùy chọn, và `SetWallData`.

**Phase 03 UI** — tab thứ năm `Create Wall` đã có đủ quy trình Grid → quét Wall → chọn
layer → Apply, bảng sửa bề dày, preview 2D/3D, chọn dòng/canvas, zoom/pan/orbit. Override
bề dày, trạng thái chọn và dòng đang chọn được giữ qua lần Apply.

**`ModelFromCadCommand.cs`** — đã truyền `selectWall`, nối preview, gọi
`CadWallCreationService.Create` và hiển thị số tạo/trùng/lỗi.

## Việc còn lại

1. Theo dõi GitHub Actions cho `v1.12.0` tới khi đủ sáu build, installer và release.
2. Kiểm tra đủ tám asset và `latest.json` HTTP 200.
3. Sau phát hành, tiếp tục smoke tab Wall trên bản vẽ thật, gồm contract bridge cửa và
   các guard âm ở `phase-04-verify.md`.

Kết quả kiểm chứng hiện tại: `542/542` test pass; 80 test Wall/Rail/Beam pass. Các guard
âm xác nhận layer khác không bridge `A-WALL`, một bridge không đủ, boundary đơn không
sinh tường ma, line gần song song không bị nắn thẳng và hai tường cap độc lập
200 mm × 10 m vẫn tách. Offset-drift clustering và cleanup duplicate bridge vẫn giữ ca
hai mặt tường so le. Build artifacts final có timestamp 11:58:54–12:01:40; chỉ DLL
deploy/hash và runtime còn chờ.

## Câu hỏi còn mở

**1. Rectangle 300×900 là vách ngắn hay cột chữ nhật?** Tỷ lệ 3:1. Mặc định
`MinimumLengthRatio = 3.0` đang nhận nó là tường. Hỏi người dùng bản vẽ có cột cỡ đó
không — nhận nhầm cả loạt cột thành tường thì hỏng nặng.

## Bài học phải theo

**Test trước, sửa sau.** Công thức cung tròn ở `v1.11.0` từng dò dấu bằng script rời,
tám vòng không hội tụ. Viết 30 test trước rồi sửa — ba lần là xanh. Toàn bộ phần tường
đã viết theo lối này.

**Không tự quyết theo tên layer.** Ở `v1.11.0` một bộ lọc `GRID`/`DIM`/`TEXT` làm hỏng
bước quét lưới vì trục nằm trên `S-GRID`, phải `git revert`. Ở tab Wall, tên layer chỉ
dùng để **tick sẵn** — người dùng bỏ tick là xong.

**Không `git add -A` cả worktree.** Có nhiều thư mục chưa tracked thuộc việc khác
(`RevitAPP/Services/CadGrid/`, `plans/`, `tools/`...). Chỉ stage đúng file mình sửa.

**Build đủ R22–R27 trước khi phát hành.** `double.IsFinite` không có trên net48 — lỗi
này chỉ lộ ở R22/R23.

## Lệnh hay dùng

```bash
dotnet test tests/RevitAPP.Tests/RevitAPP.Tests.csproj -c Release
dotnet build RevitAPP/RevitAPP.csproj -c "Release.R25" -p:DeployAddin=false -p:LaunchRevit=false
dotnet build RevitAPP/RevitAPP.csproj -c "Debug R25"   # deploy, cần Revit đóng
```
