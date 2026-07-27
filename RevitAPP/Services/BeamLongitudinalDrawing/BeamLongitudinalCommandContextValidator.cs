using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

/// <summary>Precondition không thay đổi model cho command mặt cắt dọc dầm.</summary>
public static class BeamLongitudinalCommandContextValidator
{
    public static string? Validate(UIDocument? uiDocument)
    {
        if (uiDocument == null) return "Không có project Revit đang hoạt động.";

        var document = uiDocument.Document;
        if (document.IsFamilyDocument)
            return "Lệnh Mặt Cắt Dọc Dầm chỉ chạy trong project, không chạy trong Family Editor.";

        View? activeView = null;
        try
        {
            activeView = uiDocument.ActiveView;
        }
        catch
        {
            // Revit có thể chưa có active graphical view trong một số context khởi động.
        }

        if (activeView == null || activeView.IsTemplate)
            return "Hãy mở một view project không phải View Template trước khi chạy lệnh.";

        return null;
    }
}
