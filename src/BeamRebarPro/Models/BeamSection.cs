namespace BeamRebarPro.Models;

/// <summary>Tiết diện dầm chữ nhật (mm). Chiều cao thực lấy từ geometry khi tạo thép.</summary>
public sealed record BeamSection(double WidthMm, double HeightMm);
