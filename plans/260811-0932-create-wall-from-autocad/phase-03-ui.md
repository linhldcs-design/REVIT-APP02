# Phase 03 — Giao diện tab Create Wall

**Ưu tiên:** trung bình
**Trạng thái:** chưa bắt đầu — phụ thuộc Phase 02

## Liên kết

- `RevitAPP/Views/ModelFromCadWindow.xaml` — tab Slab là mẫu gần nhất
- `RevitAPP/ViewModels/ModelFromCadViewModel.cs`
- `RevitAPP/ViewModels/CadSlabRowViewModel.cs`

## Việc cần làm

Thêm tab `Create Wall` vào cửa sổ có sẵn, theo đúng khuôn bốn tab đang chạy.

## Bố cục

```
[1. Select Grid Axes]  [2. Select Wall Lines]
     (bắt buộc trước)   (mở khi bước 1 xong)

Wall Type:    [dropdown]        Bề dày min: [100]
Base Level:   [dropdown]        Bề dày max: [400]
Top Level:    [dropdown]        Min Line:   [300]
Offset:       [0]               Tỷ lệ dài/dày: [3.0]
              [Apply / Re-analyze]

[Chọn tất cả] [Bỏ chọn] [Reset CAD]

┌ Tạo │ Dài │ Dày │ Type │ Trạng thái ┐
└─────────────────────────────────────┘
        (cột Dày sửa được, như tiết diện ở tab Beam)

[2D] [3D]  ☑ Lưới ☑ Nhãn  [+] [-] [Vừa màn hình]
```

**Bề dày min/max đặt ngay hàng đầu**, cạnh nút quét — người dùng chỉnh trước khi
quét, không phải tìm.

## Việc

**Sửa**
- `ModelFromCadWindow.xaml` — thêm tab, giữ nguyên style `{DynamicResource}`
- `ModelFromCadViewModel.cs` — chế độ Wall, danh sách, lệnh, tùy chọn
- `ModelFromCadCommand.cs` — nối service

**Tạo mới**
- `RevitAPP/ViewModels/CadWallRowViewModel.cs`

## Ràng buộc

- `CommunityToolkit.Mvvm` — `[ObservableProperty]`, `[RelayCommand]`, không tự viết `INotifyPropertyChanged`
- Mọi màu/khoảng cách dùng `{DynamicResource}`, không hardcode
- Code-behind chỉ `InitializeComponent()` + `DataContext`
- ViewModel đang ~900 dòng — nếu phần Wall làm nó phình quá, tách `ModelFromCadWallViewModel` riêng

## Review 2D và 3D — như tab Beam

Dùng lại đúng canvas và viewport của tab Beam, chỉ khác cách vẽ hình.

**2D**

- Vẽ **dải tường**: hai mép theo bề dày, trục tim nét mảnh ở giữa
- Zoom `+` / `-` / `Vừa màn hình`, kéo để dời — như các tab khác
- Bật/tắt `Lưới` và `Nhãn`
- Tường được tick hiện màu nổi; bỏ tick thì mờ đi
- Chọn dòng trong bảng thì tường đó sáng lên trong canvas, và ngược lại

**3D**

- Dựng khối hộp theo bề dày × chiều cao (Base → Top Level)
- Xoay được (orbit) như tab Beam và Slab
- Thấy ngay tường có đúng chiều cao không, góc có nối liền không

**Chỉnh trong review**

Tab Beam cho sửa tiết diện ngay trong bảng. Tường cũng vậy: sửa **bề dày** từng tường
nếu add-in đo sai, không phải quét lại. Sửa xong `Apply` là preview vẽ lại.

Chiều cao không sửa từng cái — nó theo Base/Top Level chung cho cả mẻ.

## Xong khi

- Tab hiện đủ, chọn/bỏ chọn chạy
- Đổi bề dày min/max rồi `Apply` thì phân tích lại, không phải quét lại CAD
- 2D vẽ dải tường có bề dày, zoom/dời/vừa màn hình chạy
- 3D dựng khối theo chiều cao level, xoay được
- Chọn dòng trong bảng thì tường sáng lên trong canvas
- Sửa bề dày trong bảng rồi `Apply` thì preview vẽ lại
- Theme sáng/tối đều đúng

## Rủi ro

| Rủi ro | Cách giảm |
|---|---|
| ViewModel phình quá | Tách file riêng nếu vượt ~1200 dòng |
| Bảng review chật | Tab Slab từng bị; giữ ít cột, cột nào cũng phải có nghĩa |
