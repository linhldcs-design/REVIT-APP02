using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Models.FootingSection;

namespace RevitAPP.Services.FootingSection;

/// <summary>
///     Đọc hình học móng đơn thành <see cref="FootingSectionGeometry"/> thuần (feet). Dùng bounding box tổng quát
///     (xử lý mọi dạng family: in-place, loadable, foundation slab). Hướng cắt = dọc theo cạnh dài của đế
///     (mặt cắt thấy bề rộng lớn). Bao đủ móng + cổ + cột (nếu tìm được cột phía trên).
/// </summary>
public sealed class FootingGeometryReader
{
    private readonly FootingColumnFinder _columnFinder = new();

    public bool TryRead(Document document, Element footing, FootingSectionDirection direction,
        string? viewBottomLevelName, string? viewTopLevelName,
        out FootingSectionGeometry geometry, out string error)
    {
        geometry = null!;
        error = string.Empty;

        var box = footing.get_BoundingBox(null);
        if (box == null)
        {
            error = $"Không đọc được bounding box của móng '{footing.Id}'.";
            return false;
        }

        var spanX = box.Max.X - box.Min.X;
        var spanY = box.Max.Y - box.Min.Y;
        if (spanX <= 0 || spanY <= 0)
        {
            error = $"Móng '{footing.Id}' có kích thước không hợp lệ.";
            return false;
        }

        // Hướng cắt do người dùng chọn trong dialog.
        var cutAlongX = direction == FootingSectionDirection.X;
        var cutDir = cutAlongX ? new Point3(1, 0, 0) : new Point3(0, 1, 0);
        var widthFeet = cutAlongX ? spanX : spanY;

        var bottomZ = box.Min.Z;

        // Cột gần tâm móng nhất — dùng cho cả TIM CẮT và cao độ đỉnh section.
        var column = _columnFinder.FindNearestColumn(document, box);

        // Tim cắt = TIM CỘT (mặt cắt móng cắt qua cột để thấy thép chờ/đai cổ); nếu không có cột →
        // LocationPoint móng; cuối cùng → tâm bounding box đế.
        var centerX = (box.Min.X + box.Max.X) * 0.5;
        var centerY = (box.Min.Y + box.Max.Y) * 0.5;
        var columnXy = _columnFinder.ColumnCenterXy(column);
        if (columnXy != null)
        {
            centerX = columnXy.X;
            centerY = columnXy.Y;
        }
        else if (footing.Location is LocationPoint location)
        {
            centerX = location.Point.X;
            centerY = location.Point.Y;
        }

        // Đỉnh section: Top Level của cột (vd TẦNG 1) + đoạn nhô; không có cột → đỉnh móng (cổ).
        var columnTopZ = _columnFinder.SectionTopZFeet(document, column);
        var topZ = columnTopZ ?? box.Max.Z;
        if (topZ <= bottomZ) topZ = box.Max.Z;

        var viewBottomZ = ResolveLevelElevation(document, viewBottomLevelName);
        var viewTopLevelZ = ResolveLevelElevation(document, viewTopLevelName);
        if (viewBottomZ != null && viewTopLevelZ != null && viewTopLevelZ <= viewBottomZ)
        {
            error = "Level kết thúc phải cao hơn Level bắt đầu.";
            return false;
        }

        // Giữ stub 500mm phía trên Level đích để break line và crop top không cắt sát datum.
        var viewTopZ = viewTopLevelZ == null ? null : viewTopLevelZ + 500.0 / 304.8;
        geometry = new FootingSectionGeometry(
            Center: new Point3(centerX, centerY, bottomZ),
            WidthFeet: widthFeet,
            TopZFeet: topZ,
            BottomZFeet: bottomZ,
            CutDirection: cutDir,
            Mark: ReadMark(footing),
            ViewBottomZFeet: viewBottomZ,
            ViewTopZFeet: viewTopZ);
        return true;
    }

    private static double? ResolveLevelElevation(Document document, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .FirstOrDefault(level => string.Equals(level.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Elevation;
    }

    private static string ReadMark(Element footing)
    {
        var mark = footing.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        return string.IsNullOrWhiteSpace(mark) ? $"Mong-{footing.Id}" : mark;
    }
}
