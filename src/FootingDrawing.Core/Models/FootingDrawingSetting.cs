namespace FootingDrawing.Core.Models;

/// <summary>
///     Cấu hình một lần sinh bản vẽ mặt bằng thép móng — tương ứng MỘT mục trong "Setting List" của UI.
///     Immutable, serialize JSON. Nguyên tắc: mọi thứ là TÙY CHỌN của user (không auto-tính); addin chỉ
///     thực thi đúng lựa chọn + warn nếu thiếu.
/// </summary>
public sealed record FootingDrawingSetting
{
    /// <summary>Tên setting (khoá trong list). Bắt buộc.</summary>
    public string Name { get; init; } = string.Empty;

    // ===== Combobox chọn type (tên type trong project) =====
    /// <summary>Loại dimension (OST_Dimensions), vd "@BS-Dim A1".</summary>
    public string? DimensionTypeName { get; init; }

    /// <summary>Tag lưới thép móng (OST_RebarTags), vd "A3_P_RT_DK&amp;KC_MID".</summary>
    public string? FootingRebarTagTypeName { get; init; }

    /// <summary>Tag thép chờ/đai cột (OST_RebarTags).</summary>
    public string? ColumnRebarTagTypeName { get; init; }

    /// <summary>Loại Structural Rebar Bending Detail (OST_RebarBendingDetails).</summary>
    public string? BendingDetailTypeName { get; init; }

    /// <summary>View template áp cho plan view sinh ra.</summary>
    public string? ViewTemplateName { get; init; }

    /// <summary>Plan view cha chứa ký hiệu callout.</summary>
    public string? ParentPlanViewName { get; init; }

    /// <summary>View Family Type dạng Detail dùng để tạo callout.</summary>
    public string? CalloutTypeName { get; init; }

    /// <summary>Loại viewport (OST_Viewports) khi đặt view lên sheet.</summary>
    public string? ViewportTypeName { get; init; }

    // ===== Sheet đích =====
    /// <summary>Title block dùng khi phải TẠO sheet mới.</summary>
    public string? TitleBlockName { get; init; }

    /// <summary>Số hiệu sheet đích (pick existing hoặc tạo mới).</summary>
    public string? SheetNumber { get; init; }

    /// <summary>Tên sheet đích.</summary>
    public string? SheetName { get; init; }

    // ===== Tùy chọn hiển thị (user tự bật/tắt, KHÔNG auto) =====
    public bool FootingTagEnabled { get; init; } = true;
    public bool ColumnTagEnabled { get; init; } = true;
    public bool BendingDetailEnabled { get; init; } = true;
    public bool TitleEnabled { get; init; } = true;

    // 3 lớp dim mỗi phương, độc lập:
    public bool DimOverallEnabled { get; init; } = true;   // chuỗi bao (1600/1800)
    public bool DimBaseEnabled { get; init; } = true;      // chuỗi đế (100/750/300/750/100)
    public bool DimPedestalEnabled { get; init; } = true;  // chuỗi cổ (50/200/50)

    /// <summary>Tỉ lệ view (mặc định 25 → TL 1:25).</summary>
    public int Scale { get; init; } = 25;

    /// <summary>Tiền tố tiêu đề, ghép với Mark móng → "MẶT BẰNG THÉP MÓNG M3".</summary>
    public string TitlePrefix { get; init; } = "MẶT BẰNG THÉP MÓNG";
}
