namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>
///     Cấu hình sheet đích: số hiệu + tên + tên title block (null = dùng title block đầu tiên trong project).
/// </summary>
public sealed record SheetConfig(string Number, string Name, string? TitleBlockName);
