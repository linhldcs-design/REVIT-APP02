using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Core.Services;

public static class LongitudinalDrawingSettingValidator
{
    public static IReadOnlyList<string> Validate(LongitudinalDrawingSetting setting)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        var errors = new List<string>();
        if (setting.Scale <= 0) errors.Add("Tỷ lệ phải lớn hơn 0.");
        Required(setting.DimensionTypeName, "Dimension Type", errors);
        Required(setting.LongitudinalRebarTagTypeName, "Tag thép dọc", errors);
        Required(setting.StirrupTagTypeName, "Tag thép đai", errors);
        Required(setting.CrossSupportLongitudinalMraTypeName, "MRA thép dọc MC ngang gối", errors);
        Required(setting.CrossSupportStirrupTagTypeName, "Tag thép đai MC ngang gối", errors);
        Required(setting.CrossMidLongitudinalMraTypeName, "MRA thép dọc MC ngang nhịp", errors);
        Required(setting.CrossMidStirrupTagTypeName, "Tag thép đai MC ngang nhịp", errors);
        Required(setting.DetailComponentTypeName, "Detail Component", errors);
        Required(setting.CrossBreakLineTypeName, "Detail Item nét cắt mặt cắt ngang", errors);
        Required(setting.ViewportTypeName, "Viewport Type", errors);
        Required(setting.CrossViewportTypeName, "Viewport Type mặt cắt ngang", errors);
        Required(setting.LongitudinalSectionTypeName, "Section Type dọc", errors);
        Required(setting.CrossSectionTypeName, "Section Type ngang", errors);
        Required(setting.SpotElevationTypeName, "Spot Elevation Type", errors);
        Required(setting.SheetNumber, "Sheet có sẵn", errors);
        if (setting.AnnotationOffsetMm < 0) errors.Add("Offset annotation không được âm.");
        if (setting.EndpointToleranceMm <= 0) errors.Add("Endpoint tolerance phải lớn hơn 0.");
        if (setting.AlignmentToleranceMm <= 0) errors.Add("Alignment tolerance phải lớn hơn 0.");
        return errors;
    }

    private static void Required(string? value, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{label} là bắt buộc.");
    }
}
