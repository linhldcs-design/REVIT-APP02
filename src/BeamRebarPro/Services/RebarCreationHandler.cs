using Autodesk.Revit.UI;
using BeamRebarPro.Models;
using BeamRebarPro.Services.Rebar;

namespace BeamRebarPro.Services;

/// <summary>
///     External-event bridge for modeless UI actions that must run on Revit's main thread.
/// </summary>
public sealed class RebarCreationHandler : IExternalEventHandler
{
    public RebarCreationRequest Request { get; set; } = RebarCreationRequest.CreateRebar;

    /// <summary>Configuration prepared by the ViewModel before raising the external event.</summary>
    public QuickSettingModel? Model { get; set; }

    /// <summary>Dầm đã chọn sẵn (lúc bấm Ribbon). Nếu có → tạo thép dùng luôn, không pick lại.</summary>
    public IReadOnlyList<Autodesk.Revit.DB.FamilyInstance>? PreselectedBeams { get; set; }

    /// <summary>Callback for rebar creation result. Runs on Revit's main thread.</summary>
    public Action<RebarCreationResult>? OnCompleted { get; set; }

    /// <summary>Callback for selected internal supports/columns.</summary>
    public Action<IReadOnlyList<SupportInfo>>? OnSupportsSelected { get; set; }

    /// <summary>Callback for selected secondary beams (đai tránh + tăng cường quanh dầm phụ).</summary>
    public Action<IReadOnlyList<SecondaryBeamInfo>>? OnSecondarySelected { get; set; }

    /// <summary>Callback for span info after picking a beam (Detail form). Runs on Revit's main thread.</summary>
    public Action<IReadOnlyList<SpanInfo>>? OnBeamInfo { get; set; }

    public void Execute(UIApplication app)
    {
        var uiDoc = app.ActiveUIDocument;
        if (uiDoc is null)
        {
            OnCompleted?.Invoke(new RebarCreationResult(0, 0, 0, ["Không có tài liệu Revit đang mở."]));
            return;
        }

        if (Request == RebarCreationRequest.SelectSupports)
        {
            var supports = new SupportPicker().PickSupportInfos(uiDoc, PreselectedBeams);
            OnSupportsSelected?.Invoke(supports);

            // Tính lại nhịp ngay (đang trong API context) với gối vừa thêm → Detail + preview thấy nhịp mới.
            if (PreselectedBeams is { Count: > 0 } pickedBeams)
            {
                var spans = BeamSpanReader.ReadSpans(uiDoc.Document, pickedBeams, supports.Select(s => s.Location).ToList());
                OnBeamInfo?.Invoke(spans);
            }
            return;
        }

        if (Request == RebarCreationRequest.SelectSecondary)
        {
            var secondary = new SupportPicker().PickSecondaryBeams(uiDoc, PreselectedBeams);
            OnSecondarySelected?.Invoke(secondary);
            return;
        }

        if (Request == RebarCreationRequest.PickBeamInfo)
        {
            OnBeamInfo?.Invoke(ReadBeamSpans(uiDoc));
            return;
        }

        if (Model is null)
        {
            OnCompleted?.Invoke(new RebarCreationResult(0, 0, 0, ["Chưa có cấu hình thép."]));
            return;
        }

        // Dùng dầm đã chọn lúc bấm Ribbon nếu có; nếu không thì pick.
        var beams = PreselectedBeams is { Count: > 0 } pre ? pre : new BeamPicker().PickBeams(uiDoc);
        if (beams.Count == 0)
        {
            OnCompleted?.Invoke(new RebarCreationResult(0, 0, 0, ["Chưa chọn dầm nào."]));
            return;
        }

        var result = new BeamRebarOrchestrator().Create(uiDoc.Document, beams, Model);

        // [CHAN DOAN tam] ghi warnings + so dai phu trong Model ra file de doc bang MCP/ngoai.
        try
        {
            var addCount = Model?.Stirrup.AdditionalStirrups.Count ?? -1;
            var lines = new System.Collections.Generic.List<string>
            {
                $"AdditionalStirrups trong Model.Stirrup = {addCount}",
                $"MainTop.Count = {Model?.MainTop.Count}",
                $"SpanOverrides.Count = {Model?.SpanOverrides.Count}",
                $"SpanOverrides co Stirrup = {Model?.SpanOverrides.Count(o => o.Stirrup != null)}",
                $"SpanOverride.Stirrup co AdditionalStirrups = " +
                    string.Join(",", (Model?.SpanOverrides ?? []).Select(o => o.Stirrup?.AdditionalStirrups.Count.ToString() ?? "null"))
            };
            lines.AddRange(result.Warnings.Where(w => w.Contains("Dai phu")));
            System.IO.File.WriteAllLines(@"C:\Users\Admin\Desktop\beamrebar-diag.txt", lines);
        }
        catch { }

        // Lưu cấu hình theo ID dầm để lần sau pick dầm này tự load lại (khỏi nhập lại).
        if (result.Succeeded)
            BeamConfigStore.Save(beams.Select(b => b.Id.ToValue()).ToList(), Model);

        OnCompleted?.Invoke(result);
    }

    /// <summary>Chọn dầm, gom thành nhịp (tự dò cột), trả chiều dài từng nhịp để form tính thép gia cường.</summary>
    private static IReadOnlyList<SpanInfo> ReadBeamSpans(Autodesk.Revit.UI.UIDocument uiDoc)
    {
        var beams = new BeamPicker().PickBeams(uiDoc);
        return BeamSpanReader.ReadSpans(uiDoc.Document, beams);
    }

    public string GetName() => "BeamRebarPro - Tạo thép dầm";
}

public enum RebarCreationRequest
{
    CreateRebar,
    SelectSupports,
    SelectSecondary,
    PickBeamInfo
}
