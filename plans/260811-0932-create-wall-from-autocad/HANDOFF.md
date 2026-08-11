# HANDOFF — tab Create Wall

## Câu lệnh bắt đầu

> Đọc `plans/260811-0932-create-wall-from-autocad/HANDOFF.md` và `plan.md`, kiểm tra
> `git status -sb`, rồi làm tiếp từ mục "Việc còn lại". Không đóng hoặc mở Revit nếu
> người dùng chưa cho phép.

## Đang ở đâu

| Phase | Trạng thái |
|---|---|
| 01 — Phân tích | **Xong**, 30 test |
| 02 — Dựng trong Revit | **Xong**, chưa có test tích hợp |
| 03 — Giao diện | **Chưa** — XAML chưa viết, tab chưa lên ribbon |
| 04 — Kiểm chứng, phát hành | **Chưa** |

Commit gần nhất: `268338f`. Test 527/527 pass. Build R22–R27 sạch.

**Người dùng chưa thử được gì** — không có tab nên không có đường nào gọi tới code đã viết.

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
| Cửa đi/cửa sổ | Người dùng vẽ hai line nối qua, cùng layer — add-in không xử lý |
| Review | 2D/3D như tab Beam, sửa được bề dày |
| Trước phát hành | Deploy R25 → người dùng kiểm → mới phát hành |

## Đã viết

**`RevitAPP.Core/Services/CadRailBuilder.cs`** — tách từ `CadBeamAnalyzer`, cả hai tab
dùng chung. Gom line rời thành boundary, giữ nguyên dung sai đã kiểm chứng qua 10 lỗi
thực tế ở tab Beam. Rail mang theo `Layer`, và gom theo layer nên hai layer không trộn.
16 test.

**`RevitAPP.Core/Services/CadWallAnalyzer.cs`** — nhận tường từ hai line song song và
rectangle hẹp; đo bề dày; kéo trục tới giao điểm ở góc. 14 test, gồm ca phòng bốn tường
phải gặp nhau đủ bốn góc.

**`RevitAPP.Core/Models/CadStructure/CadWallModels.cs`** — `CadWallCandidate`,
`CadWallAnalysisOptions`, `CadLayerTally`.

**`RevitAPP/Services/CadStructure/CadWallCreationService.cs`** — `Wall.Create`, dò/sinh
Wall Type theo cấu tạo lớp, bỏ tường trùng, `TransactionGroup` bọc ngoài.

**`RevitAPP/Services/CadStructure/CadWallPreviewFactory.cs`** — nối quét CAD với analyzer.

**`RevitAPP/ViewModels/CadWallRowViewModel.cs`** — dòng tường + dòng layer.

**`ModelFromCadViewModel.cs`** — đã thêm: `ModelFromCadMode.Wall`, `WallData`, `Walls`,
`WallLayers`, `WallTypes`, các `[ObservableProperty]` cho tùy chọn, và `SetWallData`.

## Việc còn lại

### 1. Hoàn tất ViewModel

Đã có: `WallOptions()`, `WallSettingsValid`, `WallCreateSettingsValid`, `SelectedWalls`,
`SetWallData`, và các command `SelectWallLines`, `ApplyWallAnalysis`, `SelectAllWalls`,
`ClearWallSelection`.

Còn thiếu:

- `SummaryLabel` chưa có nhánh `ModelFromCadMode.Wall` — hiện rơi vào nhánh mặc định và
  báo nhầm là dầm
- `CreateCommand` / `CanCreate` chưa xử `ModelFromCadMode.Wall`
- `NotifyState()` chưa báo các thuộc tính của Wall
- Đổi tùy chọn hay tick layer chưa đặt `WallAnalysisDirty = true` — xem cách phần Slab
  làm trong `OnItemChanged`

### 2. XAML — `RevitAPP/Views/ModelFromCadWindow.xaml`

Tab Slab là mẫu gần nhất. Bố cục đã chốt với người dùng:

```
[1. Select Grid Axes]  [2. Select Wall Lines]
     (bắt buộc trước)   (mở khi bước 1 xong)

Wall Type:    [dropdown]        Bề dày min: [100]
Base Level:   [dropdown]        Bề dày max: [400]
Top Level:    [dropdown]        Min Line:   [300]
Offset:       [0]               Tỷ lệ dài/dày: [3.0]
              [Apply / Re-analyze]

Layer nào là tường?
  ☑ A-WALL              86 line
  ☐ NT2-NET DAM 0.4    142 line

[Chọn tất cả] [Bỏ chọn]

┌ Tạo │ Dài │ Dày │ Vẽ bằng │ Trạng thái ┐
        (cột Dày sửa được, như tiết diện ở tab Beam)

[2D] [3D]  ☑ Lưới ☑ Nhãn  [+] [-] [Vừa màn hình]
```

**Bề dày min/max đặt hàng đầu**, cạnh nút quét — người dùng chỉnh trước khi quét.

Ràng buộc: `CommunityToolkit.Mvvm`, mọi màu dùng `{DynamicResource}`, code-behind chỉ
`InitializeComponent()`.

### 3. Preview 2D/3D

- **2D**: vẽ dải tường (hai mép theo bề dày + trục tim nét mảnh), zoom/dời/vừa màn hình,
  tick thì nổi màu, chọn dòng thì tường sáng lên
- **3D**: khối hộp theo bề dày × chiều cao level, orbit xoay được

Xem cách tab Beam vẽ trong `ModelFromCadWindow.xaml.cs`.

### 4. Nối lệnh — `RevitAPP/Commands/ModelFromCadCommand.cs`

- Truyền `selectWall` vào constructor ViewModel (tham số đã có, chưa ai truyền)
- Thêm `SelectAndBuildWallPreview` theo mẫu `SelectAndBuildSlabPreview`
- Nhánh `ModelFromCadMode.Wall` gọi `CadWallCreationService.Create`
- `ShowWallResult` báo số tường tạo/trùng/lỗi

### 5. Kiểm chứng — theo `phase-04-verify.md`

Thứ tự **bắt buộc**, không đảo:

1. Test xanh hết (kể cả Beam, Slab cũ)
2. Build R22–R27
3. Deploy `Debug R25` — chờ người dùng đóng Revit, **không tự đóng**
4. Người dùng thử trên bản vẽ thật, xác nhận đạt
5. Chỉ khi đó mới phát hành `v1.12.0` theo `HANDOFF-REVITAPP.md`

## Hai câu hỏi chưa có lời

**1. Rectangle 300×900 là vách ngắn hay cột chữ nhật?** Tỷ lệ 3:1. Mặc định
`MinimumLengthRatio = 3.0` đang nhận nó là tường. Hỏi người dùng bản vẽ có cột cỡ đó
không — nhận nhầm cả loạt cột thành tường thì hỏng nặng.

**2. Cách vẽ cửa** — tôi hiểu là người dùng vẽ hai line nối qua chỗ cửa, cùng layer
tường, nên add-in thấy tường liền mạch. Chưa được xác nhận dứt khoát.

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
