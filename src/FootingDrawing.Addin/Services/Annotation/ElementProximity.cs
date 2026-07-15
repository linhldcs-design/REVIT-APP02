using Autodesk.Revit.DB;

namespace FootingDrawing.Addin.Services.Annotation;

internal static class ElementProximity
{
    public static bool IsNearCenter(Element element, XYZ center, double radius)
    {
        if (element.Location is LocationPoint lp)
            return new XYZ(lp.Point.X - center.X, lp.Point.Y - center.Y, 0).GetLength() <= radius;

        var box = element.get_BoundingBox(null);
        if (box == null) return false;
        if (center.X >= box.Min.X && center.X <= box.Max.X && center.Y >= box.Min.Y && center.Y <= box.Max.Y)
            return true;

        var mid = (box.Min + box.Max) * 0.5;
        return new XYZ(mid.X - center.X, mid.Y - center.Y, 0).GetLength() <= radius;
    }
}
