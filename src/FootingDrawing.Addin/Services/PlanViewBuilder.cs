using Autodesk.Revit.DB;
using FootingDrawing.Core.Models;

namespace FootingDrawing.Addin.Services;

/// <summary>Tạo Detail Callout quanh móng trên parent plan view do user chọn.</summary>
public sealed class PlanViewBuilder
{
    private const double MarginFeet = 150.0 / 304.8;
    private const double AnnotationCropOffsetFeet = 2.0 / 12.0;

    public View Create(Document doc, View parentView, ElementId calloutTypeId,
        FootingGeometry geometry, int scale, ElementId? viewTemplateId, string viewName,
        List<string> warnings)
    {
        var (corner1, corner2) = BuildCalloutCorners(parentView, geometry);
        var view = ViewSection.CreateCallout(doc, parentView.Id, calloutTypeId, corner1, corner2);

        if (scale > 0) view.Scale = scale;
        TrySetUniqueName(view, viewName);
        TryApplyTemplate(view, viewTemplateId, warnings);
        ConfigureAnnotationCrop(view, warnings);
        return view;
    }

    private static void ConfigureAnnotationCrop(View view, List<string> warnings)
    {
        try
        {
            var active = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            if (active is { IsReadOnly: false }) active.Set(1);

            var manager = view.GetCropRegionShapeManager();
            if (!manager.CanHaveAnnotationCrop) return;
            manager.LeftAnnotationCropOffset = AnnotationCropOffsetFeet;
            manager.RightAnnotationCropOffset = AnnotationCropOffsetFeet;
            manager.TopAnnotationCropOffset = AnnotationCropOffsetFeet;
            manager.BottomAnnotationCropOffset = AnnotationCropOffsetFeet;
        }
        catch (Exception ex)
        {
            warnings.Add($"Không cấu hình được Annotation Crop: {ex.Message}");
        }
    }

    private static (XYZ Corner1, XYZ Corner2) BuildCalloutCorners(View parentView, FootingGeometry g)
    {
        var dirX = new XYZ(g.DirX.X, g.DirX.Y, g.DirX.Z).Normalize();
        var dirY = new XYZ(g.DirY.X, g.DirY.Y, g.DirY.Z).Normalize();
        var right = parentView.RightDirection.Normalize();
        var up = parentView.UpDirection.Normalize();
        var normal = parentView.ViewDirection.Normalize();
        var center = new XYZ(g.BaseCenter.X, g.BaseCenter.Y, g.BaseCenter.Z);

        center -= normal * normal.DotProduct(center - parentView.Origin);
        var halfRight = Math.Abs(dirX.DotProduct(right)) * g.WidthXFeet / 2
                        + Math.Abs(dirY.DotProduct(right)) * g.WidthYFeet / 2 + MarginFeet;
        var halfUp = Math.Abs(dirX.DotProduct(up)) * g.WidthXFeet / 2
                     + Math.Abs(dirY.DotProduct(up)) * g.WidthYFeet / 2 + MarginFeet;

        return (center - right * halfRight - up * halfUp,
            center + right * halfRight + up * halfUp);
    }

    private static void TryApplyTemplate(View view, ElementId? templateId, List<string> warnings)
    {
        if (templateId is null || templateId == ElementId.InvalidElementId) return;
        try
        {
            view.ViewTemplateId = templateId;
        }
        catch (Exception ex)
        {
            warnings.Add($"Không gán được View Template cho callout: {ex.Message}");
        }
    }

    private static void TrySetUniqueName(View view, string baseName)
    {
        for (var i = 0; i < 50; i++)
        {
            try
            {
                view.Name = i == 0 ? baseName : $"{baseName} ({i})";
                return;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Tên trùng — thử hậu tố tiếp theo.
            }
        }
    }
}
