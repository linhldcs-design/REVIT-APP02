using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitAPP.Core.Models;
using RevitAPP.Models;
using RevitAPP.Helpers;

namespace RevitAPP.Services.ColumnRebar;

/// <summary>
///     Phát hiện hệ cột thẳng hàng từ một cột chọn (hoặc từ selection sẵn có), đọc hình học từng tầng,
///     trả về danh sách <see cref="ColumnStackItem"/> sắp xếp từ dưới lên.
/// </summary>
public sealed class ColumnStackDetector
{
    private const double AlignToleranceFeet = 1d / 304.8; // 1mm
    private readonly ColumnSectionReader _sectionReader = new();
    private readonly BeamDepthDetector _beamDetector = new();

    /// <summary>
    ///     Lấy danh sách cột để vẽ thép. Ưu tiên selection sẵn có (nhiều cột → dùng đúng các cột đó).
    ///     Nếu chưa chọn → cho user pick NHIỀU cột (từng tầng) cho linh hoạt.
    /// </summary>
    public IReadOnlyList<FamilyInstance> PickColumns(UIDocument uiDocument, out string error)
    {
        error = string.Empty;
        var document = uiDocument.Document;

        var preselected = uiDocument.Selection.GetElementIds()
            .Select(document.GetElement)
            .OfType<FamilyInstance>()
            .Where(IsStructuralColumn)
            .OrderBy(BaseElevationFeet)
            .ToList();
        if (preselected.Count > 0) return preselected;

        try
        {
            var refs = uiDocument.Selection.PickObjects(
                ObjectType.Element, new StructuralColumnSelectionFilter(),
                "Chọn các cột (từng tầng) rồi nhấn Finish. Chọn 1 cột để tự dò cả hệ thẳng hàng.");
            return refs.Select(r => document.GetElement(r)).OfType<FamilyInstance>()
                .OrderBy(BaseElevationFeet).ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            error = "Đã huỷ chọn cột.";
            return Array.Empty<FamilyInstance>();
        }
    }

    /// <summary>Build stack từ đúng danh sách cột user chọn (sắp xếp dưới → trên).</summary>
    public IReadOnlyList<ColumnStackItem> BuildStackFromColumns(Document document,
        IReadOnlyList<FamilyInstance> columns, out string error)
        => BuildItems(document, columns.OrderBy(BaseElevationFeet), out error);

    /// <summary>Build danh sách đoạn cột thẳng hàng (theo trục X,Y của cột mỏ neo).</summary>
    public IReadOnlyList<ColumnStackItem> BuildStack(Document document, FamilyInstance seed, out string error)
    {
        error = string.Empty;
        if (seed.Location is not LocationPoint seedLocation)
        {
            error = "Cột chọn không có điểm đặt hợp lệ.";
            return Array.Empty<ColumnStackItem>();
        }

        var seedPoint = seedLocation.Point;
        var seedBox = seed.get_BoundingBox(null);
        var aligned = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(column => IsSameStack(seed, seedBox, seedPoint, column))
            .OrderBy(BaseElevationFeet);

        return BuildItems(document, aligned, out error);
    }

    /// <summary>Đọc hình học từng cột → ColumnStackItem (sắp xếp theo thứ tự truyền vào).</summary>
    private IReadOnlyList<ColumnStackItem> BuildItems(Document document,
        IEnumerable<FamilyInstance> columns, out string error)
    {
        error = string.Empty;
        var items = new List<ColumnStackItem>();
        var index = 0;
        foreach (var column in columns)
        {
            if (!_sectionReader.TryRead(document, column, out var section, out var baseMm, out var topMm,
                    out var rotation, out var readError))
            {
                error = $"Cột tại cao độ ~{Math.Round(BaseElevationFeet(column) * 304.8)}mm: {readError}";
                return Array.Empty<ColumnStackItem>();
            }

            var levelName = GetBaseLevelName(document, column);
            var storey = new ColumnStorey(index++, levelName, baseMm, topMm, section);
            var location = (LocationPoint)column.Location!;

            var topFeet = UnitUtils.ConvertToInternalUnits(topMm, UnitTypeId.Millimeters);
            var autoBeamDepth = _beamDetector.DetectBeamDepthMm(document, column.Id, topFeet);

            items.Add(new ColumnStackItem(storey, column.Id, location.Point.X, location.Point.Y, rotation, autoBeamDepth));
        }

        if (items.Count == 0) error = "Không tìm thấy cột hợp lệ.";
        return items;
    }

    private static bool IsStructuralColumn(FamilyInstance instance)
        => instance.Category?.Id.ToValue() == (long)BuiltInCategory.OST_StructuralColumns;

    /// <summary>
    ///     Cùng hệ cột nếu: tâm trùng (±1mm, cột đồng tâm) HOẶC tâm cột này nằm trong mặt bằng cột mỏ neo,
    ///     hoặc ngược lại (cột lệch tâm 1 bên / thu tiết diện flush một mặt).
    /// </summary>
    private static bool IsSameStack(FamilyInstance seed, BoundingBoxXYZ? seedBox, XYZ seedPoint, FamilyInstance candidate)
    {
        if (candidate.Location is not LocationPoint lp) return false;
        var p = lp.Point;

        // Đồng tâm (nhanh)
        if (Math.Abs(p.X - seedPoint.X) <= AlignToleranceFeet && Math.Abs(p.Y - seedPoint.Y) <= AlignToleranceFeet)
            return true;

        // Lệch tâm: tâm cột này nằm trong footprint cột mỏ neo
        if (seedBox != null && WithinPlan(seedBox, p)) return true;

        // hoặc tâm cột mỏ neo nằm trong footprint cột này (trường hợp cột trên to hơn)
        var candBox = candidate.get_BoundingBox(null);
        return candBox != null && WithinPlan(candBox, seedPoint);
    }

    private static bool WithinPlan(BoundingBoxXYZ box, XYZ point)
        => point.X >= box.Min.X - AlignToleranceFeet && point.X <= box.Max.X + AlignToleranceFeet
        && point.Y >= box.Min.Y - AlignToleranceFeet && point.Y <= box.Max.Y + AlignToleranceFeet;

    private static double BaseElevationFeet(FamilyInstance column)
    {
        var levelId = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId();
        var level = levelId != null ? column.Document.GetElement(levelId) as Level : null;
        var offset = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0;
        return (level?.Elevation ?? 0) + offset;
    }

    private static string GetBaseLevelName(Document document, FamilyInstance column)
    {
        var levelId = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId();
        return levelId != null && document.GetElement(levelId) is Level level ? level.Name : "(?)";
    }
}
