using Autodesk.Revit.DB;

namespace RevitAPP.Chat.Tools.BeamLongitudinalDrawing;

internal sealed class LongitudinalSplitBreakLinePlacer
{
    private const double VerticalPaddingFeet = 50.0 / 304.8;

    public ElementId Place(Document document, ViewSection view, FamilyInstance beam,
        string typeName, double localX, bool reverseDirection)
    {
        var symbol = ResolveSymbol(document, typeName);
        if (symbol.Family.FamilyPlacementType != FamilyPlacementType.CurveBasedDetail)
            throw new InvalidOperationException(
                $"Break Line '{typeName}' không phải Detail Item line-based.");
        if (!symbol.IsActive)
        {
            symbol.Activate();
            document.Regenerate();
        }

        var crop = view.CropBox;
        var (beamBottom, beamTop) = BeamVerticalRange(beam, view, crop);
        var bottom = Math.Max(crop.Min.Y, beamBottom - VerticalPaddingFeet);
        var top = Math.Min(crop.Max.Y, beamTop + VerticalPaddingFeet);
        if (top <= bottom)
        {
            bottom = crop.Min.Y;
            top = crop.Max.Y;
        }

        var transform = crop.Transform;
        var low = transform.OfPoint(new XYZ(localX, bottom, 0));
        var high = transform.OfPoint(new XYZ(localX, top, 0));
        var line = reverseDirection
            ? Line.CreateBound(high, low)
            : Line.CreateBound(low, high);
        var instance = document.Create.NewFamilyInstance(line, symbol, view);
        var length = instance.LookupParameter("Length");
        if (length is { IsReadOnly: false, StorageType: StorageType.Double })
            length.Set(line.Length);
        return instance.Id;
    }

    private static FamilySymbol ResolveSymbol(Document document, string typeName)
    {
        var requested = typeName.Trim();
        return new FilteredElementCollector(document)
                   .OfClass(typeof(FamilySymbol))
                   .OfCategory(BuiltInCategory.OST_DetailComponents)
                   .Cast<FamilySymbol>()
                   .FirstOrDefault(symbol =>
                       string.Equals(symbol.Name, requested, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(symbol.FamilyName, requested, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals($"{symbol.FamilyName}: {symbol.Name}", requested,
                           StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"Không tìm thấy Detail Item Break Line '{typeName}'.");
    }

    private static (double Bottom, double Top) BeamVerticalRange(
        FamilyInstance beam, View view, BoundingBoxXYZ crop)
    {
        var box = beam.get_BoundingBox(view) ?? beam.get_BoundingBox(null);
        if (box == null) return (crop.Min.Y, crop.Max.Y);
        var inverse = crop.Transform.Inverse;
        var values = new List<double>();
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            values.Add(inverse.OfPoint(box.Transform.OfPoint(new XYZ(x, y, z))).Y);
        return (values.Min(), values.Max());
    }
}
