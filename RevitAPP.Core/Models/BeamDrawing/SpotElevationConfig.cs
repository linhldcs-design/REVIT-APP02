namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>Cấu hình spot elevation mặt đứng: bật/tắt, tên type, offset (mm).</summary>
public sealed record SpotElevationConfig(bool Enabled, string? TypeName, double OffsetMm);
