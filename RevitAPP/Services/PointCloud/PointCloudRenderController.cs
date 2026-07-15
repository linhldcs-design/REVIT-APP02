using RevitAPP.Helpers;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.DB.PointClouds;
using Autodesk.Revit.UI;
using RevitAPP.Core.Models;
using Serilog;

namespace RevitAPP.Services.PointCloud;

/// <summary>
///     Điều phối custom render point cloud: đọc điểm (reader) → nạp server → đăng ký DirectContext3D,
///     ẩn point cloud gốc để không render đè 2 lớp. Bật/tắt + cập nhật state từ slider.
/// </summary>
public sealed class PointCloudRenderController
{
    private readonly IPointCloudReader _reader;
    private readonly PointCloudSettingsStore _store;
    private readonly PointCloudRenderServer _server = new();

    private bool _registered;
    private bool _enabled;
    private bool _cloudsPreviouslyHidden;
    private long _instanceId;
    private PointCloudRenderState _lastState = PointCloudRenderState.Default;

    public PointCloudRenderController(IPointCloudReader reader, PointCloudSettingsStore store)
    {
        _reader = reader;
        _store = store;
    }

    public bool IsEnabled => _enabled;

    /// <summary>Đọc state đã lưu cho view (per-view, học từ Qbitec). Null nếu chưa có.</summary>
    public PointCloudRenderState? LoadSavedState(View view) => _store.Load(view);

    /// <summary>Bật custom render cho instance trong view: đọc điểm, đăng ký server, ẩn cloud gốc.</summary>
    public bool Enable(Document document, View view, long instanceId, PointCloudRenderState state, double density)
    {
        try
        {
            if (document.GetElement(ElementIdHelper.Create(instanceId)) is not PointCloudInstance instance)
                return false;

            var result = _reader.Read(instance, view, density);
            Log.Information("Custom render: đọc {Count} điểm cho instance {Id}", result.Points.Count, instanceId);
            if (result.Points.Count == 0)
            {
                Log.Warning("Custom render: không đọc được điểm cho instance {Id}", instanceId);
                return false;
            }

            _server.SetPoints(result.Points, result.Origin);
            _server.UpdateState(state);
            RegisterServer();

            // Nhớ trạng thái ẩn trước đó để khôi phục đúng khi tắt.
            _cloudsPreviouslyHidden = view.ArePointCloudsHidden;
            SetNativeCloudsHidden(view, true);

            // Set _enabled SAU khi mọi bước Revit API thành công (tránh state desync nếu throw).
            _enabled = true;
            _instanceId = instanceId;
            _lastState = state;
            _store.Save(view, state); // lưu state khởi đầu per-view
            return true;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Bật custom render point cloud thất bại");
            return false;
        }
    }

    /// <summary>Tắt custom render: gỡ server, giải phóng buffer, khôi phục trạng thái cloud gốc.</summary>
    public void Disable(View view)
    {
        if (!_enabled) return;
        _store.Save(view, _lastState); // persist state cuối trước khi tắt (gom 1 lần, tránh spam undo)
        UnregisterServer();
        _server.ReleaseBuffers();
        SetNativeCloudsHidden(view, _cloudsPreviouslyHidden); // khôi phục đúng giá trị cũ
        _enabled = false;
    }

    /// <summary>
    ///     Cập nhật state từ slider (đã enabled). CHỈ push render — KHÔNG lưu mỗi tick
    ///     (tránh spam Transaction làm đầy undo stack). Persist khi Disable / tách qua <see cref="Persist" />.
    /// </summary>
    public bool UpdateState(View view, PointCloudRenderState state)
    {
        if (!_enabled) return false;
        _lastState = state;
        _server.UpdateState(state);
        return true;
    }

    /// <summary>Lưu state cuối per-view (gọi khi thả slider / đóng panel). Tách khỏi UpdateState để gom 1 Transaction.</summary>
    public void Persist(View view)
    {
        if (_enabled) _store.Save(view, _lastState);
    }

