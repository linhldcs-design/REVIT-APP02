using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.DB.PointClouds;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Services.PointCloud.Poc;
using Serilog;

namespace RevitAPP.Commands;

/// <summary>
///     POC Phase 0 (throwaway): verify GetPoints() đọc được điểm (kể cả engine third-party)
///     + DirectContext3D render billboard quad với point size đổi được.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class PointCloudPocCommand : ExternalCommand
{
    private static PointCloudPocServer? _activeServer;

    public override void Execute()
    {
        var uiDoc = Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        var instances = new FilteredElementCollector(doc)
            .OfClass(typeof(PointCloudInstance))
            .WhereElementIsNotElementType()
            .Cast<PointCloudInstance>()
            .ToList();

        if (instances.Count == 0)
        {
            TaskDialog.Show("POC", "Không có Point Cloud nào trong dự án.");
            return;
        }

        var report = new StringBuilder();
        var allPoints = new List<XYZ>();

        foreach (var instance in instances)
        {
            report.AppendLine($"=== {instance.Name} (SupportsOverrides={instance.SupportsOverrides}) ===");
            try
            {
                var bbox = instance.get_BoundingBox(null);
                var filter = BuildBoxFilter(bbox);
                var points = instance.GetPoints(filter, 0.5, 20000);

                var count = 0;
                var transform = instance.GetTotalTransform();
                foreach (CloudPoint cp in points)
                {
                    var modelPt = transform.OfPoint(new XYZ(cp.X, cp.Y, cp.Z));
                    allPoints.Add(modelPt);
                    count++;
                    if (count >= 20000) break;
                }

                report.AppendLine($"  GetPoints trả: {count} điểm");
                if (count > 0)
                    report.AppendLine($"  Điểm đầu (model): {allPoints[^count].ToString()}");
            }
            catch (Exception exception)
            {
                report.AppendLine($"  LỖI GetPoints: {exception.Message}");
                Log.Error(exception, "POC GetPoints fail cho {Name}", instance.Name);
            }
        }

        Log.Information("POC report:\n{Report}", report.ToString());

        // GATE C/D: render quad nếu đọc được điểm.
        if (allPoints.Count > 0)
        {
            RegisterRenderServer(allPoints, pointSizeFeet: 0.5); // ~150mm — to dễ thấy
            uiDoc.RefreshActiveView();
            report.AppendLine();
            report.AppendLine($"Đã đăng ký POC render {allPoints.Count} điểm (point size 0.5ft).");
            report.AppendLine("Chạy lại command để đổi point size → verify GATE D.");
        }
        else
        {
            report.AppendLine();
            report.AppendLine("KHÔNG đọc được điểm nào → GATE B FAIL nếu đây là PointStreamEngine.");
        }

        TaskDialog.Show("POC Point Cloud — kết quả", report.ToString());
    }

    private static PointCloudFilter BuildBoxFilter(BoundingBoxXYZ bbox)
    {
        var planes = new List<Plane>
        {
            Plane.CreateByNormalAndOrigin(XYZ.BasisX, bbox.Min),
            Plane.CreateByNormalAndOrigin(-XYZ.BasisX, bbox.Max),
            Plane.CreateByNormalAndOrigin(XYZ.BasisY, bbox.Min),
            Plane.CreateByNormalAndOrigin(-XYZ.BasisY, bbox.Max),
            Plane.CreateByNormalAndOrigin(XYZ.BasisZ, bbox.Min),
            Plane.CreateByNormalAndOrigin(-XYZ.BasisZ, bbox.Max)
        };
        return PointCloudFilterFactory.CreateMultiPlaneFilter(planes);
    }

    private static void RegisterRenderServer(IReadOnlyList<XYZ> points, double pointSizeFeet)
    {
        var service = (MultiServerService)ExternalServiceRegistry.GetService(
            ExternalServices.BuiltInExternalServices.DirectContext3DService);

        if (_activeServer != null)
        {
            service.RemoveServer(_activeServer.GetServerId());
            _activeServer = null;
        }

        _activeServer = new PointCloudPocServer(points, pointSizeFeet);
        service.AddServer(_activeServer);

        var ids = service.GetActiveServerIds();
        ids.Add(_activeServer.GetServerId());
        service.SetActiveServers(ids);
    }
}
