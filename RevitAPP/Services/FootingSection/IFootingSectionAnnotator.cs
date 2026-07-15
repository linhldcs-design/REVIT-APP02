using Autodesk.Revit.DB;
using RevitAPP.Core.Models.FootingSection;

namespace RevitAPP.Services.FootingSection;

/// <summary>
///     Ngữ cảnh view mặt cắt móng vừa tạo (view + geometry nguồn), truyền cho annotator ở T2.
/// </summary>
public sealed record FootingViewContext(
    ViewSection View,
    Element Footing,
    FootingSectionGeometry Geometry);

/// <summary>
///     Đặt annotation (rebar tag / dimension / detail item) lên mặt cắt móng đã commit + regenerate. Cài đặt ở Phase 4.
/// </summary>
public interface IFootingSectionAnnotator
{
    void Annotate(Document doc, FootingViewContext context, FootingSectionSetting setting, FootingSectionResult result);
}

/// <summary>Annotation cần thêm transaction sau commit để ổn định reference rồi hợp nhất kết quả tạm.</summary>
public interface IFootingSectionPostCommitAnnotator
{
    void FinalizeAfterCommit(Document doc, FootingViewContext context, FootingSectionResult result);
    void CleanupAfterFinalize(Document doc, FootingViewContext context, FootingSectionResult result);
}