    /// <summary>Đọc lại điểm với density mới (đổi LOD).</summary>
    public bool Reload(Document document, View view, PointCloudRenderState state, double density)
    {
        if (!_enabled) return false;
        if (document.GetElement(ElementIdHelper.Create(_instanceId)) is not PointCloudInstance instance) return false;
        var result = _reader.Read(instance, view, density);
        _server.SetPoints(result.Points, result.Origin);
        _server.UpdateState(state);
        return true;
    }

    private double _lastLodKey = -1;

    /// <summary>
    ///     LOD động (giống Revit gốc): đọc lại điểm theo zoom hiện tại của view 3D.
    ///     Gọi từ Idling (đã throttle). Zoom gần → density nhỏ (dày); xa → density lớn (thưa).
    ///     Tổng điểm render giữ ~ ổn định bất kể file lớn cỡ nào → không lag mà nhìn đủ dày.
    /// </summary>
    public void MaybeRefreshLod(UIApplication app)
    {
        if (!_enabled) return;
        var uiDoc = app.ActiveUIDocument;
        if (uiDoc?.ActiveGraphicalView is not View3D view3D) return;

        // "Zoom" ước lượng = đường kính vùng nhìn. Quantize để chỉ re-read khi đổi đáng kể.
        var scale = ApproxViewScale(view3D);
        var lodKey = Math.Round(scale, 1); // quantize bậc thang → tránh re-read liên tục
        if (Math.Abs(lodKey - _lastLodKey) < 1e-6) return; // chưa đổi đủ → bỏ qua

        if (uiDoc.Document.GetElement(ElementIdHelper.Create(_instanceId)) is not PointCloudInstance instance) return;

        var density = DensityForScale(scale);
        var result = _reader.Read(instance, view3D, density);
        _server.SetPoints(result.Points, result.Origin);
        _server.UpdateState(_lastState);
        _lastLodKey = lodKey;
        uiDoc.RefreshActiveView();
    }

    /// <summary>Ước lượng tỉ lệ nhìn của view 3D (lớn = nhìn rộng/xa, nhỏ = zoom gần).</summary>
    private static double ApproxViewScale(View3D view)
    {
        var box = view.get_BoundingBox(view);
        if (box == null) return 100;
        return (box.Max - box.Min).GetLength();
    }

    /// <summary>Map tỉ lệ nhìn → averageDistance (feet). Xa → thưa (lớn), gần → dày (nhỏ).</summary>
    private static double DensityForScale(double scale)
    {
        // scale lớn (nhìn rộng) → averageDistance lớn để không đọc quá nhiều điểm.
        // Hệ số tinh chỉnh ở Phase perf; clamp để luôn hợp lý.
        var d = scale / 2000.0;
        return Math.Clamp(d, 0.02, 2.0);
    }

    private void RegisterServer()
    {
        var service = (MultiServerService)ExternalServiceRegistry.GetService(
            ExternalServices.BuiltInExternalServices.DirectContext3DService);

        if (!_registered)
        {
            service.AddServer(_server);
            _registered = true;
        }

        var ids = service.GetActiveServerIds();
        if (!ids.Contains(_server.GetServerId()))
        {
            ids.Add(_server.GetServerId());
            service.SetActiveServers(ids);
        }
    }

    private void UnregisterServer()
    {
        var service = (MultiServerService)ExternalServiceRegistry.GetService(
            ExternalServices.BuiltInExternalServices.DirectContext3DService);
        var ids = service.GetActiveServerIds();
        if (ids.Remove(_server.GetServerId()))
            service.SetActiveServers(ids);
    }

    /// <summary>Ẩn/hiện tất cả point cloud gốc trong view (tránh render đè custom + gốc).</summary>
    private static void SetNativeCloudsHidden(View view, bool hide)
    {
        if (view.ArePointCloudsHidden == hide) return;
        using var t = new Transaction(view.Document, hide ? "Ẩn point cloud gốc" : "Hiện point cloud gốc");
        t.Start();
        view.ArePointCloudsHidden = hide;
        t.Commit();
    }
}
